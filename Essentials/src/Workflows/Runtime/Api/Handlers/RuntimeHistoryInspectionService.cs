using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>Reauthorizes every history read before touching the scoped provider or resolving an exact target.</summary>
public sealed class RuntimeHistoryInspectionService(IWorkflowExecutionStateStore workflows,
    IActivityInspectionContextAsync authorization, TimeProvider time, IRuntimeHistoryStore? history = null,
    IBookmarkStateStore? bookmarks = null)
{
    public async Task<RuntimeHistoryPageView> ReadAsync(GetRuntimeHistory request, CancellationToken token = default)
    {
        var workflow = await AuthorizeAsync(request.WorkflowExecutionId, token);
        if (history is null) return Empty(Coverage(workflow, null), time.GetUtcNow());
        var query = new RuntimeHistoryQuery(request.WorkflowExecutionId, request.Limit, request.Cursor,
            request.ActivityExecutionId, request.IncludeDescendants, request.BookmarkId, request.Category,
            request.Name, request.Search, await BindingAsync(token));
        try
        {
            var page = await history.ReadPageAsync(query, token);
            return new(Coverage(workflow, page.Head), page.Items.Select(RuntimeHistoryEventView.From).ToArray(),
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
        return new(Coverage(workflow, await history.FindHeadAsync(request.WorkflowExecutionId, token)),
            RuntimeHistoryEventView.From(item), time.GetUtcNow());
    }

    public async Task<RuntimeHistoryBookmarkPageView> ListBookmarksAsync(GetRuntimeHistoryBookmarks request, CancellationToken token = default)
    {
        var workflow = await AuthorizeAsync(request.WorkflowExecutionId, token);
        if (history is null) return new(Coverage(workflow, null), [], null, null, null, false, "History is unsupported by this host.", time.GetUtcNow());
        var query = new RuntimeHistoryBookmarkQuery(request.WorkflowExecutionId, request.Limit, request.Cursor,
            request.ActivityExecutionId, request.IncludeDescendants, request.Status, await BindingAsync(token));
        try
        {
            var page = await history.ListBookmarksAsync(query, token);
            var items = new List<RuntimeHistoryBookmarkView>(page.Items.Count);
            foreach (var item in page.Items) items.Add(await BookmarkAsync(item, token));
            return new(Coverage(workflow, page.Head), items, page.NextContinuationToken, page.TotalCount,
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
        return new(Coverage(workflow, await history.FindHeadAsync(request.WorkflowExecutionId, token)),
            await BookmarkAsync(item, token), time.GetUtcNow());
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

    private async Task<RuntimeHistoryBookmarkView> BookmarkAsync(RuntimeHistoryBookmark item, CancellationToken token)
    {
        string eligibility;
        if (item.Status is not (RuntimeHistoryBookmarkStatus.Active or RuntimeHistoryBookmarkStatus.Unknown)) eligibility = "not-active";
        else if (bookmarks is null) eligibility = "unknown";
        else
        {
            var native = await bookmarks.FindAsync(item.Bookmark.Identity.WorkflowExecutionId, item.Bookmark.Identity.BookmarkId, token);
            if (native is null) eligibility = "unavailable"; // Absence never changes the recorded lifecycle to Consumed.
            else
            {
                if (native.WorkflowExecutionId != item.Bookmark.Identity.WorkflowExecutionId ||
                    native.BookmarkId != item.Bookmark.Identity.BookmarkId ||
                    native.ActivityExecutionId != item.Bookmark.Identity.ActivityExecutionId ||
                    native.ResumeTargetId != item.Bookmark.Identity.ResumeTargetId)
                    throw new RuntimeHistoryIntegrityException(item.Bookmark.Identity.WorkflowExecutionId, "native bookmark ownership disagrees with its recorded lifecycle");
                eligibility = native.ExpiresAt is { } expires && expires <= time.GetUtcNow() ? "expired" : "eligible";
            }
        }
        return RuntimeHistoryBookmarkView.From(item, time.GetUtcNow(), eligibility);
    }

    private RuntimeHistoryCoverageView Coverage(WorkflowExecutionState workflow, RuntimeHistoryHead? head)
    {
        var policy = head?.Policy ?? workflow.HistoryCapturePolicy;
        var state = history is null ? "unsupported" : head is null ? policy is null ? "uncaptured" : "pending" :
            head.HasGaps || !head.CapturedFromStart || head.PurgedThroughSequence > 0 ? "partial" : "captured";
        var limitation = state switch
        {
            "unsupported" => "This host does not provide run history.",
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
