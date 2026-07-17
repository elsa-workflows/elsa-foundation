namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Requests exclusive, time-bounded ownership of the FIFO head for one workflow execution.
/// </summary>
public sealed class RuntimeSchedulerWorkClaimRequest
{
    public RuntimeSchedulerWorkClaimRequest(
        string workflowExecutionId,
        string ownerId,
        DateTimeOffset now,
        TimeSpan visibilityTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "Scheduler work visibility timeout must be greater than zero.");

        WorkflowExecutionId = workflowExecutionId;
        OwnerId = ownerId;
        Now = now;
        VisibilityTimeout = visibilityTimeout;
    }

    public string WorkflowExecutionId { get; }
    public string OwnerId { get; }
    public DateTimeOffset Now { get; }
    public TimeSpan VisibilityTimeout { get; }
}

/// <summary>
/// A fenced scheduler-work claim. Both the monotonically increasing token and provider revision must
/// match when the claimant renews, releases, or completes the claim.
/// </summary>
public sealed class RuntimeSchedulerWorkClaim
{
    public RuntimeSchedulerWorkClaim(
        RuntimeSchedulerWorkItem item,
        string ownerId,
        long fencingToken,
        long revision,
        DateTimeOffset claimedAt,
        DateTimeOffset visibleAfter)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (fencingToken <= 0)
            throw new ArgumentOutOfRangeException(nameof(fencingToken), "Scheduler work claim fencing token must be positive.");
        if (revision <= 0)
            throw new ArgumentOutOfRangeException(nameof(revision), "Scheduler work claim revision must be positive.");
        if (visibleAfter <= claimedAt)
            throw new ArgumentOutOfRangeException(nameof(visibleAfter), "Scheduler work visibility deadline must follow the claim time.");

        Item = item;
        OwnerId = ownerId;
        FencingToken = fencingToken;
        Revision = revision;
        ClaimedAt = claimedAt;
        VisibleAfter = visibleAfter;
    }

    public RuntimeSchedulerWorkItem Item { get; }
    public string OwnerId { get; }
    public long FencingToken { get; }
    public long Revision { get; }
    public DateTimeOffset ClaimedAt { get; }
    public DateTimeOffset VisibleAfter { get; }
}

public enum RuntimeSchedulerWorkClaimTransitionStatus
{
    Succeeded,
    AlreadyApplied,
    Stale
}

/// <summary>
/// Outcome of a fenced claim transition. A successful renewal carries the refreshed claim revision
/// and visibility deadline; completion and release do not.
/// </summary>
public sealed record RuntimeSchedulerWorkClaimTransitionResult(
    RuntimeSchedulerWorkClaimTransitionStatus Status,
    RuntimeSchedulerWorkClaim? Claim = null)
{
    public bool Succeeded => Status is RuntimeSchedulerWorkClaimTransitionStatus.Succeeded
        or RuntimeSchedulerWorkClaimTransitionStatus.AlreadyApplied;

    public static RuntimeSchedulerWorkClaimTransitionResult Applied(RuntimeSchedulerWorkClaim? claim = null) =>
        new(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded, claim);

    public static RuntimeSchedulerWorkClaimTransitionResult AlreadyApplied { get; } =
        new(RuntimeSchedulerWorkClaimTransitionStatus.AlreadyApplied);

    public static RuntimeSchedulerWorkClaimTransitionResult Stale { get; } =
        new(RuntimeSchedulerWorkClaimTransitionStatus.Stale);
}

/// <summary>Controls the renewable visibility lease used while one scheduler work item is dispatched.</summary>
public sealed class RuntimeSchedulerWorkClaimOptions
{
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromMinutes(1);
}
