using System.Text.Json;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Options;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Spec 261 R-7, research R2 (task T501): the commit and delivery paths a delayed child schedule relies on. A
/// post-commit <c>EnqueueSchedulerWork</c> intent that carries a due time must not be handed over by the in-process hop
/// or delivered by a drain's own delivery step before it is due; a Balanced or Ephemeral drain must write it durably at
/// once, with its due time; and a run whose only outstanding work is such an item must not look stalled to recovery.
/// </summary>
public sealed class RuntimeDelayedScheduleDeliveryTests
{
    private const string Wfid = "wfexec-delayed";
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DueAt = Now.AddSeconds(60);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_owned_immediate_continuation_survives_clock_rollback_but_its_retry_still_waits(bool fastPath)
    {
        var clock = new FakeTimeProvider(Now.AddTicks(-1));
        await using var provider = NewCoalescingProvider(clock, services =>
            services.AddSingleton(new RuntimeInProcessHopFastPathOptions { Enabled = fastPath }));
        await using var servicesScope = provider.CreateAsyncScope();
        var services = servicesScope.ServiceProvider;
        await using var drain = services.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>().Begin(Wfid, new(PersistenceMode.Balanced, true));
        var (_, _, immediate) = NewCommitWithDelayedAndImmediateIntents();
        var commit = NewCommit([immediate]);
        await services.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(commit);
        var outbox = services.GetRequiredService<IRuntimePostCommitOutboxStore>();
        var id = RuntimePostCommitOutboxItems.OutboxItemId(commit.CommitId, immediate);
        Assert.Equal(id, Assert.Single(await outbox.GetDeliverableAsync(new(clock.GetUtcNow(), 10, Wfid))).OutboxItemId);
        var claims = Assert.IsAssignableFrom<IRuntimePostCommitOutboxClaimStore>(outbox);
        var claim = Assert.Single(await claims.ClaimAsync(new("owner", clock.GetUtcNow(), TimeSpan.FromSeconds(30), 10, Wfid)));
        Assert.Equal(id, claim.OutboxItemId);
        Assert.Equal(Now, claim.Item.AvailableAt); // The persisted representation and replay fingerprint stay unchanged.
        Assert.Equal(Now.AddSeconds(30), claim.VisibleAfter);
        await claims.RecordDeliveryResultAsync(claim, new(id, RuntimePostCommitOutboxStatus.FailedRetryable, Now, "retry"));
        Assert.Empty(await outbox.GetDeliverableAsync(new(clock.GetUtcNow(), 10, Wfid)));
        Assert.Empty(await claims.ClaimAsync(new("owner", clock.GetUtcNow(), TimeSpan.FromSeconds(30), 10, Wfid)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_explicit_due_time_or_bare_outbox_row_is_not_an_owned_immediate_continuation(bool bareRow)
    {
        var clock = new FakeTimeProvider(Now.AddSeconds(-2));
        await using var provider = NewCoalescingProvider(clock);
        await using var servicesScope = provider.CreateAsyncScope();
        var services = servicesScope.ServiceProvider;
        await using var drain = services.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>().Begin(Wfid, new(PersistenceMode.Balanced, true));
        var intent = NewIntent("explicit", bareRow ? null : Now.AddSeconds(-1));
        var commit = NewCommit([intent]);
        if (bareRow)
            commit = commit with { PostCommitIntents = [], StateChanges = commit.StateChanges.WithPostCommitOutbox([
                new(RuntimePostCommitOutboxItems.OutboxItemId(commit.CommitId, intent), RuntimeStateChangeOperation.Upsert,
                    RuntimePostCommitOutboxItems.CreatePending(commit, intent), new Dictionary<string, string>())]) };
        await services.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(commit);
        Assert.Empty(await services.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(new(clock.GetUtcNow(), 10, Wfid)));
        Assert.Empty(await Assert.IsAssignableFrom<IRuntimePostCommitOutboxClaimStore>(services.GetRequiredService<IRuntimePostCommitOutboxStore>()).ClaimAsync(
            new("owner", clock.GetUtcNow(), TimeSpan.FromSeconds(30), 10, Wfid)));
    }

    // Check 1: the in-process hop does not publish a not-yet-due continuation to the live drain's carrier.
    [Fact]
    public async Task DelayedSchedule_TheInProcessHopDoesNotPublishANotYetDueIntent()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var committer = NewCommitter(store, liveDrain);
        var scope = new RuntimeLiveDrainDeliveryScope(Wfid);
        var (commit, delayed, immediate) = NewCommitWithDelayedAndImmediateIntents();

        using (liveDrain.Push(scope))
            await committer.CommitAsync(commit);

        Assert.False(scope.TryTakeHopWorkItem(delayed.IntentId, out _));
        Assert.True(scope.TryTakeHopWorkItem(immediate.IntentId, out _));
    }

    // Check 2: the delivery step of a live drain (and a sweep) delivers the delayed item only once it is due.
    [Fact]
    public async Task DelayedSchedule_TheImmediateDispatchDoesNotDeliverItBeforeItIsDue()
    {
        var clock = new FakeTimeProvider(Now);
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var committer = NewCommitter(store, liveDrain);
        var processor = NewProcessor(store, queue, liveDrain, clock);
        var (commit, delayed, _) = NewCommitWithDelayedAndImmediateIntents();

        using (liveDrain.Push(new RuntimeLiveDrainDeliveryScope(Wfid)))
        {
            await committer.CommitAsync(commit);
            var own = await processor.ProcessAsync(ContinuationDelivery());
            Assert.Equal(1, own.DeliveredCount);
        }

        var item = await FindOutboxItemAsync(store, commit, delayed);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, item.Status);
        Assert.Equal(DueAt, item.AvailableAt);
        Assert.Equal(["work-immediate"], (await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Wfid))).Select(work => work.WorkItemId));

        clock.SetUtcNow(DueAt.AddSeconds(-1));
        Assert.Equal(0, (await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10))).AttemptedCount);

        clock.SetUtcNow(DueAt);
        Assert.Equal(1, (await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10))).DeliveredCount);
        Assert.Equal(
            ["work-immediate", "work-delayed"],
            (await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Wfid))).Select(work => work.WorkItemId));
    }

    // Check 3: a Balanced or Ephemeral drain writes the checkpoint that carries a delayed item durably at once, with the
    // item's due time, and the end-of-drain flush keeps it pending with that due time.
    [Theory]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task DelayedSchedule_ABalancedOrEphemeralDrainFlushesItDurablyWithItsDueTime(PersistenceMode mode)
    {
        var clock = new FakeTimeProvider(Now);
        await using var provider = NewCoalescingProvider(clock);
        await using var serviceScope = provider.CreateAsyncScope();
        var services = serviceScope.ServiceProvider;
        var durable = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        var committer = services.GetRequiredService<RuntimeCheckpointCommitter>();
        var (commit, delayed, _) = NewCommitWithDelayedAndImmediateIntents();

        await using (var drain = services.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>()
                         .Begin(Wfid, new ResolvedPersistenceMode(mode, RunHasRecord: true)))
        {
            Assert.True(drain.Session.IsActive);
            await committer.CommitAsync(commit);

            // Durable before the drain ends: a kill here must not lose the delayed item.
            var written = await FindOutboxItemAsync(durable, commit, delayed);
            Assert.Equal(DueAt, written.AvailableAt);
            await drain.FlushAtQuiescenceAsync();
        }

        var flushed = await FindOutboxItemAsync(durable, commit, delayed);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, flushed.Status);
        Assert.Equal(DueAt, flushed.AvailableAt);
    }

    // Check 4: a run whose only outstanding work is a pending delayed item is no recovery candidate, and the sweep
    // neither delivers nor re-drives anything for it before the item is due.
    [Fact]
    public async Task DelayedSchedule_RecoveryIgnoresARunWhoseOnlyWorkIsAPendingDelayedItem()
    {
        var clock = new FakeTimeProvider(Now);
        await using var provider = NewCoalescingProvider(clock, services =>
            services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>());
        await using (var commitScope = provider.CreateAsyncScope())
        {
            await commitScope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(NewRunningWorkflow());
            await commitScope.ServiceProvider.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(NewCommitWithOnlyADelayedIntent());
        }

        clock.SetUtcNow(DueAt.AddSeconds(-1));
        await using (var scanScope = provider.CreateAsyncScope())
        {
            var candidates = await scanScope.ServiceProvider.GetRequiredService<IRuntimeRecoveryScanner>().ScanAsync(
                new RuntimeRecoveryScanRequest(clock.GetUtcNow(), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), limit: 10));
            Assert.Empty(candidates);

            var sweep = await scanScope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new RuntimeResumptionSweepRequest());
            Assert.Equal(0, sweep.OutboxAttemptedCount);
            Assert.Empty(sweep.Dispatches);
        }

        Assert.Equal(WorkflowExecutionStatus.Running,
            (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(Wfid))!.Status);
        Assert.Empty(await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(new RuntimeSchedulerWorkQuery(Wfid)));
    }

    // D9: the same commit replayed (a drain that crashed after its commit and re-ran the evaluation) keeps one item.
    [Fact]
    public async Task DelayedSchedule_AReplayedCommitKeepsOneItemWithTheSameIdentity()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var committer = NewCommitter(store, new AsyncLocalRuntimeLiveDrainDeliveryAccessor());
        var commit = NewCommitWithOnlyADelayedIntent();

        var first = await committer.CommitAsync(commit);
        var replay = await committer.CommitAsync(NewCommitWithOnlyADelayedIntent());

        Assert.True(first.Succeeded);
        Assert.True(replay.Succeeded);
        var id = Assert.Single(first.PendingPostCommitWorkIds);
        Assert.Equal(id, Assert.Single(replay.PendingPostCommitWorkIds));
        Assert.Equal(DueAt, (await store.FindAsync(id))!.AvailableAt);
        Assert.Single(store.ListCommits());
    }

    // The intent factory turns a ScheduleActivity work item's due time into the intent's NotBefore; a due time at or
    // before the commit, or on another kind of work, does not delay it.
    [Fact]
    public void DelayedSchedule_TheIntentFactoryReadsTheWorkItemsDueTime()
    {
        var source = NewWorkItem("work-source", WorkflowExecutionCommandKind.CompleteActivity, notBefore: null);

        Assert.Equal(DueAt, NewIntent(source, NewWorkItem("work-delayed", WorkflowExecutionCommandKind.ScheduleActivity, DueAt)).NotBefore);
        Assert.Null(NewIntent(source, NewWorkItem("work-now", WorkflowExecutionCommandKind.ScheduleActivity, Now)).NotBefore);
        Assert.Null(NewIntent(source, NewWorkItem("work-past", WorkflowExecutionCommandKind.ScheduleActivity, Now.AddSeconds(-1))).NotBefore);
        Assert.Null(NewIntent(source, NewWorkItem("work-start", WorkflowExecutionCommandKind.StartActivity, DueAt)).NotBefore);
        Assert.Null(NewIntent(source, NewWorkItem("work-plain", WorkflowExecutionCommandKind.ScheduleActivity, notBefore: null)).NotBefore);

        static RuntimePostCommitIntent NewIntent(RuntimeSchedulerWorkItem source, RuntimeSchedulerWorkItem workItem) =>
            SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(source, "actexec-parent", workItem, Now);
    }

    [Fact]
    public void DelayedSchedule_TheIntentKeepsItsDueTimeThroughSerializationAndEquivalence()
    {
        var intent = NewIntent("work-delayed", DueAt.ToOffset(TimeSpan.FromHours(2)));

        Assert.Equal(TimeSpan.Zero, intent.NotBefore!.Value.Offset);
        var roundTripped = JsonSerializer.Deserialize<RuntimePostCommitIntent>(JsonSerializer.Serialize(intent))!;
        Assert.Equal(DueAt, roundTripped.NotBefore);
        Assert.True(intent.IsEquivalentTo(roundTripped));
        Assert.False(intent.IsEquivalentTo(NewIntent("work-delayed", notBefore: null)));
        Assert.True(intent.IsDelayedPast(Now));
        Assert.False(intent.IsDelayedPast(DueAt));
    }

    [Fact]
    public void DelayedSchedule_TheScheduleRequestKeepsTheDueTimeInUtc()
    {
        var request = new RuntimeChildActivityScheduleRequest("node-a", null, notBefore: DueAt.ToOffset(TimeSpan.FromHours(-5)));

        Assert.Equal(DueAt, request.NotBefore);
        Assert.Equal(TimeSpan.Zero, request.NotBefore!.Value.Offset);
        Assert.Null(new RuntimeChildActivityScheduleRequest("node-a", null).NotBefore);
        Assert.Throws<ArgumentException>(() => new RuntimeChildActivityScheduleRequest(" ", null, notBefore: DueAt));
    }

    [Theory]
    [InlineData(true, 60, null)]
    [InlineData(true, 0, null)]
    [InlineData(true, -60, null)]
    [InlineData(false, 0, null)]
    [InlineData(false, 60, RuntimeDelayedScheduleFaultCodes.Unavailable)]
    [InlineData(true, 24 * 3600 + 1, RuntimeDelayedScheduleFaultCodes.TooFar)]
    [InlineData(true, 24 * 3600, null)]
    public void DelayedSchedule_TheGuardRefusesWhatTheHostCannotDeliver(bool capability, int seconds, string? expectedCode)
    {
        var services = new ServiceCollection();
        if (capability)
            services.AddSingleton<IRuntimeDelayedScheduleCapability, TestCapability>();
        using var provider = services.BuildServiceProvider();
        var requests = new[] { new RuntimeChildActivityScheduleRequest("node-a", null, notBefore: Now.AddSeconds(seconds)) };

        var exception = Record.Exception(() => RuntimeDelayedSchedules.Validate(provider, new FakeTimeProvider(Now), "node-parent", requests));

        if (expectedCode is null)
            Assert.Null(exception);
        else
            Assert.Equal(expectedCode, Assert.IsType<RuntimeDelayedScheduleException>(exception).FailureCode);
    }

    [Fact]
    public void DelayedSchedule_TheGuardHonoursAConfiguredMaxDelay()
    {
        var services = new ServiceCollection()
            .AddSingleton<IRuntimeDelayedScheduleCapability, TestCapability>()
            .AddSingleton(new RuntimeDelayedScheduleOptions { MaxDelay = TimeSpan.FromMinutes(1) });
        using var provider = services.BuildServiceProvider();
        var requests = new[] { new RuntimeChildActivityScheduleRequest("node-a", null, notBefore: Now.AddMinutes(2)) };

        var exception = Assert.Throws<RuntimeDelayedScheduleException>(() => RuntimeDelayedSchedules.Validate(provider, new FakeTimeProvider(Now), "node-parent", requests));
        Assert.Equal(RuntimeDelayedScheduleFaultCodes.TooFar, exception.FailureCode);
        Assert.False(exception.IsRetryable);
    }

    private sealed class TestCapability : IRuntimeDelayedScheduleCapability;

    private static RuntimeSchedulerWorkItem NewWorkItem(string workItemId, WorkflowExecutionCommandKind kind, DateTimeOffset? notBefore) =>
        new(
            workItemId: workItemId,
            workflowExecutionId: Wfid,
            commandId: $"command-{workItemId}",
            commandKind: kind,
            envelopeId: $"envelope-{workItemId}",
            idempotencyKey: $"{Wfid}:{workItemId}",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: 2,
            commandMetadata: notBefore is { } value
                ? new Dictionary<string, string> { [RuntimeMetadataKeys.ScheduleNotBefore] = RuntimeDelayedSchedules.Format(value) }
                : null);

    private static async Task<RuntimePostCommitOutboxItem> FindOutboxItemAsync(
        InMemoryRuntimeCheckpointCommitStore store,
        RuntimeCheckpointCommit commit,
        RuntimePostCommitIntent intent) =>
        await store.FindAsync(RuntimePostCommitOutboxItems.OutboxItemId(commit.CommitId, intent))
        ?? throw new Xunit.Sdk.XunitException($"Outbox item for intent '{intent.IntentId}' was not written durably.");

    private static ServiceProvider NewCoalescingProvider(TimeProvider clock, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.RemoveAll<TimeProvider>();
        services.AddSingleton(clock);
        configure?.Invoke(services);
        services.AddPersistenceModes();
        return services.BuildServiceProvider();
    }

    private static RuntimeCheckpointCommitter NewCommitter(
        InMemoryRuntimeCheckpointCommitStore store,
        IRuntimeLiveDrainDeliveryAccessor liveDrain) =>
        new(
            new ImmediateRuntimeCheckpointPersistencePolicy(),
            store,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
            enrichers: [],
            intentHandlerContributions:
            [
                new RuntimePostCommitIntentHandlerContribution(
                    RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
                    typeof(RuntimeSchedulerPostCommitIntentDispatcher),
                    RuntimeSchedulerPostCommitIntentDispatcher.RetryPolicy)
            ],
            liveDrainDeliveryAccessor: liveDrain);

    private static RuntimePostCommitOutboxProcessor NewProcessor(
        InMemoryRuntimeCheckpointCommitStore store,
        InMemoryWorkflowSchedulerWorkQueue queue,
        IRuntimeLiveDrainDeliveryAccessor liveDrain,
        TimeProvider clock) =>
        new(
            store,
            new RuntimeSchedulerPostCommitIntentDispatcher(queue, liveDrain),
            clock,
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            workflowDispatchStore: null,
            logger: null,
            liveDrain,
            coalescingSessionAccessor: null);

    private static RuntimePostCommitOutboxProcessRequest ContinuationDelivery() =>
        new(limit: 10, workflowExecutionId: Wfid, intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork);

    private static (RuntimeCheckpointCommit Commit, RuntimePostCommitIntent Delayed, RuntimePostCommitIntent Immediate) NewCommitWithDelayedAndImmediateIntents()
    {
        var delayed = NewIntent("work-delayed", DueAt);
        var immediate = NewIntent("work-immediate", notBefore: null);
        return (NewCommit([delayed, immediate]), delayed, immediate);
    }

    private static RuntimeCheckpointCommit NewCommitWithOnlyADelayedIntent() => NewCommit([NewIntent("work-delayed", DueAt)]);

    private static RuntimeCheckpointCommit NewCommit(IReadOnlyList<RuntimePostCommitIntent> intents) =>
        new(
            CommitId: "commit-delayed",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: "checkpoint-delayed",
                Name: RuntimeCheckpointNames.ActivityInspectionCaptured,
                WorkflowExecutionId: Wfid,
                OccurredAt: Now,
                ActivityExecutionIds: [],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []),
            PostCommitIntents: intents,
            Metadata: new Dictionary<string, string>());

    private static RuntimePostCommitIntent NewIntent(string workItemId, DateTimeOffset? notBefore)
    {
        var workItem = new RuntimeSchedulerWorkItem(
            workItemId: workItemId,
            workflowExecutionId: Wfid,
            commandId: $"command-{workItemId}",
            commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
            envelopeId: $"envelope-{workItemId}",
            idempotencyKey: $"{Wfid}:{workItemId}",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: 2);
        return new RuntimePostCommitIntent(
            intentId: $"intent-{workItemId}",
            workflowExecutionId: Wfid,
            kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            recordedAt: Now,
            activityExecutionId: "actexec-parent",
            idempotencyKey: $"post-commit:{workItemId}",
            payload: JsonSerializer.SerializeToElement(workItem),
            materializedSchedulerWorkItem: workItem,
            notBefore: notBefore);
    }

    private static WorkflowExecutionState NewRunningWorkflow() =>
        new(
            WorkflowExecutionId: Wfid,
            PinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            Status: WorkflowExecutionStatus.Running,
            SubStatus: null,
            CreatedAt: Now,
            StartedAt: Now,
            UpdatedAt: Now,
            CompletedAt: null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());
}
