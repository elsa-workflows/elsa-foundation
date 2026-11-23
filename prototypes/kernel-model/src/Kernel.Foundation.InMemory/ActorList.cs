using System.Buffers.Binary;
using System.Text.Json;
using Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>The point and list read routes over committed in-memory actor checkpoints.</summary>
internal sealed class ActorList : IActorReads
{
    private readonly Durable.InMemoryStore _store;
    private readonly int _pageSize;

    public ActorList(Durable.InMemoryStore store, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, int.MaxValue - 1);
        (_store, _pageSize) = (store, pageSize);
    }

    public ValueTask<ActorView?> GetAsync(BoundScope scope, ActorRef actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        var checkpoint = _store.ReadFoundationActor(scope.Tenant, actor.Id);
        return new(checkpoint.Actor is { } found ? View(scope.Tenant, found, checkpoint.AsOf) : null);
    }

    public ValueTask<Page<ActorView>> ListAsync(BoundScope scope, ActorQuery query, PageToken? from = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        // Labels are a set of filters. Equivalent label orderings use the same continuation context.
        var labels = query.Labels?.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray() ?? [];
        var context = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Read = "actor-list",
            scope.Tenant,
            query.Type,
            query.Status,
            Labels = labels,
            query.EndedBefore,
            query.Parked
        });
        var after = "";
        if (from is { } token && !_store.TryOpenActorListCursor(context, token.Value, out after))
            throw new ArgumentException("The continuation does not match this actor list.", nameof(from));

        var snapshot = _store.ReadFoundationActors(scope.Tenant);
        var page = snapshot.Actors
            .Where(actor => StringComparer.Ordinal.Compare(actor.Reference, after) > 0)
            .Where(actor => Matches(actor, query, labels))
            .OrderBy(actor => actor.Reference, StringComparer.Ordinal)
            .Take(_pageSize + 1)
            .Select(actor => View(scope.Tenant, actor, snapshot.AsOf))
            .ToList();
        PageToken? next = null;
        if (page.Count > _pageSize)
        {
            page.RemoveAt(_pageSize);
            next = new(_store.SealActorListCursor(context, page[^1].Header.Ref.Id));
        }
        return new(new Page<ActorView>(page, next, snapshot.AsOf));
    }

    public ValueTask<Page<JournalEntry>> TailAsync(BoundScope scope, ActorRef actor, PageToken? from = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        var context = Durable.FoundationJournalPaging.Context(scope.Tenant, actor.Id);
        Span<byte> cursor = stackalloc byte[Durable.FoundationJournalPaging.Bookmark.Size];
        Durable.FoundationJournalPaging.Bookmark after = default;
        if (from is { } token)
        {
            if (!_store.TryOpenReadCursor(context, token.Value, cursor))
                throw new ArgumentException("The continuation does not match this actor journal.", nameof(from));
            after = Durable.FoundationJournalPaging.Bookmark.Read(cursor);
        }

        // A commit can write several entries. Resume within that commit when a page ends partway through it.
        var snapshot = _store.ReadFoundationJournal(scope.Tenant, actor.Id, after.Position, _pageSize + 1);
        var (entries, last) = Durable.FoundationJournalPaging.Read(
            snapshot.Rows.Select(row => (row.Position, row.Revision, row.Entries)), after, _pageSize);
        var added = entries.Select(entry => new JournalEntry(entry.Revision, entry.Entry)).ToArray();

        // The final nonempty page also carries a bookmark so a caller can follow the tail after catching up.
        if (added.Length == 0) return new(new Page<JournalEntry>(added, null, snapshot.AsOf));
        last.Write(cursor);
        return new(new Page<JournalEntry>(added, new PageToken(_store.SealReadCursor(context, cursor)), snapshot.AsOf));
    }

    public ValueTask<Page<DeliveryView>> InboxAsync(BoundScope scope, ActorRef actor, PageToken? from = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        var context = JsonSerializer.SerializeToUtf8Bytes(new { Read = "actor-inbox", scope.Tenant, Actor = actor.Id });
        Span<byte> cursor = stackalloc byte[sizeof(long)];
        if (from is { } token && !_store.TryOpenReadCursor(context, token.Value, cursor))
            throw new ArgumentException("The continuation does not match this actor inbox.", nameof(from));
        var after = from is null ? long.MinValue : BinaryPrimitives.ReadInt64LittleEndian(cursor);
        var snapshot = _store.ReadFoundationInbox(scope.Tenant, actor.Id, after, _pageSize + 1);
        var page = snapshot.Rows.Take(_pageSize).Select(Delivery).ToArray();
        PageToken? next = null;
        if (snapshot.Rows.Count > _pageSize)
        {
            BinaryPrimitives.WriteInt64LittleEndian(cursor, snapshot.Rows[_pageSize - 1].Order);
            next = new(_store.SealReadCursor(context, cursor));
        }
        return new(new Page<DeliveryView>(page, next, snapshot.AsOf));
    }

    private DeliveryView Delivery(Durable.InMemoryStore.ActorDelivery row)
    {
        var message = row.Message;
        var id = message.Start is null && Durable.Events.MatchFor(message) is { } match ? match.Event :
            (message.InboxKey.HasIssuedOrigin || message.InboxScope == Durable.InboxKey.EventCreatorScope) && message.StorageId is null ? _store.MessageHandle("inbox:" + message.Id) : message.Id;
        var state = row.KeptBy switch
        {
            Durable.ParkKeepReason.Arrived => DeliveryState.Arrived,
            Durable.ParkKeepReason.Consumed or Durable.ParkKeepReason.Selected => DeliveryState.Consumed,
            _ => row.Held ? DeliveryState.Held : DeliveryState.Pending
        };
        return new(row.Handle, id, message.Start is null ? message.Kind : Kinds.Start,
            row.Sender is { } sender ? new ActorRef(sender) : null, message.Due, state, row.Attempts,
            JsonSerializer.SerializeToUtf8Bytes(message).LongLength);
    }

    private ActorView View(string tenant, Durable.InMemoryStore.ActorCheckpoint checkpoint, long asOf)
    {
        var row = checkpoint.Row;
        ActorRef? parent = row.Parent is { } parentId && row.ParentCreation > 0
            ? new ActorRef(_store.ReferenceFor(tenant, new(parentId, row.ParentCreation)))
            : null;
        var header = new ActorHeader(new(checkpoint.Reference), row.Model, tenant, (ActorStatus)row.Status,
            row.Fault, parent, checkpoint.EndedAt, checkpoint.Header.TypeData, checkpoint.Header.Labels);
        var resolution = row.ParkDiscard?.Kind switch
        {
            Durable.ParkClearKind.Terminate => "terminating",
            Durable.ParkClearKind.Discard => "discarding",
            Durable.ParkClearKind.Expiry => "expiring",
            Durable.ParkClearKind.Delete => "deleting",
            _ => row.ParkRecovery is not null ? "recovering" : null
        };
        var park = checkpoint.Park is { } mark
            ? new ParkView(mark.At, resolution)
            : null;
        return new(header, _store.SealFoundationSelection(tenant, checkpoint), park, asOf);
    }

    private static bool Matches(Durable.InMemoryStore.ActorCheckpoint actor, ActorQuery query, KeyValuePair<string, string>[] labels) =>
        (query.Type is null || query.Type == actor.Row.Model) &&
        (query.Status is null || query.Status == (ActorStatus)actor.Row.Status) &&
        (query.EndedBefore is null || actor.EndedAt < query.EndedBefore) &&
        (query.Parked is null || query.Parked == (actor.Park is not null)) &&
        labels.All(label => actor.Header.Labels.GetValueOrDefault(label.Key) == label.Value);
}
