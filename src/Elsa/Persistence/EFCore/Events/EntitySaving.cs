using Elsa.Events.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Elsa.Persistence.EFCore.Events;

/// <summary>
/// Domain event published by <c>ElsaDbContextBase.BeforeSavingChanges</c> for each modified
/// <c>Entity</c>, just before <c>SaveChangesAsync</c> flushes. Carries the active
/// <see cref="DbContext"/> and the <see cref="EntityEntry"/> so contributors can read or write
/// shadow/source columns on the entry. The write-side mirror of <see cref="EntityLoading"/>.
/// </summary>
/// <remarks>
/// Published on the default (Sequential) strategy so every contributor runs before the flush.
/// The single aggregating <c>ApplyEntitySavingHandlers</c> handler is the sole subscriber: it
/// resolves and dispatches every registered
/// <see cref="Contracts.IEntitySavingHandler{TDbContext,TEntity}"/> contributor closed over the
/// runtime DbContext + entity types. Features contribute by implementing that typed handler and
/// registering it via <c>AddEntitySavingHandler</c> / the assembly scan — they do NOT subscribe
/// to this event directly.
/// </remarks>
public sealed record EntitySaving(DbContext DbContext, EntityEntry Entry) : IEvent;