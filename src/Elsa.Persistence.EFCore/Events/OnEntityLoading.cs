using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EFCore.Events
{
    /// <summary>
    /// Domain event published by <c>EFCoreQueries.ApplyEntityLoadingHandlers</c> for each
    /// materialised <see cref="Entity"/> after it is read from the store. Handlers participate
    /// via <see cref="IEventHandler{TDomainEvent}"/> and may filter by entity type inline; they
    /// hydrate the loaded instance (e.g. deserialise a shadow/source column into rich state).
    /// Carries the active <see cref="DbContext"/> and the loaded <see cref="Entity"/>. The
    /// read-side mirror of <see cref="OnEntitySaving"/>.
    /// </summary>
    /// <remarks>
    /// Published on the default (Sequential) strategy so hydration completes before the entity is
    /// handed back to the caller — a Background dispatch would let the query return an un-hydrated
    /// entity. Coexists with the legacy
    /// <see cref="Contracts.IEntityLoadingHandler{TDbContext,TEntity}"/> dispatch path: features
    /// migrated to the §2.6.1 domain-event mechanism register against this event, while features
    /// still on the legacy interface continue to run through the older path until they're migrated.
    /// </remarks>
    public sealed record OnEntityLoading(DbContext DbContext, Entity Entity) : IEvent;
}
