using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Core.Contracts;

/// <summary>
/// Pre-processes the JavaScript execution context before a script runs — registering functions,
/// types, or values. Runs at <c>OnEvaluatingScript</c>; the single <c>PreProcessScript</c> handler
/// iterates every registered pre-processor. Implement and register via DI to extend the JavaScript
/// runtime surface before evaluation.
/// </summary>
public interface IScriptPreProcessor
{
    ValueTask PreProcess(
        string script,
        IJavaScriptExecutionContext executionContext,
        IExpressionExecutionContext expressionContext,
        IExpressionEvaluatorOptions? options,
        CancellationToken cancellationToken);
}