using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EFCore.Events;

/// <summary>
/// Domain event published for each materialised <see cref="Entity"/> after it is read from the
/// store, so contributors can hydrate the loaded instance (e.g. deserialise a shadow/source
/// column into rich state). Carries the active <see cref="DbContext"/> and the loaded
/// <see cref="Entity"/>. The read-side mirror of <see cref="EntitySaving"/>.
/// </summary>
/// <remarks>
/// <para>
/// Publication is owned by provider-specific persistence adapters that opt into this lifecycle
/// event; the base EF Core shell no longer ships a generic query/read-store adapter.
/// </para>
/// <para>
/// Published on the default (Sequential) strategy so hydration completes before the entity is
/// handed back to the caller — a Background dispatch would let the query return an un-hydrated
/// entity. The single aggregating <c>ApplyEntityLoadingHandlers</c> handler is the sole
/// subscriber: it resolves and dispatches every registered
/// <see cref="Contracts.IEntityLoadingHandler{TDbContext,TEntity}"/> contributor closed over the
/// runtime DbContext + entity types. Features contribute by implementing that typed handler and
/// registering it via <c>AddEntityLoadingHandler</c> / the assembly scan — they do NOT subscribe
/// to this event directly.
/// </para>
/// </remarks>
public sealed record EntityLoading(DbContext DbContext, Entity Entity) : IEvent;
