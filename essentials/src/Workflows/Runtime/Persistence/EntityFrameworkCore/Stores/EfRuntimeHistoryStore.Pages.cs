using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Mapping = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryProjectionMapping;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeHistoryStore
{
    public ValueTask<RuntimeHistoryPage> ReadPageAsync(RuntimeHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query); var bound = Capture(query.WorkflowExecutionId, cancellationToken);
        var binding = Binding(bound.Scope, query.WorkflowExecutionId, query.Limit, query.AccessBinding, query.ActivityExecutionId,
            query.IncludeDescendants, query.BookmarkId, query.Category, query.Name, query.Search);
        var cursor = Decode(EventPurpose, query.ContinuationToken, binding);
        return Read(bound, query.WorkflowExecutionId, "read-page", policy => EventPage(bound.Scope, query, binding, cursor, policy, cancellationToken),
            () => new RuntimeHistoryPage(null, [], 0, 0, 0, null, Encode(EventPurpose, new(binding, 0, 0, true, null))), cancellationToken);
    }
    private async Task<RuntimeHistoryPage> EventPage(string scope, RuntimeHistoryQuery query, string binding, Cursor? cursor, RuntimeHistoryCapturePolicy policy, CancellationToken token)
    {
        var run = query.WorkflowExecutionId; var head = await Head(scope, run, policy, token);
        var upper = cursor is null || cursor.CatchUp ? head?.LastSequence ?? 0 : cursor.Upper; var after = cursor?.After ?? 0;
        ValidateWindow(run, head, cursor, after, upper);
        var source = Events(scope, run);
        if (head is null && await source.AnyAsync(token)) throw Invalid(run, "event receipts have no sequence head");
        var whole = true;
        // One maintained index is the bounded scan source; remaining predicates are checked against
        // decoded content. No count/group/offset walk is needed to choose that source.
        if (query.Name is { } name) { var hash = EfRelationalIdentity.Hash(name); source = source.Where(row => row.NameHash == hash); whole = false; }
        else if (query.Category is { } category) { source = source.Where(row => row.Category == (int)category); whole = false; }
        else if (query.BookmarkId is { } bookmark) { var hash = EfRelationalIdentity.Hash(bookmark); source = source.Where(row => row.BookmarkIdHash == hash); whole = false; }
        else if (query.ActivityExecutionId is { } owner && !query.IncludeDescendants)
        { var hash = EfRelationalIdentity.Hash(owner); source = source.Where(row => row.ActivityExecutionIdHash == hash); whole = false; }
        var lower = Math.Max(after, Math.Min(head?.PurgedThroughSequence ?? 0, upper));
        var scanLimit = Math.Min(MaximumScan, checked(query.Limit * 4));
        var rows = await source.Where(row => row.Sequence > lower && row.Sequence <= upper).OrderBy(row => row.Sequence).Take(scanLimit + 1).ToArrayAsync(token);
        var events = rows.Take(scanLimit).Select(row => mapping.Read(row, scope, run, head)).ToArray();
        var ancestry = new Ancestry(this, scope, run, token);
        if (query.IncludeDescendants && query.ActivityExecutionId is { } requested)
            await ancestry.Prepare(events.Where(item => MatchesFields(item, query)).Select(item => (item.ActivityExecutionId, item.ParentActivityExecutionId)), requested);
        var items = new List<RuntimeHistoryEvent>(query.Limit); var scanned = 0; var complete = true; var through = lower;
        foreach (var item in events)
        {
            token.ThrowIfCancellationRequested();
            if (items.Count == query.Limit) break;
            if (item.Sequence <= through || whole && item.Sequence != through + 1)
                throw Invalid(run, "the retained event window has an unexplained or nonincreasing position");
            through = item.Sequence; scanned++;
            if (!MatchesFields(item, query)) continue;
            var inScope = ancestry.Contains(item.ActivityExecutionId, query.ActivityExecutionId, query.IncludeDescendants, item.ParentActivityExecutionId);
            if (inScope is null) complete = false;
            if (inScope == true && MatchesSearch(item, query.Search)) items.Add(item);
        }
        var more = rows.Length > scanned;
        if (!more && whole && through < upper) throw Invalid(run, "the retained event window ends before its acknowledged head");
        if (!more) through = Math.Max(through, upper);
        return new(head, items.AsReadOnly(), upper, through, scanned,
            more ? Encode(EventPurpose, new(binding, through, upper, false, Policy(head))) : null,
            Encode(EventPurpose, new(binding, upper, upper, true, Policy(head))))
        { ScopeComplete = complete, ScopeLimitation = complete ? null : IncompleteScope };
    }

    public ValueTask<RuntimeHistoryBookmarkPage> ListBookmarksAsync(RuntimeHistoryBookmarkQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query); var bound = Capture(query.WorkflowExecutionId, cancellationToken);
        var binding = Binding(bound.Scope, query.WorkflowExecutionId, query.Limit, query.AccessBinding,
            query.ActivityExecutionId, query.IncludeDescendants, query.Status);
        var cursor = Decode(BookmarkPurpose, query.ContinuationToken, binding);
        if (cursor is { CatchUp: true }) throw new ArgumentException("Bookmark inventory does not accept catch-up tokens.", nameof(query));
        return Read(bound, query.WorkflowExecutionId, "list-bookmarks", policy => BookmarkPage(bound.Scope, query, binding, cursor, policy, cancellationToken),
            static () => new RuntimeHistoryBookmarkPage(null, [], null), cancellationToken);
    }
    private async Task<RuntimeHistoryBookmarkPage> BookmarkPage(string scope, RuntimeHistoryBookmarkQuery query, string binding, Cursor? cursor, RuntimeHistoryCapturePolicy policy, CancellationToken token)
    {
        var run = query.WorkflowExecutionId; var head = await Head(scope, run, policy, token);
        var upper = cursor?.Upper ?? head?.LastSequence ?? 0; var after = cursor?.After ?? 0;
        ValidateWindow(run, head, cursor, after, upper);
        var source = Bookmarks(scope, run);
        if (head is null && await source.AnyAsync(token)) throw Invalid(run, "bookmark records have no sequence head");
        if (query.ActivityExecutionId is { } owner && !query.IncludeDescendants)
        { var hash = EfRelationalIdentity.Hash(owner); source = source.Where(row => row.ActivityExecutionIdHash == hash); }
        var scanLimit = Math.Min(MaximumScan, checked(query.Limit * 4));
        var rows = await source.Where(row => row.FirstRecordedSequence > after && row.FirstRecordedSequence <= upper)
            .OrderBy(row => row.FirstRecordedSequence).Take(scanLimit + 1).ToArrayAsync(token);
        var records = rows.Take(scanLimit).Select(row => mapping.Read(row, scope, run, head)).ToArray();
        var ancestry = new Ancestry(this, scope, run, token);
        if (query.IncludeDescendants && query.ActivityExecutionId is { } requested)
            await ancestry.Prepare(records.Where(item => query.Status is null || item.Status == query.Status)
                .Select(item => ((string?)item.Bookmark.Identity.ActivityExecutionId, (string?)null)), requested);
        var items = new List<RuntimeHistoryBookmark>(query.Limit); var scanned = 0; var through = after; var complete = true;
        foreach (var item in records)
        {
            token.ThrowIfCancellationRequested(); if (items.Count == query.Limit) break;
            if (item.FirstRecordedSequence <= through) throw Invalid(run, "bookmark positions are not strictly increasing");
            through = item.FirstRecordedSequence; scanned++;
            if (query.Status is { } status && item.Status != status) continue;
            var inScope = ancestry.Contains(item.Bookmark.Identity.ActivityExecutionId, query.ActivityExecutionId, query.IncludeDescendants);
            if (inScope is null) complete = false;
            if (inScope == true) items.Add(item);
        }
        var exactCounts = head is { CapturedFromStart: true, HasGaps: false, PurgedThroughSequence: 0 } &&
            head.Policy.SupportedKinds.Contains("BookmarkCreated", StringComparer.Ordinal) && head.LastSequence == upper && !query.IncludeDescendants;
        long? active = null, total = null;
        if (exactCounts)
        {
            var id = Mapping.Id("count", scope, run, Mapping.CountSubject(query.ActivityExecutionId));
            var row = await context.Set<RuntimeHistoryBookmarkCountEntity>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, token);
            if (row is null && await source.AnyAsync(token)) throw Invalid(run, "known bookmark inventory has no maintained count projection");
            var count = row is null ? new Mapping.BookmarkCount(query.ActivityExecutionId, 0, 0) : mapping.Read(row, scope, run, query.ActivityExecutionId);
            if (count.TotalCount < records.Length || count.ActiveCount < records.Count(item => item.Status == RuntimeHistoryBookmarkStatus.Active) ||
                after == 0 && rows.Length <= scanLimit && (count.TotalCount != records.Length ||
                    count.ActiveCount != records.Count(item => item.Status == RuntimeHistoryBookmarkStatus.Active)))
                throw Invalid(run, "maintained bookmark counts contradict the bounded current inventory");
            active = count.ActiveCount; total = query.Status is null ? count.TotalCount : query.Status == RuntimeHistoryBookmarkStatus.Active ? active : null;
        }
        return new(head, items.AsReadOnly(), rows.Length > scanned ? Encode(BookmarkPurpose, new(binding, through, upper, false, Policy(head))) : null, total, active)
        { ScopeComplete = complete, ScopeLimitation = complete ? null : IncompleteScope };
    }
}
