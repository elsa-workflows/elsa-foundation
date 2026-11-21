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
        var context = JsonSerializer.SerializeToUtf8Bytes(new { Read = "actor-tail", scope.Tenant, Actor = actor.Id });
        Span<byte> cursor = stackalloc byte[sizeof(long) + sizeof(int)];
        (long Position, int Ordinal) after = default;
        if (from is { } token)
        {
            if (!_store.TryOpenReadCursor(context, token.Value, cursor))
                throw new ArgumentException("The continuation does not match this actor journal.", nameof(from));
            after = (BinaryPrimitives.ReadInt64LittleEndian(cursor), BinaryPrimitives.ReadInt32LittleEndian(cursor[sizeof(long)..]));
        }

        // A commit can write several entries. Resume within that commit when a page ends partway through it.
        var snapshot = _store.ReadFoundationJournal(scope.Tenant, actor.Id, after.Position, _pageSize + 1);
        var added = new List<JournalEntry>();
        var last = after;
        foreach (var row in snapshot.Rows)
        {
            using var document = JsonDocument.Parse(row.Entries);
            var entries = document.RootElement;
            var split = entries.ValueKind == JsonValueKind.Array && entries.GetArrayLength() > 0;
            var count = split ? entries.GetArrayLength() : 1;
            var ordinal = row.Position == after.Position ? after.Ordinal + 1 : 0;
            for (; ordinal < count; ordinal++)
            {
                added.Add(new(row.Revision, split ? entries[ordinal].GetRawText() : row.Entries));
                last = (row.Position, ordinal);
                if (added.Count == _pageSize) break;
            }
            if (added.Count == _pageSize) break;
        }

        // The final nonempty page also carries a bookmark so a caller can follow the tail after catching up.
        if (added.Count == 0) return new(new Page<JournalEntry>(added, null, snapshot.AsOf));
        BinaryPrimitives.WriteInt64LittleEndian(cursor, last.Position);
        BinaryPrimitives.WriteInt32LittleEndian(cursor[sizeof(long)..], last.Ordinal);
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
        var after = from is null ? 0 : BinaryPrimitives.ReadInt64LittleEndian(cursor);
        var snapshot = _store.ReadFoundationInbox(scope.Tenant, actor.Id, after, _pageSize + 1);
        var page = snapshot.Rows.Take(_pageSize).Select(row => Delivery(actor, row)).ToArray();
        PageToken? next = null;
        if (snapshot.Rows.Count > _pageSize)
        {
            BinaryPrimitives.WriteInt64LittleEndian(cursor, snapshot.Rows[_pageSize - 1].Seq);
            next = new(_store.SealReadCursor(context, cursor));
        }
        return new(new Page<DeliveryView>(page, next, snapshot.AsOf));
    }

    private DeliveryView Delivery(ActorRef actor, Durable.InMemoryStore.ActorDelivery row)
    {
        var message = row.Message;
        var handle = _store.DeliveryHandle(actor.Id, row.Seq, message.InboxKey);
        var id = message.EventMatch is { } match ? Durable.EventMatch.Decode(match).Event :
            message.InboxKey.HasIssuedOrigin && message.StorageId is null ? _store.MessageHandle("inbox:" + message.Id) : message.Id;
        return new(handle, id, message.Start is null ? message.Kind : Kinds.Start,
            row.Sender is { } sender ? new ActorRef(sender) : null, message.Due,
            row.Held ? DeliveryState.Held : DeliveryState.Pending, row.Attempts, JsonSerializer.SerializeToUtf8Bytes(message).LongLength);
    }

    private ActorView View(string tenant, Durable.InMemoryStore.ActorCheckpoint checkpoint, long asOf)
    {
        var row = checkpoint.Row;
        ActorRef? parent = row.Parent is { } parentId && row.ParentCreation > 0
            ? new ActorRef(_store.ReferenceFor(tenant, new(parentId, row.ParentCreation)))
            : null;
        var header = new ActorHeader(new(checkpoint.Reference), row.Model, tenant, (ActorStatus)row.Status,
            row.Fault, parent, checkpoint.EndedAt, checkpoint.Header.TypeData, checkpoint.Header.Labels);
        var park = checkpoint.Park is { } mark ? new ParkView(mark.At, null) : null;
        return new(header, _store.SealFoundationSelection(tenant, checkpoint), park, asOf);
    }

    private static bool Matches(Durable.InMemoryStore.ActorCheckpoint actor, ActorQuery query, KeyValuePair<string, string>[] labels) =>
        (query.Type is null || query.Type == actor.Row.Model) &&
        (query.Status is null || query.Status == (ActorStatus)actor.Row.Status) &&
        (query.EndedBefore is null || actor.EndedAt < query.EndedBefore) &&
        (query.Parked is null || query.Parked == (actor.Park is not null)) &&
        labels.All(label => actor.Header.Labels.GetValueOrDefault(label.Key) == label.Value);
}
