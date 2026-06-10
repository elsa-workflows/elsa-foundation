namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Runtime-owned administrative control state. This is separate from workflow continuation state.
/// </summary>
public sealed class ControlPlaneState
{
    public ControlPlaneState(
        string controlPlaneStateId,
        string? workflowExecutionId = null,
        IReadOnlyCollection<ControlPlaneHold>? activeHolds = null,
        IReadOnlyCollection<ControlPlaneHold>? releasedHolds = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlPlaneStateId);

        if (workflowExecutionId is not null && string.IsNullOrWhiteSpace(workflowExecutionId))
            throw new ArgumentException("Workflow execution ID cannot be blank when provided.", nameof(workflowExecutionId));

        var activeHoldSnapshot = SnapshotHolds(activeHolds, nameof(activeHolds));
        var releasedHoldSnapshot = SnapshotHolds(releasedHolds, nameof(releasedHolds));
        ValidateUniqueHoldIds(activeHoldSnapshot.Concat(releasedHoldSnapshot));
        ValidateActiveHolds(activeHoldSnapshot, nameof(activeHolds));
        ValidateReleasedHolds(releasedHoldSnapshot, nameof(releasedHolds));

        if (workflowExecutionId is not null)
        {
            ValidateWorkflowScopedHolds(workflowExecutionId, activeHoldSnapshot, nameof(activeHolds));
            ValidateWorkflowScopedHolds(workflowExecutionId, releasedHoldSnapshot, nameof(releasedHolds));
        }

        ControlPlaneStateId = controlPlaneStateId;
        WorkflowExecutionId = workflowExecutionId;
        ActiveHolds = activeHoldSnapshot;
        ReleasedHolds = releasedHoldSnapshot;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string ControlPlaneStateId { get; }
    public string? WorkflowExecutionId { get; }
    public IReadOnlyCollection<ControlPlaneHold> ActiveHolds { get; }
    public IReadOnlyCollection<ControlPlaneHold> ReleasedHolds { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static IReadOnlyCollection<ControlPlaneHold> SnapshotHolds(IReadOnlyCollection<ControlPlaneHold>? holds, string parameterName)
    {
        var snapshot = (holds ?? []).ToArray();

        if (snapshot.Any(hold => hold is null))
            throw new ArgumentException("Control-plane holds cannot contain null entries.", parameterName);

        var duplicateHoldId = snapshot
            .GroupBy(hold => hold.HoldId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateHoldId is not null)
            throw new ArgumentException($"Control-plane hold ID '{duplicateHoldId}' is duplicated.", parameterName);

        return snapshot;
    }

    private static void ValidateUniqueHoldIds(IEnumerable<ControlPlaneHold> holds)
    {
        var duplicateHoldId = holds
            .GroupBy(hold => hold.HoldId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateHoldId is not null)
            throw new ArgumentException($"Control-plane hold ID '{duplicateHoldId}' cannot appear in more than one hold collection.");
    }

    private static void ValidateActiveHolds(IReadOnlyCollection<ControlPlaneHold> holds, string parameterName)
    {
        if (holds.Any(hold => !hold.IsEffective))
            throw new ArgumentException("Active holds must have Requested or Active status.", parameterName);
    }

    private static void ValidateReleasedHolds(IReadOnlyCollection<ControlPlaneHold> holds, string parameterName)
    {
        if (holds.Any(hold => hold.Status != ControlPlaneHoldStatus.Released))
            throw new ArgumentException("Released holds must have Released status.", parameterName);
    }

    private static void ValidateWorkflowScopedHolds(string workflowExecutionId, IReadOnlyCollection<ControlPlaneHold> holds, string parameterName)
    {
        if (holds.Any(hold => hold.WorkflowExecutionId is null || !StringComparer.Ordinal.Equals(workflowExecutionId, hold.WorkflowExecutionId)))
            throw new ArgumentException("Control-plane workflow-scoped holds must belong to the same workflow execution.", parameterName);
    }
}

public sealed class ControlPlaneHold
{
    public ControlPlaneHold(
        string holdId,
        ControlPlaneHoldScope scope,
        ControlPlaneHoldStatus status,
        DateTimeOffset requestedAt,
        string requestedBy,
        string reason,
        string? workflowExecutionId = null,
        string? activityExecutionId = null,
        string? generatorId = null,
        string? ingressSourceId = null,
        string? workerId = null,
        string? hostId = null,
        RuntimePauseContinuationPolicy continuationPolicy = RuntimePauseContinuationPolicy.StrictPause,
        IngressPausePolicy? ingressPolicy = null,
        DateTimeOffset? releasedAt = null,
        string? releasedBy = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holdId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (releasedAt is not null && releasedAt < requestedAt)
            throw new ArgumentException("Release time cannot be earlier than request time.", nameof(releasedAt));

        if (releasedBy is not null && string.IsNullOrWhiteSpace(releasedBy))
            throw new ArgumentException("Released-by cannot be blank when provided.", nameof(releasedBy));

        if (status == ControlPlaneHoldStatus.Released && releasedAt is null)
            throw new ArgumentException("Released control-plane holds require a release time.", nameof(releasedAt));

        if (status == ControlPlaneHoldStatus.Released && releasedBy is null)
            throw new ArgumentException("Released control-plane holds require a released-by value.", nameof(releasedBy));

        if (status != ControlPlaneHoldStatus.Released && releasedAt is not null)
            throw new ArgumentException("Only released control-plane holds can have a release time.", nameof(releasedAt));

        if (status != ControlPlaneHoldStatus.Released && releasedBy is not null)
            throw new ArgumentException("Only released control-plane holds can have a released-by value.", nameof(releasedBy));

        ValidateOptionalId(workflowExecutionId, nameof(workflowExecutionId), "Workflow execution ID");
        ValidateOptionalId(activityExecutionId, nameof(activityExecutionId), "Activity execution ID");
        ValidateOptionalId(generatorId, nameof(generatorId), "Generator ID");
        ValidateOptionalId(ingressSourceId, nameof(ingressSourceId), "Ingress source ID");
        ValidateOptionalId(workerId, nameof(workerId), "Worker ID");
        ValidateOptionalId(hostId, nameof(hostId), "Host ID");
        ValidateScopeTargets(scope, workflowExecutionId, activityExecutionId, generatorId, ingressSourceId, workerId, hostId, ingressPolicy);

        HoldId = holdId;
        Scope = scope;
        Status = status;
        RequestedAt = requestedAt;
        RequestedBy = requestedBy;
        Reason = reason;
        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        GeneratorId = generatorId;
        IngressSourceId = ingressSourceId;
        WorkerId = workerId;
        HostId = hostId;
        ContinuationPolicy = continuationPolicy;
        IngressPolicy = ingressPolicy;
        ReleasedAt = releasedAt;
        ReleasedBy = releasedBy;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string HoldId { get; }
    public ControlPlaneHoldScope Scope { get; }
    public ControlPlaneHoldStatus Status { get; }
    public DateTimeOffset RequestedAt { get; }
    public string RequestedBy { get; }
    public string Reason { get; }
    public string? WorkflowExecutionId { get; }
    public string? ActivityExecutionId { get; }
    public string? GeneratorId { get; }
    public string? IngressSourceId { get; }
    public string? WorkerId { get; }
    public string? HostId { get; }
    public RuntimePauseContinuationPolicy ContinuationPolicy { get; }
    public IngressPausePolicy? IngressPolicy { get; }
    public DateTimeOffset? ReleasedAt { get; }
    public string? ReleasedBy { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public bool IsEffective => Status is ControlPlaneHoldStatus.Requested or ControlPlaneHoldStatus.Active;

    public static ControlPlaneHold ForWorkflowExecution(
        string holdId,
        string workflowExecutionId,
        DateTimeOffset requestedAt,
        string requestedBy,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            holdId,
            ControlPlaneHoldScope.WorkflowExecution,
            ControlPlaneHoldStatus.Requested,
            requestedAt,
            requestedBy,
            reason,
            workflowExecutionId: workflowExecutionId,
            continuationPolicy: RuntimePauseContinuationPolicy.StrictPause,
            metadata: metadata);

    public static ControlPlaneHold ForHostDrain(
        string holdId,
        string hostId,
        DateTimeOffset requestedAt,
        string requestedBy,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            holdId,
            ControlPlaneHoldScope.HostDrain,
            ControlPlaneHoldStatus.Requested,
            requestedAt,
            requestedBy,
            reason,
            hostId: hostId,
            continuationPolicy: RuntimePauseContinuationPolicy.DrainInFlight,
            metadata: metadata);

    private static void ValidateOptionalId(string? value, string parameterName, string label)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{label} cannot be blank when provided.", parameterName);
    }

    private static void ValidateScopeTargets(
        ControlPlaneHoldScope scope,
        string? workflowExecutionId,
        string? activityExecutionId,
        string? generatorId,
        string? ingressSourceId,
        string? workerId,
        string? hostId,
        IngressPausePolicy? ingressPolicy)
    {
        switch (scope)
        {
            case ControlPlaneHoldScope.Ingress:
                Require(ingressSourceId, nameof(ingressSourceId), "Ingress control-plane holds require an ingress source ID.");
                Forbid(workflowExecutionId, nameof(workflowExecutionId), "Ingress control-plane holds are not workflow-execution scoped.");
                Forbid(activityExecutionId, nameof(activityExecutionId), "Ingress control-plane holds are not activity-execution scoped.");
                Forbid(generatorId, nameof(generatorId), "Ingress control-plane holds are not generator scoped.");
                Forbid(workerId, nameof(workerId), "Ingress control-plane holds are not worker scoped.");
                Forbid(hostId, nameof(hostId), "Ingress control-plane holds are not host scoped.");
                if (ingressPolicy is null)
                    throw new ArgumentException("Ingress control-plane holds require an ingress pause policy.", nameof(ingressPolicy));
                break;
            case ControlPlaneHoldScope.WorkflowExecution:
                Require(workflowExecutionId, nameof(workflowExecutionId), "Workflow execution control-plane holds require a workflow execution ID.");
                Forbid(activityExecutionId, nameof(activityExecutionId), "Workflow execution control-plane holds are not activity-execution scoped.");
                Forbid(generatorId, nameof(generatorId), "Workflow execution control-plane holds are not generator scoped.");
                Forbid(ingressSourceId, nameof(ingressSourceId), "Workflow execution control-plane holds are not ingress scoped.");
                Forbid(workerId, nameof(workerId), "Workflow execution control-plane holds are not worker scoped.");
                Forbid(hostId, nameof(hostId), "Workflow execution control-plane holds are not host scoped.");
                Forbid(ingressPolicy, nameof(ingressPolicy), "Ingress policy is only valid for ingress control-plane holds.");
                break;
            case ControlPlaneHoldScope.ActivityExecution:
                Require(workflowExecutionId, nameof(workflowExecutionId), "Activity execution control-plane holds require a workflow execution ID.");
                Require(activityExecutionId, nameof(activityExecutionId), "Activity execution control-plane holds require an activity execution ID.");
                Forbid(generatorId, nameof(generatorId), "Activity execution control-plane holds are not generator scoped.");
                Forbid(ingressSourceId, nameof(ingressSourceId), "Activity execution control-plane holds are not ingress scoped.");
                Forbid(workerId, nameof(workerId), "Activity execution control-plane holds are not worker scoped.");
                Forbid(hostId, nameof(hostId), "Activity execution control-plane holds are not host scoped.");
                Forbid(ingressPolicy, nameof(ingressPolicy), "Ingress policy is only valid for ingress control-plane holds.");
                break;
            case ControlPlaneHoldScope.Generator:
                Require(workflowExecutionId, nameof(workflowExecutionId), "Generator control-plane holds require a workflow execution ID.");
                Require(generatorId, nameof(generatorId), "Generator control-plane holds require a generator ID.");
                Forbid(activityExecutionId, nameof(activityExecutionId), "Generator control-plane holds use generator ID, not activity execution ID, as their generator target.");
                Forbid(ingressSourceId, nameof(ingressSourceId), "Generator control-plane holds are not ingress scoped.");
                Forbid(workerId, nameof(workerId), "Generator control-plane holds are not worker scoped.");
                Forbid(hostId, nameof(hostId), "Generator control-plane holds are not host scoped.");
                Forbid(ingressPolicy, nameof(ingressPolicy), "Ingress policy is only valid for ingress control-plane holds.");
                break;
            case ControlPlaneHoldScope.WorkerDispatcher:
                Require(workerId, nameof(workerId), "Worker/dispatcher control-plane holds require a worker ID.");
                Forbid(workflowExecutionId, nameof(workflowExecutionId), "Worker/dispatcher control-plane holds are not workflow-execution scoped.");
                Forbid(activityExecutionId, nameof(activityExecutionId), "Worker/dispatcher control-plane holds are not activity-execution scoped.");
                Forbid(generatorId, nameof(generatorId), "Worker/dispatcher control-plane holds are not generator scoped.");
                Forbid(ingressSourceId, nameof(ingressSourceId), "Worker/dispatcher control-plane holds are not ingress scoped.");
                Forbid(hostId, nameof(hostId), "Worker/dispatcher control-plane holds are not host scoped.");
                Forbid(ingressPolicy, nameof(ingressPolicy), "Ingress policy is only valid for ingress control-plane holds.");
                break;
            case ControlPlaneHoldScope.HostDrain:
                Require(hostId, nameof(hostId), "Host drain control-plane holds require a host ID.");
                Forbid(workflowExecutionId, nameof(workflowExecutionId), "Host drain control-plane holds are not workflow-execution scoped.");
                Forbid(activityExecutionId, nameof(activityExecutionId), "Host drain control-plane holds are not activity-execution scoped.");
                Forbid(generatorId, nameof(generatorId), "Host drain control-plane holds are not generator scoped.");
                Forbid(ingressSourceId, nameof(ingressSourceId), "Host drain control-plane holds are not ingress scoped.");
                Forbid(workerId, nameof(workerId), "Host drain control-plane holds are not worker scoped.");
                Forbid(ingressPolicy, nameof(ingressPolicy), "Ingress policy is only valid for ingress control-plane holds.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown control-plane hold scope.");
        }
    }

    private static void Require(string? value, string parameterName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(message, parameterName);
    }

    private static void Forbid(string? value, string parameterName, string message)
    {
        if (value is not null)
            throw new ArgumentException(message, parameterName);
    }

    private static void Forbid(object? value, string parameterName, string message)
    {
        if (value is not null)
            throw new ArgumentException(message, parameterName);
    }
}

public sealed class SchedulerPauseDecision
{
    public SchedulerPauseDecision(
        bool canAdvance,
        RuntimePauseBoundary boundary,
        RuntimePauseContinuationPolicy continuationPolicy,
        string? holdId,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (!canAdvance && string.IsNullOrWhiteSpace(holdId))
            throw new ArgumentException("Blocked scheduler pause decisions require a hold ID.", nameof(holdId));

        if (!canAdvance && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Blocked scheduler pause decisions require a reason.", nameof(reason));

        if (canAdvance && holdId is not null && string.IsNullOrWhiteSpace(holdId))
            throw new ArgumentException("Hold ID cannot be blank when provided.", nameof(holdId));

        if (canAdvance && reason is not null && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be blank when provided.", nameof(reason));

        CanAdvance = canAdvance;
        Boundary = boundary;
        ContinuationPolicy = continuationPolicy;
        HoldId = holdId;
        Reason = reason;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public bool CanAdvance { get; }
    public RuntimePauseBoundary Boundary { get; }
    public RuntimePauseContinuationPolicy ContinuationPolicy { get; }
    public string? HoldId { get; }
    public string? Reason { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class IngressPausePolicy
{
    public IngressPausePolicy(
        RuntimeIngressSourceKind sourceKind,
        IngressPauseBehavior behavior,
        int? responseStatusCode = null,
        string? reason = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (responseStatusCode is not null and (< 100 or > 599))
            throw new ArgumentOutOfRangeException(nameof(responseStatusCode), "HTTP response status code must be between 100 and 599.");

        if (reason is not null && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be blank when provided.", nameof(reason));

        if (RequiresResponseStatusCode(behavior) && responseStatusCode is null)
            throw new ArgumentException("Rejecting paused ingress requires a response status code.", nameof(responseStatusCode));

        if (!RequiresResponseStatusCode(behavior) && responseStatusCode is not null)
            throw new ArgumentException("Response status code is only valid for rejected paused ingress.", nameof(responseStatusCode));

        SourceKind = sourceKind;
        Behavior = behavior;
        ResponseStatusCode = responseStatusCode;
        Reason = reason;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public RuntimeIngressSourceKind SourceKind { get; }
    public IngressPauseBehavior Behavior { get; }
    public int? ResponseStatusCode { get; }
    public string? Reason { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public static IngressPausePolicy DefaultFor(RuntimeIngressSourceKind sourceKind) =>
        sourceKind switch
        {
            RuntimeIngressSourceKind.HttpEndpoint => new(sourceKind, IngressPauseBehavior.Reject, 503, "Paused"),
            RuntimeIngressSourceKind.Timer => new(sourceKind, IngressPauseBehavior.SkipWhilePaused, reason: "Paused"),
            RuntimeIngressSourceKind.MessageQueue => new(sourceKind, IngressPauseBehavior.StopFetchingOrLocking, reason: "Paused"),
            RuntimeIngressSourceKind.RequestResponseWebhook => new(sourceKind, IngressPauseBehavior.Reject, 503, "Paused"),
            RuntimeIngressSourceKind.DurableEventStream => new(sourceKind, IngressPauseBehavior.PauseSubscriptionOrOffset, reason: "Paused"),
            RuntimeIngressSourceKind.ManualApiStart => new(sourceKind, IngressPauseBehavior.Reject, 503, "Paused"),
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, "Unknown ingress source kind.")
        };

    private static bool RequiresResponseStatusCode(IngressPauseBehavior behavior) => behavior is IngressPauseBehavior.Reject;
}

public enum ControlPlaneHoldScope
{
    Ingress,
    WorkflowExecution,
    ActivityExecution,
    Generator,
    WorkerDispatcher,
    HostDrain
}

public enum ControlPlaneHoldStatus
{
    Requested,
    Active,
    Released
}

public enum RuntimePauseContinuationPolicy
{
    StrictPause,
    DrainInFlight
}

public enum RuntimePauseBoundary
{
    BeforeActivityExecutionStart,
    AfterActivityCompletedPropagationDrains,
    AfterCheckpointCommit,
    EnteringDurableSuspension,
    RegisteringVolatileWait,
    BeforeGeneratorEmission,
    BeforePostCommitDispatch
}

public enum RuntimeIngressSourceKind
{
    HttpEndpoint,
    Timer,
    MessageQueue,
    RequestResponseWebhook,
    DurableEventStream,
    ManualApiStart
}

public enum IngressPauseBehavior
{
    Reject,
    SkipWhilePaused,
    StopFetchingOrLocking,
    CoalesceMissedFirings,
    CatchUpMissedFirings,
    FireOnUnpause,
    PauseSubscriptionOrOffset
}
