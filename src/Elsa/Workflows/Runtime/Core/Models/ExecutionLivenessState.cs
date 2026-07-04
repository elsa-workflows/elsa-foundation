namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Runtime-owned operational coordination state. This is not workflow/domain retry state.
/// </summary>
public sealed class ExecutionLivenessState
{
    public ExecutionLivenessState(
        string operationalStateId,
        string workflowExecutionId,
        RuntimeExecutionLease? executionLease,
        RuntimeHeartbeat? heartbeat,
        RuntimeDrainState? drain,
        InterruptedExecutionState? interruptedExecution,
        IReadOnlyCollection<string>? pendingPostCommitIntentIds = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationalStateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        ValidateWorkflowExecutionId(workflowExecutionId, executionLease?.WorkflowExecutionId, nameof(executionLease));
        ValidateWorkflowExecutionId(workflowExecutionId, heartbeat?.WorkflowExecutionId, nameof(heartbeat));
        ValidateWorkflowExecutionId(workflowExecutionId, interruptedExecution?.WorkflowExecutionId, nameof(interruptedExecution));

        if (pendingPostCommitIntentIds?.Any(string.IsNullOrWhiteSpace) == true)
            throw new ArgumentException("Pending post-commit intent IDs cannot be empty.", nameof(pendingPostCommitIntentIds));

        OperationalStateId = operationalStateId;
        WorkflowExecutionId = workflowExecutionId;
        ExecutionLease = executionLease;
        Heartbeat = heartbeat;
        Drain = drain;
        InterruptedExecution = interruptedExecution;
        PendingPostCommitIntentIds = (pendingPostCommitIntentIds ?? []).ToArray();
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    // Persisted JSON key: the `operationalStateId` property name predates the W14 rename of this type
    // (ExecutionLivenessState was OperationalState). The member name is intentionally left unchanged to keep the wire key stable.
    public string OperationalStateId { get; }
    public string WorkflowExecutionId { get; }
    public RuntimeExecutionLease? ExecutionLease { get; }
    public RuntimeHeartbeat? Heartbeat { get; }
    public RuntimeDrainState? Drain { get; }
    public InterruptedExecutionState? InterruptedExecution { get; }
    public IReadOnlyCollection<string> PendingPostCommitIntentIds { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static void ValidateWorkflowExecutionId(string expected, string? actual, string parameterName)
    {
        if (actual is not null && !StringComparer.Ordinal.Equals(expected, actual))
            throw new ArgumentException("Operational child state must belong to the same workflow execution.", parameterName);
    }
}

public sealed class RuntimeExecutionLease
{
    public RuntimeExecutionLease(
        string leaseId,
        string workflowExecutionId,
        string ownerId,
        DateTimeOffset acquiredAt,
        DateTimeOffset expiresAt,
        long fencingToken,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        if (expiresAt <= acquiredAt)
            throw new ArgumentException("Lease expiration must be later than acquisition time.", nameof(expiresAt));

        if (fencingToken < 0)
            throw new ArgumentOutOfRangeException(nameof(fencingToken), "Lease fencing token cannot be negative.");

        LeaseId = leaseId;
        WorkflowExecutionId = workflowExecutionId;
        OwnerId = ownerId;
        AcquiredAt = acquiredAt;
        ExpiresAt = expiresAt;
        FencingToken = fencingToken;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string LeaseId { get; }
    public string WorkflowExecutionId { get; }
    public string OwnerId { get; }
    public DateTimeOffset AcquiredAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public long FencingToken { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;
}

public sealed class RuntimeHeartbeat
{
    public RuntimeHeartbeat(
        string heartbeatId,
        string workflowExecutionId,
        string ownerId,
        string? leaseId,
        DateTimeOffset recordedAt,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(heartbeatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        HeartbeatId = heartbeatId;
        WorkflowExecutionId = workflowExecutionId;
        OwnerId = ownerId;
        LeaseId = leaseId;
        RecordedAt = recordedAt;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string HeartbeatId { get; }
    public string WorkflowExecutionId { get; }
    public string OwnerId { get; }
    public string? LeaseId { get; }
    public DateTimeOffset RecordedAt { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class RuntimeDrainState
{
    public RuntimeDrainState(
        string drainId,
        RuntimeDrainMode mode,
        RuntimeDrainStatus status,
        DateTimeOffset requestedAt,
        string requestedBy,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(drainId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);

        DrainId = drainId;
        Mode = mode;
        Status = status;
        RequestedAt = requestedAt;
        RequestedBy = requestedBy;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string DrainId { get; }
    public RuntimeDrainMode Mode { get; }
    public RuntimeDrainStatus Status { get; }
    public DateTimeOffset RequestedAt { get; }
    public string RequestedBy { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public bool StopsNewWork => Status is RuntimeDrainStatus.Requested or RuntimeDrainStatus.Draining;
}

public sealed class InterruptedExecutionState
{
    public InterruptedExecutionState(
        string interruptionId,
        string workflowExecutionId,
        string? leaseId,
        string? lastCheckpointId,
        RuntimeInterruptionReason reason,
        RuntimeInterruptionStatus status,
        DateTimeOffset interruptedAt,
        IReadOnlyCollection<string>? activityExecutionIds = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interruptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        InterruptionId = interruptionId;
        WorkflowExecutionId = workflowExecutionId;
        LeaseId = leaseId;
        LastCheckpointId = lastCheckpointId;
        Reason = reason;
        Status = status;
        InterruptedAt = interruptedAt;
        ActivityExecutionIds = (activityExecutionIds ?? []).ToArray();
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string InterruptionId { get; }
    public string WorkflowExecutionId { get; }
    public string? LeaseId { get; }
    public string? LastCheckpointId { get; }
    public RuntimeInterruptionReason Reason { get; }
    public RuntimeInterruptionStatus Status { get; }
    public DateTimeOffset InterruptedAt { get; }
    public IReadOnlyCollection<string> ActivityExecutionIds { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public enum RuntimeDrainMode
{
    Quiesce,
    StopAcceptingNewWork,
    Shutdown
}

public enum RuntimeDrainStatus
{
    None,
    Requested,
    Draining,
    Drained,
    Cancelled
}

public enum RuntimeInterruptionReason
{
    LeaseLost,
    HostStopped,
    HeartbeatExpired,
    ExecutionAgentDeactivated,
    Unknown
}

public enum RuntimeInterruptionStatus
{
    Detected,
    Requeued,
    Ignored,
    Abandoned
}
