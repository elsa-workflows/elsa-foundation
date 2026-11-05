using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Prepares all fallible history validation before any in-memory participant is mutated.</summary>
internal static class MemoryHistoryCheckpoint
{
    internal static Action? Prepare(InMemoryRuntimeCheckpointStoreState state, IPersistenceAccessContextAccessor? access,
        RuntimeCheckpointCommit commit, DateTimeOffset recordedAt)
    {
        var batches = commit.StateChanges.HistoryBatches;
        var incomingPolicy = commit.StateChanges.WorkflowExecution?.State.HistoryCapturePolicy;
        string? scope = null;
        if (access is not null)
        {
            try { scope = access.Current.RequireScope().Value; }
            catch (InvalidOperationException) when (batches is not { Count: > 0 } && incomingPolicy is null && state.RequiredHistoryRuns.Count == 0) { return null; }
        }
        if (scope is null)
        {
            if (batches is { Count: > 0 } || incomingPolicy?.Required == true || state.RequiredHistoryRuns.Count > 0)
                throw new NotSupportedException("History checkpoint persistence requires one explicit ordinary scope.");
            return null;
        }
        var key = (scope, commit.WorkflowExecutionId);
        state.RequiredHistoryRuns.TryGetValue(key, out var required);
        if (batches is not { Count: > 0 })
        {
            if (incomingPolicy?.Required == true || required is not null)
                throw new InvalidOperationException("This execution requires history capture; a checkpoint without its pinned batch cannot commit.");
            return null;
        }
        state.HistoryHeads.TryGetValue(key, out var head);
        var policy = head?.Policy ?? incomingPolicy
            ?? throw new InvalidOperationException("History cannot be enabled retroactively without a pinned admission policy.");
        if (commit.StateChanges.WorkflowExecution is not null && !policy.IsEquivalentTo(incomingPolicy))
            throw new InvalidOperationException("An active execution's history policy cannot change implicitly.");
        if (head is null && !batches.Any(batch => batch.StartsAtRunStart) && incomingPolicy is null)
            throw new InvalidOperationException("An uncaptured legacy execution has no admitted history policy.");
        var events = new List<RuntimeHistoryEvent>();
        var bookmarks = new Dictionary<(string Scope, string Run, string Bookmark), RuntimeHistoryBookmark>();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var sequence = head?.LastSequence ?? 0;
        foreach (var batch in batches)
        {
            RuntimeHistorySafety.Validate(batch, commit.WorkflowExecutionId);
            if (!policy.IsEquivalentTo(batch.Policy)) throw new InvalidOperationException("History groups do not agree with the pinned run policy.");
            foreach (var candidate in batch.Candidates)
            {
                var e = candidate.Event;
                var fingerprint = RuntimeHistoryIdentity.Compute(candidate);
                if (seen.TryGetValue(e.HistoryEventId, out var first))
                {
                    if (first != fingerprint) throw new InvalidOperationException("Conflicting history identities cannot be folded.");
                    continue;
                }
                seen.Add(e.HistoryEventId, fingerprint);
                if (state.HistoryEventsById.TryGetValue((scope, commit.WorkflowExecutionId, e.HistoryEventId), out var old))
                {
                    if (!state.HistoryCandidateFingerprints.TryGetValue((scope, commit.WorkflowExecutionId, old.HistoryEventId), out var original) || original != fingerprint)
                        throw new InvalidOperationException("A retained history identity has conflicting or unprovable content.");
                    continue;
                }
                var stored = RuntimeHistoryIdentity.Position(e, checked(++sequence), commit.CommitId, recordedAt, candidate.CheckpointOrdinal);
                events.Add(stored);
                if (candidate.BookmarkMutation is not { } mutation) continue;
                var bookmarkKey = (scope, commit.WorkflowExecutionId, mutation.Bookmark.Identity.BookmarkId);
                if (!bookmarks.TryGetValue(bookmarkKey, out var previous)) state.HistoryBookmarks.TryGetValue(bookmarkKey, out previous);
                if (previous is not null && previous.Bookmark.Identity != mutation.Bookmark.Identity)
                    throw new InvalidOperationException("A history bookmark identity cannot change ownership.");
                var created = mutation.Status == RuntimeHistoryBookmarkStatus.Active;
                bookmarks[bookmarkKey] = new(mutation.Bookmark, stored.ExecutableNodeId ?? previous?.ExecutableNodeId,
                    mutation.Status, previous?.CreatedHistoryEventId ?? (created ? stored.HistoryEventId : null),
                    created ? null : stored.HistoryEventId, stored.HistoryEventId, stored.Sequence,
                    previous?.FirstRecordedSequence ?? stored.Sequence, created ? null : stored.OccurredAt,
                    mutation.Reason, mutation.SelectedBookmarkId,
                    previous?.PriorState == RuntimeHistoryPriorState.CreationEvent || created ? RuntimeHistoryPriorState.CreationEvent : RuntimeHistoryPriorState.CreationMetadata);
            }
        }
        var nextHead = new RuntimeHistoryHead(commit.WorkflowExecutionId,
            head is { FirstSequence: > 0 } ? head.FirstSequence : events.Count == 0 ? 0 : events[0].Sequence, sequence,
            commit.CommitId, recordedAt, policy,
            head?.CapturedFromStart ?? batches.Any(batch => batch.StartsAtRunStart), head?.HasGaps ?? false, head?.PurgedThroughSequence ?? 0);
        return () =>
        {
            state.HistoryHeads[key] = nextHead;
            if (policy.Required) state.RequiredHistoryRuns[key] = policy;
            if (!state.HistoryEvents.TryGetValue(key, out var history)) state.HistoryEvents[key] = history = [];
            foreach (var e in events)
            {
                history.Add(e);
                state.HistoryEventsById[(scope, commit.WorkflowExecutionId, e.HistoryEventId)] = e;
                state.HistoryCandidateFingerprints[(scope, commit.WorkflowExecutionId, e.HistoryEventId)] = seen[e.HistoryEventId];
                if (e.ActivityExecutionId is { } activity)
                {
                    var activityKey = (scope, commit.WorkflowExecutionId, activity);
                    if (!state.HistoryActivityEvents.TryGetValue(activityKey, out var own)) state.HistoryActivityEvents[activityKey] = own = [];
                    own.Add(e);
                    if (e.ParentActivityExecutionId is not null ||
                        e.Category == RuntimeHistoryEventCategory.ActivityLifecycle && !state.HistoryParents.ContainsKey(activityKey))
                        state.HistoryParents[activityKey] = e.ParentActivityExecutionId;
                }
                if (e.BookmarkId is { } bookmark)
                {
                    var bookmarkKey = (scope, commit.WorkflowExecutionId, bookmark);
                    if (!state.HistoryBookmarkEvents.TryGetValue(bookmarkKey, out var own)) state.HistoryBookmarkEvents[bookmarkKey] = own = [];
                    own.Add(e);
                }
            }
            foreach (var pair in bookmarks)
            {
                if (!state.HistoryBookmarks.ContainsKey(pair.Key))
                {
                    if (!state.HistoryBookmarkOrder.TryGetValue(key, out var order)) state.HistoryBookmarkOrder[key] = order = [];
                    order.Add(pair.Key.Bookmark);
                    var activityKey = (scope, commit.WorkflowExecutionId, pair.Value.Bookmark.Identity.ActivityExecutionId);
                    if (!state.HistoryActivityBookmarkOrder.TryGetValue(activityKey, out var own)) state.HistoryActivityBookmarkOrder[activityKey] = own = [];
                    own.Add(pair.Key.Bookmark);
                }
                state.HistoryBookmarks[pair.Key] = pair.Value;
            }
        };
    }
}
