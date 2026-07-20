using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeResumptionServiceTests
{
    private const string MarkerKind = "Test.Marker";
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SweepAsync_WithNothingToDo_ReturnsNoWorkResult()
    {
        var harness = new Harness();

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.False(result.DidWork);
        Assert.Equal(0, result.OutboxAttemptedCount);
        Assert.Empty(result.Dispatches);
        Assert.Empty(harness.AgentProvider.Activations);

        var outboxRequest = Assert.Single(harness.OutboxProcessor.Requests);
        Assert.Null(outboxRequest.WorkflowExecutionId);
        Assert.Null(outboxRequest.IntentKind);
    }

    [Fact]
    public async Task SweepAsync_DeliversContributedIntentCommittedThroughRealCheckpointAndOutbox()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddSingleton<Marker>();
        services.AddRuntimePostCommitIntentHandler<MarkerHandler>(MarkerKind);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var scopedProvider = scope.ServiceProvider;
        var intent = NewMarkerIntent();
        var commit = new RuntimeCheckpointCommit(
            CommitId: "commit-marker-1",
            Checkpoint: new RuntimeCheckpoint(
                "checkpoint-marker-1",
                RuntimeCheckpointNames.PostCommitIntentRecorded,
                "wfexec-marker-1",
                Now,
                [],
                new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            PostCommitIntents: [intent],
            Metadata: new Dictionary<string, string>());

        var commitResult = await scopedProvider.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(commit);
        Assert.True(commitResult.Succeeded);
        var store = scopedProvider.GetRequiredService<IRuntimePostCommitOutboxStore>();
        Assert.Single(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10)));

        var marker = scopedProvider.GetRequiredService<Marker>();
        var agentProvider = new FakeAgentProvider();
        var service = new RuntimeResumptionService(
            scopedProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>(),
            new FakeWorkQueue(),
            new FakeRecoveryScanner(),
            agentProvider,
            new ShortRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(Now),
            new InMemoryWorkflowExecutionStateStore());

        var result = await service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.Equal(1, result.OutboxDeliveredCount);
        var deliveredIntent = Assert.Single(marker.Intents);
        Assert.Equal(intent.IntentId, deliveredIntent.IntentId);
        Assert.Equal(intent.Kind, deliveredIntent.Kind);
        Assert.Equal(intent.WorkflowExecutionId, deliveredIntent.WorkflowExecutionId);
        Assert.Equal(intent.ActivityExecutionId, deliveredIntent.ActivityExecutionId);
        Assert.Equal(intent.IdempotencyKey, deliveredIntent.IdempotencyKey);
        Assert.Equal(intent.RecordedAt, deliveredIntent.RecordedAt);
        Assert.Empty(agentProvider.Activations);
        Assert.Empty(agentProvider.Agent.Envelopes);
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10)));
    }

    [Fact]
    public async Task SweepAsync_PropagatesOutboxCountsAndBatchSizes()
    {
        var harness = new Harness();
        harness.OutboxProcessor.Result = new RuntimePostCommitOutboxProcessResult(
        [
            new RuntimePostCommitOutboxProcessedItem("outbox-1", "intent-1", RuntimePostCommitOutboxStatus.Delivered, null),
            new RuntimePostCommitOutboxProcessedItem("outbox-2", "intent-2", RuntimePostCommitOutboxStatus.Delivered, null),
            new RuntimePostCommitOutboxProcessedItem("outbox-3", "intent-3", RuntimePostCommitOutboxStatus.FailedRetryable, "boom")
        ]);

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(outboxBatchSize: 7, backlogBatchSize: 5));

        Assert.True(result.DidWork);
        Assert.Equal(3, result.OutboxAttemptedCount);
        Assert.Equal(2, result.OutboxDeliveredCount);
        Assert.Equal(1, result.OutboxFailedCount);
        Assert.Equal(7, Assert.Single(harness.OutboxProcessor.Requests).Limit);
        Assert.Equal(5, Assert.Single(harness.WorkQueue.BacklogLimits));
    }

    [Fact]
    public async Task SweepAsync_RedrivesBacklogThroughAgentWithRecoveryEnvelope()
    {
        var harness = new Harness();
        harness.WorkQueue.PendingExecutionIds = ["wfexec-1"];

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        var dispatch = Assert.Single(result.Dispatches);
        Assert.Equal("wfexec-1", dispatch.WorkflowExecutionId);
        Assert.Equal(RuntimeResumptionDispatchOutcome.Accepted, dispatch.Outcome);
        Assert.Null(dispatch.Failure);

        var activation = Assert.Single(harness.AgentProvider.Activations);
        Assert.Equal(WorkflowExecutionActorActivationReason.Recovery, activation.Reason);
        Assert.Equal("runtime-resumption", activation.RequestedBy);

        var envelope = Assert.Single(harness.AgentProvider.Agent.Envelopes);
        Assert.Equal("wfexec-1", envelope.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, envelope.Command.Kind);
        Assert.Equal(WorkflowExecutionCommandDeliveryMode.AtLeastOnce, envelope.DeliveryMode);
        Assert.Equal(dispatch.EnvelopeId, envelope.EnvelopeId);
        Assert.StartsWith("runtime-resumption:wfexec-1:", envelope.IdempotencyKey);
        Assert.Equal("runtime-resumption", envelope.Metadata["source"]);
        Assert.Equal("runtime-resumption", envelope.Command.Metadata["source"]);
    }

    [Fact]
    public async Task SweepAsync_UnionsBacklogAndScannerCandidatesWithoutDuplicates()
    {
        var harness = new Harness();
        harness.WorkQueue.PendingExecutionIds = ["wfexec-b", "wfexec-a"];
        harness.RecoveryScanner.Candidates =
        [
            NewCandidate("wfexec-b"),
            NewCandidate("wfexec-c")
        ];

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.Equal(
            new[] { "wfexec-a", "wfexec-b", "wfexec-c" },
            result.Dispatches.Select(dispatch => dispatch.WorkflowExecutionId));

        var scan = Assert.Single(harness.RecoveryScanner.Requests);
        Assert.Equal(Now, scan.Now);
    }

    [Fact]
    public async Task SweepAsync_ExcludesRequestedExecutionIds()
    {
        var harness = new Harness();
        harness.WorkQueue.PendingExecutionIds = ["wfexec-a", "wfexec-b", "wfexec-c"];

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(
            excludedWorkflowExecutionIds: new HashSet<string>(StringComparer.Ordinal) { "wfexec-b" }));

        Assert.Equal(
            new[] { "wfexec-a", "wfexec-c" },
            result.Dispatches.Select(dispatch => dispatch.WorkflowExecutionId));
    }

    [Fact]
    public async Task SweepAsync_CapsExecutionsPerSweep()
    {
        var harness = new Harness();
        harness.WorkQueue.PendingExecutionIds = ["wfexec-a", "wfexec-b", "wfexec-c", "wfexec-d"];

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(maxExecutionsPerSweep: 2));

        // Deterministic ordinal order, capped to the first two.
        Assert.Equal(
            new[] { "wfexec-a", "wfexec-b" },
            result.Dispatches.Select(dispatch => dispatch.WorkflowExecutionId));
    }

    [Fact]
    public async Task SweepAsync_FaultedRedriveIsRecordedAndDoesNotAbortTheSweep()
    {
        var harness = new Harness();
        harness.WorkQueue.PendingExecutionIds = ["wfexec-1", "wfexec-2"];
        harness.AgentProvider.FailFor = "wfexec-1";

        var result = await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest());

        Assert.Collection(
            result.Dispatches,
            first =>
            {
                Assert.Equal("wfexec-1", first.WorkflowExecutionId);
                Assert.Equal(RuntimeResumptionDispatchOutcome.Faulted, first.Outcome);
                Assert.Equal("activation failed", first.Failure);
                Assert.Null(first.EnvelopeId);
            },
            second =>
            {
                Assert.Equal("wfexec-2", second.WorkflowExecutionId);
                Assert.Equal(RuntimeResumptionDispatchOutcome.Accepted, second.Outcome);
            });
    }

    [Fact]
    public async Task SweepAsync_MapsDispatchStatusesToOutcomes()
    {
        var harness = new Harness();
        harness.WorkQueue.PendingExecutionIds = ["wfexec-1"];
        harness.AgentProvider.Agent.StatusToReturn = WorkflowExecutionCommandDispatchStatus.Duplicate;
        Assert.Equal(
            RuntimeResumptionDispatchOutcome.Duplicate,
            Assert.Single((await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest())).Dispatches).Outcome);

        harness.AgentProvider.Agent.StatusToReturn = WorkflowExecutionCommandDispatchStatus.Deferred;
        Assert.Equal(
            RuntimeResumptionDispatchOutcome.Deferred,
            Assert.Single((await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest())).Dispatches).Outcome);

        harness.AgentProvider.Agent.StatusToReturn = WorkflowExecutionCommandDispatchStatus.Rejected;
        var rejected = Assert.Single((await harness.Service.SweepAsync(new RuntimeResumptionSweepRequest())).Dispatches);
        Assert.Equal(RuntimeResumptionDispatchOutcome.Rejected, rejected.Outcome);
        Assert.NotNull(rejected.Failure);
    }

    [Fact]
    public async Task SweepAsync_RejectsInvalidInput()
    {
        var harness = new Harness();

        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Service.SweepAsync(null!).AsTask());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeResumptionSweepRequest(outboxBatchSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeResumptionSweepRequest(backlogBatchSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeResumptionSweepRequest(recoveryScanBatchSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeResumptionSweepRequest(leaseTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeResumptionSweepRequest(heartbeatTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeResumptionSweepRequest(maxExecutionsPerSweep: 0));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(), cancelled.Token).AsTask());
    }

    [Fact]
    public async Task SweepAsync_DiscoversWindowCExecution_FromOwnershipLeaseLeftByCrash()
    {
        // Window C (RT-2 × W2): a drain acquired a single-writer lease and then crashed before releasing it
        // (the item had already been dequeue-deleted, so the durable backlog is empty). W5's lease population is
        // exactly what makes this interrupted execution visible: the real recovery scanner reads the persisted
        // lease from operational state, the sweep discovers it, and re-drives it through the agent mailbox.
        var operationalStore = new InMemoryExecutionLivenessStateStore();
        var leaseDuration = TimeSpan.FromMinutes(1);
        var ownership = new RuntimeExecutionOwnershipService(
            operationalStore,
            new FixedTimeProvider(Now),
            new RuntimeExecutionOwnershipOptions { OwnerId = "owner-under-test", LeaseDuration = leaseDuration });

        // Acquire but never release: this is the crash mid-drain.
        await ownership.AcquireAsync("wfexec-window-c");

        var outboxProcessor = new FakeOutboxProcessor();
        var workQueue = new FakeWorkQueue(); // empty backlog — the item was already dequeue-deleted.
        var recoveryScanner = new InMemoryRuntimeRecoveryScanner(operationalStore);
        var agentProvider = new FakeAgentProvider();
        var afterLeaseExpiry = Now + leaseDuration + TimeSpan.FromSeconds(1);
        var service = new RuntimeResumptionService(
            outboxProcessor,
            workQueue,
            recoveryScanner,
            agentProvider,
            new ShortRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(afterLeaseExpiry),
            new InMemoryWorkflowExecutionStateStore());

        var result = await service.SweepAsync(new RuntimeResumptionSweepRequest(
            leaseTimeout: leaseDuration,
            heartbeatTimeout: leaseDuration));

        var dispatch = Assert.Single(result.Dispatches);
        Assert.Equal("wfexec-window-c", dispatch.WorkflowExecutionId);
        Assert.Equal(RuntimeResumptionDispatchOutcome.Accepted, dispatch.Outcome);

        var activation = Assert.Single(agentProvider.Activations);
        Assert.Equal(WorkflowExecutionActorActivationReason.Recovery, activation.Reason);

        var envelope = Assert.Single(agentProvider.Agent.Envelopes);
        Assert.Equal("wfexec-window-c", envelope.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, envelope.Command.Kind);
    }

    [Fact]
    public async Task SweepAsync_PurgesResidualWorkForTerminalExecutionInsteadOfRedriving()
    {
        // spec 113: the drainer's terminal-status guard strands a RunSchedulerWork item in the durable queue when a
        // workflow reaches a terminal status. Backlog discovery has no terminal filter, so the completed execution is
        // surfaced every sweep. Re-driving it would enqueue yet another stranded item and emit a fresh drain span each
        // tick — perpetual churn. The sweep must instead purge the residue and never re-drive a terminal execution.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(NewResidualWorkItem("wfexec-terminal", 1));
        await queue.EnqueueAsync(NewResidualWorkItem("wfexec-terminal", 2));
        var stateStore = new InMemoryWorkflowExecutionStateStore();
        await stateStore.SaveAsync(NewState("wfexec-terminal", WorkflowExecutionStatus.Completed));
        var agentProvider = new FakeAgentProvider();
        var service = new RuntimeResumptionService(
            new FakeOutboxProcessor(),
            queue,
            new FakeRecoveryScanner(),
            agentProvider,
            new ShortRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(Now),
            stateStore);

        var result = await service.SweepAsync(new RuntimeResumptionSweepRequest());

        // No re-drive: the terminal execution is never activated and no command envelope is sent.
        Assert.Empty(result.Dispatches);
        Assert.Empty(agentProvider.Activations);
        Assert.Empty(agentProvider.Agent.Envelopes);
        // The residual work is purged, so backlog discovery can no longer resurface the execution.
        Assert.Equal(1, result.TerminalExecutionsPurged);
        Assert.Equal(2, result.PurgedWorkItemCount);
        Assert.True(result.DidWork);
        Assert.Empty(await queue.ListPendingWorkflowExecutionIdsAsync(10));
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-terminal"))).Items);
    }

    [Fact]
    public async Task SweepAsync_StillRedrivesNonTerminalExecutionWithBacklog()
    {
        // Regression guard: a suspended/running execution with genuine backlog must still be re-driven (Window C
        // recovery). Only terminal executions are purged; non-terminal ones keep their redelivery.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(NewResidualWorkItem("wfexec-live", 1));
        var stateStore = new InMemoryWorkflowExecutionStateStore();
        await stateStore.SaveAsync(NewState("wfexec-live", WorkflowExecutionStatus.Suspended));
        var agentProvider = new FakeAgentProvider();
        var service = new RuntimeResumptionService(
            new FakeOutboxProcessor(),
            queue,
            new FakeRecoveryScanner(),
            agentProvider,
            new ShortRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(Now),
            stateStore);

        var result = await service.SweepAsync(new RuntimeResumptionSweepRequest());

        var dispatch = Assert.Single(result.Dispatches);
        Assert.Equal("wfexec-live", dispatch.WorkflowExecutionId);
        Assert.Equal(RuntimeResumptionDispatchOutcome.Accepted, dispatch.Outcome);
        Assert.Equal(0, result.TerminalExecutionsPurged);
        Assert.Equal(0, result.PurgedWorkItemCount);
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, Assert.Single(agentProvider.Agent.Envelopes).Command.Kind);
    }

    private static RuntimeSchedulerWorkItem NewResidualWorkItem(string workflowExecutionId, int index) =>
        new(
            workItemId: $"work-{index}",
            workflowExecutionId: workflowExecutionId,
            commandId: $"command-{index}",
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            envelopeId: $"envelope-{index}",
            idempotencyKey: $"{workflowExecutionId}:command-{index}",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: index);

    private static WorkflowExecutionState NewState(string workflowExecutionId, WorkflowExecutionStatus status) =>
        new(
            WorkflowExecutionId: workflowExecutionId,
            PinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            Status: status,
            SubStatus: null,
            CreatedAt: Now,
            StartedAt: Now,
            UpdatedAt: Now,
            CompletedAt: status.IsTerminal() ? Now : null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());

    private static RuntimeRecoveryCandidate NewCandidate(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            operationalStateId: null,
            lastCheckpointId: "checkpoint-1",
            reason: RuntimeInterruptionReason.HostStopped,
            detectedAt: Now,
            requeueFromLastCheckpoint: true);

    private static RuntimePostCommitIntent NewMarkerIntent() =>
        new(
            intentId: "intent-marker-1",
            workflowExecutionId: "wfexec-marker-1",
            kind: MarkerKind,
            recordedAt: Now,
            activityExecutionId: "actexec-marker-1",
            idempotencyKey: "marker-1",
            payload: null,
            metadata: new Dictionary<string, string>());

    private sealed class Marker
    {
        public List<RuntimePostCommitIntent> Intents { get; } = [];
    }

    private sealed class MarkerHandler(Marker marker) : IRuntimePostCommitIntentHandler
    {
        public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
        {
            marker.Intents.Add(intent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Harness
    {
        public Harness()
        {
            Service = new RuntimeResumptionService(
                OutboxProcessor,
                WorkQueue,
                RecoveryScanner,
                AgentProvider,
                new ShortRuntimeExecutionIdGenerator(),
                new FixedTimeProvider(Now),
                StateStore);
        }

        public FakeOutboxProcessor OutboxProcessor { get; } = new();
        public FakeWorkQueue WorkQueue { get; } = new();
        public FakeRecoveryScanner RecoveryScanner { get; } = new();
        public FakeAgentProvider AgentProvider { get; } = new();
        public InMemoryWorkflowExecutionStateStore StateStore { get; } = new();
        public RuntimeResumptionService Service { get; }
    }

    private sealed class FakeOutboxProcessor : IRuntimePostCommitOutboxProcessor
    {
        public List<RuntimePostCommitOutboxProcessRequest> Requests { get; } = [];
        public RuntimePostCommitOutboxProcessResult Result { get; set; } = new([]);

        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(RuntimePostCommitOutboxProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return new(Result);
        }
    }

    private sealed class FakeWorkQueue : IWorkflowSchedulerWorkQueue
    {
        public IReadOnlyCollection<string> PendingExecutionIds { get; set; } = [];
        public List<int> BacklogLimits { get; } = [];

        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) =>
            new(new RuntimeStorePage<RuntimeSchedulerWorkItem>(query, []));

        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            new((RuntimeSchedulerWorkItem?)null);

        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default)
        {
            BacklogLimits.Add(limit);
            return new(PendingExecutionIds);
        }
    }

    private sealed class FakeRecoveryScanner : IRuntimeRecoveryScanner
    {
        public IReadOnlyCollection<RuntimeRecoveryCandidate> Candidates { get; set; } = [];
        public List<RuntimeRecoveryScanRequest> Requests { get; } = [];

        public ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ScanAsync(RuntimeRecoveryScanRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return new(Candidates);
        }
    }

    private sealed class FakeAgentProvider : IWorkflowExecutionActorProvider
    {
        public List<WorkflowExecutionActorActivationRequest> Activations { get; } = [];
        public FakeAgent Agent { get; } = new();
        public string? FailFor { get; set; }

        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.InProcessMailbox;

        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default)
        {
            if (string.Equals(request.WorkflowExecutionId, FailFor, StringComparison.Ordinal))
                throw new InvalidOperationException("activation failed");

            Activations.Add(request);
            return new(Agent);
        }

        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) => default;
    }

    private sealed class FakeAgent : IWorkflowExecutionActor
    {
        public List<WorkflowExecutionCommandEnvelope> Envelopes { get; } = [];
        public WorkflowExecutionCommandDispatchStatus StatusToReturn { get; set; } = WorkflowExecutionCommandDispatchStatus.Accepted;

        public WorkflowExecutionActorDescriptor Descriptor { get; } = new(
            workflowExecutionId: "wfexec-agent",
            agentId: "agent-1",
            providerName: "test",
            status: WorkflowExecutionActorStatus.Active,
            capabilities: WorkflowExecutionActorCapabilities.InProcessMailbox,
            activatedAt: Now);

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return new(new WorkflowExecutionCommandDispatchResult(
                envelope.EnvelopeId,
                envelope.WorkflowExecutionId,
                StatusToReturn,
                Now,
                reason: StatusToReturn is WorkflowExecutionCommandDispatchStatus.Rejected or WorkflowExecutionCommandDispatchStatus.Deferred
                    ? "set by test"
                    : null));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
