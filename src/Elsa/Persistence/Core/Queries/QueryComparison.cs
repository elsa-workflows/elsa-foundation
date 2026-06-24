using System.Linq.Expressions;
using Elsa.Primitives.Entities;

namespace Elsa.Persistence.Core.Queries;

/// <summary>
/// A single field comparison within a <see cref="Query{TEntity}"/>: a field selector, an
/// operator, and the value to compare against. The field selector is kept as a strongly typed
/// <see cref="LambdaExpression"/> so that:
/// <list type="bullet">
/// <item>relational providers (EF Core) can translate it directly to an expression tree, and</item>
/// <item>document/key-value providers (Groundwork) can extract the member name via <see cref="FieldName"/>.</item>
/// </list>
/// </summary>
/// <typeparam name="TEntity">The entity type being queried.</typeparam>
public sealed class QueryComparison<TEntity> where TEntity : Entity
{
    internal QueryComparison(LambdaExpression fieldSelector, QueryOp op, object? value)
    {
        FieldSelector = fieldSelector;
        Operator = op;
        Value = value;
    }

    /// <summary>The field being compared, as <c>Expression&lt;Func&lt;TEntity, TProp&gt;&gt;</c>.</summary>
    public LambdaExpression FieldSelector { get; }

    /// <summary>The comparison operator.</summary>
    public QueryOp Operator { get; }

    /// <summary>The value (or set of values, for <see cref="QueryOp.In"/>) to compare against.</summary>
    public object? Value { get; }

    /// <summary>
    /// The simple member name the selector points at (e.g. <c>"Name"</c>), for providers that
    /// address fields by name rather than by expression tree. Throws if the selector is not a
    /// simple member access — the closed query model only supports direct field selectors.
    /// </summary>
    public string FieldName => ExtractMemberName(FieldSelector.Body);

    private static string ExtractMemberName(Expression body)
    {
        // Unwrap the Convert that boxing value types into object introduces.
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            body = unary.Operand;

        if (body is MemberExpression member)
            return member.Member.Name;

        throw new NotSupportedException(
            $"The closed query model only supports direct field selectors; got '{body}'.");
    }
}
