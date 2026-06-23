using System.Collections;
using System.Linq.Expressions;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Persistence;

namespace Elsa.Persistence.Core.Queries;

/// <summary>
/// Evaluates a closed, provider-neutral <see cref="Query{TEntity}"/> against an in-memory sequence of
/// already-materialized entities. It is the canonical <b>fallback</b> a non-relational provider adapter
/// (e.g. Groundwork) uses for any operator it cannot yet push down to the store natively: fetch a
/// candidate set via the store's declared indexes, then apply the remaining predicate/order here.
/// <para>
/// Its semantics are deliberately aligned with <c>EFCoreQueryTranslator</c>: <see cref="QueryOp.Equal"/>
/// is value equality (a null value matches a null field; strings compare ordinally in the fallback);
/// <see cref="QueryOp.In"/> is set membership; <see cref="QueryOp.Contains"/> is a
/// <b>case-insensitive, null-field-safe</b> substring; clauses combine with <c>AND</c> and the
/// comparisons inside a clause combine with <c>OR</c>; ordering is a single field, ascending or
/// descending. EF providers may still apply database collation to equality; provider selection should
/// configure collation explicitly when case-insensitive equality is required.
/// </para>
/// </summary>
public static class InMemoryQueryEvaluator
{
    /// <summary>Applies <paramref name="query"/>'s predicates and ordering to <paramref name="source"/>.</summary>
    public static IEnumerable<TEntity> Apply<TEntity>(IEnumerable<TEntity> source, Query<TEntity> query)
        where TEntity : Entity
    {
        var filtered = source.Where(entity => Matches(entity, query));

        if (query.Order == null)
            return filtered;

        return ApplyOrder(filtered, query.Order);
    }

    private static bool Matches<TEntity>(TEntity entity, Query<TEntity> query) where TEntity : Entity
    {
        // AND across clauses; OR within a clause. Zero clauses ⇒ match all.
        foreach (var clause in query.Clauses)
        {
            var clauseMatched = clause.Count == 0;
            foreach (var comparison in clause)
            {
                if (!EvaluateComparison(entity, comparison))
                    continue;

                clauseMatched = true;
                break;
            }

            if (!clauseMatched)
                return false;
        }

        return true;
    }

    private static bool EvaluateComparison<TEntity>(TEntity entity, QueryComparison<TEntity> comparison)
        where TEntity : Entity
    {
        var fieldValue = GetFieldValue(entity, comparison.FieldSelector);

        switch (comparison.Operator)
        {
            case QueryOp.Equal:
                return ValuesEqual(fieldValue, comparison.Value);

            case QueryOp.In:
                if (comparison.Value is not IEnumerable set || comparison.Value is string)
                    return false;
                foreach (var candidate in set)
                {
                    if (ValuesEqual(candidate, fieldValue))
                        return true;
                }
                return false;

            case QueryOp.Contains:
                // Null field yields no match (never throws), mirroring the EF translator's null guard
                // and LIKE-on-NULL semantics.
                if (fieldValue is not string text || comparison.Value is not string substring)
                    return false;
                return text.Contains(substring, StringComparison.CurrentCultureIgnoreCase);

            default:
                throw new NotSupportedException($"Unsupported query operator '{comparison.Operator}'.");
        }
    }

    private static IEnumerable<TEntity> ApplyOrder<TEntity>(IEnumerable<TEntity> source, QueryOrder<TEntity> order)
        where TEntity : Entity
    {
        var selector = order.FieldSelector;
        Func<TEntity, object?> key = entity => GetFieldValue(entity, selector);

        return order.Direction == OrderDirection.Descending
            ? source.OrderByDescending(key, NullSafeComparer.Instance)
            : source.OrderBy(key, NullSafeComparer.Instance);
    }

    private static object? GetFieldValue<TEntity>(TEntity entity, LambdaExpression selector)
        => CompiledSelectorCache<TEntity>.Get(selector)(entity);

    private static bool ValuesEqual(object? left, object? right) =>
        left is string leftText && right is string rightText
            ? string.Equals(leftText, rightText, StringComparison.Ordinal)
            : Equals(left, right);

    /// <summary>
    /// Caches compiled field selectors per entity type + member so evaluation does not recompile the
    /// expression for every entity. Keyed by member name, which the closed query model guarantees is a
    /// simple member access (<see cref="QueryComparison{TEntity}.FieldName"/>).
    /// </summary>
    private static class CompiledSelectorCache<TEntity>
    {
        private static readonly Dictionary<string, Func<TEntity, object?>> Cache = new();
        private static readonly Lock Sync = new();

        public static Func<TEntity, object?> Get(LambdaExpression selector)
        {
            var key = MemberName(selector.Body);
            lock (Sync)
            {
                if (Cache.TryGetValue(key, out var cached))
                    return cached;

                var parameter = selector.Parameters[0];
                var boxed = Expression.Convert(selector.Body, typeof(object));
                var compiled = Expression.Lambda<Func<TEntity, object?>>(boxed, parameter).Compile();
                Cache[key] = compiled;
                return compiled;
            }
        }

        private static string MemberName(Expression body)
        {
            if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
                body = unary.Operand;

            return body is MemberExpression member
                ? member.Member.Name
                : body.ToString();
        }
    }

    private sealed class NullSafeComparer : IComparer<object?>
    {
        public static readonly NullSafeComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return Comparer<object>.Default.Compare(x, y);
        }
    }
}
