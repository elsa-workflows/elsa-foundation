namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimeRecoveryScanRequest
{
    public RuntimeRecoveryScanRequest(DateTimeOffset now, TimeSpan leaseTimeout, int limit, string? ownerId = null)
    {
        if (leaseTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseTimeout), "Lease timeout must be greater than zero.");

        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Recovery scan limit must be greater than zero.");

        Now = now;
        LeaseTimeout = leaseTimeout;
        Limit = limit;
        OwnerId = ownerId;
    }

    public DateTimeOffset Now { get; }
    public TimeSpan LeaseTimeout { get; }
    public int Limit { get; }
    public string? OwnerId { get; }
}

public sealed class RuntimeRecoveryCandidate
{
    public RuntimeRecoveryCandidate(
        string workflowExecutionId,
        string? operationalStateId,
        string? lastCheckpointId,
        RuntimeInterruptionReason reason,
        DateTimeOffset detectedAt,
        bool requeueFromLastCheckpoint,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        if (requeueFromLastCheckpoint && string.IsNullOrWhiteSpace(lastCheckpointId))
            throw new ArgumentException("A recovery candidate that requeues from the last checkpoint requires a last checkpoint ID.", nameof(lastCheckpointId));

        WorkflowExecutionId = workflowExecutionId;
        OperationalStateId = operationalStateId;
        LastCheckpointId = lastCheckpointId;
        Reason = reason;
        DetectedAt = detectedAt;
        RequeueFromLastCheckpoint = requeueFromLastCheckpoint;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string WorkflowExecutionId { get; }
    public string? OperationalStateId { get; }
    public string? LastCheckpointId { get; }
    public RuntimeInterruptionReason Reason { get; }
    public DateTimeOffset DetectedAt { get; }
    public bool RequeueFromLastCheckpoint { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class RuntimeDomainRetryRequest
{
    public RuntimeDomainRetryRequest(
        string workflowExecutionId,
        string? activityExecutionId,
        string failureType,
        int failureCount,
        DateTimeOffset requestedAt,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureType);

        if (failureCount < 0)
            throw new ArgumentOutOfRangeException(nameof(failureCount), "Failure count cannot be negative.");

        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        FailureType = failureType;
        FailureCount = failureCount;
        RequestedAt = requestedAt;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string WorkflowExecutionId { get; }
    public string? ActivityExecutionId { get; }
    public string FailureType { get; }
    public int FailureCount { get; }
    public DateTimeOffset RequestedAt { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class RuntimeDomainRetryDecision
{
    public RuntimeDomainRetryDecision(
        RuntimeDomainRetryMode mode,
        TimeSpan? delay,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (mode == RuntimeDomainRetryMode.RetryAfter && delay is null)
            throw new ArgumentException("RetryAfter decisions require a delay.", nameof(delay));

        if (mode == RuntimeDomainRetryMode.RetryAfter && delay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay), "RetryAfter delay must be greater than zero.");

        if (mode != RuntimeDomainRetryMode.RetryAfter && delay is not null)
            throw new ArgumentException("Only RetryAfter decisions can carry a delay.", nameof(delay));

        Mode = mode;
        Delay = delay;
        Reason = reason;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public RuntimeDomainRetryMode Mode { get; }
    public TimeSpan? Delay { get; }
    public string Reason { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public enum RuntimeDomainRetryMode
{
    DoNotRetry,
    RetryNow,
    RetryAfter,
    Fault
}
