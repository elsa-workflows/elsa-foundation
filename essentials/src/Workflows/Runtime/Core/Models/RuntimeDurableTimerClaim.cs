namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Requests a bounded batch of exclusive, time-limited due-timer claims.</summary>
public sealed class RuntimeDurableTimerClaimRequest
{
    public RuntimeDurableTimerClaimRequest(
        string ownerId,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "Durable timer visibility timeout must be greater than zero.");
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));

        OwnerId = ownerId;
        Now = now;
        VisibilityTimeout = visibilityTimeout;
        Limit = limit;
    }

    public string OwnerId { get; }
    public DateTimeOffset Now { get; }
    public TimeSpan VisibilityTimeout { get; }
    public int Limit { get; }
}

/// <summary>
/// A fenced durable-timer claim. The owner, monotonically increasing token, and provider revision must
/// all remain current for renewal, release, or completion to succeed.
/// </summary>
public sealed class RuntimeDurableTimerClaim
{
    public RuntimeDurableTimerClaim(
        DurableTimer timer,
        string ownerId,
        long fencingToken,
        long revision,
        DateTimeOffset claimedAt,
        DateTimeOffset visibleAfter,
        int failureCount)
    {
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (fencingToken <= 0)
            throw new ArgumentOutOfRangeException(nameof(fencingToken), "Durable timer claim fencing token must be positive.");
        if (revision <= 0)
            throw new ArgumentOutOfRangeException(nameof(revision), "Durable timer claim revision must be positive.");
        if (visibleAfter <= claimedAt)
            throw new ArgumentOutOfRangeException(nameof(visibleAfter), "Durable timer visibility deadline must follow the claim time.");
        if (failureCount < 0)
            throw new ArgumentOutOfRangeException(nameof(failureCount), "Durable timer failure count cannot be negative.");

        Timer = timer;
        OwnerId = ownerId;
        FencingToken = fencingToken;
        Revision = revision;
        ClaimedAt = claimedAt;
        VisibleAfter = visibleAfter;
        FailureCount = failureCount;
    }

    public DurableTimer Timer { get; }
    public string OwnerId { get; }
    public long FencingToken { get; }
    public long Revision { get; }
    public DateTimeOffset ClaimedAt { get; }
    public DateTimeOffset VisibleAfter { get; }
    public int FailureCount { get; }
}

public enum RuntimeDurableTimerClaimTransitionStatus
{
    Succeeded,
    AlreadyApplied,
    Stale
}

/// <summary>
/// Outcome of a fenced durable-timer transition. Successful renewal carries the refreshed claim;
/// release and completion do not.
/// </summary>
public sealed record RuntimeDurableTimerClaimTransitionResult(
    RuntimeDurableTimerClaimTransitionStatus Status,
    RuntimeDurableTimerClaim? Claim = null)
{
    public bool Succeeded => Status is RuntimeDurableTimerClaimTransitionStatus.Succeeded
        or RuntimeDurableTimerClaimTransitionStatus.AlreadyApplied;

    public static RuntimeDurableTimerClaimTransitionResult Applied(RuntimeDurableTimerClaim? claim = null) =>
        new(RuntimeDurableTimerClaimTransitionStatus.Succeeded, claim);

    public static RuntimeDurableTimerClaimTransitionResult AlreadyApplied { get; } =
        new(RuntimeDurableTimerClaimTransitionStatus.AlreadyApplied);

    public static RuntimeDurableTimerClaimTransitionResult Stale { get; } =
        new(RuntimeDurableTimerClaimTransitionStatus.Stale);
}
