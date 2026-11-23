using System.Buffers.Binary;
using System.Text;
using Kernel.Foundation.Contracts;
using Kernel.EntityFramework;
using Kernel.Foundation.InMemory;
using Bookmark = Kernel.Durable.FoundationJournalPaging.Bookmark;

namespace Kernel.Native.Run;

internal sealed class PostgresRootReads(EfStore store, FoundationReadCursor cursor, int pageSize)
{
    private readonly int _pageSize = pageSize is > 0 and < int.MaxValue
        ? pageSize : throw new ArgumentOutOfRangeException(nameof(pageSize));

    internal async ValueTask<Page<ActorView>> ListAsync(BoundScope scope, ActorQuery query, PageToken? from,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var labels = query.Labels?.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray() ?? [];
        var context = Encoding.UTF8.GetBytes(DurableKeyJson.Write(new
        {
            Read = "postgres-actor-list/1", scope.Tenant, query.Type, query.Status, Labels = labels, query.EndedBefore, query.Parked
        }));
        Span<byte> bookmark = stackalloc byte[64];
        if (from is { } token && !cursor.TryOpen(context, token.Value, bookmark))
            throw new ArgumentException("The continuation does not match this actor list.", nameof(from));
        var after = from is null ? "" : Encoding.ASCII.GetString(bookmark);
        int? status = query.Status is { } filter ? (int)filter : null;
        var page = await store.ReadRootActorListAsync(scope.Tenant, after, query.Type, status, labels, query.EndedBefore,
            query.Parked, _pageSize + 1, cancellationToken);
        var items = page.Items.Take(_pageSize).Select(row => new ActorView(
            new(new(row.Reference), row.Type, scope.Tenant, (ActorStatus)row.Status, null, null, row.EndedAt, row.TypeData,
                row.Labels), null, null, page.AsOf)).ToArray();
        PageToken? next = page.Items.Count > _pageSize
            ? new(cursor.Seal(context, Encoding.ASCII.GetBytes(items[^1].Header.Ref.Id))) : null;
        return new(items, next, page.AsOf);
    }

    internal async ValueTask<Page<WorkflowRunView>> ListRunsAsync(BoundScope scope, string actorType, WorkflowRunQuery query,
        PageToken? from, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = WorkflowRunList.Context("postgres-workflow-run-history/1", scope, actorType, query);
        Span<byte> bookmark = stackalloc byte[sizeof(long) + 64];
        if (from is { } token && !cursor.TryOpen(context, token.Value, bookmark))
            throw new ArgumentException("The continuation does not match this workflow run list.", nameof(from));
        long? afterTime = from is null ? null : BinaryPrimitives.ReadInt64BigEndian(bookmark);
        var afterRef = from is null ? null : Encoding.ASCII.GetString(bookmark[sizeof(long)..]);
        var lower = WorkflowRunList.UnixMilliseconds(query.From);
        var upper = WorkflowRunList.UnixMilliseconds(query.To);
        // Round inward to representable clock values. Truncating either endpoint changes an inclusive bound.
        var page = await store.ReadRootWorkflowHistoryAsync(scope.Tenant, actorType, query.Definition, (int?)query.Status,
            lower is { } min ? (long)decimal.Ceiling(min) : null, upper is { } max ? (long)decimal.Floor(max) : null,
            afterTime, afterRef, _pageSize + 1, cancellationToken);
        var items = page.Items.Take(_pageSize).Select(row => new WorkflowRunView(new(row.Reference), row.Definition,
            (ActorStatus)row.Status, DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAt))).ToArray();
        PageToken? next = null;
        if (page.Items.Count > _pageSize)
        {
            var last = page.Items[_pageSize - 1];
            Span<byte> landing = stackalloc byte[sizeof(long) + 64];
            BinaryPrimitives.WriteInt64BigEndian(landing, last.UpdatedAt);
            Encoding.ASCII.GetBytes(last.Reference, landing[sizeof(long)..]);
            next = new(cursor.Seal(context, landing));
        }
        return new(items, next, page.AsOf);
    }

    internal async ValueTask<Page<JournalEntry>> TailAsync(BoundScope scope, ActorRef actor, PageToken? from = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var context = FoundationJournalPaging.Context(scope.Tenant, actor.Id);
        Span<byte> bookmark = stackalloc byte[Bookmark.Size];
        if (from is { } token && !cursor.TryOpen(context, token.Value, bookmark))
            throw new ArgumentException("The continuation does not match this actor journal.", nameof(from));
        var after = from is null ? default : Bookmark.Read(bookmark);

        // Issued root references are ASCII hex. An unissued, unrepresentable reference is a
        // normal miss, and an empty read still carries the database clock.
        var reference = PostgresRootAdmission.IsIssuedReferenceShape(actor.Id) ? actor.Id : "";
        var snapshot = await store.ReadRootJournalAsync(scope.Tenant, reference, after.Position, _pageSize + 1, cancellationToken);
        var (entries, last) = FoundationJournalPaging.Read(
            snapshot.Rows.Select(row => (row.Position, row.Revision, row.Entries)), after, _pageSize);

        PageToken? next = null;
        if (entries.Count > 0)
        {
            Span<byte> landing = stackalloc byte[Bookmark.Size];
            last.Write(landing);
            next = new(cursor.Seal(context, landing));
        }
        return new(entries.Select(entry => new JournalEntry(entry.Revision, entry.Entry)).ToArray(), next, snapshot.AsOf);
    }
}
