using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

public sealed partial class InMemoryRuntimeCheckpointStoreState
{
    internal Dictionary<(string Scope, string Run), RuntimeHistoryHead> HistoryHeads { get; } = new();
    internal Dictionary<string, (string Scope, RuntimeHistoryCapturePolicy Policy)> HistoryRunOwners { get; } = new(StringComparer.Ordinal);
    internal Dictionary<(string Scope, string Run), RuntimeHistoryCapturePolicy> RequiredHistoryRuns { get; } = new();
    internal Dictionary<(string Scope, string Run), List<RuntimeHistoryEvent>> HistoryEvents { get; } = new();
    internal Dictionary<(string Scope, string Run, string Event), RuntimeHistoryEvent> HistoryEventsById { get; } = new();
    internal Dictionary<(string Scope, string Run, string Event), string> HistoryCandidateFingerprints { get; } = new();
    internal Dictionary<(string Scope, string Run, string Event), long> HistoryReceiptPositions { get; } = new();
    internal Dictionary<(string Scope, string Run, string Activity), List<RuntimeHistoryEvent>> HistoryActivityEvents { get; } = new();
    internal Dictionary<(string Scope, string Run, string Activity), string?> HistoryParents { get; } = new();
    internal Dictionary<(string Scope, string Run, string Bookmark), RuntimeHistoryBookmark> HistoryBookmarks { get; } = new();
    internal Dictionary<(string Scope, string Run), List<string>> HistoryBookmarkOrder { get; } = new();
    internal Dictionary<(string Scope, string Run, string Activity), List<string>> HistoryActivityBookmarkOrder { get; } = new();
    internal Dictionary<(string Scope, string Run, string Bookmark), List<RuntimeHistoryEvent>> HistoryBookmarkEvents { get; } = new();

    internal HashSet<(string Scope, string Run)> HistoryDeletedRuns { get; } = new();
    internal Dictionary<(string Scope, string Run), HashSet<string>> HistoryReceiptIds { get; } = new();
    internal Dictionary<(string Scope, string Run), HashSet<string>> HistoryActivityIds { get; } = new();
    internal Dictionary<(string Scope, string Run, string Name), List<RuntimeHistoryEvent>> HistoryNameEvents { get; } = new();
    internal Dictionary<(string Scope, string Run, RuntimeHistoryEventCategory Category), List<RuntimeHistoryEvent>> HistoryCategoryEvents { get; } = new();
    internal Dictionary<(string Scope, string Run), HashSet<string>> HistoryNames { get; } = new();
    internal Dictionary<(string Scope, string Run), HashSet<RuntimeHistoryEventCategory>> HistoryCategories { get; } = new();
    internal Dictionary<(string Scope, string Run), long> HistoryActiveBookmarks { get; } = new();
    internal Dictionary<(string Scope, string Run), HashSet<string>> HistoryCommitIds { get; } = new();
    internal Dictionary<string, string> OriginalHistoryCommitFingerprints { get; } = new(StringComparer.Ordinal);
    internal Dictionary<(string Scope, string Run, string Activity), long> HistoryActivityActiveBookmarks { get; } = new();

    internal void PublishHistoryEvent(string scope, RuntimeHistoryEvent e, string fingerprint)
    {
        var owner = (scope, e.WorkflowExecutionId);
        if (!HistoryEvents.TryGetValue(owner, out var history)) HistoryEvents[owner] = history = [];
        history.Add(e);
        HistoryEventsById[(scope, e.WorkflowExecutionId, e.HistoryEventId)] = e;
        HistoryCandidateFingerprints[(scope, e.WorkflowExecutionId, e.HistoryEventId)] = fingerprint;
        HistoryReceiptPositions[(scope, e.WorkflowExecutionId, e.HistoryEventId)] = e.Sequence;
        if (!HistoryReceiptIds.TryGetValue(owner, out var receipts)) HistoryReceiptIds[owner] = receipts = new(StringComparer.Ordinal);
        receipts.Add(e.HistoryEventId);
        var nameKey = (scope, e.WorkflowExecutionId, e.Name);
        if (!HistoryNameEvents.TryGetValue(nameKey, out var named)) HistoryNameEvents[nameKey] = named = [];
        named.Add(e);
        if (!HistoryNames.TryGetValue(owner, out var names)) HistoryNames[owner] = names = new(StringComparer.Ordinal);
        names.Add(e.Name);
        var categoryKey = (scope, e.WorkflowExecutionId, e.Category);
        if (!HistoryCategoryEvents.TryGetValue(categoryKey, out var categorized)) HistoryCategoryEvents[categoryKey] = categorized = [];
        categorized.Add(e);
        if (!HistoryCategories.TryGetValue(owner, out var categories)) HistoryCategories[owner] = categories = [];
        categories.Add(e.Category);
        if (e.ActivityExecutionId is { } activity)
        {
            var activityKey = (scope, e.WorkflowExecutionId, activity);
            if (!HistoryActivityEvents.TryGetValue(activityKey, out var own)) HistoryActivityEvents[activityKey] = own = [];
            own.Add(e);
            if (!HistoryActivityIds.TryGetValue(owner, out var ids)) HistoryActivityIds[owner] = ids = new(StringComparer.Ordinal);
            ids.Add(activity);
            if (e.ParentActivityExecutionId is not null || e.ActivityAncestryKnown == true && !HistoryParents.ContainsKey(activityKey))
                HistoryParents[activityKey] = e.ParentActivityExecutionId;
        }
        if (e.BookmarkId is { } bookmark)
        {
            var bookmarkKey = (scope, e.WorkflowExecutionId, bookmark);
            if (!HistoryBookmarkEvents.TryGetValue(bookmarkKey, out var own)) HistoryBookmarkEvents[bookmarkKey] = own = [];
            own.Add(e);
        }
    }

    internal void PublishHistoryBookmark(string scope, RuntimeHistoryBookmark projection)
    {
        var identity = projection.Bookmark.Identity;
        var owner = (scope, identity.WorkflowExecutionId);
        var key = (scope, identity.WorkflowExecutionId, identity.BookmarkId);
        var activityKey = (scope, identity.WorkflowExecutionId, identity.ActivityExecutionId);
        HistoryBookmarks.TryGetValue(key, out var previous);
        if (previous is null)
        {
            if (!HistoryBookmarkOrder.TryGetValue(owner, out var order)) HistoryBookmarkOrder[owner] = order = [];
            order.Add(identity.BookmarkId);
            if (!HistoryActivityBookmarkOrder.TryGetValue(activityKey, out var own)) HistoryActivityBookmarkOrder[activityKey] = own = [];
            own.Add(identity.BookmarkId);
        }
        var delta = (projection.Status == RuntimeHistoryBookmarkStatus.Active ? 1 : 0) - (previous?.Status == RuntimeHistoryBookmarkStatus.Active ? 1 : 0);
        HistoryActiveBookmarks[owner] = HistoryActiveBookmarks.GetValueOrDefault(owner) + delta;
        HistoryActivityActiveBookmarks[activityKey] = HistoryActivityActiveBookmarks.GetValueOrDefault(activityKey) + delta;
        HistoryBookmarks[key] = projection;
    }

    internal void DeleteHistoryForRun(string scope, string run)
    {
        lock (SyncRoot)
        {
            var owner = (scope, run);
            HistoryDeletedRuns.Add(owner);
            RequiredHistoryRuns.Remove(owner);
            if (HistoryRunOwners.TryGetValue(run, out var nativeOwner) && nativeOwner.Scope == scope) HistoryRunOwners.Remove(run);
            HistoryHeads.Remove(owner);
            HistoryEvents.Remove(owner);
            HistoryActiveBookmarks.Remove(owner);
            if (HistoryCommitIds.Remove(owner, out var commits)) foreach (var id in commits) ScrubHistoryCommitContent(id);
            if (HistoryReceiptIds.Remove(owner, out var receipts))
                foreach (var id in receipts) { HistoryEventsById.Remove((scope, run, id)); HistoryCandidateFingerprints.Remove((scope, run, id)); HistoryReceiptPositions.Remove((scope, run, id)); }
            if (HistoryActivityIds.Remove(owner, out var activities))
                foreach (var id in activities)
                {
                    HistoryActivityEvents.Remove((scope, run, id)); HistoryParents.Remove((scope, run, id));
                    HistoryActivityBookmarkOrder.Remove((scope, run, id)); HistoryActivityActiveBookmarks.Remove((scope, run, id));
                }
            if (HistoryNames.Remove(owner, out var names)) foreach (var name in names) HistoryNameEvents.Remove((scope, run, name));
            if (HistoryCategories.Remove(owner, out var categories)) foreach (var category in categories) HistoryCategoryEvents.Remove((scope, run, category));
            if (HistoryBookmarkOrder.Remove(owner, out var bookmarks))
                foreach (var bookmark in bookmarks)
                    if (HistoryBookmarks.Remove((scope, run, bookmark), out var projection))
                    {
                        HistoryActivityBookmarkOrder.Remove((scope, run, projection.Bookmark.Identity.ActivityExecutionId));
                        HistoryActivityActiveBookmarks.Remove((scope, run, projection.Bookmark.Identity.ActivityExecutionId));
                        HistoryBookmarkEvents.Remove((scope, run, bookmark));
                    }
        }
    }

    internal void ScrubHistoryCommitContent(string commitId)
    {
        if (Commits.TryGetValue(commitId, out var retained) && OriginalHistoryCommitFingerprints.ContainsKey(commitId))
            Commits[commitId] = retained with { Commit = retained.Commit with { HistoryBatches = null, HistorySourceFacts = null } };
    }

}
