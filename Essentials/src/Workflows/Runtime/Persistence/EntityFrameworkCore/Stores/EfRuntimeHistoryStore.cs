using System.Transactions;
using System.Globalization;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.ActivityExecutionEfSupport;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistorySupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Bounded, exact-scope reads over the checkpoint writer's authoritative relational rows.</summary>
internal sealed partial class EfRuntimeHistoryStore(RuntimeDbContext context, IPersistenceAccessContextAccessor access,
    IRuntimeRecoveryContinuationCodec codec, TimeProvider? timeProvider = null) : IRuntimeHistoryStore, IRuntimeHistoryObservationWriter, IRuntimeHistoryRetentionStore
{
    private const string EventPurpose = "ef-runtime-history-events-v1";
    private const string BookmarkPurpose = "ef-runtime-history-bookmarks-v1";
    private const int MaximumScan = 1000;
    private const int MaximumParentDepth = 256;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    public bool CanRecordHistory(IRuntimeHistoryStore selectedStore)
    {
        try { _ = access.Current.RequireScope(); return selectedStore is EfRuntimeHistoryStore selected && selected.Uses(context, access); }
        catch (InvalidOperationException) { return false; }
    }
    private string Scope => access.Current.RequireScope().Value;
    internal bool Uses(RuntimeDbContext candidate, IPersistenceAccessContextAccessor candidateAccess) =>
        ReferenceEquals(context, candidate) && ReferenceEquals(access, candidateAccess);

    /// <exception cref="RuntimeHistoryEntityFrameworkPersistenceException">A provider failed; includes the original cause, scope and workflow execution.</exception>
    public async ValueTask<RuntimeHistoryHead?> FindHeadAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        var scope = Scope;
        return await ReadConsistentlyAsync("reading the history head", scope, workflowExecutionId, () => HeadAsync(scope, workflowExecutionId, cancellationToken), cancellationToken);
    }

    /// <exception cref="RuntimeHistoryEntityFrameworkPersistenceException">A provider failed; includes the original cause, scope and workflow execution.</exception>
    public async ValueTask<RuntimeHistoryEvent?> FindEventAsync(string workflowExecutionId, string historyEventId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId); ArgumentException.ThrowIfNullOrWhiteSpace(historyEventId);
        var scope = Scope;
        return await ReadConsistentlyAsync("reading an exact history event", scope, workflowExecutionId, async () =>
        {
            var head = await HeadAsync(scope, workflowExecutionId, cancellationToken);
            var id = Id("event", scope, workflowExecutionId, historyEventId);
            var row = await context.RuntimeHistoryEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (row is null) return null;
            if (ReadReceipt(row, scope, workflowExecutionId, head) != historyEventId)
                throw new RuntimeHistoryIntegrityException(workflowExecutionId, "an exact event names another resource");
            return row.IsPurged ? null : Read(row, scope, workflowExecutionId, head);
        }, cancellationToken);
    }

    /// <exception cref="RuntimeHistoryEntityFrameworkPersistenceException">A provider failed; includes the original cause, scope and workflow execution.</exception>
    public async ValueTask<RuntimeHistoryBookmark?> FindBookmarkAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId); ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);
        var scope = Scope;
        return await ReadConsistentlyAsync("reading an exact history bookmark", scope, workflowExecutionId, async () =>
        {
            var head = await HeadAsync(scope, workflowExecutionId, cancellationToken);
            var id = Id("bookmark", scope, workflowExecutionId, bookmarkId);
            var row = await context.RuntimeHistoryBookmarks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (row is null) return null;
            var item = Read(row, scope, workflowExecutionId, head);
            if (item.Bookmark.Identity.BookmarkId != bookmarkId) throw new RuntimeHistoryIntegrityException(workflowExecutionId, "an exact bookmark names another resource");
            return item;
        }, cancellationToken);
    }

    /// <exception cref="RuntimeHistoryEntityFrameworkPersistenceException">A provider failed; includes the original cause, scope and workflow execution.</exception>
    public async ValueTask<RuntimeHistoryPage> ReadPageAsync(RuntimeHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scope = Scope;
        var binding = RuntimeHistoryIdentity.CreateBinding([scope, query.WorkflowExecutionId, Number(query.Limit), query.AccessBinding,
            query.ActivityExecutionId, query.IncludeDescendants ? "true" : "false", query.BookmarkId,
            query.Category is { } category ? Number((int)category) : null, query.Name, query.Search]);
        var cursor = DecodeCursor(EventPurpose, query.ContinuationToken, binding);
        return await ReadConsistentlyAsync("reading a history page", scope, query.WorkflowExecutionId, async () =>
        {
            var run = query.WorkflowExecutionId;
            var head = await HeadAsync(scope, run, cancellationToken);
            var upper = cursor is null || cursor.CatchUp ? head?.LastSequence ?? 0 : cursor.Upper;
            var after = cursor?.After ?? 0;
            ValidatePosition(run, head, after, upper);
            var source = EfRuntimeHistorySupport.Events(context, scope, run);
            if (head is null && await source.AnyAsync(cancellationToken))
                throw new RuntimeHistoryIntegrityException(run, "event rows have no sequence head");
            if (query.BookmarkId is { } bookmark)
            {
                var bh = Hash(bookmark); var bk = Encode(bookmark);
                source = source.Where(x => x.BookmarkIdHash == bh && x.BookmarkId == bk);
            }
            else if (query.ActivityExecutionId is { } activity && !query.IncludeDescendants)
            {
                var ah = Hash(activity); var ak = Encode(activity);
                source = source.Where(x => x.ActivityExecutionIdHash == ah && x.ActivityExecutionId == ak);
            }
            if (query.Name is { } kind)
            {
                var kh = Hash(kind); var kk = Encode(kind);
                source = source.Where(x => x.NameHash == kh && x.Name == kk);
            }
            if (query.Category is { } kindCategory) source = source.Where(x => x.Category == (int)kindCategory);
            var through = Math.Max(after, Math.Min(head?.PurgedThroughSequence ?? 0, upper));
            var scanLimit = Math.Min(MaximumScan, checked(query.Limit * 4));
            var rows = await source.Where(x => x.Sequence > through && x.Sequence <= upper).OrderBy(x => x.Sequence).Take(scanLimit + 1).ToArrayAsync(cancellationToken);
            var events = rows.Select(row => Read(row, scope, run, head)).ToArray();
            var ancestry = query.IncludeDescendants ? await LoadAncestryAsync(scope, run, events.Take(scanLimit).Select(e => e.ActivityExecutionId), cancellationToken) : [];
            var wholeRun = query.BookmarkId is null && query.Name is null && query.Category is null && (query.ActivityExecutionId is null || query.IncludeDescendants);
            var items = new List<RuntimeHistoryEvent>(query.Limit);
            var scanned = 0; var scopeComplete = true;
            while (scanned < events.Length && scanned < scanLimit && items.Count < query.Limit)
            {
                var item = events[scanned++];
                if (item.Sequence <= through || wholeRun && item.Sequence != through + 1)
                    throw new RuntimeHistoryIntegrityException(run, "the retained event range contains an unexplained gap or ordering conflict");
                through = item.Sequence;
                if (query.Category is { } expectedCategory && item.Category != expectedCategory ||
                    query.Name is { } name && item.Name != name || query.BookmarkId is { } expectedBookmark && item.BookmarkId != expectedBookmark) continue;
                var inScope = InScope(run, item.ActivityExecutionId, query.ActivityExecutionId, query.IncludeDescendants, ancestry);
                if (inScope is null) scopeComplete = false;
                if (inScope != true || !MatchesSearch(item, query.Search)) continue;
                items.Add(item);
            }
            var hasMore = scanned < events.Length;
            if (!hasMore && wholeRun && through < upper)
                throw new RuntimeHistoryIntegrityException(run, "the retained event range ends before its acknowledged head");
            if (!hasMore) through = Math.Max(through, upper);
            return new RuntimeHistoryPage(head, items, upper, through, scanned,
                hasMore ? EncodeCursor(EventPurpose, new(binding, through, upper, false)) : null,
                EncodeCursor(EventPurpose, new(binding, upper, upper, true)))
            { ScopeComplete = scopeComplete, ScopeLimitation = Limitation(scopeComplete) };
        }, cancellationToken);
    }

    /// <exception cref="RuntimeHistoryEntityFrameworkPersistenceException">A provider failed; includes the original cause, scope and workflow execution.</exception>
    public async ValueTask<RuntimeHistoryBookmarkPage> ListBookmarksAsync(RuntimeHistoryBookmarkQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scope = Scope;
        var binding = RuntimeHistoryIdentity.CreateBinding([scope, query.WorkflowExecutionId, Number(query.Limit), query.AccessBinding,
            query.ActivityExecutionId, query.IncludeDescendants ? "true" : "false", query.Status is { } status ? Number((int)status) : null]);
        var cursor = DecodeCursor(BookmarkPurpose, query.ContinuationToken, binding);
        if (cursor?.CatchUp == true) throw new ArgumentException("Bookmark inventory does not accept an event catch-up cursor.", nameof(query));
        return await ReadConsistentlyAsync("reading a bookmark history page", scope, query.WorkflowExecutionId, async () =>
        {
            var run = query.WorkflowExecutionId;
            var head = await HeadAsync(scope, run, cancellationToken);
            var upper = cursor?.Upper ?? head?.LastSequence ?? 0; var after = cursor?.After ?? 0;
            ValidatePosition(run, head, after, upper);
            var source = Bookmarks(context, scope, run);
            if (head is null && await source.AnyAsync(cancellationToken)) throw new RuntimeHistoryIntegrityException(run, "bookmark rows have no sequence head");
            if (query.ActivityExecutionId is { } activity && !query.IncludeDescendants)
            {
                var ah = Hash(activity); var ak = Encode(activity);
                source = source.Where(x => x.ActivityExecutionIdHash == ah && x.ActivityExecutionId == ak);
            }
            var scanLimit = Math.Min(MaximumScan, checked(query.Limit * 4));
            var rows = await source.Where(x => x.FirstRecordedSequence > after && x.FirstRecordedSequence <= upper)
                .OrderBy(x => x.FirstRecordedSequence).Take(scanLimit + 1).ToArrayAsync(cancellationToken);
            var records = rows.Select(row => Read(row, scope, run, head)).ToArray();
            var ancestry = query.IncludeDescendants ? await LoadAncestryAsync(scope, run, records.Take(scanLimit).Select(item => (string?)item.Bookmark.Identity.ActivityExecutionId), cancellationToken) : [];
            var items = new List<RuntimeHistoryBookmark>(query.Limit);
            var scanned = 0; var through = after; var scopeComplete = true;
            while (scanned < records.Length && scanned < scanLimit && items.Count < query.Limit)
            {
                var item = records[scanned++];
                if (item.FirstRecordedSequence <= through) throw new RuntimeHistoryIntegrityException(run, "bookmark creation positions are not strictly increasing");
                through = item.FirstRecordedSequence;
                if (query.Status is { } expected && item.Status != expected) continue;
                var inScope = InScope(run, item.Bookmark.Identity.ActivityExecutionId, query.ActivityExecutionId, query.IncludeDescendants, ancestry);
                if (inScope is null) scopeComplete = false;
                if (inScope == true) items.Add(item);
            }
            long? total = null; long? active = null;
            if (head is { CapturedFromStart: true, HasGaps: false, PurgedThroughSequence: 0 } && !query.IncludeDescendants && head.Policy.SupportedKinds.Contains("BookmarkCreated", StringComparer.Ordinal))
            {
                var countId = Id("count", scope, run, CountSubject(query.ActivityExecutionId));
                var row = await context.RuntimeHistoryBookmarkCounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == countId, cancellationToken);
                var count = row is null ? new BookmarkCount(query.ActivityExecutionId, 0, 0) : Read(row, scope, run, query.ActivityExecutionId);
                total = count.TotalCount; active = count.ActiveCount;
                if (row is null && records.Length != 0) throw new RuntimeHistoryIntegrityException(run, "bookmark inventory has no count projection");
            }
            return new RuntimeHistoryBookmarkPage(head, items, scanned < records.Length ? EncodeCursor(BookmarkPurpose, new(binding, through, upper, false)) : null, total, active)
            { ScopeComplete = scopeComplete, ScopeLimitation = Limitation(scopeComplete) };
        }, cancellationToken);
    }

    private async Task<RuntimeHistoryHead?> HeadAsync(string scope, string run, CancellationToken cancellationToken)
    {
        var id = Id("head", scope, run);
        var row = await context.RuntimeHistoryHeads.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return row is null ? null : Read(row, scope, run);
    }
    private async Task<T> ReadConsistentlyAsync<T>(string operation, string scope, string run, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        if (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null ||
            context.Database.GetEnlistedTransaction() is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("History inspection requires committed rows and a clean context without a caller transaction.");
        try
        {
            // Hold the one run head while bounded rows are read: a concurrent purge or bookmark mutation must not
            // combine a pre-write head with post-write projections and masquerade as corruption.
            await using var transaction = await EfRuntimeReadTransaction.BeginAsync(context, cancellationToken);
            // The mutation lock order is native run, then history head. Match it before reading the head within this snapshot,
            // including descendant queries which may consult native ancestry later in this read boundary.
            var runId = EfWorkflowExecutionStateStore.CreateId(scope, run);
            _ = await context.WorkflowExecutionStates.AsNoTracking().Where(x => x.Id == runId).Select(x => x.Id).SingleOrDefaultAsync(cancellationToken);
            var result = await read();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { throw new RuntimeHistoryEntityFrameworkPersistenceException(operation, scope, run, exception); }
    }
    private async Task<Dictionary<string, string?>> LoadAncestryAsync(string scope, string run, IEnumerable<string?> owners, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        var missing = new HashSet<string>(StringComparer.Ordinal);
        var pending = owners.OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        for (var depth = 0; pending.Length != 0 && depth <= MaximumParentDepth; depth++)
        {
            var next = new HashSet<string>(StringComparer.Ordinal);
            foreach (var chunk in pending.Chunk(128))
            {
                var ids = chunk.Select(activity => Id("activity", scope, run, activity)).ToArray();
                foreach (var row in await context.RuntimeHistoryActivities.AsNoTracking().Where(x => ids.Contains(x.Id)).ToArrayAsync(cancellationToken))
                {
                    var activity = Decode(row.ActivityExecutionId);
                    var item = Read(row, scope, run, activity);
                    result[activity] = item.ParentActivityExecutionId;
                }
                // Native hierarchy is authoritative when a late-captured bookmark has no prior lifecycle event.
                var absent = chunk.Where(activity => !result.ContainsKey(activity)).ToArray();
                var stateIds = absent.Select(activity => CreateId("state", scope, run, activity)).ToArray();
                foreach (var row in await context.ActivityExecutionStates.AsNoTracking().Where(x => stateIds.Contains(x.Id)).ToArrayAsync(cancellationToken))
                {
                    var activity = Decode(row.ActivityExecutionId);
                    var state = EfActivityExecutionStateStore.ReadChecked(row, scope, run, activity);
                    result[activity] = state.ParentActivityExecutionId;
                }
                foreach (var activity in chunk)
                {
                    if (!result.TryGetValue(activity, out var parent)) { missing.Add(activity); continue; }
                    if (parent is not null && !result.ContainsKey(parent) && !missing.Contains(parent)) next.Add(parent);
                }
            }
            pending = next.ToArray();
        }
        return result;
    }
    private static bool? InScope(string run, string? owner, string? requested, bool descendants, Dictionary<string, string?> parents)
    {
        if (requested is null) return true;
        if (owner == requested) return true;
        if (!descendants || owner is null) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal) { owner };
        for (var depth = 0; depth < MaximumParentDepth; depth++)
        {
            if (!parents.TryGetValue(owner, out var parent)) return null;
            if (parent is null) return false;
            if (!seen.Add(parent)) throw new RuntimeHistoryIntegrityException(run, "execution ancestry contains a cycle");
            if (parent == requested) return true;
            owner = parent;
        }
        throw new RuntimeHistoryIntegrityException(run, "execution ancestry exceeds the bounded depth");
    }
    private static bool MatchesSearch(RuntimeHistoryEvent item, string? search) => string.IsNullOrEmpty(search) ||
        new[] { item.HistoryEventId, item.Name, item.ActivityExecutionId, item.ExecutableNodeId, item.BookmarkId, item.IncidentId,
            item.CheckpointId, item.PhysicalCommitId, item.Causation?.DeliveryId, item.Causation?.CommandId, item.Causation?.SchedulerWorkItemId,
            item.Causation?.WorkflowExecutionId is null || item.Causation.WorkflowExecutionId == item.WorkflowExecutionId ? item.Causation?.HistoryEventId : null }
            .Any(value => value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
    private static string? Limitation(bool complete) => complete ? null : "Some recorded execution ancestry is unavailable; this window cannot establish the complete descendant scope.";
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static void ValidatePosition(string run, RuntimeHistoryHead? head, long after, long upper)
    {
        ValidateHead(run, head);
        if (after < 0 || upper < after || upper > (head?.LastSequence ?? 0))
            throw new ArgumentException("The history continuation no longer identifies an available range.", "continuationToken");
    }
    private string EncodeCursor(string purpose, Cursor value) => codec.Encode(purpose, JsonSerializer.SerializeToUtf8Bytes(value));
    private Cursor? DecodeCursor(string purpose, string? token, string binding)
    {
        if (token is null) return null;
        try
        {
            var value = JsonSerializer.Deserialize<Cursor>(codec.Decode(purpose, token));
            if (value is null || value.Binding != binding || value.After < 0 || value.Upper < value.After) throw new FormatException();
            return value;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or InvalidOperationException)
        { throw new ArgumentException("The history continuation is invalid or belongs to another query or authorization context.", nameof(token), exception); }
    }
    private sealed record Cursor(string Binding, long After, long Upper, bool CatchUp);
}
