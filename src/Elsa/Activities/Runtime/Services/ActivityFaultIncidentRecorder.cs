using System.Text.Json;
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
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
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
            PostCommitIntents: NewPostCommitIntents(request, occurredAt),
            Metadata: metadata);

        await request.CheckpointCommitter.CommitAsync(commit, cancellationToken);
    }

    /// <summary>
    /// The deterministic incident id for a fault. Exposed so callers that schedule fault-propagation work
    /// (child-fault parent evaluation) can reference the same incident id before the incident is committed.
    /// </summary>
    public static string IncidentId(string workItemId, string activityExecutionId, string subStatus) =>
        $"incident:{workItemId}:{activityExecutionId}:{subStatus}";

    private static string NewIncidentId(ActivityFaultIncidentRecordRequest request) =>
        IncidentId(request.WorkItem.WorkItemId, request.ActivityExecutionId, request.SubStatus);

    // Any work items the caller wants enqueued atomically with the incident (e.g. propagating the fault to a
    // parent fork/join for evaluation) ride along as post-commit intents on the incident checkpoint.
    private static IReadOnlyList<RuntimePostCommitIntent> NewPostCommitIntents(
        ActivityFaultIncidentRecordRequest request,
        DateTimeOffset occurredAt) =>
        request.PostCommitSchedulerWorkItems
            .Select(workItem => new RuntimePostCommitIntent(
                intentId: $"{request.WorkItem.WorkItemId}:post-commit:{workItem.WorkItemId}",
                workflowExecutionId: request.WorkItem.WorkflowExecutionId,
                kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
                recordedAt: occurredAt,
                activityExecutionId: request.ActivityExecutionId,
                idempotencyKey: $"{request.WorkItem.IdempotencyKey}:post-commit:{workItem.IdempotencyKey}",
                payload: JsonSerializer.SerializeToElement(workItem),
                metadata: request.WorkItem.CommandMetadata))
            .ToArray();

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
    IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? ValueSnapshots = null,
    IReadOnlyCollection<RuntimeSchedulerWorkItem>? PostCommitSchedulerWorkItemsOrNull = null)
{
    /// <summary>Scheduler work items to enqueue as post-commit intents on the incident checkpoint (never null).</summary>
    public IReadOnlyCollection<RuntimeSchedulerWorkItem> PostCommitSchedulerWorkItems => PostCommitSchedulerWorkItemsOrNull ?? [];
}
