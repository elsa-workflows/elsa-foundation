using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Mapping = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryProjectionMapping;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.ActivityExecutionEfSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Private history participant under construction. The caller owns the native transaction, its
/// rollback/tracker restoration, final participant save and marker-last commit. No provider is registered here.
/// </summary>
internal static class EfRuntimeHistoryCheckpoint
{
    internal static async ValueTask<IReadOnlyList<RuntimeHistoryEvent>> StageAsync(RuntimeDbContext context,
        IPersistenceAccessContextAccessor access, RuntimeCheckpointCommit commit, string scope,
        DateTimeOffset recordedAt, EfSchemaChain chain, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("History must join the caller-owned native checkpoint transaction.");
        var accessSnapshot = access.Current;
        void EnsureAccess()
        {
            if (access.Current != accessSnapshot || accessSnapshot.RequireScope().Value != scope)
                throw new InvalidOperationException("History checkpoint changed persistence access.");
        }
        EnsureAccess(); cancellationToken.ThrowIfCancellationRequested();
        var run = commit.WorkflowExecutionId;
        var batches = commit.HistoryBatches;
        var incoming = commit.StateChanges.WorkflowExecution?.State;
        if (incoming is not null && (incoming.WorkflowExecutionId != run || incoming.HistoryCaptureEnabledAtAdmission is { } enabled &&
            enabled != (incoming.HistoryCapturePolicy is not null)))
            throw new InvalidOperationException("Native history admission provenance disagrees with its pinned policy or execution.");

        // A current write lock precedes the first consistent native/head read. The head revision CAS
        // still detects a snapshot established earlier by another native participant (notably InnoDB).
        if (batches is { Count: > 0 } || incoming?.HistoryCapturePolicy is not null)
            await EfWorkflowExecutionStateStore.LockWorkflowExecutionForWriteAsync(context, scope, run, cancellationToken);
        var nativeId = EfWorkflowExecutionStateStore.CreateId(scope, run);
        var nativeRow = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == nativeId, cancellationToken);
        var native = nativeRow is null ? null : EfWorkflowExecutionStateStore.ReadChecked(nativeRow, scope, run);
        if (native is not null) accessSnapshot.EnsureTenantScope(native.TenantId);
        if (incoming is not null) accessSnapshot.EnsureTenantScope(incoming.TenantId);
        var policy = native is null ? incoming?.HistoryCapturePolicy : native.HistoryCapturePolicy;
        if (native is not null && (native.HistoryCaptureEnabledAtAdmission is { } retainedEnabled && retainedEnabled != (policy is not null) ||
            incoming is not null && (native.HistoryCaptureEnabledAtAdmission != incoming.HistoryCaptureEnabledAtAdmission ||
                !(policy?.IsEquivalentTo(incoming.HistoryCapturePolicy) ?? incoming.HistoryCapturePolicy is null))))
            throw new InvalidOperationException("History must retain the native execution's admitted policy, including legacy absence.");
        var mapping = new Mapping(chain);
        var headId = Mapping.Id("head", scope, run);
        var headRow = await context.Set<RuntimeHistoryHeadEntity>().SingleOrDefaultAsync(row => row.Id == headId, cancellationToken);
        var head = headRow is null ? null : mapping.Read(headRow, scope, run);
        EnsureAccess();
        if (head is not null && !head.Policy.IsEquivalentTo(policy))
            throw new RuntimeHistoryIntegrityException(run, "the retained head disagrees with the native admission policy");
        if (batches is not { Count: > 0 })
        {
            if (policy?.Required == true)
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The pinned required history contribution remains owed.");
            if (policy is null) return [];
            await EfExecutionLivenessStateStore.EnsureWorkflowExecutionNotDeletedAsync(context, scope, run, cancellationToken);
            var gap = head is null ? new RuntimeHistoryHead(run, 0, 0, commit.CommitId, recordedAt, policy, false, true, 0)
                : head with { HasGaps = true, LastPhysicalCommitId = commit.CommitId, LastCommittedAt = recordedAt };
            EnsureAccess();
            await ReserveHeadAsync(context, mapping, chain, scope, run, headRow, gap, cancellationToken);
            EnsureAccess(); return [];
        }
        if (policy is null)
            throw new InvalidOperationException("History cannot be enabled retroactively without a pinned native admission policy.");
        await EfExecutionLivenessStateStore.EnsureWorkflowExecutionNotDeletedAsync(context, scope, run, cancellationToken);
        var ordered = new List<(RuntimeHistoryCandidate Candidate, string Fingerprint)>();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var batch in batches)
        {
            RuntimeHistorySafety.Validate(batch, run);
            if (!policy.IsEquivalentTo(batch.Policy)) throw new InvalidOperationException("History groups disagree with the native pinned policy.");
            foreach (var candidate in batch.Candidates)
            {
                var fingerprint = RuntimeHistoryIdentity.Compute(candidate);
                if (seen.TryGetValue(candidate.Event.HistoryEventId, out var prior))
                {
                    if (prior != fingerprint) throw new InvalidOperationException("Conflicting history identities cannot be folded.");
                    continue;
                }
                seen.Add(candidate.Event.HistoryEventId, fingerprint); ordered.Add((candidate, fingerprint));
            }
        }
        var retained = new Dictionary<string, RuntimeHistoryEventEntity>(StringComparer.Ordinal);
        foreach (var chunk in seen.Keys.Select(id => Mapping.Id("event", scope, run, id)).Chunk(128))
        foreach (var row in await context.Set<RuntimeHistoryEventEntity>().AsNoTracking().Where(row => chunk.Contains(row.Id)).ToArrayAsync(cancellationToken))
        {
            var receipt = mapping.ReadReceipt(row, scope, run, head);
            if (!receipt.IsPurged) _ = mapping.Read(row, scope, run, head);
            if (receipt.CandidateFingerprint != seen.GetValueOrDefault(receipt.HistoryEventId))
                throw new InvalidOperationException("A retained history identity has conflicting content.");
            retained.Add(receipt.HistoryEventId, row);
        }
        await ValidateCleanupSourcesAsync(context, commit, scope, ordered.Select(entry => entry.Candidate).ToArray(), retained, cancellationToken);
        var bookmarkRows = new Dictionary<string, RuntimeHistoryBookmarkEntity>(StringComparer.Ordinal);
        foreach (var chunk in ordered.Where(entry => entry.Candidate.BookmarkMutation is not null)
            .Select(entry => Mapping.Id("bookmark", scope, run, entry.Candidate.BookmarkMutation!.Bookmark.Identity.BookmarkId)).Distinct(StringComparer.Ordinal).Chunk(128))
        foreach (var row in await context.Set<RuntimeHistoryBookmarkEntity>().Where(row => chunk.Contains(row.Id)).ToArrayAsync(cancellationToken))
        {
            var bookmark = mapping.Read(row, scope, run, head);
            bookmarkRows.Add(bookmark.Bookmark.Identity.BookmarkId, row);
        }
        var sequence = head?.LastSequence ?? 0;
        var newCount = ordered.Count(entry => !retained.ContainsKey(entry.Candidate.Event.HistoryEventId));
        var next = new RuntimeHistoryHead(run, head is { FirstSequence: > 0 } ? head.FirstSequence : newCount == 0 ? 0 : checked(sequence + 1),
            checked(sequence + newCount), commit.CommitId, recordedAt, policy, head?.CapturedFromStart ?? batches.Any(batch => batch.StartsAtRunStart),
            head?.HasGaps ?? false, head?.PurgedThroughSequence ?? 0);
        EnsureAccess();
        await ReserveHeadAsync(context, mapping, chain, scope, run, headRow, next, cancellationToken);
        var events = new List<RuntimeHistoryEvent>(newCount);
        var deltas = new Dictionary<string, (string? Activity, long Total, long Active)>(StringComparer.Ordinal);
        foreach (var entry in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (retained.ContainsKey(entry.Candidate.Event.HistoryEventId)) continue;
            var stored = RuntimeHistoryIdentity.Position(entry.Candidate.Event, checked(++sequence), commit.CommitId, recordedAt, entry.Candidate.CheckpointOrdinal);
            context.Set<RuntimeHistoryEventEntity>().Add(mapping.Row(scope, stored, entry.Fingerprint)); events.Add(stored);
            if (entry.Candidate.BookmarkMutation is not { } mutation) continue;
            var bookmarkId = mutation.Bookmark.Identity.BookmarkId;
            bookmarkRows.TryGetValue(bookmarkId, out var row);
            // A projection updated earlier in this same group is already beyond the original head.
            var previous = row is null ? null : mapping.Read(row, scope, run, next);
            if (previous is not null && !SameSummary(previous.Bookmark, mutation.Bookmark))
                throw new InvalidOperationException("A history bookmark cannot change its immutable owner or definition.");
            var active = mutation.Status == RuntimeHistoryBookmarkStatus.Active;
            if (active && previous is { Status: not RuntimeHistoryBookmarkStatus.Active })
                throw new InvalidOperationException("A disposed bookmark needs a new handle before it can wait again.");
            var item = new RuntimeHistoryBookmark(mutation.Bookmark, stored.ExecutableNodeId ?? previous?.ExecutableNodeId, mutation.Status,
                previous?.CreatedHistoryEventId ?? (active ? stored.HistoryEventId : null), active ? null : stored.HistoryEventId,
                stored.HistoryEventId, stored.Sequence, previous?.FirstRecordedSequence ?? stored.Sequence,
                active || stored.OccurredAtKnown == false ? null : stored.OccurredAt, mutation.Reason, mutation.SelectedBookmarkId,
                previous?.PriorState == RuntimeHistoryCreationEvidence.CreationEvent || active ? RuntimeHistoryCreationEvidence.CreationEvent : RuntimeHistoryCreationEvidence.CreationMetadata)
                { SupersededByActivityExecutionId = mutation.RelatedActivityExecutionId ?? previous?.SupersededByActivityExecutionId };
            var mapped = mapping.Row(scope, item);
            if (row is null) { row = mapped; bookmarkRows.Add(bookmarkId, row); context.Set<RuntimeHistoryBookmarkEntity>().Add(row); }
            else { row.Status = mapped.Status; row.ContentJson = mapped.ContentJson; row.SchemaVersion = chain.CurrentVersion; row.Revision = checked(row.Revision + 1); }
            foreach (var activity in new[] { null, item.Bookmark.Identity.ActivityExecutionId })
            {
                var subject = Mapping.CountSubject(activity); deltas.TryGetValue(subject, out var delta);
                deltas[subject] = (activity, checked(delta.Total + (previous is null ? 1 : 0)),
                    checked(delta.Active + (active ? 1 : 0) - (previous?.Status == RuntimeHistoryBookmarkStatus.Active ? 1 : 0)));
            }
        }
        await StageAncestryAsync(context, mapping, scope, run, events, cancellationToken);
        await StageCountsAsync(context, mapping, scope, run, deltas, cancellationToken);
        EnsureAccess();
        await EfRuntimeHistoryOccurrenceProjection.StageAsync(context, scope, run, chain, events, cancellationToken);
        EnsureAccess(); return events.AsReadOnly();
    }

    internal static async ValueTask ReserveHeadAsync(RuntimeDbContext context, Mapping mapping, EfSchemaChain chain,
        string scope, string run, RuntimeHistoryHeadEntity? previous, RuntimeHistoryHead next, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("History reservation requires the caller-owned native transaction.");
        EnsureWritable(context, chain);
        Mapping.ValidateHead(run, next);
        if (previous is null)
        {
            context.Set<RuntimeHistoryHeadEntity>().Add(mapping.Row(scope, next));
            // Still the native transaction; later failure must roll back this reservation and every sibling.
            await context.SaveChangesAsync(cancellationToken); return;
        }
        var revision = previous.Revision; var id = previous.Id;
        context.Entry(previous).State = EntityState.Detached;
        var scopeKey = Encode(scope); var scopeHash = Hash(scope); var runKey = Encode(run); var runHash = Hash(run);
        var content = RuntimeArtifactJson.Serialize(next);
        var changed = await context.Set<RuntimeHistoryHeadEntity>().Where(row => row.Id == id && row.Revision == revision &&
            row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash && row.WorkflowExecutionId == runKey && row.WorkflowExecutionIdHash == runHash)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.FirstSequence, next.FirstSequence).SetProperty(row => row.LastSequence, next.LastSequence)
                .SetProperty(row => row.PurgedThroughSequence, next.PurgedThroughSequence).SetProperty(row => row.ContentJson, content)
                .SetProperty(row => row.SchemaVersion, chain.CurrentVersion).SetProperty(row => row.Revision, checked(revision + 1)), cancellationToken);
        if (changed != 1) throw new HeadAllocationConflictException();
    }

    private static async ValueTask StageAncestryAsync(RuntimeDbContext context, Mapping mapping, string scope, string run,
        IReadOnlyList<RuntimeHistoryEvent> events, CancellationToken cancellationToken)
    {
        var parents = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var item in events)
        {
            if (item.ActivityExecutionId is not { } activity || item.ActivityAncestryKnown != true && item.ParentActivityExecutionId is null) continue;
            if (item.ParentActivityExecutionId == activity)
                throw new InvalidOperationException("An exact execution cannot be its own known parent.");
            if (parents.TryGetValue(activity, out var existing) && existing != item.ParentActivityExecutionId)
                throw new InvalidOperationException("One exact execution cannot have conflicting known ancestry.");
            parents[activity] = item.ParentActivityExecutionId;
        }
        var rows = new Dictionary<string, RuntimeHistoryActivityEntity>(StringComparer.Ordinal);
        foreach (var chunk in parents.Keys.Select(id => Mapping.Id("activity", scope, run, id)).Chunk(128))
        foreach (var row in await context.Set<RuntimeHistoryActivityEntity>().Where(row => chunk.Contains(row.Id)).ToArrayAsync(cancellationToken)) rows.Add(row.Id, row);
        foreach (var (activity, parent) in parents)
        {
            if (rows.TryGetValue(Mapping.Id("activity", scope, run, activity), out var row))
            {
                if (mapping.Read(row, scope, run, activity).ParentActivityExecutionId != parent)
                    throw new InvalidOperationException("A known history ancestry identity cannot be reparented.");
            }
            else context.Set<RuntimeHistoryActivityEntity>().Add(mapping.Row(scope, run, new Mapping.ActivityParent(activity, parent)));
        }
    }

    private static async ValueTask StageCountsAsync(RuntimeDbContext context, Mapping mapping, string scope, string run,
        Dictionary<string, (string? Activity, long Total, long Active)> deltas, CancellationToken cancellationToken)
    {
        var rows = new Dictionary<string, RuntimeHistoryBookmarkCountEntity>(StringComparer.Ordinal);
        foreach (var chunk in deltas.Keys.Select(subject => Mapping.Id("count", scope, run, subject)).Chunk(128))
        foreach (var row in await context.Set<RuntimeHistoryBookmarkCountEntity>().Where(row => chunk.Contains(row.Id)).ToArrayAsync(cancellationToken)) rows.Add(row.Id, row);
        foreach (var (subject, delta) in deltas)
        {
            rows.TryGetValue(Mapping.Id("count", scope, run, subject), out var row);
            var previous = row is null ? new Mapping.BookmarkCount(delta.Activity, 0, 0) : mapping.Read(row, scope, run, delta.Activity);
            var next = new Mapping.BookmarkCount(delta.Activity, checked(previous.TotalCount + delta.Total), checked(previous.ActiveCount + delta.Active));
            if (next.ActiveCount < 0 || next.TotalCount < next.ActiveCount) throw new InvalidDataException("History bookmark count delta is invalid.");
            var mapped = mapping.Row(scope, run, next);
            if (row is null) context.Set<RuntimeHistoryBookmarkCountEntity>().Add(mapped);
            else { row.TotalCount = mapped.TotalCount; row.ActiveCount = mapped.ActiveCount; row.ContentJson = mapped.ContentJson;
                row.SchemaVersion = mapped.SchemaVersion; row.Revision = checked(row.Revision + 1); }
        }
    }

    private static async ValueTask ValidateCleanupSourcesAsync(RuntimeDbContext context, RuntimeCheckpointCommit commit, string scope,
        IReadOnlyList<RuntimeHistoryCandidate> ordered, IReadOnlyDictionary<string, RuntimeHistoryEventEntity> retained, CancellationToken cancellationToken)
    {
        var summaries = new Dictionary<string, ActivityExecutionBookmarkSummary>(StringComparer.Ordinal);
        foreach (var cleanup in commit.StateChanges.ActivityScopeCleanups)
        foreach (var summary in cleanup.HistoryBookmarks ?? [])
        {
            if (cleanup.WorkflowExecutionId != commit.WorkflowExecutionId || summary.Identity.WorkflowExecutionId != commit.WorkflowExecutionId ||
                !cleanup.BookmarkIds.Contains(summary.Identity.BookmarkId, StringComparer.Ordinal) ||
                summaries.TryGetValue(summary.Identity.BookmarkId, out var prior) && !SameSummary(prior, summary))
                throw new InvalidOperationException("Cleanup history requires an exact owned native bookmark source.");
            summaries[summary.Identity.BookmarkId] = summary;
        }
        var claimed = ordered.Where(candidate => !retained.ContainsKey(candidate.Event.HistoryEventId) && candidate.BookmarkMutation is
            { Status: RuntimeHistoryBookmarkStatus.Removed or RuntimeHistoryBookmarkStatus.Superseded } mutation && summaries.ContainsKey(mutation.Bookmark.Identity.BookmarkId)).ToArray();
        if (claimed.Length == 0) return;
        var rows = new Dictionary<string, BookmarkState>(StringComparer.Ordinal);
        foreach (var chunk in claimed.Select(candidate => EfBookmarkStateStore.CreateId(scope, commit.WorkflowExecutionId, candidate.Event.BookmarkId!)).Distinct(StringComparer.Ordinal).Chunk(128))
        foreach (var row in await context.Bookmarks.AsNoTracking().Where(row => chunk.Contains(row.Id)).ToArrayAsync(cancellationToken))
            rows.Add(row.BookmarkId, EfBookmarkStateStore.MapChecked(row, scope, commit.WorkflowExecutionId, row.BookmarkId, row.Id));
        var created = new Dictionary<string, ActivityExecutionBookmarkSummary>(StringComparer.Ordinal);
        foreach (var candidate in ordered)
        {
            if (candidate.BookmarkMutation is not { } mutation) continue;
            var id = mutation.Bookmark.Identity.BookmarkId;
            if (mutation.Status == RuntimeHistoryBookmarkStatus.Active)
            {
                if (!retained.ContainsKey(candidate.Event.HistoryEventId)) created[id] = mutation.Bookmark;
                continue;
            }
            if (!retained.ContainsKey(candidate.Event.HistoryEventId) && mutation.Status is RuntimeHistoryBookmarkStatus.Removed or RuntimeHistoryBookmarkStatus.Superseded &&
                summaries.TryGetValue(id, out var expected) && (!SameSummary(expected, mutation.Bookmark) ||
                !(created.TryGetValue(id, out var sameGroup) && SameSummary(sameGroup, expected)) &&
                !(rows.TryGetValue(id, out var physical) && SameSummary(ActivityExecutionBookmarkSummary.From(physical), expected))))
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.cleanup-source-unavailable", "The exact native cleanup source changed or remains unavailable.");
            created.Remove(id);
        }
    }

    private static bool SameSummary(ActivityExecutionBookmarkSummary left, ActivityExecutionBookmarkSummary right) =>
        left.Identity == right.Identity && left.StimulusType == right.StimulusType && left.StimulusHash == right.StimulusHash &&
        left.CreatedAt.EqualsExact(right.CreatedAt) && (left.ExpiresAt is null && right.ExpiresAt is null ||
            left.ExpiresAt is { } a && right.ExpiresAt is { } b && a.EqualsExact(b));

    private static void EnsureWritable(RuntimeDbContext context, EfSchemaChain chain)
    {
        // The head reservation bypasses SaveChanges interceptors, so it must honor the same family gate explicitly.
        var gate = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());
        var state = gate?.StateOf(chain.Family);
        if (gate is not null && state is null) throw new InvalidOperationException("The history family has not been admitted by the schema gate.");
        if (state is { WritesRefused: true }) throw new EfSchemaFamilyWritesRefusedException(chain.Family,
            state.WriteVersion, state.UnreadableFinalizedVersion!, state.ReadableVersions);
        if (state is not null && state.WriteVersion != chain.CurrentVersion)
            throw new EfSchemaWriteRefusedException(chain.Family, state.WriteVersion, chain.CurrentVersion);
    }

    internal sealed class HeadAllocationConflictException : Exception;
}
