namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Requests a bounded batch of exclusive, time-limited claims on due recurring-trigger occurrences.</summary>
public sealed class RecurringTriggerOccurrenceClaimRequest
{
    public RecurringTriggerOccurrenceClaimRequest(
        string ownerId,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "A recurring-trigger claim visibility timeout must be greater than zero.");
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
/// The durable in-flight marker of one recurring-trigger occurrence (#2198): a fenced, time-limited claim on a schedule
/// whose cursor (<see cref="RecurringTriggerSchedule.NextOccurrence"/>) still names the occurrence being fired. The cursor
/// moves past the occurrence only when the claim is settled, so a claimant that dies before settling leaves the
/// occurrence claimable by a peer once <see cref="VisibleAfter"/> passes.
/// </summary>
/// <remarks>
/// The owner, the fencing token and the provider revision must all still be current for a renewal, settlement or
/// release to succeed. Any other change to the schedule row (a re-claim by a peer, a deactivation, a delete) makes the
/// claim stale.
/// </remarks>
public sealed class RecurringTriggerOccurrenceClaim
{
    public RecurringTriggerOccurrenceClaim(
        RecurringTriggerSchedule schedule,
        string ownerId,
        long fencingToken,
        long revision,
        DateTimeOffset claimedAt,
        DateTimeOffset visibleAfter,
        int failureCount)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (fencingToken <= 0)
            throw new ArgumentOutOfRangeException(nameof(fencingToken), "A recurring-trigger claim fencing token must be positive.");
        if (revision <= 0)
            throw new ArgumentOutOfRangeException(nameof(revision), "A recurring-trigger claim revision must be positive.");
        if (visibleAfter <= claimedAt)
            throw new ArgumentOutOfRangeException(nameof(visibleAfter), "A recurring-trigger claim visibility deadline must follow the claim time.");
        if (failureCount < 0)
            throw new ArgumentOutOfRangeException(nameof(failureCount), "A recurring-trigger claim failure count cannot be negative.");

        Schedule = schedule;
        OwnerId = ownerId;
        FencingToken = fencingToken;
        Revision = revision;
        ClaimedAt = claimedAt;
        VisibleAfter = visibleAfter;
        FailureCount = failureCount;
    }

    /// <summary>The schedule as it was claimed. Its <see cref="RecurringTriggerSchedule.NextOccurrence"/> is the occurrence in flight.</summary>
    public RecurringTriggerSchedule Schedule { get; }

    public string OwnerId { get; }
    public long FencingToken { get; }
    public long Revision { get; }
    public DateTimeOffset ClaimedAt { get; }

    /// <summary>When the lease lapses: from then on a peer may claim the occurrence and fire it again.</summary>
    public DateTimeOffset VisibleAfter { get; }

    /// <summary>How many earlier attempts at this occurrence were released after a failed fire.</summary>
    public int FailureCount { get; }
}
