using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Provider-neutral compare-and-set rules shared by the in-memory and EF work stores.</summary>
public static class BackgroundActivityWorkLifecycle
{
    public static BackgroundActivityWorkItem CreatePending(BackgroundActivityWorkRequest request, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(request, BackgroundActivityWorkStatus.Pending, 0, 0, recordedAt, recordedAt);
    }

    public static BackgroundActivityWorkItem? TryClaim(
        BackgroundActivityWorkItem current,
        string ownerId,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        out BackgroundActivityWorkClaim? claim)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ValidateLease(visibilityTimeout);
        claim = null;
        var eligible = current.Status == BackgroundActivityWorkStatus.Pending ||
                       current.Status == BackgroundActivityWorkStatus.Claimed && current.VisibleUntil <= now;
        if (!eligible)
            return null;

        var fence = checked(current.ClaimFence + 1);
        var revision = checked(current.Revision + 1);
        var visibleUntil = now.Add(visibilityTimeout);
        claim = new BackgroundActivityWorkClaim(
            current.Request.WorkflowExecutionId,
            current.Request.WorkId,
            ownerId,
            fence,
            now,
            visibleUntil,
            revision);
        return current with
        {
            Status = BackgroundActivityWorkStatus.Claimed,
            ClaimOwnerId = ownerId,
            ClaimFence = fence,
            ClaimedAt = now,
            VisibleUntil = visibleUntil,
            Revision = revision,
            UpdatedAt = now
        };
    }

    public static BackgroundActivityWorkItem? TryRenewClaim(
        BackgroundActivityWorkItem current,
        BackgroundActivityWorkClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        out BackgroundActivityWorkClaim? renewedClaim)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(claim);
        ValidateLease(visibilityTimeout);
        renewedClaim = null;
        if (current.Status != BackgroundActivityWorkStatus.Claimed || now >= current.VisibleUntil || !IsExactActiveClaim(current, claim))
            return null;

        var visibleUntil = now.Add(visibilityTimeout);
        var updated = current with { VisibleUntil = visibleUntil, Revision = checked(current.Revision + 1), UpdatedAt = now };
        renewedClaim = new BackgroundActivityWorkClaim(
            current.Request.WorkflowExecutionId,
            current.Request.WorkId,
            claim.ClaimOwnerId,
            claim.ClaimFence,
            claim.ClaimedAt,
            visibleUntil,
            updated.Revision);
        return updated;
    }

    public static BackgroundActivityWorkItem RecordResult(
        BackgroundActivityWorkItem current,
        BackgroundActivityWorkClaim claim,
        BackgroundActivityWorkTerminalResult result,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);
        var digest = BackgroundActivityWorkReceipt.ComputeDigest(current.Request, result);

        if (current.Status is BackgroundActivityWorkStatus.ResultReady or BackgroundActivityWorkStatus.Accepted)
        {
            if (IsRecordedClaim(current, claim) && StringComparer.Ordinal.Equals(current.ReceiptDigest, digest))
                return current;
            throw Conflict(current, "a different or unowned terminal receipt is already recorded");
        }

        if (current.Status == BackgroundActivityWorkStatus.Cancelled)
            throw Conflict(current, "the cancellation tombstone fenced this receipt");
        if (current.Status != BackgroundActivityWorkStatus.Claimed || now >= current.VisibleUntil || !IsExactActiveClaim(current, claim))
            throw Conflict(current, "the worker claim is stale, expired, or superseded");

        return current with
        {
            Status = BackgroundActivityWorkStatus.ResultReady,
            ClaimOwnerId = null,
            ClaimedAt = null,
            VisibleUntil = null,
            Revision = checked(current.Revision + 1),
            UpdatedAt = now,
            Result = result,
            ReceiptDigest = digest,
            ResultClaimOwnerId = claim.ClaimOwnerId,
            ResultClaimFence = claim.ClaimFence,
            ResultClaimedAt = claim.ClaimedAt,
            ResultClaimVisibleUntil = claim.VisibleUntil,
            ResultClaimRevision = claim.Revision
        };
    }

    public static BackgroundActivityWorkItem Accept(
        BackgroundActivityWorkItem current,
        BackgroundActivityWorkAcceptance acceptance)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(acceptance);
        if (!StringComparer.Ordinal.Equals(current.Request.WorkflowExecutionId, acceptance.WorkflowExecutionId))
            throw Conflict(current, "acceptance names another workflow execution");
        if (!StringComparer.Ordinal.Equals(current.Request.WorkId, acceptance.WorkId))
            throw Conflict(current, "acceptance names another work item");
        if (!StringComparer.Ordinal.Equals(current.ReceiptDigest, acceptance.ExpectedReceiptDigest))
            throw Conflict(current, "acceptance does not name the exact recorded receipt");
        if (current.Status == BackgroundActivityWorkStatus.Accepted)
            return current;
        if (current.Status != BackgroundActivityWorkStatus.ResultReady)
            throw Conflict(current, "only a ready receipt can be accepted");

        return current with
        {
            Status = BackgroundActivityWorkStatus.Accepted,
            Revision = checked(current.Revision + 1),
            UpdatedAt = acceptance.AcceptedAt,
            AcceptedAt = acceptance.AcceptedAt
        };
    }

    public static BackgroundActivityWorkItem Cancel(BackgroundActivityWorkItem current, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Status is BackgroundActivityWorkStatus.Accepted or BackgroundActivityWorkStatus.Cancelled)
            return current;

        return current with
        {
            Status = BackgroundActivityWorkStatus.Cancelled,
            ClaimFence = checked(current.ClaimFence + 1),
            ClaimOwnerId = null,
            ClaimedAt = null,
            VisibleUntil = null,
            Revision = checked(current.Revision + 1),
            UpdatedAt = now,
            CancelledAt = now
        };
    }

    private static bool IsExactActiveClaim(BackgroundActivityWorkItem current, BackgroundActivityWorkClaim claim) =>
        StringComparer.Ordinal.Equals(current.Request.WorkflowExecutionId, claim.WorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(current.Request.WorkId, claim.WorkId) &&
        StringComparer.Ordinal.Equals(current.ClaimOwnerId, claim.ClaimOwnerId) &&
        current.ClaimFence == claim.ClaimFence &&
        current.Revision == claim.Revision &&
        current.ClaimedAt == claim.ClaimedAt &&
        current.VisibleUntil == claim.VisibleUntil;

    private static bool IsRecordedClaim(BackgroundActivityWorkItem current, BackgroundActivityWorkClaim claim) =>
        StringComparer.Ordinal.Equals(current.Request.WorkflowExecutionId, claim.WorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(current.Request.WorkId, claim.WorkId) &&
        StringComparer.Ordinal.Equals(current.ResultClaimOwnerId, claim.ClaimOwnerId) &&
        current.ResultClaimFence == claim.ClaimFence &&
        current.ResultClaimedAt == claim.ClaimedAt &&
        current.ResultClaimVisibleUntil == claim.VisibleUntil &&
        current.ResultClaimRevision == claim.Revision;

    private static BackgroundActivityWorkConflictException Conflict(BackgroundActivityWorkItem item, string reason) =>
        new(item.Request.WorkflowExecutionId, item.Request.WorkId, reason);

    private static void ValidateLease(TimeSpan visibilityTimeout)
    {
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "A worker claim visibility timeout must be positive.");
    }
}
