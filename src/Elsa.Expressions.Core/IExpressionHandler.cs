using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Elsa.Expressions.Core
{
    /// <summary>
    /// Evaluates an expression.
    /// </summary>
    public interface IExpressionHandler
    {
        /// <summary>
        /// Evaluates an expression.
        /// </summary>
        /// <param name="expression">The expression to evaluate.</param>
        /// <param name="returnType">The expected return type.</param>
        /// <param name="context">The context in which the expression is evaluated.</param>
        /// <param name="options">An optional set of options.</param>
        /// <returns>The result of the evaluation.</returns>
        ValueTask<object?> EvaluateAsync(IExpression expression, Type returnType, IExpressionExecutionContext context, IExpressionEvaluatorOptions options);
    }
}
