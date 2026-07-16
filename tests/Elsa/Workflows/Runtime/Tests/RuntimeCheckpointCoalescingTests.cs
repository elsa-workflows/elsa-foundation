using System.Text.Json;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workflows.Runtime.Tests;

// End-to-end coverage for the opt-in burst-coalescing checkpoint persistence policy (W9, findings E3-6/RT-10).
// A straight-line workflow is driven to completion through the in-process agent under the default (Immediate) policy
// and again under the coalescing policy over the same in-memory durable substrate; the two runs must reach identical
// terminal state while coalescing performs strictly fewer durable checkpoint commits (Elsa-3-style burst folding).
public sealed class RuntimeCheckpointCoalescingTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddCoalescingRuntimeCheckpointPersistence_SelectsCoalescingPolicyAndDecoratesStores()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddCoalescingRuntimeCheckpointPersistence();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<CoalescingRuntimeCheckpointPersistencePolicy>(provider.GetRequiredService<IRuntimeCheckpointPersistencePolicy>());
        Assert.IsType<CoalescingRuntimeCheckpointCommitStore>(provider.GetRequiredService<IRuntimeCheckpointCommitStore>());
        Assert.IsType<CoalescingWorkflowSchedulerWorkQueue>(provider.GetRequiredService<IWorkflowSchedulerWorkQueue>());
        Assert.IsType<CoalescingRuntimePostCommitOutboxStore>(provider.GetRequiredService<IRuntimePostCommitOutboxStore>());
        Assert.IsType<CoalescingWorkflowExecutionStateStore>(provider.GetRequiredService<IWorkflowExecutionStateStore>());
        Assert.IsType<CoalescingActivityExecutionStateStore>(provider.GetRequiredService<IActivityExecutionStateStore>());
        Assert.IsType<CoalescingDurableValueStateStore>(provider.GetRequiredService<IDurableValueStateStore>());
        Assert.IsType<CoalescingSchedulerStateStore>(provider.GetRequiredService<ISchedulerStateStore>());
        Assert.NotNull(provider.GetRequiredService<IRuntimeCoalescingSessionAccessor>());
        Assert.NotNull(provider.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>());
    }

    [Fact]
    public async Task Coalescing_workflow_store_preserves_bounded_history_queries()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddCoalescingRuntimeCheckpointPersistence();

        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IWorkflowExecutionStateStore>();
        await store.SaveAsync(new WorkflowExecutionState(
            "wfexec-history",
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            WorkflowExecutionStatus.Completed,
            null,
            Now.AddMinutes(-2),
            Now.AddMinutes(-1),
            Now,
            Now,
            null,
            null,
            "tenant-1",
            new Dictionary<string, string>()));

        var page = await store.QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 10));

        Assert.Equal("wfexec-history", Assert.Single(page.Items).WorkflowExecutionId);
    }

    // W8's Delay is the first real suspending activity: it writes a durable timer (via IDurableTimerStore) and
    // creates a bookmark, then W8's background timer pump resumes off the DURABLE timer + bookmark stores at due
    // time. Coalescing decorates only the seven core checkpoint stores, so neither the durable-timer store nor the
    // bookmark store is ever wrapped by the buffer — even with both features composed. A Delay suspension's timer
    // and bookmark are therefore durable the instant they are written (before quiescence ends), so the pump can
    // never race an in-memory-only bookmark/timer. Proven here against W8's landed IDurableTimerStore surface.
    [Fact]
    public void Coalescing_DoesNotDecorateDurableTimerOrBookmarkStores_SoDelaySuspensionStaysDurable()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddSingleton<IDurableTimerStore, InMemoryDurableTimerStore>();
        services.AddCoalescingRuntimeCheckpointPersistence();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryDurableTimerStore>(provider.GetRequiredService<IDurableTimerStore>());
        Assert.IsType<InMemoryBookmarkStateStore>(provider.GetRequiredService<IBookmarkStateStore>());
    }

    [Fact]
    public void WithoutOptIn_KeepsImmediatePolicyAndUndecoratedStores()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<ImmediateRuntimeCheckpointPersistencePolicy>(provider.GetRequiredService<IRuntimeCheckpointPersistencePolicy>());
        Assert.IsType<InMemoryWorkflowSchedulerWorkQueue>(provider.GetRequiredService<IWorkflowSchedulerWorkQueue>());
        Assert.Null(provider.GetService<IRuntimeCoalescingSessionAccessor>());
        Assert.Null(provider.GetService<IRuntimeCoalescingDrainScopeFactory>());
    }

    [Fact]
    public async Task Coalescing_ReachesSameTerminalStateWithFewerCommitsThanImmediate()
    {
        var immediate = await DriveAsync(coalescing: false);
        var coalescing = await DriveAsync(coalescing: true);

        output.WriteLine($"Immediate durable checkpoint commits: {immediate.CommitCount}");
        output.WriteLine($"Coalescing durable checkpoint commits: {coalescing.CommitCount}");

        // Behavior parity: identical terminal activity-execution snapshot.
        Assert.Equal(immediate.Snapshot, coalescing.Snapshot);
        Assert.NotEmpty(coalescing.Snapshot);

        // Burst folding: coalescing performs strictly fewer durable commits, converging toward Elsa 3's one-per-burst.
        Assert.True(coalescing.CommitCount < immediate.CommitCount,
            $"Expected coalescing ({coalescing.CommitCount}) < immediate ({immediate.CommitCount}).");
        Assert.Equal(1, coalescing.CommitCount);
    }

    [Fact]
    public async Task CrashMidSegment_DurableQueueStillHoldsSegmentEntry_AndNoPartialCheckpointPersisted()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddCoalescingRuntimeCheckpointPersistence();
        // Crash injection: the post-commit outbox processor throws inside the drain loop, after the first hops have been
        // buffered into the in-memory working set but before the quiescence flush lands. This models a process crash
        // mid-segment: nothing durable has been written and the segment-entry command is still in the durable queue.
        services.AddSingleton<IRuntimePostCommitOutboxProcessor>(new ThrowingOutboxProcessor());

        using var provider = services.BuildServiceProvider();
        await SeedAsync(provider);

        await Assert.ThrowsAnyAsync<Exception>(() => EnqueueStartAsync(provider).AsTask());

        // Condition B: the durable scheduler queue never advanced past the last flushed state — the segment-entry
        // command is still present because the coalescing buffer is only dequeued as part of the (never-reached) flush.
        var innerQueue = provider.GetRequiredService<CoalescingInner<IWorkflowSchedulerWorkQueue>>().Value;
        var pending = await innerQueue.ListPendingWorkflowExecutionIdsAsync(10);
        Assert.Contains("wfexec-1", pending);

        // Nothing partial persisted: the durable checkpoint store recorded no commit for the crashed segment.
        var innerStore = provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        Assert.Empty(innerStore.ListCommits());
    }

    [Fact]
    public async Task Coalescing_BookmarkSuspend_FlushesDurableBookmarkImmediately()
    {
        // A bookmark-suspend is a mandatory flush boundary: under coalescing the BookmarkCreated checkpoint must still
        // land in the DURABLE bookmark store within the segment, so a durable timer/stimulus pump (which reads the
        // durable bookmark store) can never race an in-memory-only bookmark. Delay-style suspensions rely on this.
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddCoalescingRuntimeCheckpointPersistence();

        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(NewExecutableWithResumeTarget());
        await provider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(NewRunningActivityState());

        var agentProvider = provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        var agent = await agentProvider.GetAgentAsync(NewSchedulerWorkActivationRequest("wfexec-1"));
        await agent.EnqueueAsync(NewCreateBookmarkEnvelope());

        // The bookmark is durable (flushed at the suspend boundary, not left buffered in-memory).
        var bookmark = await provider.GetRequiredService<IBookmarkStateStore>().FindAsync("wfexec-1", "bookmark-1");
        Assert.NotNull(bookmark);
        Assert.Equal("delivery-status", bookmark!.StimulusType);
        Assert.Equal("sha256:delivery-status:order-123", bookmark.StimulusHash);

        // The activity durably transitioned to Suspended.
        var state = await provider.GetRequiredService<IActivityExecutionStateStore>().FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Suspended, state!.Status);

        // Exactly one durable commit landed and it is the BookmarkCreated boundary — coalescing did not defer it.
        var commit = Assert.Single(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        Assert.Equal(RuntimeCheckpointNames.BookmarkCreated, commit.Commit.Checkpoint.Name);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(50)]
    public async Task Coalescing_FlushesAndDeactivatesWhenLongSegmentExceedsConfiguredCap(int cap)
    {
        var innerQueue = new InMemoryWorkflowSchedulerWorkQueue();
        var innerStore = new InMemoryRuntimeCheckpointCommitStore();
        var session = new RuntimeCoalescingSession(
            "wfexec-cap",
            innerQueue,
            new CoalescingRuntimeCheckpointPersistenceOptions { MaxSegmentCheckpoints = cap });
        var accessor = new FixedCoalescingSessionAccessor(session);
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(innerStore),
            accessor);

        Assert.Same(session, accessor.Current);
        Assert.True(session.AppliesTo("wfexec-cap"));

        for (var checkpoint = 1; checkpoint <= cap; checkpoint++)
            await store.CommitAsync(NewEmptyDeferredCommit(checkpoint), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Equal(cap, session.HopCount);
        Assert.True(session.IsActive);
        Assert.Empty(innerStore.ListCommits());

        await store.CommitAsync(NewEmptyDeferredCommit(cap + 1), new(RuntimeCheckpointPersistenceMode.Deferred));

        Assert.Equal(0, session.HopCount);
        Assert.False(session.IsActive);
        Assert.Single(innerStore.ListCommits());
    }

    private static async Task<(IReadOnlyList<(string NodeId, ActivityExecutionStatus Status)> Snapshot, int CommitCount)> DriveAsync(bool coalescing)
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        if (coalescing)
            services.AddCoalescingRuntimeCheckpointPersistence();

        using var provider = services.BuildServiceProvider();
        await SeedAsync(provider);
        await EnqueueStartAsync(provider);

        var snapshot = await SnapshotAsync(provider);
        var commitCount = provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits().Count;
        return (snapshot, commitCount);
    }

    private static async Task SeedAsync(ServiceProvider provider)
    {
        var store = provider.GetRequiredService<IWorkflowExecutableStore>();
        await store.SaveAsync(NewExecutable());
    }

    private static async ValueTask EnqueueStartAsync(ServiceProvider provider)
    {
        var executable = NewExecutable();
        var agentProvider = provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        var agent = await agentProvider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        await agent.EnqueueAsync(NewStartEnvelope(executable.Identity));
    }

    private static async Task<IReadOnlyList<(string NodeId, ActivityExecutionStatus Status)>> SnapshotAsync(ServiceProvider provider)
    {
        var stateStore = provider.GetRequiredService<IActivityExecutionStateStore>();
        var states = await stateStore.ListAsync("wfexec-1");
        return states
            .Select(state => (state.Execution.ExecutableNodeId, state.Status))
            .OrderBy(entry => entry.ExecutableNodeId, StringComparer.Ordinal)
            .ToList();
    }

    private static WorkflowExecutionActorActivationRequest NewActivationRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.Start,
            requestedAt: Now,
            requestedBy: "runtime-test",
            requiredCapabilities: WorkflowExecutionActorCapabilities.InProcessMailbox);

    private static WorkflowExecutionCommandEnvelope NewStartEnvelope(WorkflowExecutableIdentity pinnedExecutable)
    {
        var payload = new WorkflowExecutionStartCommandPayload(pinnedExecutable, pinnedExecutable.ArtifactId);
        var command = new WorkflowExecutionCommand(
            CommandId: "command-start",
            WorkflowExecutionId: "wfexec-1",
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: Now,
            Payload: JsonSerializer.SerializeToElement(payload),
            Metadata: new Dictionary<string, string> { ["source"] = "test" });

        return new(
            envelopeId: "envelope-start",
            workflowExecutionId: "wfexec-1",
            command: command,
            idempotencyKey: "wfexec-1:start:artifact-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now,
            sequence: 1,
            metadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private static RuntimeCheckpointCommit NewEmptyDeferredCommit(int checkpoint) =>
        new(
            CommitId: $"commit-cap-{checkpoint}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint-cap-{checkpoint}",
                Name: "CapProbe",
                WorkflowExecutionId: "wfexec-cap",
                OccurredAt: Now.AddTicks(checkpoint),
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
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

    private static WorkflowExecutionActorActivationRequest NewSchedulerWorkActivationRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.SchedulerWork,
            requestedAt: Now,
            requestedBy: "runtime-test",
            requiredCapabilities: WorkflowExecutionActorCapabilities.InProcessMailbox);

    private static WorkflowExecutionCommandEnvelope NewCreateBookmarkEnvelope()
    {
        var payload = JsonSerializer.SerializeToElement(new RuntimeCreateBookmarkCommandPayload(
            pinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            bookmarkId: "bookmark-1",
            activityExecutionId: "actexec-1",
            executableNodeId: "node-wait",
            resumeTargetId: "resume-target:delivery",
            stimulusType: "delivery-status",
            stimulusHash: "sha256:delivery-status:order-123",
            payload: JsonSerializer.SerializeToElement(new { orderId = "order-123" }),
            expiresAt: Now.AddMinutes(30),
            reason: RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason,
            metadata: new Dictionary<string, string> { ["customer"] = "northwind" },
            valueSnapshots: []));

        var command = new WorkflowExecutionCommand(
            CommandId: "command-create-bookmark",
            WorkflowExecutionId: "wfexec-1",
            Kind: WorkflowExecutionCommandKind.CreateBookmark,
            EnqueuedAt: Now,
            Payload: payload,
            Metadata: new Dictionary<string, string> { ["source"] = "test" });

        return new(
            envelopeId: "envelope-create-bookmark",
            workflowExecutionId: "wfexec-1",
            command: command,
            idempotencyKey: "wfexec-1:create-bookmark:bookmark-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now,
            sequence: 1,
            metadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private static ActivityExecutionState NewRunningActivityState() =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-1",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-wait",
                AuthoredActivityId: "authored-node-wait",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: Now.AddMinutes(-3),
            StartedAt: Now.AddMinutes(-2),
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static WorkflowExecutable NewExecutableWithResumeTarget()
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var node = new ExecutableNode(
            executableNodeId: "node-wait",
            authoredActivityId: "authored-node-wait",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

        return new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                ["resume-target:delivery"] = new(
                    ResumeTargetId: "resume-target:delivery",
                    ExecutableNodeId: "node-wait",
                    HandlerKey: "test-handler",
                    Metadata: new Dictionary<string, string>())
            },
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static WorkflowExecutable NewExecutable()
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var node = new ExecutableNode(
            executableNodeId: "node-start",
            authoredActivityId: "authored-node-start",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

        return new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private sealed class ThrowingOutboxProcessor : IRuntimePostCommitOutboxProcessor
    {
        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected crash before quiescence flush.");
    }

    private sealed class FixedCoalescingSessionAccessor(RuntimeCoalescingSession session) : IRuntimeCoalescingSessionAccessor
    {
        public RuntimeCoalescingSession? Current => session;

        public IDisposable Push(RuntimeCoalescingSession? pushedSession) =>
            throw new NotSupportedException("The cap test provides a fixed ambient session.");
    }
}
