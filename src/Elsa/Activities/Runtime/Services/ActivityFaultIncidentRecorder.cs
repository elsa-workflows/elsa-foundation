using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
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
    private readonly ActivityActivationFailureHandler _activationFailures;

    public ActivityFaultIncidentRecorder(TimeProvider timeProvider)
        : this(timeProvider, null, DefaultRuntimeFaultCapturePolicy.CreateDefault(), null)
    {
    }

    public ActivityFaultIncidentRecorder(
        TimeProvider timeProvider,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator)
        : this(timeProvider, inspectionAccumulator, DefaultRuntimeFaultCapturePolicy.CreateDefault(), null)
    {
    }

    public ActivityFaultIncidentRecorder(
        TimeProvider timeProvider,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        IRuntimeFaultCapturePolicy faultCapturePolicy,
        ActivityActivationFailureHandler? activationFailures = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(faultCapturePolicy);
        _timeProvider = timeProvider;
        _inspectionAccumulator = inspectionAccumulator;
        _faultCapturePolicy = faultCapturePolicy;
        _activationFailures = activationFailures ?? new ActivityActivationFailureHandler();
    }

    public async ValueTask CommitAsync(ActivityFaultIncidentRecordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var occurredAt = _timeProvider.GetUtcNow();
        var activationFailure = _activationFailures.Classify(
            request.Exception,
            ArtifactId(request.WorkItem.CommandMetadata),
            request.ExecutableNodeId);
        var incidentId = NewIncidentId(request);
        var faultInfo = _faultCapturePolicy.Capture(request.Exception);
        var metadata = NewCommitMetadata(request, incidentId, activationFailure);
        var faultedState = NewFaultedActivityState(request, incidentId, occurredAt, faultInfo, activationFailure);
        var incident = NewIncident(request, incidentId, occurredAt, faultInfo, activationFailure);
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
            PostCommitIntents: activationFailure is null ? NewPostCommitIntents(request, occurredAt) : [],
            Metadata: metadata);

        await request.CheckpointCommitter.CommitAsync(commit, cancellationToken);
    }

    /// <summary>
    /// The deterministic incident id for a fault. Exposed so callers that schedule fault-propagation work
    /// (child-fault parent evaluation) can reference the same incident id before the incident is committed.
    /// The work-item and activity-execution ids are folded into fixed-length fingerprints (#923) so the id
    /// stays within the 128-char <c>by-incident-id</c> projection column (GW-PHYSICAL-037) regardless of how
    /// long those ids are — dispatched executions embed <c>dispatch:v1:&lt;sha-256&gt;</c> shapes in both
    /// (#1031). The human-readable ids are preserved in the incident metadata, not the id.
    /// </summary>
    public static string IncidentId(string workItemId, string activityExecutionId, string subStatus) =>
        $"incident:{RuntimeChainId.Fingerprint(workItemId)}:{RuntimeChainId.Fingerprint(activityExecutionId)}:{subStatus}";

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

    private static Dictionary<string, string> NewCommitMetadata(
        ActivityFaultIncidentRecordRequest request,
        string incidentId,
        ActivityActivationFailure? activationFailure)
    {
        var metadata = request.IncidentMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var item in NewBaseMetadata(request))
            metadata[item.Key] = item.Value;

        metadata[RuntimeMetadataKeys.IncidentId] = incidentId;
        metadata[RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory;
        AddCausationMetadata(metadata, request.Exception);
        AddActivationMetadata(metadata, activationFailure);

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

    private ActivityExecutionState NewFaultedActivityState(
        ActivityFaultIncidentRecordRequest request,
        string incidentId,
        DateTimeOffset completedAt,
        RuntimeFaultInfo faultInfo,
        ActivityActivationFailure? activationFailure)
    {
        var metadata = request.State.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var item in request.ActivityMetadata)
            metadata[item.Key] = item.Value;

        AddExceptionMetadata(metadata, faultInfo, request.Exception);
        AddCausationMetadata(metadata, request.Exception);
        metadata[RuntimeMetadataKeys.IncidentId] = incidentId;
        AddActivationMetadata(metadata, activationFailure);

        if (activationFailure is not null)
        {
            return request.State with
            {
                Status = ActivityExecutionStatus.Waiting,
                SubStatus = ActivityActivationFailureHandler.IncidentFailureType,
                CompletedAt = null,
                IncidentIds = request.State.IncidentIds.Append(incidentId).Distinct(StringComparer.Ordinal).ToArray(),
                Metadata = metadata
            };
        }

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
            Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault,
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

    private IncidentState NewIncident(
        ActivityFaultIncidentRecordRequest request,
        string incidentId,
        DateTimeOffset occurredAt,
        RuntimeFaultInfo faultInfo,
        ActivityActivationFailure? activationFailure)
    {
        var metadata = request.IncidentMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var item in NewBaseMetadata(request))
            metadata[item.Key] = item.Value;
        AddExceptionMetadata(metadata, faultInfo, request.Exception);
        AddCausationMetadata(metadata, request.Exception);
        AddActivationMetadata(metadata, activationFailure);

        return new IncidentState(
            incidentId: incidentId,
            workflowExecutionId: request.WorkItem.WorkflowExecutionId,
            activityExecutionId: request.ActivityExecutionId,
            executableNodeId: request.ExecutableNodeId,
            severity: IncidentSeverity.Error,
            status: IncidentStatus.Blocking,
            resolutionAction: IncidentResolutionAction.WaitForIntervention,
            failureType: activationFailure is null ? request.SubStatus : ActivityActivationFailureHandler.IncidentFailureType,
            message: faultInfo.Message,
            createdAt: occurredAt,
            resolvedAt: null,
            metadata: metadata);
    }

    private static void AddActivationMetadata(
        IDictionary<string, string> metadata,
        ActivityActivationFailure? activationFailure)
    {
        if (activationFailure is null)
            return;

        foreach (var item in activationFailure.Metadata)
            metadata[item.Key] = item.Value;
        if (!string.IsNullOrWhiteSpace(activationFailure.ArtifactId))
            metadata[RuntimeMetadataKeys.ExecutableArtifactId] = activationFailure.ArtifactId;
    }

    private static string? ArtifactId(IReadOnlyDictionary<string, string> metadata) =>
        metadata.GetValueOrDefault(RuntimeMetadataKeys.PinnedArtifactId) ??
        metadata.GetValueOrDefault(RuntimeMetadataKeys.ExecutableArtifactId);

    private static void AddCausationMetadata(IDictionary<string, string> metadata, Exception exception)
    {
        if (exception is not IActivityFaultCausation causation)
            return;

        metadata[RuntimeMetadataKeys.CausalIncidentId] = causation.CausalIncidentId;
        metadata[RuntimeMetadataKeys.CausalActivityExecutionId] = causation.CausalActivityExecutionId;
        metadata[RuntimeMetadataKeys.CausalExecutableNodeId] = causation.CausalExecutableNodeId;
        metadata[RuntimeMetadataKeys.CausationKind] = causation.CausationKind;
    }

    private void AddExceptionMetadata(IDictionary<string, string> metadata, RuntimeFaultInfo faultInfo, Exception exception)
    {
        metadata[RuntimeMetadataKeys.FaultType] = faultInfo.ExceptionType;
        metadata[RuntimeMetadataKeys.FaultMessage] = faultInfo.Message;

        if (!string.IsNullOrWhiteSpace(faultInfo.StackTrace))
            metadata[RuntimeMetadataKeys.FaultStackTrace] = faultInfo.StackTrace;

        if (_faultCapturePolicy.CaptureInner(exception) is not { } innerFaultInfo)
            return;

        metadata[RuntimeMetadataKeys.FaultInnerType] = innerFaultInfo.ExceptionType;
        metadata[RuntimeMetadataKeys.FaultInnerMessage] = innerFaultInfo.Message;
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
