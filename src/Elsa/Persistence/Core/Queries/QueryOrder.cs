using System.Linq.Expressions;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Persistence;

namespace Elsa.Persistence.Core.Queries;

/// <summary>
/// A single-field ordering instruction within a <see cref="Query{TEntity}"/>. Only one ordering
/// is supported by design — it is all the design lanes use (latest-version resolution via
/// <c>SemVerSortKey</c>), and it keeps the contract honourable for non-relational providers.
/// </summary>
/// <typeparam name="TEntity">The entity type being queried.</typeparam>
public sealed class QueryOrder<TEntity> where TEntity : Entity
{
    internal QueryOrder(LambdaExpression fieldSelector, OrderDirection direction)
    {
        FieldSelector = fieldSelector;
        Direction = direction;
    }

    /// <summary>The field to order by, as <c>Expression&lt;Func&lt;TEntity, TProp&gt;&gt;</c>.</summary>
    public LambdaExpression FieldSelector { get; }

    /// <summary>The order direction.</summary>
    public OrderDirection Direction { get; }

    /// <summary>The simple member name the selector points at, for providers that order by name.</summary>
    public string FieldName => FieldSelector.Body is MemberExpression member
        ? member.Member.Name
        : throw new NotSupportedException("Ordering only supports direct field selectors.");
}
