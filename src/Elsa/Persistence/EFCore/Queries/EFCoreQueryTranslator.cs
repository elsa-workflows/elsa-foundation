using System.Linq.Expressions;
using Elsa.Persistence.Core.Queries;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Persistence;

namespace Elsa.Persistence.EFCore.Queries;

/// <summary>
/// Translates a provider-neutral <see cref="Query{TEntity}"/> into LINQ over an
/// <see cref="IQueryable{T}"/> so EF Core can execute it server-side. The emitted expression shapes
/// (equality, <c>IN</c> via <see cref="Enumerable.Contains{TSource}(IEnumerable{TSource}, TSource)"/>,
/// and the two-argument <see cref="string.Contains(string, StringComparison)"/> for case-insensitive
/// substring search) are exactly the shapes the legacy <c>IFilter&lt;T&gt;.Apply</c> implementations
/// already used, so EF translation behaviour is unchanged.
/// </summary>
public static class EFCoreQueryTranslator
{
    private static readonly System.Reflection.MethodInfo ContainsStringMethod =
        typeof(string).GetMethod(nameof(string.Contains), [typeof(string), typeof(StringComparison)])!;

    /// <summary>
    /// Applies <paramref name="query"/>'s predicates and ordering to <paramref name="source"/>.
    /// Tenant-agnostic handling is the adapter's concern (EF's <c>IgnoreQueryFilters</c>), not the
    /// translator's, so this method stays pure LINQ.
    /// </summary>
    public static IQueryable<TEntity> Apply<TEntity>(IQueryable<TEntity> source, Query<TEntity> query)
        where TEntity : Entity
    {
        foreach (var clause in query.Clauses)
        {
            var predicate = BuildClause(clause);
            if (predicate != null)
                source = source.Where(predicate);
        }

        if (query.Order != null)
            source = ApplyOrder(source, query.Order);

        return source;
    }

    private static Expression<Func<TEntity, bool>>? BuildClause<TEntity>(IReadOnlyList<QueryComparison<TEntity>> clause)
        where TEntity : Entity
    {
        if (clause.Count == 0)
            return null;

        var parameter = Expression.Parameter(typeof(TEntity), "x");
        Expression? body = null;

        foreach (var comparison in clause)
        {
            var comparisonBody = BuildComparison(comparison, parameter);
            body = body == null ? comparisonBody : Expression.OrElse(body, comparisonBody);
        }

        return Expression.Lambda<Func<TEntity, bool>>(body!, parameter);
    }

    private static Expression BuildComparison<TEntity>(QueryComparison<TEntity> comparison, ParameterExpression parameter)
        where TEntity : Entity
    {
        var field = new ParameterReplacer(parameter).Visit(comparison.FieldSelector.Body);

        switch (comparison.Operator)
        {
            case QueryOp.Equal:
                return Expression.Equal(field, Expression.Constant(comparison.Value, field.Type));

            case QueryOp.In:
                {
                    var elementType = field.Type;
                    var enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
                    var containsMethod = typeof(Enumerable).GetMethods()
                        .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
                        .MakeGenericMethod(elementType);
                    return Expression.Call(containsMethod, Expression.Constant(comparison.Value, enumerableType), field);
                }

            case QueryOp.Contains:
                {
                    // Null-guard so the same shape is safe whether executed as SQL (EF) or in memory
                    // (e.g. a non-relational provider's fallback): a null field yields no match
                    // instead of throwing, matching EF's effective LIKE-on-NULL semantics.
                    var call = Expression.Call(
                        field,
                        ContainsStringMethod,
                        Expression.Constant(comparison.Value, typeof(string)),
                        Expression.Constant(StringComparison.CurrentCultureIgnoreCase));
                    return Expression.AndAlso(Expression.NotEqual(field, Expression.Constant(null, field.Type)), call);
                }

            default:
                throw new NotSupportedException($"Unsupported query operator '{comparison.Operator}'.");
        }
    }

    private static IQueryable<TEntity> ApplyOrder<TEntity>(IQueryable<TEntity> source, QueryOrder<TEntity> order)
        where TEntity : Entity
    {
        var keyType = order.FieldSelector.ReturnType;
        var methodName = order.Direction == OrderDirection.Descending
            ? nameof(Queryable.OrderByDescending)
            : nameof(Queryable.OrderBy);

        var method = typeof(Queryable).GetMethods()
            .First(m => m.Name == methodName && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TEntity), keyType);

        return (IQueryable<TEntity>)method.Invoke(null, [source, order.FieldSelector])!;
    }

    private sealed class ParameterReplacer(ParameterExpression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => replacement;
    }
}
