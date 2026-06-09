using System.Linq.Expressions;
using System.Reflection;

namespace Elsa.Primitives.Extensions;

public static class PropertyAccessorExtentions
{
    /// <summary>
    /// Gets the property referenced by the expression.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <typeparam name="T">The type of the object containing the property.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <returns>The property referenced by the expression.</returns>
    public static PropertyInfo? GetProperty<T, TProperty>(this Expression<Func<T, TProperty>> expression) =>
        expression.Body is MemberExpression memberExpression
            ? memberExpression.Member as PropertyInfo
            : expression.Body is UnaryExpression unaryExpression
                ? unaryExpression.Operand is MemberExpression unaryMemberExpression
                    ? unaryMemberExpression.Member as PropertyInfo
                    : null
                : null;
}