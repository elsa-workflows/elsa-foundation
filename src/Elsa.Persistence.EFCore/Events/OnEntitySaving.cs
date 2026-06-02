using Elsa.Events.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Elsa.Persistence.EFCore.Events
{
    /// <summary>
    /// Domain event published by <c>ElsaDbContextBase.BeforeSavingChanges</c> for each modified
    /// <c>Entity</c>. Handlers participate via <see cref="IEventHandler{TDomainEvent}"/> and
    /// may filter by entity type inline. Carries the active <see cref="DbContext"/> and the
    /// <see cref="EntityEntry"/> so handlers can read or write shadow properties on the entry.
    /// </summary>
    public sealed record OnEntitySaving(DbContext DbContext, EntityEntry Entry) : IEvent;
}
