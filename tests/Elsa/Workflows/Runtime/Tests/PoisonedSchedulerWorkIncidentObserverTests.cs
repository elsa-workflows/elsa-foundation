using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Guardrails for dispatch-fault surfacing: a scheduler work item parked in the poison store must become a
/// blocking incident (visible on the incidents API) and — through the blocking-incident fault observer running
/// next in the chain — transition the workflow execution to <see cref="WorkflowExecutionStatus.Faulted"/>,
/// instead of leaving the instance silently stuck in Running.
/// </summary>
public sealed class PoisonedSchedulerWorkIncidentObserverTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    private readonly Harness _harness;

    public PoisonedSchedulerWorkIncidentObserverTests() => _harness = new(_now);

    [Fact]
    public async Task OnDrainedAsync_WithPoisonedRecordAndFaultedDrain_CommitsBlockingIncident()
    {
        await _harness.RecordPoison(RuntimeSchedulerPoisonDisposition.Poisoned);

        await _harness.Observer.OnDrainedAsync(_harness.Envelope, _harness.FaultedDrainResult);

        var incident = await _harness.IncidentStore.FindAsync("wfexec-1", PoisonedSchedulerWorkIncidentObserver.IncidentId("workitem-1"));
        Assert.NotNull(incident);
        Assert.Equal(IncidentStatus.Blocking, incident!.Status);
        Assert.Equal(IncidentSeverity.Critical, incident.Severity);
        Assert.Equal(IncidentResolutionAction.WaitForIntervention, incident.ResolutionAction);
        Assert.Equal(PoisonedSchedulerWorkIncidentObserver.IncidentFailureType, incident.FailureType);
        Assert.Null(incident.ActivityExecutionId);
        Assert.Contains("workitem-1", incident.Message);
        Assert.Contains("System.InvalidOperationException: dispatch exploded", incident.Message);
        Assert.Equal("workitem-1", incident.Metadata[RuntimeMetadataKeys.SchedulerWorkItemId]);
        Assert.Equal(nameof(WorkflowSchedulerDrainer), incident.Metadata[RuntimeMetadataKeys.SchedulerPoisonHandlerName]);
        Assert.Equal("1", incident.Metadata[RuntimeMetadataKeys.SchedulerPoisonFailureCount]);
        Assert.Equal("System.InvalidOperationException", incident.Metadata[RuntimeMetadataKeys.FaultType]);

        var commit = Assert.Single(_harness.CommitStore.ListCommits()).Commit;
        Assert.Equal(RuntimeCheckpointNames.IncidentRecorded, commit.Checkpoint.Name);
        Assert.Equal(incident.IncidentId, Assert.Single(commit.StateChanges.Incidents).StateId);
    }

    [Fact]
    public async Task OnDrainedAsync_WithRetryScheduledRecord_RecordsNoIncident()
    {
        await _harness.RecordPoison(RuntimeSchedulerPoisonDisposition.RetryScheduled, nextRetryAt: _now.AddMinutes(1));

        await _harness.Observer.OnDrainedAsync(_harness.Envelope, _harness.FaultedDrainResult);

        Assert.Empty(await _harness.IncidentStore.ListAsync("wfexec-1"));
        Assert.Empty(_harness.CommitStore.ListCommits());
    }

    [Fact]
    public async Task OnDrainedAsync_WithoutFaultedDrainItem_RecordsNoIncident()
    {
        await _harness.RecordPoison(RuntimeSchedulerPoisonDisposition.Poisoned);

        await _harness.Observer.OnDrainedAsync(_harness.Envelope, _harness.FaultFreeDrainResult);

        Assert.Empty(await _harness.IncidentStore.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task OnDrainedAsync_WhenIncidentAlreadyExists_DoesNotOverwriteOrRecommit()
    {
        await _harness.RecordPoison(RuntimeSchedulerPoisonDisposition.Poisoned);

        await _harness.Observer.OnDrainedAsync(_harness.Envelope, _harness.FaultedDrainResult);
        await _harness.Observer.OnDrainedAsync(_harness.Envelope, _harness.FaultedDrainResult);

        Assert.Single(await _harness.IncidentStore.ListAsync("wfexec-1"));
        Assert.Single(_harness.CommitStore.ListCommits());
    }

    [Fact]
    public async Task OnDrainedAsync_WhenIncidentWasResolved_DoesNotResurrectIt()
    {
        await _harness.RecordPoison(RuntimeSchedulerPoisonDisposition.Poisoned);
        var incidentId = PoisonedSchedulerWorkIncidentObserver.IncidentId("workitem-1");
        await _harness.IncidentStore.SaveAsync(new IncidentState(
            incidentId: incidentId,
            workflowExecutionId: "wfexec-1",
            activityExecutionId: null,
            executableNodeId: null,
            severity: IncidentSeverity.Critical,
            status: IncidentStatus.Resolved,
            resolutionAction: IncidentResolutionAction.None,
            failureType: PoisonedSchedulerWorkIncidentObserver.IncidentFailureType,
            message: "resolved by operator",
            createdAt: _now.AddMinutes(-5),
            resolvedAt: _now.AddMinutes(-1)));

        await _harness.Observer.OnDrainedAsync(_harness.Envelope, _harness.FaultedDrainResult);

        var incident = await _harness.IncidentStore.FindAsync("wfexec-1", incidentId);
        Assert.Equal(IncidentStatus.Resolved, incident!.Status);
        Assert.Empty(_harness.CommitStore.ListCommits());
    }

    [Fact]
    public async Task ObserverChain_PoisonedRecord_TransitionsWorkflowToFaulted()
    {
        await _harness.SaveWorkflow(WorkflowExecutionStatus.Running);
        await _harness.RecordPoison(RuntimeSchedulerPoisonDisposition.Poisoned);

        await _harness.Observer.OnDrainedAsync(_harness.Envelope, _harness.FaultedDrainResult);
        await _harness.FaultObserver.OnDrainedAsync(_harness.Envelope, _harness.FaultedDrainResult);

        var state = await _harness.WorkflowStore.FindAsync("wfexec-1");
        Assert.Equal(WorkflowExecutionStatus.Faulted, state!.Status);
        Assert.Equal(_now, state.CompletedAt);
    }

    private sealed class Harness
    {
        private readonly DateTimeOffset _now;
        public InMemoryWorkflowSchedulerPoisonStore PoisonStore { get; } = new();
        public InMemoryIncidentStateStore IncidentStore { get; } = new();
        public InMemoryWorkflowExecutionStateStore WorkflowStore { get; } = new();
        public InMemoryRuntimeCheckpointCommitStore CommitStore { get; }
        public PoisonedSchedulerWorkIncidentObserver Observer { get; }
        public BlockingIncidentWorkflowFaultObserver FaultObserver { get; }
        public WorkflowExecutionCommandEnvelope Envelope { get; }
        public RuntimeSchedulerDrainResult FaultedDrainResult { get; }
        public RuntimeSchedulerDrainResult FaultFreeDrainResult { get; }

        public Harness(DateTimeOffset now)
        {
            _now = now;
            var activityStore = new InMemoryActivityExecutionStateStore();
            var inspectionStore = new InMemoryActivityExecutionInspectionStore();
            CommitStore = new InMemoryRuntimeCheckpointCommitStore(
                WorkflowStore,
                activityExecutionStateStore: activityStore,
                incidentStateStore: IncidentStore,
                activityExecutionInspectionWriter: inspectionStore,
                rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
            var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), CommitStore);
            var timeProvider = new FixedTimeProvider(now);
            Observer = new PoisonedSchedulerWorkIncidentObserver(PoisonStore, IncidentStore, committer, timeProvider);
            FaultObserver = new BlockingIncidentWorkflowFaultObserver(
                IncidentStore,
                WorkflowStore,
                activityStore,
                new RuntimeActivityExecutionInspectionAccumulator(inspectionStore),
                committer,
                timeProvider);
            Envelope = NewEnvelope();
            FaultedDrainResult = new RuntimeSchedulerDrainResult("wfexec-1", now, now,
            [
                new RuntimeSchedulerWorkItemResult(
                    workItemId: "workitem-1",
                    workflowExecutionId: "wfexec-1",
                    commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
                    status: RuntimeSchedulerWorkItemResultStatus.Faulted,
                    handlerName: nameof(WorkflowSchedulerDrainer),
                    startedAt: now,
                    completedAt: now,
                    error: "System.InvalidOperationException: dispatch exploded")
            ]);
            FaultFreeDrainResult = new RuntimeSchedulerDrainResult("wfexec-1", now, now, []);
        }

        public ValueTask<RuntimeSchedulerPoisonRecord> RecordPoison(
            RuntimeSchedulerPoisonDisposition disposition,
            DateTimeOffset? nextRetryAt = null) =>
            PoisonStore.RecordAsync(new RuntimeSchedulerPoisonRecord(
                workflowExecutionId: "wfexec-1",
                workItemId: "workitem-1",
                commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
                handlerName: nameof(WorkflowSchedulerDrainer),
                fault: new RuntimeFaultInfo("System.InvalidOperationException", "dispatch exploded"),
                failureCount: 1,
                disposition: disposition,
                firstFailedAt: _now,
                lastFailedAt: _now,
                nextRetryAt: nextRetryAt));

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
