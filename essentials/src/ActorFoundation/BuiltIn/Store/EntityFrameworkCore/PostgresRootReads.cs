using System.Buffers.Binary;
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

    private ActorView View(BoundScope scope, EfStore.RootListItem row, long asOf)
    {
        var header = new ActorHeader(new(row.Reference), row.Type, scope.Tenant, (ActorStatus)row.Status, row.ParkFault, null,
            row.EndedAt, row.TypeData, row.Labels);
        var selection = row switch
        {
            { SupportsSelection: true, Status: InstanceStatus.Running } =>
                PostgresRootSelection.SealRunning(cursor, scope.Tenant, row.Reference),
            { TerminalRevision: > 0 and var revision, EndedAt: { } at } =>
                PostgresRootSelection.SealTerminal(cursor, scope.Tenant, row.Reference, revision, at),
            _ => null
        };
        return new(header, selection, row.ParkAt is { } parkAt ? new(parkAt, null) : null, asOf);
    }

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

    public async ValueTask<Page<DeliveryView>> InboxAsync(BoundScope scope, ActorRef actor, PageToken? from = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        var context = Encoding.UTF8.GetBytes(DurableKeyJson.Write(new { Read = "postgres-root-inbox/1", scope.Tenant, Actor = actor.Id }));
        // The longest form is operator/ plus the reference, operation digest and occurrence GUID.
        const int occurrenceBytes = 9 + 64 + 1 + 64 + 1 + 32;
        var bookmark = new byte[sizeof(long) + occurrenceBytes];
        if (from is { } token && !cursor.TryOpen(context, token.Value, bookmark))
            throw new ArgumentException("The continuation does not match this actor inbox.", nameof(from));
        var afterSeq = BinaryPrimitives.ReadInt64LittleEndian(bookmark);
        var afterOccurrence = Encoding.ASCII.GetString(bookmark, sizeof(long), occurrenceBytes).TrimEnd('\0');
        var reference = PostgresRootAdmission.IsIssuedReferenceShape(actor.Id) ? actor.Id : "";
        var snapshot = await store.ReadRootInboxAsync(scope.Tenant, reference, afterSeq, afterOccurrence, _pageSize + 1, cancellationToken);
        var page = snapshot.Rows.Take(_pageSize).Select(row => new DeliveryView(
            cursor.InboxHandle(DurableKeyJson.Write(new { scope.Tenant, Reference = reference, row.Occurrence, row.Seq })),
            row.Id, row.Kind, null, row.Due, row.KeptBy switch
            {
                ParkKeepReason.Consumed => DeliveryState.Consumed,
                ParkKeepReason.Arrived => DeliveryState.Arrived,
                null => DeliveryState.Pending,
                _ => throw new StoreFormatException("The root inbox has an unsupported kept reason.")
            }, row.Attempts, row.Size)).ToArray();
        PageToken? next = null;
        if (snapshot.Rows.Count > _pageSize)
        {
            var last = snapshot.Rows[_pageSize - 1];
            Array.Clear(bookmark);
            BinaryPrimitives.WriteInt64LittleEndian(bookmark, last.Seq);
            Encoding.ASCII.GetBytes(last.Occurrence.AsSpan(), bookmark.AsSpan(sizeof(long)));
            next = new(cursor.Seal(context, bookmark));
        }
        return new(page, next, snapshot.AsOf);
    }
}

public sealed partial class EfStore
{
    /// <summary>
    /// Opens the public point, list and journal reads for a PostgreSQL generated-root preview.
    /// The ordinary preview uses the public KernelDb context on an already installed database.
    /// The trusted host supplies each BoundScope. The direct-input root profiles also expose their pending inbox metadata.
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
