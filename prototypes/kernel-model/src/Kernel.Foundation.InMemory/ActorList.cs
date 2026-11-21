using System.Globalization;
using System.Text.Json;
using Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>The point and list read routes over committed in-memory actor checkpoints.</summary>
internal sealed class ActorList
{
    private readonly Durable.InMemoryStore _store;
    private readonly int _pageSize;

    public ActorList(Durable.InMemoryStore store, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfEqual(pageSize, int.MaxValue);
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
            .Select(actor => View(scope.Tenant, actor, snapshot.AsOf))
            .Where(actor => Matches(actor, query, labels))
            .OrderBy(actor => actor.Header.Ref.Id, StringComparer.Ordinal)
            .Take(_pageSize + 1)
            .ToList();
        PageToken? next = null;
        if (page.Count > _pageSize)
        {
            page.RemoveAt(_pageSize);
            next = new(_store.SealActorListCursor(context, page[^1].Header.Ref.Id));
        }
        return new(new Page<ActorView>(page, next, snapshot.AsOf));
    }

    private ActorView View(string tenant, Durable.InMemoryStore.ActorCheckpoint checkpoint, long asOf)
    {
        var row = checkpoint.Row;
        ActorRef? parent = row.Parent is { } parentId && row.ParentCreation > 0
            ? new ActorRef(_store.ReferenceFor(tenant, new(parentId, row.ParentCreation)))
            : null;
        // The current prototype projects workflow definition and version. K3 persists arbitrary actor labels.
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["definition"] = row.Definition,
            ["version"] = row.StateVersion.ToString(CultureInfo.InvariantCulture)
        };
        var header = new ActorHeader(new(checkpoint.Reference), row.Model, tenant, (ActorStatus)row.Status,
            row.Fault, parent, checkpoint.EndedAt, row.Definition, labels);
        var park = row is { Status: Durable.InstanceStatus.Faulted, Fault: not null } && checkpoint.EndedAt is { } parkedAt
            ? new ParkView(parkedAt, null)
            : null;
        return new(header, null, park, asOf);
    }

    private static bool Matches(ActorView view, ActorQuery query, KeyValuePair<string, string>[] labels)
    {
        var header = view.Header;
        return (query.Type is null || query.Type == header.Type) &&
            (query.Status is null || query.Status == header.Status) &&
            (query.EndedBefore is null || header.EndedAt < query.EndedBefore) &&
            (query.Parked is null || query.Parked == (view.Park is not null)) &&
            labels.All(label => header.Labels.GetValueOrDefault(label.Key) == label.Value);
    }
}
