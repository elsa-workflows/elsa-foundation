using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Agent.Specialists.Editorial.Contracts;
using Elsa.Agent.Specialists.Editorial.Models;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Locking.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Agent.Specialists.Editorial.Services;

/// <summary>Bounds for issued decision envelopes.</summary>
public sealed class EditorialReviewOptions
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
internal sealed partial class EditorialReviewService(IWorkflowExecutionStateStore workflows, IActivityExecutionStateStore activities,
    IBookmarkStateStore bookmarks, IBookmarkResumeDispatcher dispatcher, IWorkflowSchedulerWorkQueue queue, IDistributedLockProvider locks,
    ICurrentSubjectPermissionAuthorizer subjects, EditorialDecisionCodec codec, EditorialReviewOptions options, TimeProvider time) : IEditorialReviews
{
    private const string RequestedBy = "elsa.agent.editorial-review";
    private const int MaximumQueuedItems = 1_000;

    public async ValueTask<EditorialReviewView?> GetAsync(EditorialReviewReference reference, EditorialDecider caller, CancellationToken cancellationToken = default)
    {
        var located = await LocateAsync(reference.WorkflowExecutionId, reference.ActivityExecutionId, caller, cancellationToken);
        return located is null ? null : View(located.Value.Activity, located.Value.Review);
    }

    public async ValueTask<EditorialDecisionReceipt> DecideAsync(EditorialDecisionCommand command, EditorialDecider decider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(decider);
        if (!RequestIdPattern().IsMatch(command.RequestId ?? string.Empty) || !Enum.IsDefined(command.Decision) ||
            command.Comment?.Length > EditorialReviewIds.MaximumCommentCharacters || string.IsNullOrWhiteSpace(command.ArticleHash))
            return new(EditorialDecisionReceiptStatus.Conflict, Reason: "The decision request is malformed.");
        try
        {
            await using var handle = await locks.TryAcquireLockAsync(
                $"elsa.agent.editorial-review:{command.WorkflowExecutionId}:{command.ActivityExecutionId}", TimeSpan.FromSeconds(10), cancellationToken);
            if (handle is null) return new(EditorialDecisionReceiptStatus.Unavailable, Reason: "Another decision for this review is being submitted.");
            return await DecideLockedAsync(command, decider, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (EditorialReviewException exception) when (!exception.IsUnavailable)
        {
            return new(EditorialDecisionReceiptStatus.NotFound, Reason: exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(EditorialDecisionReceiptStatus.Unavailable, Reason: "A review dependency is temporarily unavailable; retry with the same request ID.");
        }
    }

    private async ValueTask<EditorialDecisionReceipt> DecideLockedAsync(EditorialDecisionCommand command, EditorialDecider decider, CancellationToken cancellationToken)
    {
        var located = await LocateAsync(command.WorkflowExecutionId, command.ActivityExecutionId, decider, cancellationToken);
        if (located is not { } found) return new(EditorialDecisionReceiptStatus.NotFound, Reason: "No visible editorial review matches.");
        var (activity, review) = found;
        if (command.Round != review.Round || !string.Equals(command.ArticleHash, review.ArticleHash, StringComparison.Ordinal))
            return new(EditorialDecisionReceiptStatus.Conflict, View(activity, review), "The article or round changed; this decision targeted another wait.");

        if (Admitted(activity) is { } admitted)
            return Reconcile(admitted, command, decider, activity, review);
        if (activity.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or
            ActivityExecutionStatus.Recovered or ActivityExecutionStatus.Superseded)
            return new(EditorialDecisionReceiptStatus.Closed, View(activity, review), "The review ended without a decision.");

        var bookmark = await FindWaitAsync(activity, review, cancellationToken);
        if (bookmark is null) return new(EditorialDecisionReceiptStatus.Unavailable, View(activity, review), "The review wait is not committed yet.");
        var idempotencyKey = $"{review.WorkflowExecutionId}:bookmark-resume:{bookmark.BookmarkId}:{EditorialReviewIds.StimulusType}:{review.WaitId}:{command.RequestId}";
        var queued = await QueuedAsync(review, idempotencyKey, cancellationToken);
        if (queued is { } earlier)
            return earlier.SameContent(Claims(command, decider, review, bookmark.BookmarkId, earlier.IssuedAt))
                ? new(EditorialDecisionReceiptStatus.Pending, View(activity, review))
                : new(EditorialDecisionReceiptStatus.Conflict, View(activity, review), "This request ID already carries a different decision.");

        var current = await subjects.AuthorizeAsync(new(decider.TenantId, decider.ActorId, EditorialReviewIds.DecidePermission), cancellationToken);
        if (current.Status != CurrentSubjectPermissionStatus.Granted)
            return new(EditorialDecisionReceiptStatus.Forbidden, Reason: "You do not currently hold the editorial review permission.");

        var claims = Claims(command, decider, review, bookmark.BookmarkId, time.GetUtcNow());
        var input = JsonSerializer.SerializeToElement(new EditorialDecisionDelivery(EditorialReviewIds.FormatVersion, codec.Protect(claims)));
        var dispatched = await dispatcher.DispatchAsync(new BookmarkResumeDispatchRequest(review.WorkflowExecutionId, EditorialReviewIds.StimulusType,
            review.WaitId, input, command.RequestId, RequestedBy, null, EditorialReviewRecords.PayloadType, EditorialReviewIds.ProviderId,
            bookmark.BookmarkId, null), cancellationToken: cancellationToken);
        if (dispatched.WorkflowExecutionId != review.WorkflowExecutionId ||
            dispatched.Bookmark is { } resolved && resolved.BookmarkId != bookmark.BookmarkId)
            throw EditorialReviewException.Invalid("The dispatcher resolved a different wait.");

        // No dispatch status is itself an acknowledgement: reconcile from the native records it produced.
        var after = await activities.FindAsync(review.WorkflowExecutionId, review.ActivityExecutionId, cancellationToken) ?? activity;
        if (Admitted(after) is { } settled) return Reconcile(settled, command, decider, after, review);
        return dispatched.Status is BookmarkResumeDispatchStatus.Dispatched or BookmarkResumeDispatchStatus.Duplicate
            ? new(EditorialDecisionReceiptStatus.Pending, View(after, review))
            : dispatched.Status == BookmarkResumeDispatchStatus.NotFound
                ? new(EditorialDecisionReceiptStatus.Closed, View(after, review), "The review wait is no longer open.")
                : new(EditorialDecisionReceiptStatus.Unavailable, View(after, review), "The decision could not be queued now; retry with the same request ID.");
    }

    private EditorialDecisionReceipt Reconcile(EditorialAdmittedDecision admitted, EditorialDecisionCommand command, EditorialDecider decider,
        ActivityExecutionState activity, EditorialReviewState review)
    {
        var view = View(activity, review);
        if (admitted.RequestId != command.RequestId) return new(EditorialDecisionReceiptStatus.AlreadyResolved, view, "Another decision was already admitted.");
        var comment = string.IsNullOrWhiteSpace(command.Comment) ? null : command.Comment;
        return admitted.ActorId == decider.ActorId && admitted.Decision == command.Decision && admitted.Comment == comment
            ? new(EditorialDecisionReceiptStatus.Accepted, view)
            : new(EditorialDecisionReceiptStatus.Conflict, view, "This request ID already carries a different decision.");
    }

    private async ValueTask<(ActivityExecutionState Activity, EditorialReviewState Review)?> LocateAsync(string workflowExecutionId, string activityExecutionId,
        EditorialDecider caller, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workflowExecutionId) || string.IsNullOrWhiteSpace(activityExecutionId)) return null;
        var parent = await workflows.FindAsync(workflowExecutionId, cancellationToken);
        // A foreign tenant's review is indistinguishable from a missing one.
        if (parent is null || parent.WorkflowExecutionId != workflowExecutionId || parent.TenantId != caller.TenantId) return null;
        var activity = await activities.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        if (activity is null || EditorialReviewRecords.ReadPinned(activity, parent.TenantId!, parent.Partition?.Value) is not { } review) return null;
        return review.TenantId == caller.TenantId && review.Partition == parent.Partition?.Value ? (activity, review) : null;
    }

    private async ValueTask<BookmarkState?> FindWaitAsync(ActivityExecutionState activity, EditorialReviewState review, CancellationToken cancellationToken)
    {
        BookmarkState? match = null;
        foreach (var bookmarkId in activity.BookmarkIds)
        {
            var bookmark = await bookmarks.FindAsync(review.WorkflowExecutionId, bookmarkId, cancellationToken);
            if (bookmark is null || bookmark.ActivityExecutionId != review.ActivityExecutionId || bookmark.StimulusType != EditorialReviewIds.StimulusType ||
                bookmark.StimulusHash != review.WaitId) continue;
            if (match is not null) throw EditorialReviewException.Invalid("The review has more than one matching wait.");
            match = bookmark;
        }
        return match;
    }

    /// <summary>A queued, not yet admitted decision carrying this request ID, found by a bounded per-workflow scan.</summary>
    private async ValueTask<EditorialDecisionClaims?> QueuedAsync(EditorialReviewState review, string idempotencyKey, CancellationToken cancellationToken)
    {
        var seen = 0;
        string? token = null;
        do
        {
            var page = await queue.ListAsync(new RuntimeSchedulerWorkQuery(review.WorkflowExecutionId, RuntimeStorePageRequest.MaximumLimit, token), cancellationToken);
            foreach (var item in page.Items)
            {
                if (++seen > MaximumQueuedItems) throw EditorialReviewException.Unavailable("The workflow's queued work exceeds the reconciliation bound.");
                if (item.IdempotencyKey != idempotencyKey || item.Payload is not { ValueKind: JsonValueKind.Object } payload) continue;
                var input = payload.EnumerateObject().FirstOrDefault(property => property.NameEquals("input") || property.NameEquals("Input")).Value;
                return EditorialReviewRecords.ReadDelivery(codec, input.ValueKind == JsonValueKind.Undefined ? null : input);
            }
            token = page.NextContinuationToken;
        } while (token is not null);
        return null;
    }

    private EditorialAdmittedDecision? Admitted(ActivityExecutionState activity) => EditorialReviewRecords.Admitted(activity, codec);

    private EditorialReviewView View(ActivityExecutionState activity, EditorialReviewState review)
    {
        var admitted = Admitted(activity);
        var status = admitted is not null
            ? admitted.Decision == EditorialDecision.Approve ? EditorialReviewStatus.Approved : EditorialReviewStatus.Rejected
            : activity.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or
                ActivityExecutionStatus.Recovered or ActivityExecutionStatus.Superseded
                ? EditorialReviewStatus.Closed
                : EditorialReviewStatus.Pending;
        return new(review.WorkflowExecutionId, review.ActivityExecutionId, review.Round, review.ArticleHash, review.Article, review.ReviewComments,
            status, admitted?.ActorId, admitted?.Comment, admitted?.RequestId);
    }

    private EditorialDecisionClaims Claims(EditorialDecisionCommand command, EditorialDecider decider, EditorialReviewState review, string bookmarkId, DateTimeOffset issuedAt) =>
        new(EditorialDecisionCodec.Purpose, EditorialDecisionCodec.Codec, review.TenantId, review.Partition, review.WorkflowExecutionId,
            review.ActivityExecutionId, review.ExecutableNodeId, review.WaitId, bookmarkId, review.Round, review.ArticleHash, decider.ActorId,
            command.Decision, string.IsNullOrWhiteSpace(command.Comment) ? null : command.Comment, command.RequestId, issuedAt,
            issuedAt + options.DecisionLifetime);

    [GeneratedRegex("^[A-Za-z0-9._:-]{8,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex RequestIdPattern();
}
