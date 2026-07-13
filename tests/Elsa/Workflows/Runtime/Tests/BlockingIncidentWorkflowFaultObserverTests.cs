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
        await harness.SaveActivity("actexec-sequence", ActivityExecutionStatus.Running, activityType: "Elsa.Sequence");
        await harness.SaveActivity("actexec-1", ActivityExecutionStatus.Faulted, parentActivityExecutionId: "actexec-sequence");
        await harness.SaveBlockingIncident();

        await harness.Observer.OnDrainedAsync(harness.Envelope, harness.DrainResult);

        var state = await harness.WorkflowStore.FindAsync("wfexec-1");
        var sequence = await harness.ActivityStore.FindAsync("wfexec-1", "actexec-sequence");
        Assert.NotNull(state);
        Assert.Equal(WorkflowExecutionStatus.Faulted, state!.Status);
        Assert.Equal(_now, state.CompletedAt);
        Assert.NotNull(sequence);
        Assert.Equal(ActivityExecutionStatus.Faulted, sequence!.Status);
        Assert.Equal(_now, sequence.CompletedAt);
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
    public async Task OnDrainedAsync_WithNestedFlowchartAndSequence_FaultsAncestorsButNotSiblingBranches()
    {
        var harness = new Harness(_now);
        await harness.SaveWorkflow(WorkflowExecutionStatus.Running);
        await harness.SaveActivity("actexec-flowchart", ActivityExecutionStatus.Running, activityType: "Elsa.Flowchart");
        await harness.SaveActivity("actexec-sequence", ActivityExecutionStatus.Running, "actexec-flowchart", "Elsa.Sequence");
        await harness.SaveActivity("actexec-1", ActivityExecutionStatus.Faulted, "actexec-sequence");
        await harness.SaveActivity("actexec-waiting-sibling", ActivityExecutionStatus.Waiting, "actexec-flowchart");
        await harness.SaveActivity("actexec-suspended-sibling", ActivityExecutionStatus.Suspended, "actexec-flowchart");
        await harness.SaveBlockingIncident();

        await harness.Observer.OnDrainedAsync(harness.Envelope, harness.DrainResult);

        var flowchart = await harness.ActivityStore.FindAsync("wfexec-1", "actexec-flowchart");
        var sequence = await harness.ActivityStore.FindAsync("wfexec-1", "actexec-sequence");
        var child = await harness.ActivityStore.FindAsync("wfexec-1", "actexec-1");
        var waitingSibling = await harness.ActivityStore.FindAsync("wfexec-1", "actexec-waiting-sibling");
        var suspendedSibling = await harness.ActivityStore.FindAsync("wfexec-1", "actexec-suspended-sibling");

        Assert.Equal(ActivityExecutionStatus.Faulted, flowchart!.Status);
        Assert.Equal(0, flowchart.FaultCount);
        Assert.Equal(1, flowchart.AggregateFaultCount);
        Assert.Equal(ActivityExecutionStatus.Faulted, sequence!.Status);
        Assert.Equal(0, sequence.FaultCount);
        Assert.Equal(1, sequence.AggregateFaultCount);
        Assert.Equal(["incident-1"], child!.IncidentIds);
        Assert.Equal(1, child.FaultCount);
        Assert.Equal(1, child.AggregateFaultCount);
        Assert.Equal(ActivityExecutionStatus.Waiting, waitingSibling!.Status);
        Assert.Null(waitingSibling.CompletedAt);
        Assert.Equal(ActivityExecutionStatus.Suspended, suspendedSibling!.Status);
        Assert.Null(suspendedSibling.CompletedAt);
        var flowchartInspection = await harness.InspectionStore.FindAsync("wfexec-1", "actexec-flowchart");
        var sequenceInspection = await harness.InspectionStore.FindAsync("wfexec-1", "actexec-sequence");
        Assert.Equal(ActivityExecutionStatus.Faulted, flowchartInspection!.Status);
        Assert.Equal(ActivityExecutionStatus.Faulted, sequenceInspection!.Status);

        var commit = Assert.Single(harness.CommitStore.ListCommits()).Commit;
        Assert.Equal(["actexec-flowchart", "actexec-sequence"], commit.Checkpoint.ActivityExecutionIds);
        Assert.Equal(commit.Checkpoint.ActivityExecutionIds, commit.StateChanges.ActivityExecutions.Select(change => change.StateId));
        Assert.Equal(commit.Checkpoint.ActivityExecutionIds, commit.StateChanges.ActivityExecutionInspections.Select(change => change.StateId));
    }

    [Fact]
    public async Task OnDrainedAsync_WhenIncidentActivityIsMissing_StillFaultsWorkflowWithoutActivityChanges()
    {
        var harness = new Harness(_now);
        await harness.SaveWorkflow(WorkflowExecutionStatus.Running);
        await harness.SaveBlockingIncident();

        await harness.Observer.OnDrainedAsync(harness.Envelope, harness.DrainResult);

        Assert.Equal(WorkflowExecutionStatus.Faulted, (await harness.WorkflowStore.FindAsync("wfexec-1"))!.Status);
        var commit = Assert.Single(harness.CommitStore.ListCommits()).Commit;
        Assert.Empty(commit.Checkpoint.ActivityExecutionIds);
        Assert.Empty(commit.StateChanges.ActivityExecutions);
    }

    [Fact]
    public async Task OnDrainedAsync_WithTerminalAncestor_PreservesItAndContinuesToActiveOuterAncestor()
    {
        var harness = new Harness(_now);
        await harness.SaveWorkflow(WorkflowExecutionStatus.Running);
        await harness.SaveActivity("actexec-flowchart", ActivityExecutionStatus.Running, activityType: "Elsa.Flowchart");
        await harness.SaveActivity("actexec-terminal-sequence", ActivityExecutionStatus.Completed, "actexec-flowchart", "Elsa.Sequence");
        await harness.SaveActivity("actexec-1", ActivityExecutionStatus.Faulted, "actexec-terminal-sequence");
        await harness.SaveBlockingIncident();

        await harness.Observer.OnDrainedAsync(harness.Envelope, harness.DrainResult);

        var flowchart = await harness.ActivityStore.FindAsync("wfexec-1", "actexec-flowchart");
        var sequence = await harness.ActivityStore.FindAsync("wfexec-1", "actexec-terminal-sequence");
        Assert.Equal(ActivityExecutionStatus.Faulted, flowchart!.Status);
        Assert.Equal(ActivityExecutionStatus.Completed, sequence!.Status);
        Assert.Equal(["actexec-flowchart"], Assert.Single(harness.CommitStore.ListCommits()).Commit.Checkpoint.ActivityExecutionIds);
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
        public InMemoryActivityExecutionStateStore ActivityStore { get; }
        public InMemoryIncidentStateStore IncidentStore { get; }
        public InMemoryActivityExecutionInspectionStore InspectionStore { get; }
        public InMemoryRuntimeCheckpointCommitStore CommitStore { get; }
        public BlockingIncidentWorkflowFaultObserver Observer { get; }
        public WorkflowExecutionCommandEnvelope Envelope { get; }
        public RuntimeSchedulerDrainResult DrainResult { get; }

        public Harness(DateTimeOffset now)
        {
            _now = now;
            WorkflowStore = new InMemoryWorkflowExecutionStateStore();
            ActivityStore = new InMemoryActivityExecutionStateStore();
            IncidentStore = new InMemoryIncidentStateStore();
            InspectionStore = new InMemoryActivityExecutionInspectionStore();
            CommitStore = new InMemoryRuntimeCheckpointCommitStore(
                WorkflowStore,
                activityExecutionStateStore: ActivityStore,
                incidentStateStore: IncidentStore,
                activityExecutionInspectionWriter: InspectionStore,
                rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
            var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), CommitStore);
            Observer = new BlockingIncidentWorkflowFaultObserver(
                IncidentStore,
                WorkflowStore,
                ActivityStore,
                new RuntimeActivityExecutionInspectionAccumulator(InspectionStore),
                committer,
                new FixedTimeProvider(now));
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

        public ValueTask<ActivityExecutionState> SaveActivity(
            string activityExecutionId,
            ActivityExecutionStatus status,
            string? parentActivityExecutionId = null,
            string activityType = "Elsa.WriteLine") =>
            ActivityStore.SaveAsync(new ActivityExecutionState(
                Execution: new ActivityExecution(
                    activityExecutionId,
                    "wfexec-1",
                    $"node-{activityExecutionId}",
                    $"activity-{activityExecutionId}",
                    activityType,
                    "1"),
                Status: status,
                SubStatus: null,
                ScheduledAt: _now,
                StartedAt: _now,
                CompletedAt: status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Recovered
                    ? _now
                    : null,
                SchedulingActivityExecutionId: parentActivityExecutionId,
                ParentActivityExecutionId: parentActivityExecutionId,
                BranchId: null,
                IterationId: null,
                CallStackDepth: null,
                BookmarkIds: [],
                IncidentIds: status == ActivityExecutionStatus.Faulted ? ["incident-1"] : [],
                FaultCount: status == ActivityExecutionStatus.Faulted ? 1 : 0,
                AggregateFaultCount: status == ActivityExecutionStatus.Faulted ? 1 : 0,
                Metadata: new Dictionary<string, string>()));

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
