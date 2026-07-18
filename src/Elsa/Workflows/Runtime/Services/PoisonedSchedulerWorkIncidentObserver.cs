using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Projects parked scheduler poison records into blocking incidents so a dispatch-time fault is observable.
/// Without this, a work item that faults during dispatch (before the activity fault path can record its own
/// incident) is recorded only in <see cref="IWorkflowSchedulerPoisonStore"/>: the workflow stays Running, the
/// activity stays Scheduled, and neither the incidents API nor the Studio timeline surfaces anything.
///
/// <para>Runs as a drain observer <b>before</b> <see cref="BlockingIncidentWorkflowFaultObserver"/>: the blocking
/// incident committed here is picked up by that observer in the same notification pass, which transitions the
/// workflow execution to <see cref="WorkflowExecutionStatus.Faulted"/>.</para>
///
/// <para>Only <see cref="RuntimeSchedulerPoisonDisposition.Poisoned"/> records are surfaced — a
/// <see cref="RuntimeSchedulerPoisonDisposition.RetryScheduled"/> record is still being re-driven by the retry
/// policy and must not fault the workflow. Incident ids are deterministic per work item and an existing incident
/// is never overwritten, so an operator-resolved incident stays resolved and repeated drains are idempotent.</para>
/// </summary>
public sealed class PoisonedSchedulerWorkIncidentObserver : IWorkflowSchedulerDrainObserver
{
    /// <summary>The <see cref="IncidentState.FailureType"/> of incidents projected from poison records.</summary>
    public const string IncidentFailureType = "SchedulerWorkPoisoned";

    private readonly IWorkflowSchedulerPoisonStore _poisonStore;
    private readonly IIncidentStateStore _incidentStateStore;
    private readonly RuntimeCheckpointCommitter _checkpointCommitter;
    private readonly TimeProvider _timeProvider;

    public PoisonedSchedulerWorkIncidentObserver(
        IWorkflowSchedulerPoisonStore poisonStore,
        IIncidentStateStore incidentStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(poisonStore);
        ArgumentNullException.ThrowIfNull(incidentStateStore);
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _poisonStore = poisonStore;
        _incidentStateStore = incidentStateStore;
        _checkpointCommitter = checkpointCommitter;
        _timeProvider = timeProvider;
    }

    /// <summary>The deterministic incident id projected for a poisoned scheduler work item.</summary>
    public static string IncidentId(string workItemId) => $"incident:{workItemId}:scheduler-poison";

    public async ValueTask OnDrainedAsync(
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeSchedulerDrainResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(result);

        // A poison record is only ever written by a drain that produced a Faulted item result, so a fault-free
        // drain needs no poison-store lookup. (A process crash between that drain and this notification leaves
        // the record unsurfaced until the workflow's next faulted drain; the record itself stays inspectable.)
        if (!result.StoppedOnFault)
            return;

        var workflowExecutionId = result.WorkflowExecutionId;
        var poisonRecords = await _poisonStore.ListAsync(workflowExecutionId, cancellationToken);

        foreach (var record in poisonRecords.OrderBy(item => item.WorkItemId, StringComparer.Ordinal))
        {
            if (record.Disposition != RuntimeSchedulerPoisonDisposition.Poisoned)
                continue;

            var incidentId = IncidentId(record.WorkItemId);
            var existing = await _incidentStateStore.FindAsync(workflowExecutionId, incidentId, cancellationToken);
            if (existing is not null)
                continue;

            await CommitIncidentAsync(workflowExecutionId, incidentId, record, cancellationToken);
        }
    }

    private async ValueTask CommitIncidentAsync(
        string workflowExecutionId,
        string incidentId,
        RuntimeSchedulerPoisonRecord record,
        CancellationToken cancellationToken)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var metadata = NewMetadata(incidentId, record);
        var incident = new IncidentState(
            incidentId: incidentId,
            workflowExecutionId: workflowExecutionId,
            activityExecutionId: null,
            executableNodeId: null,
            severity: IncidentSeverity.Critical,
            status: IncidentStatus.Blocking,
            resolutionAction: IncidentResolutionAction.WaitForIntervention,
            failureType: IncidentFailureType,
            message: $"Scheduler work item '{record.WorkItemId}' ({record.CommandKind}) was poisoned during dispatch " +
                     $"by handler '{record.HandlerName}' after {record.FailureCount} failure(s): {record.Fault.ToSummaryString()}",
            createdAt: occurredAt,
            resolvedAt: null,
            metadata: metadata);

        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{workflowExecutionId}:scheduler-poison:{record.WorkItemId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint:{workflowExecutionId}:scheduler-poison:{record.WorkItemId}",
                Name: RuntimeCheckpointNames.IncidentRecorded,
                WorkflowExecutionId: workflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents:
                [
                    new RuntimeStateChange<IncidentState>(
                        StateId: incidentId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: incident,
                        Metadata: metadata)
                ],
                operational: [],
                activityExecutionInspections: []),
            PostCommitIntents: [],
            Metadata: metadata);

        await _checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private static Dictionary<string, string> NewMetadata(string incidentId, RuntimeSchedulerPoisonRecord record)
    {
        var metadata = record.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.IncidentId] = incidentId;
        metadata[RuntimeMetadataKeys.CheckpointReason] = IncidentFailureType;
        metadata[RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory;
        metadata[RuntimeMetadataKeys.SchedulerWorkItemId] = record.WorkItemId;
        metadata[RuntimeMetadataKeys.CommandKind] = record.CommandKind.ToString();
        metadata[RuntimeMetadataKeys.SchedulerPoisonHandlerName] = record.HandlerName;
        metadata[RuntimeMetadataKeys.SchedulerPoisonFailureCount] = record.FailureCount.ToString();
        metadata[RuntimeMetadataKeys.FaultType] = record.Fault.ExceptionType;
        metadata[RuntimeMetadataKeys.FaultMessage] = record.Fault.Message;
        if (!string.IsNullOrWhiteSpace(record.Fault.StackTrace))
            metadata[RuntimeMetadataKeys.FaultStackTrace] = record.Fault.StackTrace;
        return metadata;
    }
}
