using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EFCore.Events;

/// <summary>
/// Domain event published for each materialised <see cref="Entity"/> after it is read from the
/// store, so contributors can hydrate the loaded instance (e.g. deserialise a shadow/source
/// column into rich state). Carries the active <see cref="DbContext"/> and the loaded
/// <see cref="Entity"/>. The read-side mirror of <see cref="OnEntitySaving"/>.
/// </summary>
/// <remarks>
/// <para>
/// Publication sites: <see cref="Services.EFCoreReadStore{TDbContext,TEntity}"/>
/// (<c>QueryAsync</c> / <c>FirstOrDefaultAsync</c>) publishes it for every entity returned by the
/// read-only (<c>AsNoTracking</c>) query path; mutate-then-save commands that load through their
/// own tracked context publish it themselves so the already-tracked instance is hydrated by the
/// same context that will save it.
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
public sealed record OnEntityLoading(DbContext DbContext, Entity Entity) : IEvent;