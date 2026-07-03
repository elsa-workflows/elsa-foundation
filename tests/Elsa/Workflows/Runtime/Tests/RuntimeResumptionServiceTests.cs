using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeResumptionServiceTests
{
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
        Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, outboxRequest.IntentKind);
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
        Assert.Equal(WorkflowExecutionAgentActivationReason.Recovery, activation.Reason);
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

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Service.SweepAsync(new RuntimeResumptionSweepRequest(), cancelled.Token).AsTask());
    }

    private static RuntimeRecoveryCandidate NewCandidate(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            operationalStateId: null,
            lastCheckpointId: "checkpoint-1",
            reason: RuntimeInterruptionReason.HostStopped,
            detectedAt: Now,
            requeueFromLastCheckpoint: true);

    private sealed class Harness
    {
        public Harness()
        {
            Service = new RuntimeResumptionService(
                OutboxProcessor,
                WorkQueue,
                RecoveryScanner,
                AgentProvider,
                new GuidRuntimeExecutionIdGenerator(),
                new FixedTimeProvider(Now));
        }

        public FakeOutboxProcessor OutboxProcessor { get; } = new();
        public FakeWorkQueue WorkQueue { get; } = new();
        public FakeRecoveryScanner RecoveryScanner { get; } = new();
        public FakeAgentProvider AgentProvider { get; } = new();
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

        public ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) =>
            new([]);

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

    private sealed class FakeAgentProvider : IWorkflowExecutionAgentProvider
    {
        public List<WorkflowExecutionAgentActivationRequest> Activations { get; } = [];
        public FakeAgent Agent { get; } = new();
        public string? FailFor { get; set; }

        public WorkflowExecutionAgentCapabilities Capabilities => WorkflowExecutionAgentCapabilities.InProcessMailbox;

        public ValueTask<IWorkflowExecutionAgent> GetAgentAsync(WorkflowExecutionAgentActivationRequest request, CancellationToken cancellationToken = default)
        {
            if (string.Equals(request.WorkflowExecutionId, FailFor, StringComparison.Ordinal))
                throw new InvalidOperationException("activation failed");

            Activations.Add(request);
            return new(Agent);
        }

        public ValueTask PassivateAsync(WorkflowExecutionAgentPassivationRequest request, CancellationToken cancellationToken = default) => default;
    }

    private sealed class FakeAgent : IWorkflowExecutionAgent
    {
        public List<WorkflowExecutionCommandEnvelope> Envelopes { get; } = [];
        public WorkflowExecutionCommandDispatchStatus StatusToReturn { get; set; } = WorkflowExecutionCommandDispatchStatus.Accepted;

        public WorkflowExecutionAgentDescriptor Descriptor { get; } = new(
            workflowExecutionId: "wfexec-agent",
            agentId: "agent-1",
            providerName: "test",
            status: WorkflowExecutionAgentStatus.Active,
            capabilities: WorkflowExecutionAgentCapabilities.InProcessMailbox,
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
