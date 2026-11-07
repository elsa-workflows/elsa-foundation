using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.ActivityExecutionEfSupport;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistorySupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Stages queryable history in the checkpoint transaction; never introduces another checkpoint.</summary>
internal static class EfRuntimeHistoryCheckpoint
{
    internal static bool IsHeadConflict(Exception exception) => exception is HeadAllocationConflictException || (exception is DbUpdateConcurrencyException concurrency
        ? concurrency.Entries.Count > 0 && concurrency.Entries.All(entry => entry.Entity is RuntimeHistoryHeadEntity)
        : exception is DbUpdateException update && update.Entries.Count > 0 && update.Entries.All(entry => entry.Entity is RuntimeHistoryHeadEntity) &&
          EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey));

    internal static async ValueTask StageAsync(RuntimeDbContext context, IPersistenceAccessContextAccessor access,
        RuntimeCheckpointCommit commit, string scope, DateTimeOffset recordedAt, CancellationToken cancellationToken)
    {
        if (access.Current.RequireScope().Value != scope) throw new InvalidOperationException("History checkpoint changed physical scope.");
        var run = commit.WorkflowExecutionId;
        var batches = commit.HistoryBatches;
        var runId = EfWorkflowExecutionStateStore.CreateId(scope, run);
        // The retained native policy is authoritative even when capture was removed from composition or a caller
        // presents a no-history checkpoint. An absent incoming projection must never remove the requirement.
        // Acquire the current root before the first consistent read. InnoDB's default repeatable-read snapshot
        // must not be established before waiting for a concurrent writer's root lock.
        if (batches is { Count: > 0 })
            await EfWorkflowExecutionStateStore.LockWorkflowExecutionForWriteAsync(context, scope, run, cancellationToken);
        var native = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        var retained = native is null ? null : EfWorkflowExecutionStateStore.ReadChecked(native, scope, run);
        if (retained is not null) access.Current.EnsureTenantScope(retained.TenantId);
        var incoming = commit.StateChanges.WorkflowExecution?.State;
        var headId = Id("head", scope, run);
        var headRow = await context.RuntimeHistoryHeads.SingleOrDefaultAsync(x => x.Id == headId, cancellationToken);
        var head = headRow is null ? null : Read(headRow, scope, run);
        var policy = head?.Policy ?? retained?.HistoryCapturePolicy ?? incoming?.HistoryCapturePolicy;
        if (batches is not { Count: > 0 })
        {
            if (policy?.Required == true || incoming?.HistoryCapturePolicy?.Required == true)
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "This execution requires history capture; its pinned batch is unavailable and persistence remains owed.");
            return;
        }
        if (policy is null || retained is not null && !policy.IsEquivalentTo(retained.HistoryCapturePolicy) ||
            incoming is not null && !policy.IsEquivalentTo(incoming.HistoryCapturePolicy))
            throw new InvalidOperationException("History requires the execution's unchanged admitted capture policy.");
        if (incoming is not null && incoming.WorkflowExecutionId != run)
            throw new InvalidOperationException("The history execution does not match the physical checkpoint.");
        await EfExecutionLivenessStateStore.EnsureWorkflowExecutionNotDeletedAsync(context, scope, run, cancellationToken);
        foreach (var batch in batches)
        {
            RuntimeHistorySafety.Validate(batch, run);
            if (!policy.IsEquivalentTo(batch.Policy)) throw new InvalidOperationException("History batches disagree with the pinned policy.");
        }
        EnsureWritable(context);
        var candidates = new Dictionary<string, (RuntimeHistoryCandidate Candidate, string Fingerprint)>(StringComparer.Ordinal);
        foreach (var candidate in batches.SelectMany(batch => batch.Candidates))
        {
            var fingerprint = RuntimeHistoryIdentity.Compute(candidate);
            if (candidates.TryGetValue(candidate.Event.HistoryEventId, out var previous))
            {
                if (previous.Fingerprint != fingerprint) throw new InvalidOperationException("Conflicting history identities cannot be folded.");
            }
            else candidates.Add(candidate.Event.HistoryEventId, (candidate, fingerprint));
        }
        var retainedEvents = new Dictionary<string, RuntimeHistoryEventEntity>(StringComparer.Ordinal);
        foreach (var chunk in candidates.Keys.Select(eventId => Id("event", scope, run, eventId)).Chunk(128))
            foreach (var row in await context.RuntimeHistoryEvents.AsNoTracking().Where(row => chunk.Contains(row.Id)).ToArrayAsync(cancellationToken))
            {
                var eventId = ReadReceipt(row, scope, run, head);
                if (!row.IsPurged) _ = Read(row, scope, run, head);
                retainedEvents.Add(eventId, row);
            }
        var mutations = candidates.Values.Where(x => x.Candidate.BookmarkMutation is not null).Select(x => x.Candidate.BookmarkMutation!).ToArray();
        var bookmarkRows = new Dictionary<string, RuntimeHistoryBookmarkEntity>(StringComparer.Ordinal);
        foreach (var chunk in mutations.Select(m => Id("bookmark", scope, run, m.Bookmark.Identity.BookmarkId)).Distinct(StringComparer.Ordinal).Chunk(128))
            foreach (var row in await context.RuntimeHistoryBookmarks.Where(row => chunk.Contains(row.Id)).ToArrayAsync(cancellationToken))
            {
                var item = Read(row, scope, run, head);
                bookmarkRows.Add(item.Bookmark.Identity.BookmarkId, row);
            }
        await ValidateCleanupSourcesAsync(context, commit, scope, candidates.Values.Select(entry => entry.Candidate).ToArray(), retainedEvents, cancellationToken);
        var sequence = head?.LastSequence ?? 0;
        var newCount = candidates.Values.Count(entry => !retainedEvents.ContainsKey(entry.Candidate.Event.HistoryEventId));
        var hasGaps = head?.HasGaps ?? await HasUnrecordedSourceGapAsync(context, scope, run, policy, cancellationToken);
        var next = new RuntimeHistoryHead(run,
            head is { FirstSequence: > 0 } ? head.FirstSequence : newCount == 0 ? 0 : checked(sequence + 1),
            checked(sequence + newCount), commit.CommitId, recordedAt, policy,
            head?.CapturedFromStart ?? batches.Any(batch => batch.StartsAtRunStart), hasGaps, head?.PurgedThroughSequence ?? 0);
        // Reserve the complete group before inserting any event. InnoDB may still expose an older consistent
        // snapshot after waiting for the native root lock; this current-write revision CAS detects it here, rather
        // than accidentally failing an event sequence unique key after participants have been staged.
        await ReserveHeadAsync(context, scope, run, headRow, next, cancellationToken);
        var counts = new Dictionary<string, (string? Activity, long Total, long Active)>(StringComparer.Ordinal);
        foreach (var entry in candidates.Values)
        {
            var candidate = entry.Candidate;
            if (retainedEvents.TryGetValue(candidate.Event.HistoryEventId, out var old))
            {
                // Persist the complete candidate fingerprint: the final bookmark projection cannot reconstruct an
                // older mutation after consumption/removal, and event-only hashes omit that semantic contribution.
                if (old.CandidateFingerprint != entry.Fingerprint) throw new InvalidOperationException("A retained history identity has conflicting content.");
                continue;
            }
            var stored = RuntimeHistoryIdentity.Position(candidate.Event, checked(++sequence), commit.CommitId, recordedAt, candidate.CheckpointOrdinal);
            context.RuntimeHistoryEvents.Add(EventRow(scope, stored, entry.Fingerprint));
            if (candidate.BookmarkMutation is not { } mutation) continue;
            var bookmarkId = mutation.Bookmark.Identity.BookmarkId;
            bookmarkRows.TryGetValue(bookmarkId, out var row);
            var previous = row is null ? null : Deserialize<RuntimeHistoryBookmark>(row.ContentJson);
            if (previous is not null && previous.Bookmark.Identity != mutation.Bookmark.Identity)
                throw new InvalidOperationException("A bookmark identity cannot change its owning activity execution.");
            var created = mutation.Status == RuntimeHistoryBookmarkStatus.Active;
            var item = new RuntimeHistoryBookmark(mutation.Bookmark, stored.ExecutableNodeId ?? previous?.ExecutableNodeId,
                mutation.Status, previous?.CreatedHistoryEventId ?? (created ? stored.HistoryEventId : null), created ? null : stored.HistoryEventId,
                stored.HistoryEventId, stored.Sequence, previous?.FirstRecordedSequence ?? stored.Sequence, created ? null : stored.OccurredAt,
                mutation.Reason, mutation.SelectedBookmarkId, previous?.PriorState == RuntimeHistoryPriorState.CreationEvent || created
                    ? RuntimeHistoryPriorState.CreationEvent : RuntimeHistoryPriorState.CreationMetadata)
            { SupersededByActivityExecutionId = mutation.RelatedActivityExecutionId ?? previous?.SupersededByActivityExecutionId };
            if (row is null) { row = BookmarkRow(scope, item); bookmarkRows.Add(bookmarkId, row); context.RuntimeHistoryBookmarks.Add(row); }
            else Update(row, item);
            foreach (var activity in new[] { null, item.Bookmark.Identity.ActivityExecutionId })
            {
                var subject = CountSubject(activity);
                counts.TryGetValue(subject, out var delta);
                counts[subject] = (activity, checked(delta.Total + (previous is null ? 1 : 0)),
                    checked(delta.Active + (item.Status == RuntimeHistoryBookmarkStatus.Active ? 1 : 0) - (previous?.Status == RuntimeHistoryBookmarkStatus.Active ? 1 : 0)));
            }
        }
        await StageAncestryAsync(context, scope, run, candidates.Values.Select(x => x.Candidate.Event), cancellationToken);
        await StageCountsAsync(context, scope, run, counts, cancellationToken);
    }

    internal static async ValueTask<bool> HasUnrecordedSourceGapAsync(RuntimeDbContext context, string scope, string run,
        RuntimeHistoryCapturePolicy policy, CancellationToken cancellationToken)
    {
        var sh = Hash(scope); var sk = Encode(scope); var rh = Hash(run); var rk = Encode(run);
        // This first-head lookup uses the exact source-receipt equality keys. Ordinary source ACK requires an
        // acknowledged native/head, so an earlier retired empty source here must carry explicit discard proof.
        var row = await context.DurableTimers.AsNoTracking().Where(x => x.ScopeKeyHash == sh && x.ScopeKey == sk &&
            x.WorkflowExecutionIdHash == rh && x.WorkflowExecutionId == rk && !x.HistoryPending && x.HistoryRetired).OrderBy(x => x.TimerIdHash).FirstOrDefaultAsync(cancellationToken);
        if (row is null) return false;
        var timer = EfDurableTimerStore.ReadChecked(row, scope, run);
        if (timer.HistoryResponsibility is not { Retired: true, PendingEvents.Count: 0, NoRecordCompletion: not null } history ||
            !policy.IsEquivalentTo(history.Policy))
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable",
                "An earlier timer source has no provable unrecorded completion; history coverage cannot be established.");
        return true;
    }

    private static async ValueTask ValidateCleanupSourcesAsync(RuntimeDbContext context, RuntimeCheckpointCommit commit, string scope,
        IReadOnlyList<RuntimeHistoryCandidate> ordered, IReadOnlyDictionary<string, RuntimeHistoryEventEntity> retained, CancellationToken cancellationToken)
    {
        var summaries = new Dictionary<string, ActivityExecutionBookmarkSummary>(StringComparer.Ordinal);
        foreach (var cleanup in commit.StateChanges.ActivityScopeCleanups)
        {
            var ownedBookmarks = cleanup.BookmarkIds.ToHashSet(StringComparer.Ordinal);
            foreach (var summary in cleanup.HistoryBookmarks ?? [])
            {
                if (summary.Identity.WorkflowExecutionId != commit.WorkflowExecutionId || cleanup.WorkflowExecutionId != commit.WorkflowExecutionId ||
                    !ownedBookmarks.Contains(summary.Identity.BookmarkId))
                    throw new InvalidOperationException("A cleanup history source does not belong to its exact native cleanup.");
                if (summaries.TryGetValue(summary.Identity.BookmarkId, out var old) && !SameSummary(old, summary))
                    throw new InvalidOperationException("A cleanup contains conflicting exact bookmark source summaries.");
                summaries[summary.Identity.BookmarkId] = summary;
            }
        }
        if (summaries.Count == 0) return;
        var claimed = ordered.Where(candidate => !retained.ContainsKey(candidate.Event.HistoryEventId) &&
            candidate.BookmarkMutation is { Status: RuntimeHistoryBookmarkStatus.Removed or RuntimeHistoryBookmarkStatus.Superseded } mutation &&
            summaries.ContainsKey(mutation.Bookmark.Identity.BookmarkId)).ToArray();
        if (claimed.Length == 0) return;
        var rows = new Dictionary<string, BookmarkState>(StringComparer.Ordinal);
        foreach (var chunk in claimed.Select(candidate => EfBookmarkStateStore.CreateId(scope, commit.WorkflowExecutionId, candidate.Event.BookmarkId!)).Distinct(StringComparer.Ordinal).Chunk(128))
        foreach (var row in await context.Bookmarks.AsNoTracking().Where(row => chunk.Contains(row.Id)).ToArrayAsync(cancellationToken))
        {
            var bookmarkId = row.BookmarkId;
            rows.Add(bookmarkId, EfBookmarkStateStore.MapChecked(row, scope, commit.WorkflowExecutionId, bookmarkId, row.Id));
        }
        var createdInGroup = new Dictionary<string, ActivityExecutionBookmarkSummary>(StringComparer.Ordinal);
        foreach (var candidate in ordered)
        {
            if (candidate.BookmarkMutation is not { } mutation) continue;
            var bookmarkId = mutation.Bookmark.Identity.BookmarkId;
            if (mutation.Status == RuntimeHistoryBookmarkStatus.Active)
            {
                // A retained creation receipt belongs to an earlier physical group. It cannot prove that this
                // attempt owns a still-live registration which has since disappeared.
                if (!retained.ContainsKey(candidate.Event.HistoryEventId)) createdInGroup[bookmarkId] = mutation.Bookmark;
                continue;
            }
            if (!retained.ContainsKey(candidate.Event.HistoryEventId) &&
                mutation.Status is RuntimeHistoryBookmarkStatus.Removed or RuntimeHistoryBookmarkStatus.Superseded && summaries.TryGetValue(bookmarkId, out var summary))
            {
                if (!SameSummary(mutation.Bookmark, summary)) throw new InvalidOperationException("A removal event disagrees with its exact cleanup source.");
                var foldedProof = createdInGroup.TryGetValue(bookmarkId, out var created) && SameSummary(created, summary);
                var physicalProof = rows.TryGetValue(bookmarkId, out var physical) && SameSummary(ActivityExecutionBookmarkSummary.From(physical), summary);
                if (!foldedProof && !physicalProof)
                    throw new RuntimeSchedulerWorkUnavailableException("runtime.history.cleanup-source-unavailable",
                        "The exact bookmark cleanup source changed or is unavailable; checkpoint persistence remains owed.");
            }
            createdInGroup.Remove(bookmarkId);
        }
    }

    private static bool SameSummary(ActivityExecutionBookmarkSummary left, ActivityExecutionBookmarkSummary right) =>
        left.Identity == right.Identity && left.StimulusType == right.StimulusType && left.StimulusHash == right.StimulusHash &&
        left.CreatedAt.EqualsExact(right.CreatedAt) && (left.ExpiresAt is null && right.ExpiresAt is null ||
            left.ExpiresAt is { } a && right.ExpiresAt is { } b && a.EqualsExact(b));

    internal sealed class HeadAllocationConflictException : Exception { }

    private static async ValueTask ReserveHeadAsync(RuntimeDbContext context, string scope, string run,
        RuntimeHistoryHeadEntity? previous, RuntimeHistoryHead next, CancellationToken cancellationToken)
    {
        if (previous is null)
        {
            context.RuntimeHistoryHeads.Add(HeadRow(scope, next));
            // This is still the checkpoint transaction. Its earlier staged fence/siblings, this reservation and
            // every later participant roll back together, and the immutable marker remains the final write.
            await context.SaveChangesAsync(cancellationToken);
            return;
        }
        var revision = previous.Revision;
        var id = previous.Id;
        context.Entry(previous).State = EntityState.Detached;
        var sh = Hash(scope); var sk = Encode(scope); var rh = Hash(run); var rk = Encode(run);
        var content = Serialize(next);
        var changed = await context.RuntimeHistoryHeads.Where(row => row.Id == id && row.Revision == revision &&
            row.ScopeKeyHash == sh && row.ScopeKey == sk && row.WorkflowExecutionIdHash == rh && row.WorkflowExecutionId == rk)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.FirstSequence, next.FirstSequence)
                .SetProperty(row => row.LastSequence, next.LastSequence).SetProperty(row => row.PurgedThroughSequence, next.PurgedThroughSequence)
                .SetProperty(row => row.ContentJson, content).SetProperty(row => row.Revision, checked(revision + 1)), cancellationToken);
        if (changed != 1) throw new HeadAllocationConflictException();
    }

    private static async ValueTask StageAncestryAsync(RuntimeDbContext context, string scope, string run,
        IEnumerable<RuntimeHistoryEvent> events, CancellationToken cancellationToken)
    {
        var parents = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            if (e.ActivityExecutionId is not { } activity || e.ActivityAncestryKnown != true && e.ParentActivityExecutionId is null) continue;
            if (parents.TryGetValue(activity, out var parent) && parent != e.ParentActivityExecutionId)
                throw new InvalidOperationException("History activity ancestry cannot change within one checkpoint.");
            parents[activity] = e.ParentActivityExecutionId;
        }
        var rows = new Dictionary<string, RuntimeHistoryActivityEntity>(StringComparer.Ordinal);
        foreach (var chunk in parents.Keys.Select(activity => Id("activity", scope, run, activity)).Chunk(128))
            foreach (var row in await context.RuntimeHistoryActivities.Where(row => chunk.Contains(row.Id)).ToArrayAsync(cancellationToken)) rows.Add(row.Id, row);
        foreach (var pair in parents)
        {
            var id = Id("activity", scope, run, pair.Key);
            if (rows.TryGetValue(id, out var old))
            {
                if (Read(old, scope, run, pair.Key).ParentActivityExecutionId != pair.Value)
                    throw new InvalidOperationException("A history activity identity cannot change its execution ancestry.");
                continue;
            }
            context.RuntimeHistoryActivities.Add(new() { Id = id, ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope),
                WorkflowExecutionId = Encode(run), WorkflowExecutionIdHash = Hash(run), ActivityExecutionId = Encode(pair.Key),
                ActivityExecutionIdHash = Hash(pair.Key), ParentActivityExecutionId = pair.Value is null ? null : Encode(pair.Value),
                ContentJson = Serialize(new ActivityParent(pair.Key, pair.Value)), SchemaVersion = RuntimeHistoryEfModule.SchemaVersion, Revision = 1 });
        }
    }

    private static async ValueTask StageCountsAsync(RuntimeDbContext context, string scope, string run,
        Dictionary<string, (string? Activity, long Total, long Active)> deltas, CancellationToken cancellationToken)
    {
        var rows = new Dictionary<string, RuntimeHistoryBookmarkCountEntity>(StringComparer.Ordinal);
        foreach (var chunk in deltas.Keys.Select(subject => Id("count", scope, run, subject)).Chunk(128))
            foreach (var row in await context.RuntimeHistoryBookmarkCounts.Where(row => chunk.Contains(row.Id)).ToArrayAsync(cancellationToken)) rows.Add(row.Id, row);
        foreach (var pair in deltas)
        {
            var id = Id("count", scope, run, pair.Key);
            rows.TryGetValue(id, out var row);
            var previous = row is null ? new BookmarkCount(pair.Value.Activity, 0, 0) : Read(row, scope, run, pair.Value.Activity);
            var next = new BookmarkCount(pair.Value.Activity, checked(previous.TotalCount + pair.Value.Total), checked(previous.ActiveCount + pair.Value.Active));
            if (next.ActiveCount < 0 || next.TotalCount < next.ActiveCount) throw new InvalidDataException("History bookmark count delta is invalid.");
            if (row is null)
            {
                row = new() { Id = id, ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope), WorkflowExecutionId = Encode(run), WorkflowExecutionIdHash = Hash(run),
                    ActivityExecutionId = pair.Value.Activity is null ? null : Encode(pair.Value.Activity), ActivityExecutionIdHash = Hash(pair.Key), SchemaVersion = RuntimeHistoryEfModule.SchemaVersion, Revision = 1 };
                context.RuntimeHistoryBookmarkCounts.Add(row);
            }
            else row.Revision = checked(row.Revision + 1);
            row.TotalCount = next.TotalCount; row.ActiveCount = next.ActiveCount; row.ContentJson = Serialize(next);
        }
    }
}
