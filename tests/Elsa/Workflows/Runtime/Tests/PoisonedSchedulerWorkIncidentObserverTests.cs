using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Guardrails for dispatch-fault surfacing: a scheduler work item parked in the poison store must become a
/// blocking incident (visible on the incidents API) with a system-authored Wait outcome. The terminal safety
/// observer must preserve that explicit intervention decision and leave the workflow nonterminal.
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
        Assert.Equal("WaitForIntervention", incident.ResolutionOutcome!.ActionKind);
        Assert.Equal("PoisonedSchedulerWork", incident.ResolutionOutcome.SystemSource);
        Assert.Equal(PoisonedSchedulerWorkIncidentObserver.IncidentFailureType, incident.FailureType);
        Assert.Null(incident.ActivityExecutionId);
        Assert.Contains("workitem-1", incident.Message);
        Assert.Contains("System.InvalidOperationException: dispatch exploded", incident.Message);
        Assert.Equal("workitem-1", incident.Metadata[RuntimeMetadataKeys.SchedulerWorkItemId]);
        Assert.Equal(nameof(WorkflowSchedulerDrainer), incident.Metadata[RuntimeMetadataKeys.SchedulerPoisonHandlerName]);
        Assert.Equal("1", incident.Metadata[RuntimeMetadataKeys.SchedulerPoisonFailureCount]);
        Assert.Equal("System.InvalidOperationException", incident.Metadata[RuntimeMetadataKeys.FaultType]);
        // No inner exception was captured, so the inner-fault keys must be absent rather than empty.
        Assert.False(incident.Metadata.ContainsKey(RuntimeMetadataKeys.FaultInnerType));
        Assert.False(incident.Metadata.ContainsKey(RuntimeMetadataKeys.FaultInnerMessage));

        var commit = Assert.Single(_harness.CommitStore.ListCommits()).Commit;
        Assert.Equal(RuntimeCheckpointNames.IncidentRecorded, commit.Checkpoint.Name);
        Assert.Equal(incident.IncidentId, Assert.Single(commit.StateChanges.Incidents).StateId);
    }

    [Fact]
    public async Task OnDrainedAsync_WithInnerFaultOnRecord_ProjectsInnerFaultIntoIncident()
    {
        // #1031: the root cause (GW-PHYSICAL-037) was nested one level under a checkpoint-writer wrapper exception
        // and was invisible on the projected incident. The inner fault carried on the poison record must surface in
        // both the incident message and the FaultInner* metadata keys (mirroring ActivityFaultIncidentRecorder).
        var innerFault = new RuntimeFaultInfo(
            "Groundwork.Documents.Store.GroundworkPhysicalStoreException",
            "GW-PHYSICAL-037: Projected string column 'by-incident-id' exceeds its declared maximum length of 128.");
        await _harness.RecordPoison(RuntimeSchedulerPoisonDisposition.Poisoned, innerFault: innerFault);

        await _harness.Observer.OnDrainedAsync(_harness.Envelope, _harness.FaultedDrainResult);

        var incident = await _harness.IncidentStore.FindAsync("wfexec-1", PoisonedSchedulerWorkIncidentObserver.IncidentId("workitem-1"));
        Assert.NotNull(incident);
        Assert.Contains("System.InvalidOperationException: dispatch exploded", incident!.Message);
        Assert.Contains($"---> {innerFault.ToSummaryString()}", incident.Message);
        Assert.Equal(innerFault.ExceptionType, incident.Metadata[RuntimeMetadataKeys.FaultInnerType]);
        Assert.Equal(innerFault.Message, incident.Metadata[RuntimeMetadataKeys.FaultInnerMessage]);
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
            resolutionOutcome: new IncidentResolutionOutcome(
                "Acme.OperatorResolution",
                _now.AddMinutes(-1),
                strategy: null,
                systemSource: "TestResolution"),
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
    public void IncidentId_StaysWithinProjectionColumnBudget_RegardlessOfWorkItemIdLength()
    {
        // A pre-fix chain-style work item id: multi-KB after only a few activities (the #923 growth). The projected
        // 'by-incident-id' column is capped at 128 (RuntimeExecutionIdProjectionLength); the derived incident id must
        // fit with margin no matter how long the source work item id is.
        var hugeWorkItemId = string.Join(":", Enumerable.Range(0, 200).Select(i => $"schedule-child:node-{i}:activity-{Guid.NewGuid()}"));

        var incidentId = PoisonedSchedulerWorkIncidentObserver.IncidentId(hugeWorkItemId);

        Assert.True(incidentId.Length <= 128, $"Incident id length {incidentId.Length} exceeds the 128-char column budget.");
        // Deterministic: same work item id always projects the same incident id (so dedupe / resolve stays stable).
        Assert.Equal(incidentId, PoisonedSchedulerWorkIncidentObserver.IncidentId(hugeWorkItemId));
    }

    [Fact]
    public async Task OnDrainedAsync_WhenIncidentPersistenceThrows_DoesNotPropagateAndContinues()
    {
        // Reproduces GW-PHYSICAL-037 (#922): recording the incident fails during persistence. The observer must swallow
        // the failure (logging it with the original poison record), leave the durable poison record intact, and keep
        // draining the remaining records — so the test-run / dispatch API call is not sunk by incident-recording.
        var throwingCommitIncidentStore = new ThrowingIncidentStateStore();
        var harness = new Harness(_now, commitIncidentStore: throwingCommitIncidentStore);
        await harness.RecordPoison(RuntimeSchedulerPoisonDisposition.Poisoned, workItemId: "workitem-1");
        await harness.RecordPoison(RuntimeSchedulerPoisonDisposition.Poisoned, workItemId: "workitem-2");

        var exception = await Record.ExceptionAsync(() =>
            harness.Observer.OnDrainedAsync(harness.Envelope, harness.FaultedDrainResult).AsTask());

        Assert.Null(exception);
        // Both records were attempted (the first failure did not abort the loop).
        Assert.Equal(2, throwingCommitIncidentStore.SaveAttempts);
        // Nothing was persisted, but the poison records remain durable and inspectable.
        Assert.Empty(await harness.IncidentStore.ListAsync("wfexec-1"));
        Assert.Equal(2, (await harness.PoisonStore.ListAsync("wfexec-1")).Count);
    }

    [Fact]
    public async Task ObserverChain_PoisonedRecord_PreservesSystemWaitAndNonterminalWorkflow()
    {
        await _harness.SaveWorkflow(WorkflowExecutionStatus.Running);
        await _harness.RecordPoison(RuntimeSchedulerPoisonDisposition.Poisoned);

        await _harness.Observer.OnDrainedAsync(_harness.Envelope, _harness.FaultedDrainResult);
        await _harness.FaultObserver.OnDrainedAsync(_harness.Envelope, _harness.FaultedDrainResult);

        var state = await _harness.WorkflowStore.FindAsync("wfexec-1");
        Assert.Equal(WorkflowExecutionStatus.Running, state!.Status);
        Assert.Null(state.CompletedAt);
        var incident = Assert.Single(await _harness.IncidentStore.ListAsync("wfexec-1"));
        Assert.Equal(IncidentStatus.Blocking, incident.Status);
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, incident.ResolutionOutcome!.ActionKind);
        Assert.Equal(IncidentResolutionSystemSources.PoisonedSchedulerWork, incident.ResolutionOutcome.SystemSource);
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

        public Harness(DateTimeOffset now, IIncidentStateStore? commitIncidentStore = null)
        {
            _now = now;
            var activityStore = new InMemoryActivityExecutionStateStore();
            var inspectionStore = new InMemoryActivityExecutionInspectionStore();
            CommitStore = RuntimeCheckpointTestStores.Create(
                workflowExecutionStateStore: WorkflowStore,
                activityExecutionStateStore: activityStore,
                // Separate store for the commit-time incident write so a test can make persistence throw while the
                // observer's own dedupe lookup (against IncidentStore) still succeeds.
                incidentStateStore: commitIncidentStore ?? IncidentStore,
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
            DateTimeOffset? nextRetryAt = null,
            string workItemId = "workitem-1",
            RuntimeFaultInfo? innerFault = null) =>
            PoisonStore.RecordAsync(new RuntimeSchedulerPoisonRecord(
                workflowExecutionId: "wfexec-1",
                workItemId: workItemId,
                commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
                handlerName: nameof(WorkflowSchedulerDrainer),
                fault: new RuntimeFaultInfo("System.InvalidOperationException", "dispatch exploded"),
                failureCount: 1,
                disposition: disposition,
                firstFailedAt: _now,
                lastFailedAt: _now,
                nextRetryAt: nextRetryAt,
                innerFault: innerFault));

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

    /// <summary>Simulates a projection-column overflow (GW-PHYSICAL-037): every incident write throws.</summary>
    private sealed class ThrowingIncidentStateStore : IIncidentStateStore, IInMemoryCheckpointTransactionSource
    {
        private readonly InMemoryIncidentStateStore _transactionParticipant = new();
        public int SaveAttempts { get; private set; }

        IEnumerable<object?> IInMemoryCheckpointTransactionSource.GetCheckpointTransactionParticipants() => [_transactionParticipant];

        public ValueTask<bool> TryAddAsync(IncidentState state, CancellationToken cancellationToken = default) => throw Boom();

        public ValueTask<IncidentState> SaveAsync(IncidentState state, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            throw Boom();
        }

        public ValueTask<IncidentState?> FindAsync(string workflowExecutionId, string incidentId, CancellationToken cancellationToken = default) =>
            new((IncidentState?)null);

        public ValueTask<IReadOnlyCollection<IncidentState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            new((IReadOnlyCollection<IncidentState>)[]);

        public ValueTask<IReadOnlyCollection<IncidentState>> ListBlockingAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            new((IReadOnlyCollection<IncidentState>)[]);

        private static InvalidOperationException Boom() => new(
            "GW-PHYSICAL-037: Projected string column 'by-incident-id' exceeds its declared maximum length of 128.");
    }
}
