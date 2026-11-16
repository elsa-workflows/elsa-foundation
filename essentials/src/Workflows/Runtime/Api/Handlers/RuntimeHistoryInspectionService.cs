using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>Reauthorizes every history read before touching the scoped provider or resolving an exact target.</summary>
public sealed class RuntimeHistoryInspectionService(IWorkflowExecutionStateStore workflows,
    IActivityInspectionContextAsync authorization, TimeProvider time, IRuntimeHistoryStore? history = null,
    IBookmarkStateStore? bookmarks = null, IActivityExecutionStateStore? activities = null,
    ILogger<RuntimeHistoryInspectionService>? logger = null)
{
    // Supplied by API composition and resolved only after authorization/read. Optional capture-owner
    // outages must not hide readable retained evidence or eagerly activate writers on denied reads.
    internal Func<RuntimeHistoryCapturePolicy, bool>? CaptureAvailability { get; init; }

    public async Task<RuntimeHistoryPageView> ReadAsync(GetRuntimeHistory request, CancellationToken token = default)
    {
        var workflow = await AuthorizeAsync(request.WorkflowExecutionId, token);
        if (request.Category is { } category && !Enum.IsDefined(category))
            throw new ArgumentException("Unknown history category.", nameof(request));
        var query = new RuntimeHistoryQuery(request.WorkflowExecutionId, request.Limit, request.Cursor,
            request.ActivityExecutionId, request.IncludeDescendants, request.BookmarkId, request.Category,
            request.Name, request.Search, await BindingAsync(token));
        if (history is null) return Empty(await CoverageAsync(workflow, null, token), time.GetUtcNow());
        try
        {
            var page = await history.ReadPageAsync(query, token);
            if (page.Items.Count > query.Limit || page.UpperSequence < 0 || page.UpperSequence > (page.Head?.LastSequence ?? 0) ||
                page.ScannedThroughSequence < 0 || page.ScannedThroughSequence > page.UpperSequence || page.ScannedCount < page.Items.Count)
                throw new RuntimeHistoryIntegrityException(workflow.WorkflowExecutionId, "event page disagrees with its committed window");
            long previous = 0;
            foreach (var item in page.Items)
            {
                EnsureEventOwner(workflow.WorkflowExecutionId, item);
                EnsureEventPosition(item, page.Head);
                if (item.Sequence <= previous || item.Sequence > page.ScannedThroughSequence)
                    throw new RuntimeHistoryIntegrityException(workflow.WorkflowExecutionId, "event page positions disagree with their scanned window");
                previous = item.Sequence;
            }
            return new(await CoverageAsync(workflow, page.Head, token), page.Items.Select(RuntimeHistoryEventView.From).ToArray(),
                page.UpperSequence, page.ScannedThroughSequence, page.ScannedCount, page.NextContinuationToken,
                page.CatchUpToken, page.ScopeComplete, page.ScopeLimitation, time.GetUtcNow());
        }
        catch (RuntimeHistoryIntegrityException)
        {
            return Empty(InvalidCoverage(), time.GetUtcNow());
        }
    }

    public async Task<RuntimeHistoryEventDetailsView> FindEventAsync(GetRuntimeHistoryEvent request, CancellationToken token = default)
    {
        var workflow = await AuthorizeAsync(request.WorkflowExecutionId, token);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.HistoryEventId);
        if (history is not IRuntimeHistoryDetailsStore details) throw new RuntimeHistoryUnsupportedSignal();
        var (head, item) = await details.FindEventDetailsAsync(request.WorkflowExecutionId, request.HistoryEventId, token);
        if (item is null) throw new RuntimeResourceMissingSignal();
        EnsureEventOwner(workflow.WorkflowExecutionId, item);
        if (item.HistoryEventId != request.HistoryEventId)
            throw new RuntimeHistoryIntegrityException(workflow.WorkflowExecutionId, "event detail disagrees with the requested snapshot");
        EnsureEventPosition(item, head);
        return new(await CoverageAsync(workflow, head, token), RuntimeHistoryEventView.From(item), time.GetUtcNow());
    }

    public async Task<RuntimeHistoryBookmarkPageView> ListBookmarksAsync(GetRuntimeHistoryBookmarks request, CancellationToken token = default)
    {
        var workflow = await AuthorizeAsync(request.WorkflowExecutionId, token);
        if (request.Status is { } status && !Enum.IsDefined(status))
            throw new ArgumentException("Unknown bookmark status.", nameof(request));
        var query = new RuntimeHistoryBookmarkQuery(request.WorkflowExecutionId, request.Limit, request.Cursor,
            request.ActivityExecutionId, request.IncludeDescendants, request.Status, await BindingAsync(token));
        if (history is null) return new(await CoverageAsync(workflow, null, token), [], null, null, null, false, "History is unsupported by this host.", time.GetUtcNow());
        try
        {
            var page = await history.ListBookmarksAsync(query, token);
            var items = new List<RuntimeHistoryBookmarkView>(page.Items.Count);
            if (page.Items.Count > query.Limit)
                throw new RuntimeHistoryIntegrityException(workflow.WorkflowExecutionId, "bookmark inventory exceeds its bounded window");
            foreach (var item in page.Items)
            {
                EnsureBookmarkPosition(item, page.Head);
                items.Add(await BookmarkAsync(workflow, item, token));
            }
            return new(await CoverageAsync(workflow, page.Head, token), items, page.NextContinuationToken, page.TotalCount,
                page.ActiveCount, page.ScopeComplete, page.ScopeLimitation, time.GetUtcNow());
        }
        catch (RuntimeHistoryIntegrityException)
        {
            return new(InvalidCoverage(), [], null, null, null, false, "Bookmark history integrity could not be established.", time.GetUtcNow());
        }
    }

    public async Task<RuntimeHistoryBookmarkDetailsView> FindBookmarkAsync(GetRuntimeHistoryBookmark request, CancellationToken token = default)
    {
        var workflow = await AuthorizeAsync(request.WorkflowExecutionId, token);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BookmarkId);
        if (history is not IRuntimeHistoryDetailsStore details) throw new RuntimeHistoryUnsupportedSignal();
        var (head, item) = await details.FindBookmarkDetailsAsync(request.WorkflowExecutionId, request.BookmarkId, token);
        if (item is null) throw new RuntimeResourceMissingSignal();
        if (item.Bookmark.Identity.BookmarkId != request.BookmarkId)
            throw new RuntimeHistoryIntegrityException(workflow.WorkflowExecutionId, "bookmark detail disagrees with the requested snapshot");
        EnsureBookmarkPosition(item, head);
        return new(await CoverageAsync(workflow, head, token), await BookmarkAsync(workflow, item, token), time.GetUtcNow());
    }

    private async Task<WorkflowExecutionState> AuthorizeAsync(string run, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(run);
        var workflow = await workflows.FindAsync(run, token);
        if (workflow is null || workflow.WorkflowExecutionId != run || !await authorization.CanInspectStructureAsync(workflow, token))
            throw new RuntimeResourceMissingSignal();
        return workflow;
    }

    private async ValueTask<string> BindingAsync(CancellationToken token) => RuntimeHistoryIdentity.CreateBinding(
        [authorization.TenantScope, authorization.AuditSubject, await authorization.GetAuthorizationProfileAsync(token)]);

    private async Task<RuntimeHistoryBookmarkView> BookmarkAsync(WorkflowExecutionState workflow, RuntimeHistoryBookmark item, CancellationToken token)
    {
        if (item.Bookmark.Identity.WorkflowExecutionId != workflow.WorkflowExecutionId)
            throw new RuntimeHistoryIntegrityException(workflow.WorkflowExecutionId, "bookmark belongs to a different run");
        string eligibility;
        string? reason = null;
        DateTimeOffset? eligibilityExpiresAt = null;
        if (item.Status is not (RuntimeHistoryBookmarkStatus.Active or RuntimeHistoryBookmarkStatus.Unknown)) eligibility = "not-active";
        else if (bookmarks is null) { eligibility = "unknown"; reason = "eligibility-unavailable"; }
        else
        {
            try
            {
                var native = await bookmarks.FindAsync(item.Bookmark.Identity.WorkflowExecutionId, item.Bookmark.Identity.BookmarkId, token);
                if (native is null) { eligibility = "unavailable"; reason = "native-handle-missing"; } // Never implies Consumed.
                else
                {
                    if (native.WorkflowExecutionId != item.Bookmark.Identity.WorkflowExecutionId ||
                        native.BookmarkId != item.Bookmark.Identity.BookmarkId ||
                        native.ActivityExecutionId != item.Bookmark.Identity.ActivityExecutionId ||
                        native.ResumeTargetId != item.Bookmark.Identity.ResumeTargetId || item.ExecutableNodeId is { } node && native.ExecutableNodeId != node)
                        throw new RuntimeHistoryIntegrityException(item.Bookmark.Identity.WorkflowExecutionId, "native bookmark ownership disagrees with its recorded lifecycle");
                    eligibilityExpiresAt = native.ExpiresAt;
                    var now = time.GetUtcNow();
                    if (native.StimulusType != item.Bookmark.StimulusType || native.StimulusHash != item.Bookmark.StimulusHash ||
                        native.CreatedAt != item.Bookmark.CreatedAt || native.ExpiresAt != item.Bookmark.ExpiresAt)
                        { eligibility = "unknown"; reason = "handle-changed"; }
                    else if (native.ExpiresAt is { } expires && expires <= now) { eligibility = "expired"; reason = "handle-expired"; }
                    else if (workflow.Status.IsTerminal()) { eligibility = "ineligible"; reason = "workflow-terminal"; }
                    else if (workflow.TestScope?.IsExpired(now) == true) { eligibility = "ineligible"; reason = "test-scope-expired"; }
                    else if (activities is null) { eligibility = "unknown"; reason = "eligibility-unavailable"; }
                    else
                    {
                        var owner = await activities.FindAsync(native.WorkflowExecutionId, native.ActivityExecutionId, token);
                        if (owner is null) { eligibility = "unavailable"; reason = "owner-unavailable"; }
                        else if (owner.Execution.WorkflowExecutionId != native.WorkflowExecutionId ||
                            owner.Execution.ActivityExecutionId != native.ActivityExecutionId || owner.Execution.ExecutableNodeId != native.ExecutableNodeId)
                            throw new RuntimeHistoryIntegrityException(native.WorkflowExecutionId, "native activity ownership disagrees with its recorded bookmark");
                        else if (owner.SupersededByActivityExecutionId is not null || owner.SupersededAt is not null)
                            { eligibility = "ineligible"; reason = "activity-superseded"; }
                        else if (owner.Status != ActivityExecutionStatus.Suspended) { eligibility = "ineligible"; reason = "activity-not-waiting"; }
                        else if (!owner.BookmarkIds.Contains(native.BookmarkId, StringComparer.Ordinal)) { eligibility = "ineligible"; reason = "bookmark-not-owned"; }
                        else eligibility = "eligible";
                    }
                }
            }
            catch (Exception error) when (IsAvailabilityFailure(error))
            {
                logger?.LogWarning("Bookmark eligibility could not be read ({FailureType}).", error.GetType().Name);
                eligibility = "unknown"; reason = "eligibility-unavailable";
            }
        }
        // The owner read above may cross an expiry boundary. Attribute the time-sensitive
        // decision to one final observation instant rather than the earlier handle read.
        var observedAt = time.GetUtcNow();
        if (eligibility == "eligible" && eligibilityExpiresAt is { } expiresAt && expiresAt <= observedAt)
            { eligibility = "expired"; reason = "handle-expired"; }
        else if (eligibility == "eligible" && workflow.TestScope?.IsExpired(observedAt) == true)
            { eligibility = "ineligible"; reason = "test-scope-expired"; }
        return RuntimeHistoryBookmarkView.From(item, observedAt, eligibility, reason);
    }

    private ValueTask<RuntimeHistoryCoverageView> CoverageAsync(WorkflowExecutionState workflow, RuntimeHistoryHead? head, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (workflow.HistoryCaptureEnabledAtAdmission == false && (head is not null || workflow.HistoryCapturePolicy is not null) ||
            head is not null && (head.WorkflowExecutionId != workflow.WorkflowExecutionId ||
            workflow.HistoryCapturePolicy is not { } retained || !retained.IsEquivalentTo(head.Policy) ||
            head.FirstSequence < 0 || head.LastSequence < 0 || head.PurgedThroughSequence < 0 ||
            head.PurgedThroughSequence > head.LastSequence ||
            head.FirstSequence > head.LastSequence && (head.LastSequence == long.MaxValue || head.FirstSequence != head.LastSequence + 1)))
            throw new RuntimeHistoryIntegrityException(workflow.WorkflowExecutionId, "coverage disagrees with its native owner");
        var policy = head?.Policy ?? workflow.HistoryCapturePolicy;
        bool? captureAvailable = null;
        if (policy is not null)
        {
            try { captureAvailable = CaptureAvailability?.Invoke(policy) ?? false; }
            catch (Exception error) when (IsAvailabilityFailure(error))
            {
                logger?.LogWarning("Current history capture could not be checked ({FailureType}).", error.GetType().Name);
                captureAvailable = false;
            }
        }
        // These later owner contracts are intentionally not installed in this API slice. An unknown
        // responsibility is never represented as zero pending or source closure.
        var needsSettlement = policy?.SupportedKinds.Any(kind => kind is
            "TimerScheduled" or "TimerDispatched" or "TimerRetired" or "ChildWorkflowDispatchRequested" or
            "StimulusPublicationCommitted" or "ChildWorkflowSettled") == true;
        var sourceLimitation = head is not null && needsSettlement
            ? "This host cannot establish the source history settlement covered by this capture policy. Recorded events remain readable."
            : null;
        var coverage = Coverage(workflow, head);
        if (sourceLimitation is not null && coverage.State == "captured")
            coverage = coverage with { State = "partial", Limitation = sourceLimitation };
        return ValueTask.FromResult(coverage with
        {
            CaptureAvailable = captureAvailable,
            CaptureLimitation = captureAvailable == false ? "Required history capture is unavailable in this host's current composition. Retained history remains readable." : null,
            SourceHistoryPending = null,
            SourceHistoryLimitation = sourceLimitation
        });
    }

    private static bool IsAvailabilityFailure(Exception error) => error is not
        (OperationCanceledException or RuntimeHistoryIntegrityException or ArgumentException or
         OutOfMemoryException or StackOverflowException or AccessViolationException);

    private static void EnsureEventOwner(string run, RuntimeHistoryEvent item)
    {
        if (item.WorkflowExecutionId != run)
            throw new RuntimeHistoryIntegrityException(run, "event belongs to a different run");
    }

    private static void EnsureEventPosition(RuntimeHistoryEvent item, RuntimeHistoryHead? head)
    {
        if (head is null || item.Sequence <= 0 || item.Sequence < head.FirstSequence ||
            item.Sequence <= head.PurgedThroughSequence || item.Sequence > head.LastSequence)
            throw new RuntimeHistoryIntegrityException(item.WorkflowExecutionId, "event has no acknowledged retained position");
    }

    private static void EnsureBookmarkPosition(RuntimeHistoryBookmark item, RuntimeHistoryHead? head)
    {
        if (head is null || item.FirstRecordedSequence <= 0 || item.LastSequence < item.FirstRecordedSequence || item.LastSequence > head.LastSequence)
            throw new RuntimeHistoryIntegrityException(item.Bookmark.Identity.WorkflowExecutionId, "bookmark has no acknowledged lifecycle position");
    }

    private RuntimeHistoryCoverageView Coverage(WorkflowExecutionState workflow, RuntimeHistoryHead? head)
    {
        var policy = head?.Policy ?? workflow.HistoryCapturePolicy;
        var state = history is null ? "unsupported" : head is null ? workflow.HistoryCaptureEnabledAtAdmission == false ? "disabled" : policy is null ? "uncaptured" : "pending" :
            head.HasGaps || !head.CapturedFromStart || head.PurgedThroughSequence > 0 ? "partial" : "captured";
        var limitation = state switch
        {
            "unsupported" => "This host does not provide run history.",
            "disabled" => "History capture was disabled when this run was admitted.",
            "uncaptured" => "This run has no recorded capture policy. It may predate history capture or have been admitted with capture disabled.",
            "pending" => "This run has a capture policy, but no committed history boundary is readable yet.",
            "partial" => "Some run history is unavailable. Inspect the capture and retention boundaries.",
            _ => null
        };
        return new(state, limitation, policy?.PolicyId, policy?.Version, policy?.SupportedKinds ?? [],
            head?.CapturedFromStart ?? false, head?.HasGaps ?? false, head?.FirstSequence ?? 0,
            head?.LastSequence ?? 0, head?.LastSequence ?? 0, head?.PurgedThroughSequence ?? 0,
            head?.LastPhysicalCommitId, head?.LastCommittedAt);
    }

    private static RuntimeHistoryCoverageView InvalidCoverage() => new("invalid", "History integrity could not be established.",
        null, null, [], false, true, 0, 0, 0, 0, null, null);

    private static RuntimeHistoryPageView Empty(RuntimeHistoryCoverageView coverage, DateTimeOffset readAt) =>
        new(coverage, [], 0, 0, 0, null, null, false, coverage.Limitation, readAt);
}
