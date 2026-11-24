using System.Text;
using Kernel.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Bookmark = Kernel.Durable.FoundationJournalPaging.Bookmark;

namespace Kernel.EntityFramework;

internal sealed class PostgresRootReads(EfStore store, FoundationReadCursor cursor, int pageSize) : IActorReads
{
    private readonly int _pageSize = pageSize is > 0 and < int.MaxValue
        ? pageSize : throw new ArgumentOutOfRangeException(nameof(pageSize));

    public async ValueTask<ActorView?> GetAsync(BoundScope scope, ActorRef actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (!PostgresRootAdmission.IsIssuedReferenceShape(actor.Id)) return null;
        var read = await store.ReadRootActorAsync(scope.Tenant, actor.Id, cancellationToken);
        return read is { } found ? View(scope, found.Item, found.AsOf) : null;
    }

    public async ValueTask<Page<ActorView>> ListAsync(BoundScope scope, ActorQuery query, PageToken? from = null,
        CancellationToken cancellationToken = default)
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
        var items = page.Items.Take(_pageSize).Select(row => View(scope, row, page.AsOf)).ToArray();
        PageToken? next = page.Items.Count > _pageSize
            ? new(cursor.Seal(context, Encoding.ASCII.GetBytes(items[^1].Header.Ref.Id))) : null;
        return new(items, next, page.AsOf);
    }

    private static ActorView View(BoundScope scope, EfStore.RootListItem row, long asOf) =>
        new(new(new(row.Reference), row.Type, scope.Tenant, (ActorStatus)row.Status, null, null, row.EndedAt, row.TypeData,
            row.Labels), null, null, asOf);

    public async ValueTask<Page<JournalEntry>> TailAsync(BoundScope scope, ActorRef actor, PageToken? from = null,
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

    public ValueTask<Page<DeliveryView>> InboxAsync(BoundScope scope, ActorRef actor, PageToken? from = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Inbox inspection is outside the generated-root preview.");
}

public sealed partial class EfStore
{
    /// <summary>
    /// Opens the public point, list and journal reads for a PostgreSQL generated-root preview.
    /// The ordinary preview uses the public KernelDb context on an already installed database.
    /// The trusted host supplies each BoundScope. Inbox and operator reads belong to the full provider.
    /// </summary>
    public async ValueTask<IActorReads> OpenPreviewRootReadsAsync(int pageSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfEqual(pageSize, int.MaxValue);
        if (!postgres || Engine is not null)
            throw new NotSupportedException("Generated-root preview reads require PostgreSQL.");
        await using var db = create();
        if (!db.Database.IsNpgsql())
            throw new NotSupportedException("Generated-root preview reads require PostgreSQL.");
        var cursor = await PostgresRootAdmission.OpenJournalCursorAsync(db, cancellationToken);
        return new PostgresRootReads(this, cursor, pageSize);
    }
}
