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
    private readonly IRuntimeFaultCapturePolicy _faultCapturePolicy;

    public ActivityFaultIncidentRecorder(TimeProvider timeProvider)
        : this(timeProvider, null, DefaultRuntimeFaultCapturePolicy.CreateDefault())
    {
    }

    public ActivityFaultIncidentRecorder(
        TimeProvider timeProvider,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator)
        : this(timeProvider, inspectionAccumulator, DefaultRuntimeFaultCapturePolicy.CreateDefault())
    {
    }

    public ActivityFaultIncidentRecorder(
        TimeProvider timeProvider,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        IRuntimeFaultCapturePolicy faultCapturePolicy)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(faultCapturePolicy);
        _timeProvider = timeProvider;
        _inspectionAccumulator = inspectionAccumulator;
        _faultCapturePolicy = faultCapturePolicy;
    }

    public async ValueTask CommitAsync(ActivityFaultIncidentRecordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var occurredAt = _timeProvider.GetUtcNow();
        var incidentId = NewIncidentId(request);
        var faultInfo = _faultCapturePolicy.Capture(request.Exception);
        var metadata = NewCommitMetadata(request, incidentId);
        var faultedState = NewFaultedActivityState(request, incidentId, occurredAt, faultInfo);
        var incident = NewIncident(request, incidentId, occurredAt, faultInfo);
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
        DateTimeOffset completedAt,
        RuntimeFaultInfo faultInfo)
    {
        var metadata = request.State.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var item in request.ActivityMetadata)
            metadata[item.Key] = item.Value;

        AddExceptionMetadata(metadata, faultInfo, request.Exception);
        metadata[RuntimeMetadataKeys.IncidentId] = incidentId;

        var state = EndOpenAttempt(request.State, incidentId, completedAt);
        return RuntimeContainerScopeService.CloseOwnedFrames(state with
        {
            Status = ActivityExecutionStatus.Faulted,
            SubStatus = request.SubStatus,
            CompletedAt = completedAt,
            IncidentIds = state.IncidentIds.Append(incidentId).Distinct(StringComparer.Ordinal).ToArray(),
            FaultCount = state.FaultCount + 1,
            AggregateFaultCount = state.AggregateFaultCount + 1,
            Fault = state.Fault ?? new NormalizedActivityFault(
                request.SubStatus,
                faultInfo.ExceptionType,
                faultInfo.Message,
                faultInfo.StackTrace,
                isRetryable: false),
            Metadata = metadata
        });
    }

    private static ActivityExecutionState EndOpenAttempt(
        ActivityExecutionState state,
        string incidentId,
        DateTimeOffset endedAt)
    {
        var attempts = state.Attempts ?? [];
        var openAttempt = attempts
            .Where(attempt => attempt.EndedAt is null)
            .OrderByDescending(attempt => attempt.Ordinal)
            .FirstOrDefault();
        if (openAttempt is null)
            return state;

        var endedAttempt = new ActivityAttempt(
            openAttempt.AttemptId,
            openAttempt.InvocationId,
            openAttempt.Ordinal,
            openAttempt.Reason,
            openAttempt.StartedAt,
            endedAt,
            openAttempt.TriggerDeliveryId,
            ActivityTransitionKind.Fault,
            incidentId);
        return state with
        {
            Attempts = attempts
                .Where(attempt => attempt.AttemptId != openAttempt.AttemptId)
                .Append(endedAttempt)
                .OrderBy(attempt => attempt.Ordinal)
                .ToArray()
        };
    }

    private static IncidentState NewIncident(
        ActivityFaultIncidentRecordRequest request,
        string incidentId,
        DateTimeOffset occurredAt,
        RuntimeFaultInfo faultInfo)
    {
        var metadata = request.IncidentMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var item in NewBaseMetadata(request))
            metadata[item.Key] = item.Value;
        AddExceptionMetadata(metadata, faultInfo, request.Exception);

        return new IncidentState(
            incidentId: incidentId,
            workflowExecutionId: request.WorkItem.WorkflowExecutionId,
            activityExecutionId: request.ActivityExecutionId,
            executableNodeId: request.ExecutableNodeId,
            severity: IncidentSeverity.Error,
            status: IncidentStatus.Blocking,
            resolutionAction: IncidentResolutionAction.WaitForIntervention,
            failureType: request.SubStatus,
            message: faultInfo.Message,
            createdAt: occurredAt,
            resolvedAt: null,
            metadata: metadata);
    }

    private static void AddExceptionMetadata(IDictionary<string, string> metadata, RuntimeFaultInfo faultInfo, Exception exception)
    {
        metadata[RuntimeMetadataKeys.FaultType] = faultInfo.ExceptionType;
        metadata[RuntimeMetadataKeys.FaultMessage] = faultInfo.Message;

        if (!string.IsNullOrWhiteSpace(faultInfo.StackTrace))
            metadata[RuntimeMetadataKeys.FaultStackTrace] = faultInfo.StackTrace;

        if (exception.InnerException is not { } inner)
            return;

        metadata[RuntimeMetadataKeys.FaultInnerType] = inner.GetType().FullName ?? inner.GetType().Name;
        metadata[RuntimeMetadataKeys.FaultInnerMessage] = inner.Message;
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
