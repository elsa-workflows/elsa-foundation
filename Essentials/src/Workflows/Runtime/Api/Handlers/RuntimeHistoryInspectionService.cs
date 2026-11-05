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
    IRuntimeCheckpointCommitStore? writer = null, IEnumerable<IRuntimeCheckpointCommitEnricher>? captureChain = null,
    IDurableTimerStore? timers = null, ILogger<RuntimeHistoryInspectionService>? logger = null)
{
    public async Task<RuntimeHistoryPageView> ReadAsync(GetRuntimeHistory request, CancellationToken token = default)
    {
        var workflow = await AuthorizeAsync(request.WorkflowExecutionId, token);
        if (history is null) return Empty(await CoverageAsync(workflow, null, token), time.GetUtcNow());
        var query = new RuntimeHistoryQuery(request.WorkflowExecutionId, request.Limit, request.Cursor,
            request.ActivityExecutionId, request.IncludeDescendants, request.BookmarkId, request.Category,
            request.Name, request.Search, await BindingAsync(token));
        try
        {
            var page = await history.ReadPageAsync(query, token);
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
        if (history is null) throw new RuntimeHistoryUnsupportedSignal();
        var item = await history.FindEventAsync(request.WorkflowExecutionId, request.HistoryEventId, token)
            ?? throw new RuntimeResourceMissingSignal();
        return new(await CoverageAsync(workflow, await history.FindHeadAsync(request.WorkflowExecutionId, token), token),
            RuntimeHistoryEventView.From(item), time.GetUtcNow());
    }

    public async Task<RuntimeHistoryBookmarkPageView> ListBookmarksAsync(GetRuntimeHistoryBookmarks request, CancellationToken token = default)
    {
        var workflow = await AuthorizeAsync(request.WorkflowExecutionId, token);
        if (history is null) return new(await CoverageAsync(workflow, null, token), [], null, null, null, false, "History is unsupported by this host.", time.GetUtcNow());
        var query = new RuntimeHistoryBookmarkQuery(request.WorkflowExecutionId, request.Limit, request.Cursor,
            request.ActivityExecutionId, request.IncludeDescendants, request.Status, await BindingAsync(token));
        try
        {
            var page = await history.ListBookmarksAsync(query, token);
            var items = new List<RuntimeHistoryBookmarkView>(page.Items.Count);
            foreach (var item in page.Items) items.Add(await BookmarkAsync(workflow, item, token));
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
        if (history is null) throw new RuntimeHistoryUnsupportedSignal();
        var item = await history.FindBookmarkAsync(request.WorkflowExecutionId, request.BookmarkId, token)
            ?? throw new RuntimeResourceMissingSignal();
        return new(await CoverageAsync(workflow, await history.FindHeadAsync(request.WorkflowExecutionId, token), token),
            await BookmarkAsync(workflow, item, token), time.GetUtcNow());
    }

    private async Task<WorkflowExecutionState> AuthorizeAsync(string run, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(run);
        var workflow = await workflows.FindAsync(run, token);
        if (workflow is null || !await authorization.CanInspectStructureAsync(workflow, token))
            throw new RuntimeResourceMissingSignal();
        return workflow;
    }

    private async ValueTask<string> BindingAsync(CancellationToken token) => RuntimeHistoryIdentity.CreateBinding(
        [authorization.TenantScope, authorization.AuditSubject, await authorization.GetAuthorizationProfileAsync(token)]);

    private async Task<RuntimeHistoryBookmarkView> BookmarkAsync(WorkflowExecutionState workflow, RuntimeHistoryBookmark item, CancellationToken token)
    {
        string eligibility;
        string? reason = null;
        if (item.Status is not (RuntimeHistoryBookmarkStatus.Active or RuntimeHistoryBookmarkStatus.Unknown)) eligibility = "not-active";
        else if (bookmarks is null) { eligibility = "unknown"; reason = "eligibility-unavailable"; }
        else
        {
            var native = await bookmarks.FindAsync(item.Bookmark.Identity.WorkflowExecutionId, item.Bookmark.Identity.BookmarkId, token);
            if (native is null) { eligibility = "unavailable"; reason = "native-handle-missing"; } // Never implies Consumed.
            else
            {
                if (native.WorkflowExecutionId != item.Bookmark.Identity.WorkflowExecutionId ||
                    native.BookmarkId != item.Bookmark.Identity.BookmarkId ||
                    native.ActivityExecutionId != item.Bookmark.Identity.ActivityExecutionId ||
                    native.ResumeTargetId != item.Bookmark.Identity.ResumeTargetId)
                    throw new RuntimeHistoryIntegrityException(item.Bookmark.Identity.WorkflowExecutionId, "native bookmark ownership disagrees with its recorded lifecycle");
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
        return RuntimeHistoryBookmarkView.From(item, time.GetUtcNow(), eligibility, reason);
    }

    private async ValueTask<RuntimeHistoryCoverageView> CoverageAsync(WorkflowExecutionState workflow, RuntimeHistoryHead? head, CancellationToken token)
    {
        var policy = head?.Policy ?? workflow.HistoryCapturePolicy;
        bool? captureAvailable = null;
        if (policy is not null)
        {
            try
            {
                captureAvailable = history is not null && writer is { SupportsHistory: true } && writer.CanCommitHistory(history) &&
                    (captureChain ?? []).OfType<IRuntimeHistoryCaptureSupport>().Any(support => support.CanCapture(policy));
            }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
            {
                captureAvailable = false;
            }
        }
        bool? sourcePending = null;
        string? sourceLimitation = null;
        if (head is not null && policy?.SupportedKinds.Any(kind => DurableTimerHistoryFacts.Kinds.Contains(kind, StringComparer.Ordinal)) == true)
        {
            if (timers is not IDurableTimerHistoryStore { SupportsHistoryResponsibility: true } timerHistory)
                sourceLimitation = "This host cannot inspect the timer history responsibility covered by this run's capture policy.";
            else
            {
                try { sourcePending = await timerHistory.HasPendingHistoryAsync(workflow.WorkflowExecutionId, token); }
                catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
                {
                    logger?.LogWarning("Timer history settlement could not be read for an authorized run ({FailureType}).", error.GetType().Name);
                    sourceLimitation = "Timer history settlement could not be checked. Recorded events remain readable.";
                }
                // A source append+ACK can occur after the first page snapshot. Refresh the head after observing
                // no pending work; retain the page's original traversal watermark and catch-up token.
                if (sourcePending == false && history is not null)
                {
                    var refreshed = await history.FindHeadAsync(workflow.WorkflowExecutionId, token);
                    if (refreshed is not null && refreshed.LastSequence >= head.LastSequence) head = refreshed;
                    else sourceLimitation = "History changed while source settlement was read. Refresh to inspect its current boundary.";
                }
            }
        }
        var coverage = Coverage(workflow, head);
        if (sourcePending == true && coverage.State == "captured")
            coverage = coverage with { State = "pending", Limitation = "Timer source history is awaiting acknowledgement. The recorded prefix remains readable." };
        else if (sourceLimitation is not null && coverage.State == "captured")
            coverage = coverage with { State = "partial", Limitation = sourceLimitation };
        return coverage with
        {
            CaptureAvailable = captureAvailable,
            CaptureLimitation = captureAvailable == false ? "Required history capture is unavailable in this host's current composition. Retained history remains readable." : null,
            SourceHistoryPending = sourcePending,
            SourceHistoryLimitation = sourceLimitation
        };
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
