using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.Runtime.Services;

public sealed class ActivityFaultIncidentRecorder
{
    private readonly TimeProvider _timeProvider;
    private readonly IRuntimeActivityExecutionInspectionAccumulator? _inspectionAccumulator;

    public ActivityFaultIncidentRecorder(TimeProvider timeProvider)
        : this(timeProvider, null)
    {
    }

    public ActivityFaultIncidentRecorder(
        TimeProvider timeProvider,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _inspectionAccumulator = inspectionAccumulator;
    }

    public async ValueTask CommitAsync(ActivityFaultIncidentRecordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var occurredAt = _timeProvider.GetUtcNow();
        var incidentId = NewIncidentId(request);
        var metadata = NewCommitMetadata(request, incidentId);
        var faultedState = NewFaultedActivityState(request, incidentId, occurredAt);
        var incident = NewIncident(request, incidentId, occurredAt);
        var checkpointId = $"checkpoint:{request.WorkItem.WorkItemId}:incident-recorded:{incidentId}";
        var inspection = _inspectionAccumulator is null
            ? null
            : await _inspectionAccumulator.BuildProjectionAsync(
                faultedState,
                checkpointId,
                occurredAt,
                incidents: [ActivityExecutionIncidentSummary.From(incident)],
                valueSnapshots: request.ValueSnapshots,
                metadata: metadata,
                cancellationToken: cancellationToken);
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{request.WorkItem.WorkItemId}:incident-recorded:{incidentId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.IncidentRecorded,
                WorkflowExecutionId: request.WorkItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [request.ActivityExecutionId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: request.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: faultedState,
                        Metadata: metadata)
                ],
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
                activityExecutionInspections: inspection is null
                    ? []
                    :
                    [
                        new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                            StateId: request.ActivityExecutionId,
                            Operation: RuntimeStateChangeOperation.Upsert,
                            State: inspection,
                            Metadata: metadata)
                    ]),
            PostCommitIntents: [],
            Metadata: metadata);

        await request.CheckpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private static string NewIncidentId(ActivityFaultIncidentRecordRequest request) =>
        $"incident:{request.WorkItem.WorkItemId}:{request.ActivityExecutionId}:{request.SubStatus}";

    private static Dictionary<string, string> NewCommitMetadata(ActivityFaultIncidentRecordRequest request, string incidentId)
    {
        var metadata = request.IncidentMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var item in NewBaseMetadata(request))
            metadata[item.Key] = item.Value;

        metadata[RuntimeMetadataKeys.IncidentId] = incidentId;
        metadata[RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory;

        return metadata;
    }

    private static Dictionary<string, string> NewBaseMetadata(ActivityFaultIncidentRecordRequest request) =>
        new(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = request.WorkItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = request.WorkItem.CommandId,
            [RuntimeMetadataKeys.ActivityExecutionId] = request.ActivityExecutionId,
            [RuntimeMetadataKeys.ExecutableNodeId] = request.ExecutableNodeId,
            [RuntimeMetadataKeys.FaultSubStatus] = request.SubStatus
        };

    private static ActivityExecutionState NewFaultedActivityState(
        ActivityFaultIncidentRecordRequest request,
        string incidentId,
        DateTimeOffset completedAt)
    {
        var metadata = request.State.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var item in request.ActivityMetadata)
            metadata[item.Key] = item.Value;

        metadata[RuntimeMetadataKeys.FaultType] = request.Exception.GetType().FullName ?? request.Exception.GetType().Name;
        metadata[RuntimeMetadataKeys.FaultMessage] = request.Exception.Message;
        metadata[RuntimeMetadataKeys.IncidentId] = incidentId;

        return request.State with
        {
            Status = ActivityExecutionStatus.Faulted,
            SubStatus = request.SubStatus,
            CompletedAt = completedAt,
            IncidentIds = request.State.IncidentIds.Append(incidentId).Distinct(StringComparer.Ordinal).ToArray(),
            FaultCount = request.State.FaultCount + 1,
            AggregateFaultCount = request.State.AggregateFaultCount + 1,
            Metadata = metadata
        };
    }

    private static IncidentState NewIncident(
        ActivityFaultIncidentRecordRequest request,
        string incidentId,
        DateTimeOffset occurredAt)
    {
        var metadata = request.IncidentMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var item in NewBaseMetadata(request))
            metadata[item.Key] = item.Value;

        return new IncidentState(
            incidentId: incidentId,
            workflowExecutionId: request.WorkItem.WorkflowExecutionId,
            activityExecutionId: request.ActivityExecutionId,
            executableNodeId: request.ExecutableNodeId,
            severity: IncidentSeverity.Error,
            status: IncidentStatus.Blocking,
            resolutionAction: IncidentResolutionAction.WaitForIntervention,
            failureType: request.SubStatus,
            message: request.Exception.Message,
            createdAt: occurredAt,
            resolvedAt: null,
            metadata: metadata);
    }
}

public sealed record ActivityFaultIncidentRecordRequest(
    RuntimeCheckpointCommitter CheckpointCommitter,
    RuntimeSchedulerWorkItem WorkItem,
    string ActivityExecutionId,
    string ExecutableNodeId,
    ActivityExecutionState State,
    Exception Exception,
    string SubStatus,
    IReadOnlyDictionary<string, string> ActivityMetadata,
    IReadOnlyDictionary<string, string> IncidentMetadata,
    IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? ValueSnapshots = null);
