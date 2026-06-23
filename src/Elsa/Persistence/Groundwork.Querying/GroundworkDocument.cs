using Elsa.Primitives.Entities;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// The persistence envelope every closed-query-backed Groundwork document is stored in. It carries the
/// domain <paramref name="Entity"/> payload plus a constant <paramref name="Collection"/> partition
/// value. The collection value lets the unfiltered "load every document of this kind" read be served
/// through the declared-index <b>equality</b> query that every Groundwork provider supports, rather than
/// relying on a provider-specific "scan all" capability — the same technique the runtime executable
/// bridge uses.
/// </summary>
/// <param name="Collection">Constant partition value, indexed by the unit's by-collection keyword index.</param>
/// <param name="Entity">The domain entity payload.</param>
/// <typeparam name="TEntity">The entity type.</typeparam>
public sealed record GroundworkDocument<TEntity>(string Collection, TEntity Entity)
    where TEntity : Entity;
