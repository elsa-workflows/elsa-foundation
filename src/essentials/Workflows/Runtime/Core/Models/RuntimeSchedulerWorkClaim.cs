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
    /// <summary>
    /// How long a single claim renewal keeps the item hidden. The drainer renews on a cadence of one third of this
    /// while the handler runs, so this bounds the window after a <em>process</em> death before the item is visible
    /// again — not how long one dispatch may run, which is <see cref="MaxDispatchDuration"/>.
    /// </summary>
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The maximum total wall time one dispatch may occupy before the drainer stops renewing its claim, cancels the
    /// dispatch, and records the item as poisoned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> <see cref="VisibilityTimeout"/> is a renewal <em>interval</em>, and the renewal loop
    /// renewed for as long as the handler ran, without limit. That protects against a process <em>dying</em> but not
    /// against one <em>hanging</em>: an activity blocked on a socket with no timeout kept its claim renewed and its
    /// execution-ownership heartbeat fresh, and those are the only two signals the recovery scanner reads — so a hung
    /// dispatch was indistinguishable from healthy work at every layer that could have detected it. Zeebe's job
    /// activation timeout and Camunda 7's lock expiry are both absolute for this reason.
    /// </para>
    /// <para>
    /// <b>Why the default is generous rather than absent.</b> A dispatch holds its workflow execution's single writer
    /// for its whole duration, so a genuinely long wait is meant to suspend on a bookmark or timer, not block a
    /// dispatch. Thirty minutes is far outside any legitimate synchronous activity while still catching a hang in
    /// bounded time. Set <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to restore the previous unbounded
    /// behavior.
    /// </para>
    /// <para>
    /// <b>What it does not do.</b> Cancellation is cooperative: an activity blocked in unmanaged code will not
    /// observe it. The deadline still stops renewal, so the claim lapses and a survivor can take the work — the
    /// store-side stays correct even when the thread does not unwind.
    /// </para>
    /// </remarks>
    public TimeSpan MaxDispatchDuration { get; set; } = TimeSpan.FromMinutes(30);
}
