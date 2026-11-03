using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Agent.Specialists.Review.Contracts;
using Elsa.Agent.Specialists.Review.Models;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Locking.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Agent.Specialists.Review.Services;

/// <summary>Bounds for issued decision envelopes, set only through <c>SpecialistsReviewFeature</c>.</summary>
internal sealed class HumanReviewOptions
{
    /// <summary>How long an issued decision may wait for native admission. Defaults to fifteen minutes.</summary>
    public TimeSpan DecisionLifetime { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Resolves the exact pinned review wait under the caller's tenant, rechecks the decider's current
/// permission and dispatches one protected decision to the exact saved bookmark. Every receipt is
/// reconciled from native records (the consumed trigger delivery and queued resume), never from a
/// module-owned approval store. A per-wait lock serializes submits; native admission alone decides which
/// decision consumes the wait.
/// </summary>
internal sealed partial class HumanReviewService(IWorkflowExecutionStateStore workflows, IActivityExecutionStateStore activities,
    IBookmarkStateStore bookmarks, IBookmarkResumeDispatcher dispatcher, IWorkflowSchedulerWorkQueue queue, IDistributedLockProvider locks,
    ICurrentSubjectPermissionAuthorizer subjects, ReviewDecisionCodec codec, HumanReviewOptions options, TimeProvider time) : IHumanReviews
{
    private const string RequestedBy = "elsa.agent.human-review";
    private const int MaximumQueuedItems = 1_000;

    public async ValueTask<HumanReviewView?> GetAsync(HumanReviewReference reference, ReviewDecider caller, CancellationToken cancellationToken = default)
    {
        var located = await LocateAsync(reference.WorkflowExecutionId, reference.ActivityExecutionId, caller, cancellationToken);
        return located is null ? null : View(located.Value.Activity, located.Value.Review);
    }

    public async ValueTask<ReviewDecisionReceipt> DecideAsync(ReviewDecisionCommand command, ReviewDecider decider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(decider);
        if (!RequestIdPattern().IsMatch(command.RequestId ?? string.Empty) || !Enum.IsDefined(command.Decision) ||
            command.Comment?.Length > HumanReviewIds.MaximumTextCharacters || string.IsNullOrWhiteSpace(command.ContentHash))
            return new(ReviewDecisionReceiptStatus.Conflict, "The decision request is malformed.");
        try
        {
            await using var handle = await locks.TryAcquireLockAsync(
                $"elsa.agent.human-review:{command.WorkflowExecutionId}:{command.ActivityExecutionId}", TimeSpan.FromSeconds(10), cancellationToken);
            if (handle is null) return new(ReviewDecisionReceiptStatus.Unavailable, "Another decision for this review is being submitted.");
            return await DecideLockedAsync(command, decider, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HumanReviewException exception) when (!exception.IsUnavailable)
        {
            return new(ReviewDecisionReceiptStatus.NotFound, Reason: exception.Message);
        }
        catch (Elsa.Primitives.Exceptions.SchemaWriteRefusedException) { throw; } // the API answers the framework 409 with its dormancy detail
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(ReviewDecisionReceiptStatus.Unavailable, "A review dependency is temporarily unavailable; retry with the same request ID.");
        }
    }

    private async ValueTask<ReviewDecisionReceipt> DecideLockedAsync(ReviewDecisionCommand command, ReviewDecider decider, CancellationToken cancellationToken)
    {
        var located = await LocateAsync(command.WorkflowExecutionId, command.ActivityExecutionId, decider, cancellationToken);
        if (located is not { } found) return new(ReviewDecisionReceiptStatus.NotFound, "No visible human review matches.");
        var (activity, review) = found;
        if (!string.Equals(command.ContentHash, review.ContentHash, StringComparison.Ordinal))
            return new(ReviewDecisionReceiptStatus.Conflict, "The content changed; this decision targeted another wait.");

        if (Admitted(activity) is { } admitted)
            return Reconcile(admitted, command, decider);
        if (activity.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or
            ActivityExecutionStatus.Recovered or ActivityExecutionStatus.Superseded)
            return new(ReviewDecisionReceiptStatus.Closed, "The review ended without a decision.");

        var bookmark = await FindWaitAsync(activity, review, cancellationToken);
        if (bookmark is null) return new(ReviewDecisionReceiptStatus.Unavailable, "The review wait is not committed yet.");
        var idempotencyKey = $"{review.WorkflowExecutionId}:bookmark-resume:{bookmark.BookmarkId}:{HumanReviewIds.StimulusType}:{review.WaitId}:{command.RequestId}";
        var queued = await QueuedAsync(review, idempotencyKey, cancellationToken);
        if (queued is { } earlier)
            return earlier.SameContent(Claims(command, decider, review, bookmark.BookmarkId, earlier.IssuedAt))
                ? new(ReviewDecisionReceiptStatus.Pending)
                : new(ReviewDecisionReceiptStatus.Conflict, "This request ID already carries a different decision.");

        var current = await subjects.AuthorizeAsync(new(decider.TenantId, decider.ActorId, HumanReviewIds.DecidePermission), cancellationToken);
        if (current.Status != CurrentSubjectPermissionStatus.Granted)
            return new(ReviewDecisionReceiptStatus.Forbidden, "You do not currently hold the human review permission.");

        var claims = Claims(command, decider, review, bookmark.BookmarkId, time.GetUtcNow());
        var input = JsonSerializer.SerializeToElement(new ReviewDecisionDelivery(HumanReviewIds.FormatVersion, codec.Protect(claims)), HumanReviewJson.Runtime);
        var dispatched = await dispatcher.DispatchAsync(new BookmarkResumeDispatchRequest(review.WorkflowExecutionId, HumanReviewIds.StimulusType,
            review.WaitId, input, command.RequestId, RequestedBy, null, HumanReviewRecords.PayloadType, HumanReviewIds.ProviderId,
            bookmark.BookmarkId, null), cancellationToken: cancellationToken);
        if (dispatched.WorkflowExecutionId != review.WorkflowExecutionId ||
            dispatched.Bookmark is { } resolved && resolved.BookmarkId != bookmark.BookmarkId)
            throw HumanReviewException.Invalid("The dispatcher resolved a different wait.");

        // No dispatch status is itself an acknowledgement: reconcile from the native records it produced. Pending is
        // reported only while the decision is provably owed; a Duplicate the actor remembers but nothing retains
        // means native admission already refused this request.
        if (dispatched.Status is not (BookmarkResumeDispatchStatus.Dispatched or BookmarkResumeDispatchStatus.Duplicate or BookmarkResumeDispatchStatus.NotFound))
            return new(ReviewDecisionReceiptStatus.Unavailable, "The decision could not be queued now; retry with the same request ID.");
        // Scan the queue before reading the activity: admission consumes the work item and records the delivery in one
        // commit, so a decision that leaves the queue is always visible on the activity read that follows.
        var stillQueued = await QueuedAsync(review, idempotencyKey, cancellationToken) is not null;
        var after = await activities.FindAsync(review.WorkflowExecutionId, review.ActivityExecutionId, cancellationToken) ?? activity;
        if (Admitted(after) is { } settled) return Reconcile(settled, command, decider);
        // A terminal activity can never admit, even while a residual queued resume awaits the sweep's purge.
        if (after.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or
            ActivityExecutionStatus.Recovered or ActivityExecutionStatus.Superseded)
            return new(ReviewDecisionReceiptStatus.Closed, "The review wait is no longer open.");
        if (stillQueued) return new(ReviewDecisionReceiptStatus.Pending);
        if (dispatched.Status == BookmarkResumeDispatchStatus.NotFound)
            return new(ReviewDecisionReceiptStatus.Closed, "The review wait is no longer open.");
        return new(ReviewDecisionReceiptStatus.Refused, "This decision was not admitted; check your permission and submit a new request ID.");
    }

    private static ReviewDecisionReceipt Reconcile(AdmittedReviewDecision admitted, ReviewDecisionCommand command, ReviewDecider decider)
    {
        if (admitted.RequestId != command.RequestId) return new(ReviewDecisionReceiptStatus.AlreadyResolved, "Another decision was already admitted.");
        var comment = string.IsNullOrWhiteSpace(command.Comment) ? null : command.Comment;
        return admitted.ActorId == decider.ActorId && admitted.Decision == command.Decision && admitted.Comment == comment
            ? new(ReviewDecisionReceiptStatus.Accepted)
            : new(ReviewDecisionReceiptStatus.Conflict, "This request ID already carries a different decision.");
    }

    private async ValueTask<(ActivityExecutionState Activity, HumanReviewState Review)?> LocateAsync(string workflowExecutionId, string activityExecutionId,
        ReviewDecider caller, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workflowExecutionId) || string.IsNullOrWhiteSpace(activityExecutionId)) return null;
        var parent = await workflows.FindAsync(workflowExecutionId, cancellationToken);
        // A foreign tenant's review is indistinguishable from a missing one.
        if (parent is null || parent.WorkflowExecutionId != workflowExecutionId || parent.TenantId != caller.TenantId) return null;
        var activity = await activities.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        if (activity is null || HumanReviewRecords.ReadPinned(activity, parent.TenantId!, parent.Partition?.Value) is not { } review) return null;
        return review.TenantId == caller.TenantId && review.Partition == parent.Partition?.Value ? (activity, review) : null;
    }

    private async ValueTask<BookmarkState?> FindWaitAsync(ActivityExecutionState activity, HumanReviewState review, CancellationToken cancellationToken)
    {
        BookmarkState? match = null;
        foreach (var bookmarkId in activity.BookmarkIds)
        {
            var bookmark = await bookmarks.FindAsync(review.WorkflowExecutionId, bookmarkId, cancellationToken);
            if (bookmark is null || bookmark.ActivityExecutionId != review.ActivityExecutionId || bookmark.StimulusType != HumanReviewIds.StimulusType ||
                bookmark.StimulusHash != review.WaitId) continue;
            if (match is not null) throw HumanReviewException.Invalid("The review has more than one matching wait.");
            match = bookmark;
        }
        return match;
    }

    /// <summary>A queued, not yet admitted decision carrying this request ID, found by a bounded per-workflow scan.</summary>
    private async ValueTask<ReviewDecisionClaims?> QueuedAsync(HumanReviewState review, string idempotencyKey, CancellationToken cancellationToken)
    {
        var seen = 0;
        string? token = null;
        do
        {
            var page = await queue.ListAsync(new RuntimeSchedulerWorkQuery(review.WorkflowExecutionId, RuntimeStorePageRequest.MaximumLimit, token), cancellationToken);
            foreach (var item in page.Items)
            {
                if (++seen > MaximumQueuedItems) throw HumanReviewException.Unavailable("The workflow's queued work exceeds the reconciliation bound.");
                if (item.IdempotencyKey != idempotencyKey || item.Payload is not { ValueKind: JsonValueKind.Object } payload) continue;
                var input = payload.EnumerateObject().FirstOrDefault(property => property.NameEquals("input") || property.NameEquals("Input")).Value;
                return HumanReviewRecords.ReadDelivery(codec, input.ValueKind == JsonValueKind.Undefined ? null : input);
            }
            token = page.NextContinuationToken;
        } while (token is not null);
        return null;
    }

    private AdmittedReviewDecision? Admitted(ActivityExecutionState activity) => HumanReviewRecords.Admitted(activity, codec);

    private HumanReviewView View(ActivityExecutionState activity, HumanReviewState review)
    {
        var admitted = Admitted(activity);
        var status = admitted is not null
            ? admitted.Decision == ReviewDecision.Approve ? HumanReviewStatus.Approved : HumanReviewStatus.Rejected
            : activity.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or
                ActivityExecutionStatus.Recovered or ActivityExecutionStatus.Superseded
                ? HumanReviewStatus.Closed
                : HumanReviewStatus.Pending;
        return new(review.WorkflowExecutionId, review.ActivityExecutionId, review.Title, review.Content, review.Notes, review.ContentHash,
            status, admitted?.ActorId, admitted?.Comment, admitted?.RequestId);
    }

    private ReviewDecisionClaims Claims(ReviewDecisionCommand command, ReviewDecider decider, HumanReviewState review, string bookmarkId, DateTimeOffset issuedAt) =>
        new(ReviewDecisionCodec.Purpose, ReviewDecisionCodec.Codec, review.TenantId, review.Partition, review.WorkflowExecutionId,
            review.ActivityExecutionId, review.ExecutableNodeId, review.WaitId, bookmarkId, review.ContentHash, decider.ActorId,
            command.Decision, string.IsNullOrWhiteSpace(command.Comment) ? null : command.Comment, command.RequestId, issuedAt,
            issuedAt + options.DecisionLifetime);

    [GeneratedRegex("^[A-Za-z0-9._:-]{8,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex RequestIdPattern();
}
