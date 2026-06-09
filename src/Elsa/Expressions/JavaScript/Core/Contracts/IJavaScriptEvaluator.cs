using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    /// <summary>
    /// Evaluates JavaScript expressions.
    /// </summary>
    public interface IJavaScriptEvaluator
    {
        /// <summary>
        /// Evaluates a JavaScript expression.
        /// </summary>
        /// <param name="expression">The expression to evaluate.</param>
        /// <param name="returnType">The type of the return value.</param>
        /// <param name="context">The context in which the expression is evaluated.</param>
        /// <param name="options">The options to use when evaluating the expression.</param>
        /// <param name="cancellationToken">An optional cancellation token.</param>
        /// <returns>The result of the evaluation.</returns>
        Task<object?> EvaluateAsync(
            string expression,
            Type returnType,
            IExpressionExecutionContext context,
            IExpressionEvaluatorOptions? options = default,
            IEnumerable<IJavaScriptFunction>? additionalFunctions = null,
            CancellationToken cancellationToken = default
        );
    }
}
