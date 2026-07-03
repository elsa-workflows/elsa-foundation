using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Guardrails for RT-1 gap a / RT-5: a workflow with a blocking incident must transition out of Running to
/// <see cref="WorkflowExecutionStatus.Faulted"/> so it is observable, and the transition must be idempotent and
/// only apply to non-terminal workflows.
/// </summary>
public sealed class BlockingIncidentWorkflowFaultObserverTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OnDrainedAsync_WithBlockingIncidentOnRunningWorkflow_CommitsFaultedStatus()
    {
        var harness = new Harness(_now);
        await harness.SaveWorkflow(WorkflowExecutionStatus.Running);
        await harness.SaveBlockingIncident();

        await harness.Observer.OnDrainedAsync(harness.Envelope, harness.DrainResult);

        var state = await harness.WorkflowStore.FindAsync("wfexec-1");
        Assert.NotNull(state);
        Assert.Equal(WorkflowExecutionStatus.Faulted, state!.Status);
        Assert.Equal(_now, state.CompletedAt);
    }

    [Fact]
    public async Task OnDrainedAsync_WithoutBlockingIncident_LeavesWorkflowRunning()
    {
        var harness = new Harness(_now);
        await harness.SaveWorkflow(WorkflowExecutionStatus.Running);

        await harness.Observer.OnDrainedAsync(harness.Envelope, harness.DrainResult);

        var state = await harness.WorkflowStore.FindAsync("wfexec-1");
        Assert.Equal(WorkflowExecutionStatus.Running, state!.Status);
    }

    [Fact]
    public async Task OnDrainedAsync_WithTerminalWorkflow_DoesNotOverwriteStatus()
    {
        var harness = new Harness(_now);
        await harness.SaveWorkflow(WorkflowExecutionStatus.Completed);
        await harness.SaveBlockingIncident();

        await harness.Observer.OnDrainedAsync(harness.Envelope, harness.DrainResult);

        var state = await harness.WorkflowStore.FindAsync("wfexec-1");
        Assert.Equal(WorkflowExecutionStatus.Completed, state!.Status);
    }

    [Fact]
    public async Task OnDrainedAsync_WhenAlreadyFaulted_IsIdempotent()
    {
        var harness = new Harness(_now);
        await harness.SaveWorkflow(WorkflowExecutionStatus.Running);
        await harness.SaveBlockingIncident();

        await harness.Observer.OnDrainedAsync(harness.Envelope, harness.DrainResult);
        await harness.Observer.OnDrainedAsync(harness.Envelope, harness.DrainResult);

        var state = await harness.WorkflowStore.FindAsync("wfexec-1");
        Assert.Equal(WorkflowExecutionStatus.Faulted, state!.Status);
    }

    private sealed class Harness
    {
        private readonly DateTimeOffset _now;
        public InMemoryWorkflowExecutionStateStore WorkflowStore { get; }
        public InMemoryIncidentStateStore IncidentStore { get; }
        public BlockingIncidentWorkflowFaultObserver Observer { get; }
        public WorkflowExecutionCommandEnvelope Envelope { get; }
        public RuntimeSchedulerDrainResult DrainResult { get; }

        public Harness(DateTimeOffset now)
        {
            _now = now;
            WorkflowStore = new InMemoryWorkflowExecutionStateStore();
            IncidentStore = new InMemoryIncidentStateStore();
            var commitStore = new InMemoryRuntimeCheckpointCommitStore(WorkflowStore, null, null, null, IncidentStore);
            var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), commitStore);
            Observer = new BlockingIncidentWorkflowFaultObserver(IncidentStore, WorkflowStore, committer, new FixedTimeProvider(now));
            Envelope = NewEnvelope();
            DrainResult = new RuntimeSchedulerDrainResult("wfexec-1", now, now, []);
        }

        public ValueTask<WorkflowExecutionState> SaveWorkflow(WorkflowExecutionStatus status) =>
            WorkflowStore.SaveAsync(new WorkflowExecutionState(
                WorkflowExecutionId: "wfexec-1",
                PinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
                Status: status,
                SubStatus: null,
                CreatedAt: _now,
                StartedAt: _now,
                UpdatedAt: _now,
                CompletedAt: status.IsTerminal() ? _now : null,
                CorrelationId: null,
                ParentWorkflowExecutionId: null,
                TenantId: null,
                SystemMetadata: new Dictionary<string, string>()));

        public ValueTask<bool> SaveBlockingIncident() =>
            IncidentStore.TryAddAsync(new IncidentState(
                incidentId: "incident-1",
                workflowExecutionId: "wfexec-1",
                activityExecutionId: "actexec-1",
                executableNodeId: "node-1",
                severity: IncidentSeverity.Error,
                status: IncidentStatus.Blocking,
                resolutionAction: IncidentResolutionAction.WaitForIntervention,
                failureType: "System.InvalidOperationException",
                message: "boom",
                createdAt: _now,
                resolvedAt: null));

        private WorkflowExecutionCommandEnvelope NewEnvelope()
        {
            var command = new WorkflowExecutionCommand(
                CommandId: "command-1",
                WorkflowExecutionId: "wfexec-1",
                Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
                EnqueuedAt: _now,
                Payload: null,
                Metadata: new Dictionary<string, string>());

            return new(
                envelopeId: "envelope-1",
                workflowExecutionId: "wfexec-1",
                command: command,
                idempotencyKey: "wfexec-1:command-1",
                deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
                enqueuedAt: _now,
                sequence: 1,
                metadata: new Dictionary<string, string>());
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
