using System.Linq.Expressions;
using System.Reflection;
using Elsa.Persistence.Core.Queries;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Persistence;

namespace Elsa.Persistence.EFCore.Queries;

/// <summary>
/// Translates a provider-neutral <see cref="Query{TEntity}"/> into LINQ over an
/// <see cref="IQueryable{T}"/> so EF Core can execute it server-side. The emitted expression shapes
/// (equality, <c>IN</c> via <see cref="Enumerable.Contains{TSource}(IEnumerable{TSource}, TSource)"/>,
/// and case-insensitive substring search via <c>field.ToLower().Contains(value)</c>) are all shapes
/// EF Core can translate to SQL — the latter becomes <c>lower(field) LIKE '%value%'</c> — while still
/// evaluating correctly in memory for non-relational fallbacks.
/// </summary>
public static class EFCoreQueryTranslator
{
    private static readonly NullabilityInfoContext NullabilityContext = new();
    private static readonly System.Reflection.MethodInfo ContainsStringMethod =
        typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
    private static readonly System.Reflection.MethodInfo ToLowerMethod =
        typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;

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
                    // Case-insensitive substring match expressed as field.ToLower().Contains(value.ToLower()).
                    // EF Core translates this to lower(field) LIKE '%value%', and it also evaluates correctly
                    // in memory (e.g. a non-relational provider's fallback). The two-argument
                    // string.Contains(string, StringComparison) overload is intentionally avoided because no
                    // EF Core provider can translate it to SQL.
                    var loweredValue = ((string)comparison.Value!).ToLower();
                    var call = Expression.Call(
                        Expression.Call(field, ToLowerMethod),
                        ContainsStringMethod,
                        Expression.Constant(loweredValue, typeof(string)));

                    // Null-guard so a null field yields no match instead of throwing when evaluated in
                    // memory, matching EF's effective LIKE-on-NULL semantics. AndAlso short-circuits before
                    // ToLower() is invoked on the null field.
                    return RequiresNullGuard(field)
                        ? Expression.AndAlso(Expression.NotEqual(field, Expression.Constant(null, field.Type)), call)
                        : call;
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

    private static bool RequiresNullGuard(Expression field)
    {
        var memberExpression = UnwrapMemberExpression(field);
        return memberExpression?.Member switch
        {
            PropertyInfo property => NullabilityContext.Create(property).ReadState != NullabilityState.NotNull,
            FieldInfo fieldInfo => NullabilityContext.Create(fieldInfo).ReadState != NullabilityState.NotNull,
            _ => true
        };
    }

    private static MemberExpression? UnwrapMemberExpression(Expression expression)
    {
        if (expression is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            expression = unary.Operand;

        return expression as MemberExpression;
    }

    private sealed class ParameterReplacer(ParameterExpression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => replacement;
    }
}
