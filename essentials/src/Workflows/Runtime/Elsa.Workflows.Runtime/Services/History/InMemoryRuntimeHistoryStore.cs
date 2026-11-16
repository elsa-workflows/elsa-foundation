using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Bounded read projection over the same native state that atomically accepts checkpoint history.</summary>
internal sealed class InMemoryRuntimeHistoryStore(
    InMemoryRuntimeCheckpointStoreState state,
    IPersistenceAccessContextAccessor access,
    IRuntimeRecoveryContinuationCodec codec) : IRuntimeHistoryStore
{
    private const string EventPurpose = "runtime-history-events-v1";
    private const string BookmarkPurpose = "runtime-history-bookmarks-v1";
    private const int MaximumScan = 1000;
    private const int MaximumParentDepth = 256;

    internal bool Uses(InMemoryRuntimeCheckpointStoreState candidate, PersistenceScope scope) =>
        ReferenceEquals(state, candidate) && StringComparer.Ordinal.Equals(Scope, scope.Value);

    internal bool UsesAccessor(IPersistenceAccessContextAccessor candidate) => ReferenceEquals(access, candidate);

    private string Scope => access.Current.RequireScope().Value;

    public ValueTask<RuntimeHistoryHead?> FindHeadAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = Scope;
        lock (state.SyncRoot)
        {
            var head = state.HistoryHeads.GetValueOrDefault((scope, workflowExecutionId));
            ValidateHead(workflowExecutionId, head);
            return ValueTask.FromResult(head);
        }
    }

    public ValueTask<RuntimeHistoryEvent?> FindEventAsync(string workflowExecutionId, string historyEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyEventId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = Scope;
        lock (state.SyncRoot)
        {
            var item = state.HistoryEventsById.GetValueOrDefault((scope, workflowExecutionId, historyEventId));
            var head = state.HistoryHeads.GetValueOrDefault((scope, workflowExecutionId));
            ValidateHead(workflowExecutionId, head);
            if (item is null && state.HistoryReceiptPositions.TryGetValue((scope, workflowExecutionId, historyEventId), out var position))
            {
                if (head is null || position <= 0 || position > head.LastSequence || position > head.PurgedThroughSequence)
                    throw new RuntimeHistoryIntegrityException(workflowExecutionId, "an exact event receipt has unacknowledged missing content");
            }
            if (item is not null && (item.WorkflowExecutionId != workflowExecutionId || item.HistoryEventId != historyEventId))
                throw new RuntimeHistoryIntegrityException(workflowExecutionId, "an exact event projection names a different resource");
            if (item is not null)
            {
                if (head is null || item.Sequence <= 0 || item.Sequence < head.FirstSequence ||
                    item.Sequence <= head.PurgedThroughSequence || item.Sequence > head.LastSequence)
                    throw new RuntimeHistoryIntegrityException(workflowExecutionId, "an exact event has no acknowledged retained position");
            }
            return ValueTask.FromResult(item);
        }
    }

    public ValueTask<RuntimeHistoryBookmark?> FindBookmarkAsync(string workflowExecutionId, string bookmarkId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = Scope;
        lock (state.SyncRoot)
        {
            var item = state.HistoryBookmarks.GetValueOrDefault((scope, workflowExecutionId, bookmarkId));
            ValidateBookmarkIdentity(workflowExecutionId, bookmarkId, item);
            if (item is not null)
                ValidateBookmarkPosition(workflowExecutionId, item, state.HistoryHeads.GetValueOrDefault((scope, workflowExecutionId)));
            return ValueTask.FromResult(item);
        }
    }

    public ValueTask<RuntimeHistoryPage> ReadPageAsync(RuntimeHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = Scope;
        var binding = Binding(scope, query.WorkflowExecutionId, query.Limit, query.AccessBinding,
            query.ActivityExecutionId, query.IncludeDescendants, query.BookmarkId, query.Category, query.Name, query.Search);
        var cursor = Decode(EventPurpose, query.ContinuationToken, binding);
        lock (state.SyncRoot)
        {
            var head = state.HistoryHeads.GetValueOrDefault((scope, query.WorkflowExecutionId));
            var upper = cursor is null || cursor.CatchUp ? head?.LastSequence ?? 0 : cursor.Upper;
            var after = cursor?.After ?? 0;
            ValidatePosition(query.WorkflowExecutionId, head, after, upper);
            var source = EventSource(scope, query);
            if (head is null && source.Count != 0)
                throw new RuntimeHistoryIntegrityException(query.WorkflowExecutionId, "event records have no sequence head");
            var wholeRunSource = query.Name is null && query.Category is null && query.BookmarkId is null && (query.ActivityExecutionId is null || query.IncludeDescendants);
            var index = FirstAfter(source, after);
            var items = new List<RuntimeHistoryEvent>(query.Limit);
            var scanned = 0;
            var scopeComplete = true;
            var through = Math.Max(after, Math.Min(head?.PurgedThroughSequence ?? 0, upper));
            var scanLimit = Math.Min(MaximumScan, checked(query.Limit * 4));
            while (index < source.Count && source[index].Sequence <= upper && scanned < scanLimit && items.Count < query.Limit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = source[index++];
                if (item.WorkflowExecutionId != query.WorkflowExecutionId)
                    throw new RuntimeHistoryIntegrityException(query.WorkflowExecutionId, "an event belongs to another workflow execution");
                if (item.Sequence <= through)
                    throw new RuntimeHistoryIntegrityException(query.WorkflowExecutionId, "event positions are not strictly increasing");
                if (wholeRunSource && item.Sequence != through + 1)
                    throw new RuntimeHistoryIntegrityException(query.WorkflowExecutionId, "the retained event range contains an unexplained gap");
                through = item.Sequence;
                scanned++;
                if (Matches(scope, item, query, ref scopeComplete)) items.Add(item);
            }
            var hasMore = index < source.Count && source[index].Sequence <= upper;
            if (!hasMore && wholeRunSource && through < upper)
                throw new RuntimeHistoryIntegrityException(query.WorkflowExecutionId, "the retained range ends before its acknowledged head");
            // Exhausting a filtered index also establishes that no further matching entry exists up to this bound.
            if (!hasMore) through = Math.Max(through, upper);
            var next = hasMore ? Encode(EventPurpose, new(binding, through, upper, false)) : null;
            var catchUp = Encode(EventPurpose, new(binding, upper, upper, true));
            return ValueTask.FromResult(new RuntimeHistoryPage(head, items, upper, through, scanned, next, catchUp)
            {
                ScopeComplete = scopeComplete,
                ScopeLimitation = scopeComplete ? null : "Some recorded execution ancestry is unavailable; this window cannot establish the complete descendant scope."
            });
        }
    }

    public ValueTask<RuntimeHistoryBookmarkPage> ListBookmarksAsync(RuntimeHistoryBookmarkQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = Scope;
        var binding = Binding(scope, query.WorkflowExecutionId, query.Limit, query.AccessBinding,
            query.ActivityExecutionId, query.IncludeDescendants, query.Status);
        var cursor = Decode(BookmarkPurpose, query.ContinuationToken, binding);
        lock (state.SyncRoot)
        {
            var head = state.HistoryHeads.GetValueOrDefault((scope, query.WorkflowExecutionId));
            var upper = cursor?.Upper ?? head?.LastSequence ?? 0;
            var after = cursor?.After ?? 0;
            ValidatePosition(query.WorkflowExecutionId, head, after, upper);
            IReadOnlyList<string> order = query.ActivityExecutionId is { } activity && !query.IncludeDescendants
                ? state.HistoryActivityBookmarkOrder.GetValueOrDefault((scope, query.WorkflowExecutionId, activity)) ?? []
                : state.HistoryBookmarkOrder.GetValueOrDefault((scope, query.WorkflowExecutionId)) ?? [];
            RuntimeHistoryBookmark Resolve(int index)
            {
                var item = state.HistoryBookmarks.GetValueOrDefault((scope, query.WorkflowExecutionId, order[index]))
                    ?? throw new RuntimeHistoryIntegrityException(query.WorkflowExecutionId, "bookmark order references missing lifecycle evidence");
                ValidateBookmarkIdentity(query.WorkflowExecutionId, order[index], item);
                ValidateBookmarkPosition(query.WorkflowExecutionId, item, head);
                return item;
            }
            var low = 0;
            var high = order.Count;
            while (low < high)
            {
                var mid = low + (high - low) / 2;
                if (Resolve(mid).FirstRecordedSequence <= after) low = mid + 1; else high = mid;
            }
            var items = new List<RuntimeHistoryBookmark>(query.Limit);
            var scanned = 0;
            var scopeComplete = true;
            var through = after;
            var scanLimit = Math.Min(MaximumScan, checked(query.Limit * 4));
            while (low < order.Count && Resolve(low).FirstRecordedSequence <= upper && scanned < scanLimit && items.Count < query.Limit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = Resolve(low++);
                if (item.FirstRecordedSequence <= through)
                    throw new RuntimeHistoryIntegrityException(query.WorkflowExecutionId, "bookmark positions are not strictly increasing");
                through = item.FirstRecordedSequence;
                scanned++;
                if (query.Status is { } status && item.Status != status) continue;
                var inScope = InExecutionScope(scope, query.WorkflowExecutionId, item.Bookmark.Identity.ActivityExecutionId,
                    query.ActivityExecutionId, query.IncludeDescendants, null);
                if (inScope is null) scopeComplete = false;
                if (inScope != true) continue;
                items.Add(item);
            }
            var hasMore = low < order.Count && Resolve(low).FirstRecordedSequence <= upper;
            var next = hasMore ? Encode(BookmarkPurpose, new(binding, through, upper, false)) : null;
            var exactCounts = head is { CapturedFromStart: true, HasGaps: false, PurgedThroughSequence: 0 } && head.Policy.SupportedKinds.Contains("BookmarkCreated", StringComparer.Ordinal) && head.LastSequence == upper && !query.IncludeDescendants;
            long? active = exactCounts ? query.ActivityExecutionId is { } owner
                ? state.HistoryActivityActiveBookmarks.GetValueOrDefault((scope, query.WorkflowExecutionId, owner))
                : state.HistoryActiveBookmarks.GetValueOrDefault((scope, query.WorkflowExecutionId)) : null;
            long? total = exactCounts && query.Status is null ? order.Count : query.Status == RuntimeHistoryBookmarkStatus.Active ? active : null;
            return ValueTask.FromResult(new RuntimeHistoryBookmarkPage(head, items, next, total, active)
            {
                ScopeComplete = scopeComplete,
                ScopeLimitation = scopeComplete ? null : "Some recorded execution ancestry is unavailable; this window cannot establish the complete descendant scope."
            });
        }
    }

    private IReadOnlyList<RuntimeHistoryEvent> EventSource(string scope, RuntimeHistoryQuery query)
    {
        var indexes = new List<IReadOnlyList<RuntimeHistoryEvent>>();
        if (query.Name is { } name)
            indexes.Add(state.HistoryNameEvents.GetValueOrDefault((scope, query.WorkflowExecutionId, name)) ?? []);
        if (query.Category is { } category)
            indexes.Add(state.HistoryCategoryEvents.GetValueOrDefault((scope, query.WorkflowExecutionId, category)) ?? []);
        if (query.BookmarkId is { } bookmark)
            indexes.Add(state.HistoryBookmarkEvents.GetValueOrDefault((scope, query.WorkflowExecutionId, bookmark)) ?? []);
        if (query.ActivityExecutionId is { } activity && !query.IncludeDescendants)
            indexes.Add(state.HistoryActivityEvents.GetValueOrDefault((scope, query.WorkflowExecutionId, activity)) ?? []);
        if (indexes.Count != 0) return indexes.MinBy(index => index.Count)!;
        return state.HistoryEvents.GetValueOrDefault((scope, query.WorkflowExecutionId)) ?? [];
    }

    private bool Matches(string scope, RuntimeHistoryEvent item, RuntimeHistoryQuery query, ref bool scopeComplete)
    {
        if (query.Category is { } category && item.Category != category) return false;
        if (query.Name is { } name && !StringComparer.Ordinal.Equals(name, item.Name)) return false;
        if (query.BookmarkId is { } bookmark && !StringComparer.Ordinal.Equals(bookmark, item.BookmarkId)) return false;
        var inScope = InExecutionScope(scope, query.WorkflowExecutionId, item.ActivityExecutionId, query.ActivityExecutionId,
            query.IncludeDescendants, item.ParentActivityExecutionId);
        if (inScope is null) scopeComplete = false;
        if (inScope != true) return false;
        if (string.IsNullOrEmpty(query.Search)) return true;
        // Only explicitly safe common fields are searched; no captured payload or arbitrary metadata scan.
        return new[] { item.HistoryEventId, item.Name, item.ActivityExecutionId, item.ExecutableNodeId,
                item.BookmarkId, item.IncidentId, item.CheckpointId, item.PhysicalCommitId, item.Causation?.HistoryEventId, item.Causation?.DeliveryId,
                item.Causation?.CommandId, item.Causation?.SchedulerWorkItemId }
            .Any(value => value?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) == true);
    }

    private bool? InExecutionScope(string scope, string run, string? owner, string? requested, bool descendants, string? parent)
    {
        if (requested is null) return true;
        if (StringComparer.Ordinal.Equals(owner, requested)) return true;
        if (!descendants || owner is null) return false;
        if (parent is null && !state.HistoryParents.TryGetValue((scope, run, owner), out parent)) return null;
        var seen = new HashSet<string>(StringComparer.Ordinal) { owner };
        for (var depth = 0; parent is not null && depth < MaximumParentDepth; depth++)
        {
            if (!seen.Add(parent)) throw new RuntimeHistoryIntegrityException(run, "execution ancestry contains a cycle");
            if (StringComparer.Ordinal.Equals(parent, requested)) return true;
            if (!state.HistoryParents.TryGetValue((scope, run, parent), out parent)) return null;
        }
        if (parent is not null) throw new RuntimeHistoryIntegrityException(run, "execution ancestry exceeds the bounded depth");
        return false;
    }

    private static int FirstAfter(IReadOnlyList<RuntimeHistoryEvent> source, long after)
    {
        var low = 0;
        var high = source.Count;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (source[mid].Sequence <= after) low = mid + 1; else high = mid;
        }
        return low;
    }

    private static void ValidatePosition(string run, RuntimeHistoryHead? head, long after, long upper)
    {
        ValidateHead(run, head);
        if (after < 0 || upper < 0 || after > upper || upper > (head?.LastSequence ?? 0))
            throw new ArgumentException("The history continuation no longer identifies an available range.", "continuationToken");
    }

    private static void ValidateHead(string run, RuntimeHistoryHead? head)
    {
        if (head is not null && head.WorkflowExecutionId != run)
            throw new RuntimeHistoryIntegrityException(run, "the sequence head belongs to another workflow execution");
        if (head is { } value && (value.FirstSequence < 0 || value.LastSequence < 0 || value.PurgedThroughSequence < 0 ||
            value.PurgedThroughSequence > value.LastSequence ||
            value.FirstSequence > value.LastSequence && (value.LastSequence == long.MaxValue || value.FirstSequence != value.LastSequence + 1)))
            throw new RuntimeHistoryIntegrityException(run, "the retained sequence head is invalid");
    }

    private static void ValidateBookmarkIdentity(string run, string bookmark, RuntimeHistoryBookmark? item)
    {
        if (item is not null && (item.Bookmark.Identity.WorkflowExecutionId != run || item.Bookmark.Identity.BookmarkId != bookmark))
            throw new RuntimeHistoryIntegrityException(run, "a bookmark projection names a different resource");
    }

    private static void ValidateBookmarkPosition(string run, RuntimeHistoryBookmark item, RuntimeHistoryHead? head)
    {
        ValidateHead(run, head);
        if (head is null || item.FirstRecordedSequence <= 0 || item.LastSequence < item.FirstRecordedSequence || item.LastSequence > head.LastSequence)
            throw new RuntimeHistoryIntegrityException(run, "a bookmark lifecycle has no acknowledged position");
    }

    private static string Binding(params object?[] fields) => RuntimeHistoryIdentity.CreateBinding(fields
        .Select(field => field is IFormattable formatted ? formatted.ToString(null, CultureInfo.InvariantCulture) : field?.ToString()).ToArray());

    private string Encode(string purpose, Cursor cursor) => codec.Encode(purpose, JsonSerializer.SerializeToUtf8Bytes(cursor));

    private Cursor? Decode(string purpose, string? token, string binding)
    {
        if (token is null) return null;
        try
        {
            var cursor = JsonSerializer.Deserialize<Cursor>(codec.Decode(purpose, token));
            if (cursor is null || cursor.Binding != binding || cursor.After < 0 || cursor.Upper < cursor.After)
                throw new FormatException();
            return cursor;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or InvalidOperationException)
        {
            throw new ArgumentException("The history continuation is invalid or belongs to another query or authorization context.", nameof(token), exception);
        }
    }

    private sealed record Cursor(string Binding, long After, long Upper, bool CatchUp);
}
