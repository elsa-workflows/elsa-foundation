using System.Linq.Expressions;
using Elsa.Primitives.Entities;

namespace Elsa.Persistence.Core.Queries;

/// <summary>
/// A closed, provider-neutral query specification. It is intentionally <b>not</b>
/// <see cref="IQueryable{T}"/>: the only expressible shape is an <c>AND</c> of <c>OR</c>-groups of
/// <see cref="QueryComparison{TEntity}"/> (operators limited to <see cref="QueryOp"/>), plus at most
/// one <see cref="QueryOrder{TEntity}"/>. That bounded shape is exactly what the Elsa design lanes
/// need and exactly what every persistence provider can be asked to translate — which is what makes
/// a single host-selected provider able to back every module.
/// <para>
/// This type is an internal provider-translation detail behind the named persistence ports; callers
/// do not build queries by hand against it.
/// </para>
/// </summary>
/// <typeparam name="TEntity">The entity type being queried.</typeparam>
public sealed class Query<TEntity> where TEntity : Entity
{
    private readonly List<List<QueryComparison<TEntity>>> _clauses = [];

    /// <summary>The conjunctive clauses; every clause is a disjunction (<c>OR</c>) of comparisons.</summary>
    public IReadOnlyList<IReadOnlyList<QueryComparison<TEntity>>> Clauses => _clauses;

    /// <summary>The optional single-field ordering.</summary>
    public QueryOrder<TEntity>? Order { get; private set; }

    /// <summary>When <c>true</c>, the provider should bypass ambient tenant filtering for this query.</summary>
    public bool TenantAgnostic { get; private set; }

    /// <summary>Starts a query with no constraints (matches everything).</summary>
    public static Query<TEntity> All() => new();

    /// <summary>Starts a query with a first comparison clause.</summary>
    public static Query<TEntity> Where<TProp>(Expression<Func<TEntity, TProp>> field, QueryOp op, object? value) =>
        new Query<TEntity>().And(field, op, value);

    /// <summary>Adds a new conjunctive (<c>AND</c>) clause.</summary>
    public Query<TEntity> And<TProp>(Expression<Func<TEntity, TProp>> field, QueryOp op, object? value)
    {
        _clauses.Add([new QueryComparison<TEntity>(field, op, value)]);
        return this;
    }

    /// <summary>Adds a disjunctive (<c>OR</c>) comparison to the most recent clause.</summary>
    public Query<TEntity> Or<TProp>(Expression<Func<TEntity, TProp>> field, QueryOp op, object? value)
    {
        if (_clauses.Count == 0)
            return And(field, op, value);

        _clauses[^1].Add(new QueryComparison<TEntity>(field, op, value));
        return this;
    }

    /// <summary>Orders results ascending by the selected field.</summary>
    public Query<TEntity> OrderBy<TProp>(Expression<Func<TEntity, TProp>> field)
    {
        Order = new QueryOrder<TEntity>(field, Primitives.Persistence.OrderDirection.Ascending);
        return this;
    }

    /// <summary>Orders results descending by the selected field.</summary>
    public Query<TEntity> OrderByDescending<TProp>(Expression<Func<TEntity, TProp>> field)
    {
        Order = new QueryOrder<TEntity>(field, Primitives.Persistence.OrderDirection.Descending);
        return this;
    }

    /// <summary>Marks the query as tenant-agnostic (bypass ambient tenant filtering).</summary>
    public Query<TEntity> IgnoreTenant(bool ignore = true)
    {
        TenantAgnostic = ignore;
        return this;
    }
}
