namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimeRecoveryScanRequest
{
    // Keep the pre-paging constructors as real metadata so already compiled scanners and hosts can continue to
    // construct requests. The continuation-bearing overload below is additive; optional parameters alone would
    // preserve source compatibility but would remove the old constructor signatures from the binary surface.
    public RuntimeRecoveryScanRequest(
        DateTimeOffset now,
        TimeSpan leaseTimeout,
        TimeSpan heartbeatTimeout,
        int limit)
        : this(now, leaseTimeout, heartbeatTimeout, limit, null, null)
    {
    }

    public RuntimeRecoveryScanRequest(
        DateTimeOffset now,
        TimeSpan leaseTimeout,
        TimeSpan heartbeatTimeout,
        int limit,
        string? ownerId)
        : this(now, leaseTimeout, heartbeatTimeout, limit, ownerId, null)
    {
    }

    public RuntimeRecoveryScanRequest(
        DateTimeOffset now,
        TimeSpan leaseTimeout,
        TimeSpan heartbeatTimeout,
        int limit,
        string? ownerId = null,
        string? continuationToken = null)
    {
        if (leaseTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseTimeout), "Lease timeout must be greater than zero.");

        if (heartbeatTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(heartbeatTimeout), "Heartbeat timeout must be greater than zero.");

        limit = RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));

        if (ownerId is not null && string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("Recovery scan owner filter cannot be blank.", nameof(ownerId));

        RuntimeStorePageRequest.ValidateContinuationToken(continuationToken, nameof(continuationToken));

        Now = now;
        LeaseTimeout = leaseTimeout;
        HeartbeatTimeout = heartbeatTimeout;
        Limit = limit;
        OwnerId = ownerId;
        ContinuationToken = continuationToken;
    }

    public DateTimeOffset Now { get; }
    public TimeSpan LeaseTimeout { get; }
    public TimeSpan HeartbeatTimeout { get; }
    public int Limit { get; }
    public string? OwnerId { get; }

    /// <summary>
    /// Opaque continuation returned by <see cref="IRuntimeRecoveryScanner.ScanPageAsync"/>.
    /// </summary>
    public string? ContinuationToken { get; }
}

/// <summary>
/// One bounded recovery result page.
/// </summary>
/// <remarks>
/// Recovery traversals may need to advance provider cursors past terminal, held, or overlapping rows before
/// finding another candidate. Unlike a generic store page, an empty recovery page may carry a continuation so the
/// caller can make bounded progress without forcing the scanner to materialize or drain the whole population.
/// </remarks>
public sealed record RuntimeRecoveryPage
{
    public RuntimeRecoveryPage(
        RuntimeRecoveryScanRequest request,
        IReadOnlyList<RuntimeRecoveryCandidate> items,
        string? nextContinuationToken = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count > request.Limit)
            throw new ArgumentException("A recovery page cannot exceed its requested limit.", nameof(items));

        nextContinuationToken = RuntimeStorePageRequest.ValidateContinuationToken(
            nextContinuationToken,
            nameof(nextContinuationToken));
        if (nextContinuationToken is not null &&
            StringComparer.Ordinal.Equals(request.ContinuationToken, nextContinuationToken))
        {
            throw new ArgumentException("A recovery continuation must advance the traversal.", nameof(nextContinuationToken));
        }

        Items = items;
        NextContinuationToken = nextContinuationToken;
    }

    public IReadOnlyList<RuntimeRecoveryCandidate> Items { get; }
    public string? NextContinuationToken { get; }
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
