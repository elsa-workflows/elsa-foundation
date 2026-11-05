using Elsa.Workflows.Runtime.Core.Exceptions;
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
        var batches = commit.HistoryBatches;
        var incomingPolicy = commit.StateChanges.WorkflowExecution?.State.HistoryCapturePolicy;
        if (commit.StateChanges.WorkflowExecution?.State is { } incoming && incoming.HistoryCaptureEnabledAtAdmission is { } enabled &&
            enabled != (incoming.HistoryCapturePolicy is not null))
            throw new InvalidOperationException("Native history admission provenance disagrees with its pinned policy.");
        string? scope = null;
        if (access is not null)
        {
            try { scope = access.Current.RequireScope().Value; }
            catch (InvalidOperationException) when (batches is not { Count: > 0 } && incomingPolicy is null && state.RequiredHistoryRuns.Count == 0) { return null; }
        }
        if (scope is null)
        {
            if (batches is { Count: > 0 } || incomingPolicy?.Required == true || state.RequiredHistoryRuns.Count > 0)
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "History checkpoint persistence requires its scoped provider.");
            return null;
        }
        var key = (scope, commit.WorkflowExecutionId);
        if (state.HistoryDeletedRuns.Contains(key)) throw new InvalidOperationException("Deleted execution history cannot be recreated.");
        state.RequiredHistoryRuns.TryGetValue(key, out var required);
        if (batches is not { Count: > 0 })
        {
            if (incomingPolicy?.Required == true || required is not null)
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "This execution requires its pinned history capture contribution.");
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
                if (state.HistoryCandidateFingerprints.TryGetValue((scope, commit.WorkflowExecutionId, e.HistoryEventId), out var original))
                {
                    if (original != fingerprint)
                        throw new InvalidOperationException("A retained history identity has conflicting or unprovable content.");
                    if (!state.HistoryReceiptPositions.TryGetValue((scope, commit.WorkflowExecutionId, e.HistoryEventId), out var position) ||
                        position <= 0 || head is null || position > head.LastSequence ||
                        position > head.PurgedThroughSequence && !state.HistoryEventsById.ContainsKey((scope, commit.WorkflowExecutionId, e.HistoryEventId)))
                        throw new InvalidOperationException("A retained history receipt has missing content outside its acknowledged purge boundary.");
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
                    previous?.PriorState == RuntimeHistoryPriorState.CreationEvent || created ? RuntimeHistoryPriorState.CreationEvent : RuntimeHistoryPriorState.CreationMetadata)
                { SupersededByActivityExecutionId = mutation.RelatedActivityExecutionId ?? previous?.SupersededByActivityExecutionId };
            }
        }
        var nextHead = new RuntimeHistoryHead(commit.WorkflowExecutionId,
            head is { FirstSequence: > 0 } ? head.FirstSequence : events.Count == 0 ? 0 : events[0].Sequence, sequence,
            commit.CommitId, recordedAt, policy,
            head?.CapturedFromStart ?? batches.Any(batch => batch.StartsAtRunStart), head?.HasGaps ?? state.NoRecordTimerCompletionRuns.Contains(key), head?.PurgedThroughSequence ?? 0);
        return () =>
        {
            state.HistoryHeads[key] = nextHead;
            if (policy.Required) state.RequiredHistoryRuns[key] = policy;
            foreach (var e in events) state.PublishHistoryEvent(scope, e, seen[e.HistoryEventId]);
            foreach (var projection in bookmarks.Values) state.PublishHistoryBookmark(scope, projection);
        };
    }
}
