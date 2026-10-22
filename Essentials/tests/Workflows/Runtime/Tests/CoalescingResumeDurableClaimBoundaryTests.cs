using System.Text.Json;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class CoalescingResumeDurableClaimBoundaryTests
{
    private const string WorkflowExecutionId = "wf-coalesced-resume-boundary";
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(17);

    [Fact]
    public async Task Resume_boundary_flushes_coalesced_work_then_durable_deferral_survives_provider_rebuild()
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: true, enqueueScheduleSuffixAfterSuccessfulResume: true);
        var drainOptions = new WorkflowDrainOrchestratorOptions(maxCyclesWithoutProgress: 1, outboxDeliveryBatchSize: 1);
        await using var firstProvider = BuildProvider(shared, probe, drainOptions);
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 1));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-2", 2));
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 3));

        RuntimeSchedulerDrainResult deferred;
        await using (var scope = firstProvider.CreateAsyncScope())
        {
            var orchestrator = scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>();
            deferred = await orchestrator.DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));
        }

        Assert.True(deferred.StoppedOnDeferral);
        Assert.Equal(RuntimeSchedulerDrainStopReason.Deferred, deferred.StopReason);
        Assert.Equal(
            ["schedule-1", "schedule-2", "resume-1"],
            deferred.Items.Select(item => item.WorkItemId));
        Assert.Equal(
            [
                RuntimeSchedulerWorkItemResultStatus.Completed,
                RuntimeSchedulerWorkItemResultStatus.Completed,
                RuntimeSchedulerWorkItemResultStatus.Deferred
            ],
            deferred.Items.Select(item => item.Status));

        Assert.Equal(new long?[] { null, 1 }, probe.ScheduleObservedVersions);
        Assert.Equal(new[] { 0, 0 }, probe.ScheduleDurableCommitCounts);
        var resumeAtDeferral = Assert.Single(probe.ResumeSnapshots);
        AssertDurableResumeSnapshot(resumeAtDeferral, schedulerVersion: 2, durableCommitCount: 1);

        await using (var auditScope = firstProvider.CreateAsyncScope())
        {
            var durableFlush = Assert.Single(InnerCommitStore(auditScope.ServiceProvider).ListCommits());
            Assert.Equal(RuntimeCoalescingMetadataKeys.FlushCheckpointName, durableFlush.Commit.Checkpoint.Name);
            Assert.Equal(2L, durableFlush.Commit.StateChanges.Scheduler?.State.Version);
            Assert.Empty(await auditScope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>()
                .ListAsync(WorkflowExecutionId));
        }
        Assert.Equal("resume-1", Assert.Single(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).WorkItemId);

        // A newly built provider sees the same queue row and its future visibility. The early read is through the
        // coalescing decorator with no active session, so it exercises the durable provider path used after restart.
        await using var reopenedProvider = BuildProvider(shared, probe, drainOptions);
        await using var reopenedScope = reopenedProvider.CreateAsyncScope();
        var reopenedQueue = reopenedScope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var notYetVisibleAt = Now + RetryDelay - TimeSpan.FromTicks(1);
        Assert.Empty(await reopenedQueue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(notYetVisibleAt)));

        shared.TimeProvider.Advance(RetryDelay);
        Assert.Equal(
            [WorkflowExecutionId],
            await reopenedQueue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(Now + RetryDelay)));

        var retried = await reopenedScope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.False(retried.StoppedOnDeferral);
        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, retried.StopReason);
        Assert.Equal(
            ["resume-1", "schedule-3", "schedule-4"],
            retried.Items.Select(item => item.WorkItemId));
        Assert.All(retried.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Equal(2, probe.ResumeSnapshots.Count);
        Assert.All(probe.ResumeSnapshots, snapshot => AssertDurableResumeSnapshot(snapshot, schedulerVersion: 2, durableCommitCount: 1));
        Assert.Equal(new long?[] { null, 1, 2, 3 }, probe.ScheduleObservedVersions);
        Assert.Equal(new[] { 0, 0, 2, 2 }, probe.ScheduleDurableCommitCounts);
        var commitsAfterRetry = InnerCommitStore(reopenedScope.ServiceProvider).ListCommits();
        Assert.Equal(3, commitsAfterRetry.Count);
        var resumeCheckpoint = Assert.Single(commitsAfterRetry.Where(item => item.Commit.Checkpoint.Name == RuntimeCheckpointNames.BookmarkConsumed));
        Assert.Equal(2, resumeCheckpoint.Commit.StateChanges.PostCommitOutbox.Count);
        Assert.Equal(
            ["schedule-3", "schedule-4"],
            resumeCheckpoint.Commit.StateChanges.PostCommitOutbox
                .Select(change => change.State.Intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!.WorkItemId));
        var suffixWorkOutboxIds = resumeCheckpoint.Commit.StateChanges.PostCommitOutbox
            .Select(change => change.State.OutboxItemId)
            .ToArray();
        var innerOutbox = reopenedScope.ServiceProvider.GetRequiredService<CoalescingInner<IRuntimePostCommitOutboxStore>>().Value;
        var outboxLookup = reopenedScope.ServiceProvider.GetRequiredService<CoalescingInner<IPostCommitOutboxLookupStore>>().Value;
        foreach (var outboxId in suffixWorkOutboxIds)
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await outboxLookup.FindAsync(outboxId))?.Status);
        Assert.Empty(await innerOutbox.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            DateTimeOffset.MaxValue,
            limit: 10,
            workflowExecutionId: WorkflowExecutionId,
            intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork)));
        var flushCommits = commitsAfterRetry
            .Where(item => item.Commit.Checkpoint.Name == RuntimeCoalescingMetadataKeys.FlushCheckpointName)
            .ToArray();
        Assert.Equal(2, flushCommits.Length);
        Assert.Equal(new long?[] { 2, 4 }, flushCommits.Select(item => item.Commit.StateChanges.Scheduler?.State.Version));
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task Single_resume_continuation_runs_within_the_original_one_item_drain_request()
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(
            deferFirstResume: false,
            enqueueScheduleSuffixAfterSuccessfulResume: true,
            scheduleSuffixCount: 1);
        await using var provider = BuildProvider(shared, probe);
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 1));

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId, maxWorkItems: 1));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(["resume-1", "schedule-3"], result.Items.Select(item => item.WorkItemId));
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Equal(1, result.OutboxDeliveredCount);
        Assert.Equal(new long?[] { null }, probe.ScheduleObservedVersions);
        Assert.Equal(1L, (await shared.SchedulerStateStore.FindAsync(WorkflowExecutionId))?.Version);

        var resumeCheckpoint = Assert.Single(InnerCommitStore(scope.ServiceProvider).ListCommits()
            .Where(item => item.Commit.Checkpoint.Name == RuntimeCheckpointNames.BookmarkConsumed));
        var continuationOutbox = Assert.Single(resumeCheckpoint.Commit.StateChanges.PostCommitOutbox);
        Assert.Equal("schedule-3", continuationOutbox.State.Intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!.WorkItemId);
        var outboxLookup = scope.ServiceProvider.GetRequiredService<CoalescingInner<IPostCommitOutboxLookupStore>>().Value;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await outboxLookup.FindAsync(continuationOutbox.State.OutboxItemId))?.Status);
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task Resume_reprobes_all_durable_continuations_after_external_delivery_supersedes_each_batch()
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false, enqueueScheduleSuffixAfterSuccessfulResume: true);
        var externalDelivery = new ExternalOutboxDeliveryProbe();
        var drainOptions = new WorkflowDrainOrchestratorOptions(maxCyclesWithoutProgress: 1, outboxDeliveryBatchSize: 1);
        await using var provider = BuildProvider(shared, probe, drainOptions, externalDelivery);
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 1));

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(["resume-1", "schedule-3", "schedule-4"], result.Items.Select(item => item.WorkItemId));
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Equal(new long?[] { null, 1 }, probe.ScheduleObservedVersions);
        Assert.Equal([1, 1], probe.ScheduleDurableCommitCounts);

        var commits = InnerCommitStore(scope.ServiceProvider).ListCommits();
        var resumeCheckpoint = Assert.Single(commits.Where(item => item.Commit.Checkpoint.Name == RuntimeCheckpointNames.BookmarkConsumed));
        Assert.Equal(2, resumeCheckpoint.Commit.StateChanges.PostCommitOutbox.Count);
        var outboxIds = resumeCheckpoint.Commit.StateChanges.PostCommitOutbox
            .Select(change => change.State.OutboxItemId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var workItemIds = resumeCheckpoint.Commit.StateChanges.PostCommitOutbox
            .Select(change => change.State.Intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!.WorkItemId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(outboxIds, externalDelivery.Settlements.Select(item => item.OutboxItemId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(workItemIds, externalDelivery.Settlements.Select(item => item.WorkItemId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(["schedule-3", "schedule-4"], workItemIds);

        var flush = Assert.Single(commits.Where(item => item.Commit.Checkpoint.Name == RuntimeCoalescingMetadataKeys.FlushCheckpointName));
        Assert.Equal(2L, flush.Commit.StateChanges.Scheduler?.State.Version);
        var innerOutbox = scope.ServiceProvider.GetRequiredService<CoalescingInner<IRuntimePostCommitOutboxStore>>().Value;
        var outboxLookup = scope.ServiceProvider.GetRequiredService<CoalescingInner<IPostCommitOutboxLookupStore>>().Value;
        foreach (var outboxId in outboxIds)
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await outboxLookup.FindAsync(outboxId))?.Status);
        Assert.Empty(await innerOutbox.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            DateTimeOffset.MaxValue,
            limit: 10,
            workflowExecutionId: WorkflowExecutionId,
            intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork)));
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task Ordinary_schedule_work_remains_coalesced_until_drain_quiescence()
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false);
        await using var provider = BuildProvider(shared, probe);
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 1));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-2", 2));

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(2, result.DrainedCount);
        Assert.Equal(new long?[] { null, 1 }, probe.ScheduleObservedVersions);
        Assert.Equal(new[] { 0, 0 }, probe.ScheduleDurableCommitCounts);
        var folded = Assert.Single(InnerCommitStore(scope.ServiceProvider).ListCommits());
        Assert.Equal(RuntimeCoalescingMetadataKeys.FlushCheckpointName, folded.Commit.Checkpoint.Name);
        Assert.Equal(2L, folded.Commit.StateChanges.Scheduler?.State.Version);
        Assert.Equal(2L, (await shared.SchedulerStateStore.FindAsync(WorkflowExecutionId))?.Version);
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public async Task Resume_at_queue_head_coalesces_following_work_within_the_requested_budget(int? maxWorkItems)
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false);
        await using var provider = BuildProvider(shared, probe);
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 1));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 2));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-2", 3));

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId, maxWorkItems));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        if (maxWorkItems == 1)
        {
            Assert.Equal("resume-1", Assert.Single(result.Items).WorkItemId);
            Assert.Empty(probe.ScheduleObservedVersions);
            Assert.Empty(InnerCommitStore(scope.ServiceProvider).ListCommits());
            Assert.Equal(["schedule-1", "schedule-2"],
                (await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId))).Select(item => item.WorkItemId));
            return;
        }
        Assert.Equal(["resume-1", "schedule-1", "schedule-2"], result.Items.Select(item => item.WorkItemId));
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Equal(new long?[] { null, 1 }, probe.ScheduleObservedVersions);
        Assert.Equal(new[] { 0, 0 }, probe.ScheduleDurableCommitCounts);
        AssertDurableResumeSnapshot(Assert.Single(probe.ResumeSnapshots), schedulerVersion: null, durableCommitCount: 0);

        var flush = Assert.Single(InnerCommitStore(scope.ServiceProvider).ListCommits());
        Assert.Equal(RuntimeCoalescingMetadataKeys.FlushCheckpointName, flush.Commit.Checkpoint.Name);
        Assert.Equal(2L, flush.Commit.StateChanges.Scheduler?.State.Version);
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task Successful_resume_between_schedule_segments_coalesces_both_segments()
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false);
        await using var provider = BuildProvider(shared, probe);
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 1));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-2", 2));
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 3));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-3", 4));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-4", 5));

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(
            ["schedule-1", "schedule-2", "resume-1", "schedule-3", "schedule-4"],
            result.Items.Select(item => item.WorkItemId));
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Equal(new long?[] { null, 1, 2, 3 }, probe.ScheduleObservedVersions);
        Assert.Equal(new[] { 0, 0, 1, 1 }, probe.ScheduleDurableCommitCounts);
        AssertDurableResumeSnapshot(Assert.Single(probe.ResumeSnapshots), schedulerVersion: 2, durableCommitCount: 1);

        AssertCoalescedVersions(scope.ServiceProvider, 2, 4);
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    [Fact]
    public async Task Repeated_successful_resumes_start_a_fresh_coalesced_segment_each_time()
    {
        var shared = new SharedPersistence(new FakeTimeProvider(Now));
        var probe = new ProbeRecorder(deferFirstResume: false);
        await using var provider = BuildProvider(shared, probe);
        await shared.Queue.EnqueueAsync(ResumeWork("resume-1", 1));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-1", 2));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-2", 3));
        await shared.Queue.EnqueueAsync(ResumeWork("resume-2", 4));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-3", 5));
        await shared.Queue.EnqueueAsync(ScheduleWork("schedule-4", 6));

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(Envelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(
            ["resume-1", "schedule-1", "schedule-2", "resume-2", "schedule-3", "schedule-4"],
            result.Items.Select(item => item.WorkItemId));
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Equal(new long?[] { null, 1, 2, 3 }, probe.ScheduleObservedVersions);
        Assert.Equal(new[] { 0, 0, 1, 1 }, probe.ScheduleDurableCommitCounts);
        Assert.Equal(2, probe.ResumeSnapshots.Count);
        AssertDurableResumeSnapshot(probe.ResumeSnapshots[0], schedulerVersion: null, durableCommitCount: 0, workItemId: "resume-1");
        AssertDurableResumeSnapshot(probe.ResumeSnapshots[1], schedulerVersion: 2, durableCommitCount: 1, workItemId: "resume-2");

        AssertCoalescedVersions(scope.ServiceProvider, 2, 4);
        Assert.Empty(await shared.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)));
    }

    private static void AssertCoalescedVersions(IServiceProvider services, params long[] versions)
    {
        var commits = InnerCommitStore(services).ListCommits();
        Assert.All(commits, item => Assert.Equal(RuntimeCoalescingMetadataKeys.FlushCheckpointName, item.Commit.Checkpoint.Name));
        Assert.Equal(versions.Select(version => (long?)version), commits.Select(item => item.Commit.StateChanges.Scheduler?.State.Version));
    }

    private static void AssertDurableResumeSnapshot(
        ResumeSnapshot snapshot,
        long? schedulerVersion,
        int durableCommitCount,
        string workItemId = "resume-1")
    {
        Assert.Equal(schedulerVersion, snapshot.SchedulerVersion);
        Assert.Equal(durableCommitCount, snapshot.DurableCommitCount);
        Assert.False(snapshot.SessionActive);
        Assert.Equal([workItemId], snapshot.ActiveClaimWorkItemIds);
    }

    private static ServiceProvider BuildProvider(
        SharedPersistence shared,
        ProbeRecorder probe,
        WorkflowDrainOrchestratorOptions? drainOptions = null,
        ExternalOutboxDeliveryProbe? externalOutboxDelivery = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(shared.TimeProvider);
        services.AddSingleton(new RuntimeSchedulerWorkClaimOptions { UnavailableRetryDelay = RetryDelay });
        services.AddSingleton(drainOptions ?? new WorkflowDrainOrchestratorOptions(outboxDeliveryBatchSize: 1));
        services.AddSingleton<IWorkflowSchedulerWorkQueue>(shared.Queue);
        services.AddSingleton<ISchedulerStateStore>(shared.SchedulerStateStore);
        services.AddSingleton(shared.CommitState);
        services.AddSingleton<IRuntimePersistenceModeResolver>(new FixedPersistenceModeResolver(PersistenceMode.Balanced));
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.RemoveAll<IWorkflowSchedulerWorkHandler>();
        services.AddSingleton(probe);
        services.AddScoped<IWorkflowSchedulerWorkHandler, CoalescingProbeHandler>();
        services.AddPersistenceModes();
        if (externalOutboxDelivery is not null)
            ReplaceOutboxProcessorWithExternalDeliveryProbe(services, externalOutboxDelivery);
        return services.BuildServiceProvider();
    }

    private static void ReplaceOutboxProcessorWithExternalDeliveryProbe(
        IServiceCollection services,
        ExternalOutboxDeliveryProbe probe)
    {
        var originalRegistration = services.Single(descriptor => descriptor.ServiceType == typeof(IRuntimePostCommitOutboxProcessor));
        var originalFactory = originalRegistration.ImplementationFactory ??
            throw new InvalidOperationException("The runtime outbox processor must be registered through a factory in this fixture.");
        services.Remove(originalRegistration);
        services.Add(new ServiceDescriptor(
            typeof(IRuntimePostCommitOutboxProcessor),
            serviceProvider =>
            {
                var original = (IRuntimePostCommitOutboxProcessor)(originalFactory(serviceProvider) ??
                    throw new InvalidOperationException("The runtime outbox processor factory returned null."));
                var innerOutbox = serviceProvider.GetRequiredService<CoalescingInner<IRuntimePostCommitOutboxStore>>().Value;
                return new ExternalOutboxDeliveryProcessor(
                    original,
                    serviceProvider.GetRequiredService<IRuntimeCoalescingSessionAccessor>(),
                    innerOutbox as IRuntimePostCommitOutboxClaimStore ??
                        throw new InvalidOperationException("The coalescing probe requires durable outbox claim support."),
                    innerOutbox as IRuntimePostCommitOutboxClaimCompletionStore ??
                        throw new InvalidOperationException("The coalescing probe requires durable outbox completion support."),
                    serviceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>(),
                    serviceProvider.GetRequiredService<CoalescingInner<IWorkflowSchedulerWorkQueue>>().Value,
                    serviceProvider.GetRequiredService<TimeProvider>(),
                    probe);
            },
            originalRegistration.Lifetime));
    }

    private static InMemoryRuntimeCheckpointCommitStore InnerCommitStore(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<CoalescingInner<IRuntimeCheckpointCommitStore>>().Value as InMemoryRuntimeCheckpointCommitStore ??
        throw new InvalidOperationException("The coalescing probe requires the in-memory checkpoint store.");

    private static RuntimeSchedulerWorkItem ScheduleWork(string workItemId, long sequence) => Work(
        workItemId,
        WorkflowExecutionCommandKind.ScheduleActivity,
        sequence);

    private static RuntimeSchedulerWorkItem ResumeWork(string workItemId, long sequence) => Work(
        workItemId,
        WorkflowExecutionCommandKind.ResumeBookmark,
        sequence);

    private static RuntimeSchedulerWorkItem Work(string workItemId, WorkflowExecutionCommandKind kind, long sequence) =>
        new(
            workItemId,
            WorkflowExecutionId,
            $"command-{workItemId}",
            kind,
            $"envelope-{workItemId}",
            $"{WorkflowExecutionId}:{workItemId}",
            Now,
            Now,
            sequence,
            commandMetadata: new Dictionary<string, string>());

    private static WorkflowExecutionCommandEnvelope Envelope()
    {
        var command = new WorkflowExecutionCommand(
            "drain-command",
            WorkflowExecutionId,
            WorkflowExecutionCommandKind.RunSchedulerWork,
            Now,
            Payload: null,
            Metadata: new Dictionary<string, string>());
        return new WorkflowExecutionCommandEnvelope(
            "drain-envelope",
            WorkflowExecutionId,
            command,
            "drain-idempotency",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            Now);
    }

    private sealed class SharedPersistence(FakeTimeProvider timeProvider)
    {
        public FakeTimeProvider TimeProvider { get; } = timeProvider;
        public InMemoryWorkflowSchedulerWorkQueue Queue { get; } = new();
        public InMemorySchedulerStateStore SchedulerStateStore { get; } = new();
        public InMemoryRuntimeCheckpointStoreState CommitState { get; } = new();
    }

    private sealed class ProbeRecorder(
        bool deferFirstResume,
        bool enqueueScheduleSuffixAfterSuccessfulResume = false,
        int scheduleSuffixCount = 2)
    {
        private bool _didDefer;
        private bool _didEnqueueScheduleSuffix;

        public List<long?> ScheduleObservedVersions { get; } = [];
        public List<int> ScheduleDurableCommitCounts { get; } = [];
        public List<ResumeSnapshot> ResumeSnapshots { get; } = [];
        public int ScheduleSuffixCount => scheduleSuffixCount;

        public bool ShouldDeferResume()
        {
            if (!deferFirstResume || _didDefer)
                return false;

            _didDefer = true;
            return true;
        }

        public bool ShouldEnqueueScheduleSuffixAfterSuccessfulResume()
        {
            if (!enqueueScheduleSuffixAfterSuccessfulResume || _didEnqueueScheduleSuffix)
                return false;

            _didEnqueueScheduleSuffix = true;
            return true;
        }
    }

    private sealed record ResumeSnapshot(
        long? SchedulerVersion,
        int DurableCommitCount,
        bool SessionActive,
        IReadOnlyCollection<string> ActiveClaimWorkItemIds);

    private sealed class ExternalOutboxDeliveryProbe
    {
        public List<ExternalOutboxSettlement> Settlements { get; } = [];
    }

    private sealed record ExternalOutboxSettlement(string OutboxItemId, string WorkItemId);

    private sealed class ExternalOutboxDeliveryProcessor(
        IRuntimePostCommitOutboxProcessor inner,
        IRuntimeCoalescingSessionAccessor sessionAccessor,
        IRuntimePostCommitOutboxClaimStore claimStore,
        IRuntimePostCommitOutboxClaimCompletionStore claimCompletionStore,
        IRuntimePostCommitIntentDispatcher intentDispatcher,
        IWorkflowSchedulerWorkQueue innerQueue,
        TimeProvider timeProvider,
        ExternalOutboxDeliveryProbe probe) : IRuntimePostCommitOutboxProcessor
    {
        public async ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            if (sessionAccessor.Current?.IsActive != false)
                return await inner.ProcessAsync(request, cancellationToken);

            var now = timeProvider.GetUtcNow();
            var claims = await claimStore.ClaimAsync(
                new RuntimePostCommitOutboxClaimRequest(
                    "other-continuation-worker",
                    now,
                    RuntimePostCommitOutboxProcessing.ClaimVisibilityTimeout,
                    request.Limit,
                    request.WorkflowExecutionId,
                    request.IntentKind),
                cancellationToken);
            if (claims.Count == 0)
                return await inner.ProcessAsync(request, cancellationToken);

            var results = new List<RuntimePostCommitOutboxProcessedItem>(claims.Count);
            foreach (var claim in claims)
            {
                var schedulerWorkItem = claim.Item.Intent.Payload?.Deserialize<RuntimeSchedulerWorkItem>() ??
                    throw new InvalidOperationException($"Outbox intent '{claim.Item.Intent.IntentId}' has no scheduler-work payload.");
                await intentDispatcher.DispatchAsync(claim.Item.Intent, cancellationToken);
                var durableQueueItems = await innerQueue.ListAsync(
                    new RuntimeSchedulerWorkQuery(claim.Item.Intent.WorkflowExecutionId, limit: 100),
                    cancellationToken);
                if (durableQueueItems.Items.All(item => !StringComparer.Ordinal.Equals(item.WorkItemId, schedulerWorkItem.WorkItemId)))
                    throw new InvalidOperationException($"External continuation delivery did not persist scheduler work '{schedulerWorkItem.WorkItemId}'.");

                var completion = await claimCompletionStore.CompleteClaimAsync(
                    new RuntimePostCommitOutboxClaimCompletion(
                        claim,
                        new RuntimePostCommitOutboxDeliveryResult(
                            claim.OutboxItemId,
                            RuntimePostCommitOutboxStatus.Delivered,
                            timeProvider.GetUtcNow())),
                    cancellationToken);
                if (completion != RuntimePostCommitOutboxClaimCompletionOutcome.Persisted)
                    throw new InvalidOperationException($"External continuation delivery did not complete outbox item '{claim.OutboxItemId}'.");

                probe.Settlements.Add(new ExternalOutboxSettlement(claim.OutboxItemId, schedulerWorkItem.WorkItemId));
                results.Add(new RuntimePostCommitOutboxProcessedItem(
                    claim.OutboxItemId,
                    claim.Item.Intent.IntentId,
                    RuntimePostCommitOutboxStatus.Delivered,
                    FailureMessage: null,
                    Outcome: RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner));
            }

            return new RuntimePostCommitOutboxProcessResult(results);
        }
    }

    private sealed class CoalescingProbeHandler(
        RuntimeCheckpointCommitter committer,
        ISchedulerStateStore schedulerStateStore,
        IRuntimeCoalescingSessionAccessor sessionAccessor,
        CoalescingInner<IWorkflowSchedulerWorkQueue> innerQueue,
        CoalescingInner<IRuntimeCheckpointCommitStore> innerCommitStore,
        TimeProvider timeProvider,
        ProbeRecorder probe) : IWorkflowSchedulerWorkHandler
    {
        private readonly InMemoryRuntimeCheckpointCommitStore _commits =
            innerCommitStore.Value as InMemoryRuntimeCheckpointCommitStore ??
            throw new InvalidOperationException("The coalescing probe requires the in-memory checkpoint store.");

        public string Name => nameof(CoalescingProbeHandler);

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) =>
            workItem.CommandKind is WorkflowExecutionCommandKind.ScheduleActivity or WorkflowExecutionCommandKind.ResumeBookmark;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            if (workItem.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity)
            {
                var current = await schedulerStateStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken);
                probe.ScheduleObservedVersions.Add(current?.Version);
                var nextState = new SchedulerState(workItem.WorkflowExecutionId, (current?.Version ?? 0) + 1);
                await committer.CommitAsync(Commit(workItem, nextState), cancellationToken);
                probe.ScheduleDurableCommitCounts.Add(_commits.ListCommits().Count);
                return;
            }

            var persistedState = await schedulerStateStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken);
            var activeClaims = await (innerQueue.Value as IWorkflowSchedulerWorkClaimInspection ??
                                      throw new InvalidOperationException("The coalescing probe requires claim inspection."))
                .ListActiveClaimsAsync(workItem.WorkflowExecutionId, timeProvider.GetUtcNow(), cancellationToken);
            probe.ResumeSnapshots.Add(new ResumeSnapshot(
                persistedState?.Version,
                _commits.ListCommits().Count,
                sessionAccessor.Current?.IsActive ?? false,
                activeClaims.Select(claim => claim.Item.WorkItemId).ToArray()));

            if (probe.ShouldDeferResume())
                throw new RuntimeSchedulerWorkUnavailableException(
                    "probe.resume-unavailable",
                    "The resume dependency is temporarily unavailable.");

            if (probe.ShouldEnqueueScheduleSuffixAfterSuccessfulResume())
            {
                var suffix = Enumerable.Range(0, probe.ScheduleSuffixCount)
                    .Select(index => ScheduleWork($"schedule-{index + 3}", index + 4))
                    .ToArray();
                var continuationIntents = suffix
                    .Select(schedule => SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(
                        workItem,
                        "activity-resume-1",
                        schedule,
                        Now))
                    .ToArray();
                await committer.CommitAsync(Commit(
                    workItem,
                    state: null,
                    postCommitIntents: continuationIntents,
                    consumeBookmark: true), cancellationToken);
            }
        }

        private static RuntimeCheckpointCommit Commit(
            RuntimeSchedulerWorkItem workItem,
            SchedulerState? state,
            IReadOnlyList<RuntimePostCommitIntent>? postCommitIntents = null,
            bool consumeBookmark = false)
        {
            var checkpointMetadata = consumeBookmark
                ? new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory
                }
                : new Dictionary<string, string>();
            var checkpoint = new RuntimeCheckpoint(
                $"checkpoint-{workItem.WorkItemId}",
                consumeBookmark ? RuntimeCheckpointNames.BookmarkConsumed : "CoalescingProbe",
                workItem.WorkflowExecutionId,
                Now,
                [],
                checkpointMetadata);
            var changes = new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: state is null
                    ? null
                    : new RuntimeStateChange<SchedulerState>(
                        workItem.WorkflowExecutionId,
                        RuntimeStateChangeOperation.Upsert,
                        state,
                        new Dictionary<string, string>()),
                activityExecutions: [],
                bookmarks: consumeBookmark
                    ? [new RuntimeStateChange<BookmarkState>(
                        "bookmark-resume-1",
                        RuntimeStateChangeOperation.Delete,
                        new BookmarkState(
                            "bookmark-resume-1",
                            workItem.WorkflowExecutionId,
                            "activity-resume-1",
                            "resume-node",
                            "resume-target",
                            "stimulus",
                            "stimulus-hash",
                            null,
                            new Dictionary<string, string>(),
                            Now,
                            null),
                        checkpointMetadata)]
                    : [],
                durableValues: [],
                incidents: [],
                operational: []);
            return new RuntimeCheckpointCommit(
                $"commit-{workItem.WorkItemId}",
                checkpoint,
                changes,
                postCommitIntents ?? [],
                new Dictionary<string, string>());
        }
    }
}
