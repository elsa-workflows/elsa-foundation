using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Core.Contracts;

/// <summary>
/// Post-processes the result of a JavaScript evaluation — e.g. copying engine variables back into
/// the workflow context. Runs at <c>OnScriptEvaluated</c>; the single <c>PostProcessScript</c>
/// handler iterates every registered post-processor. Implement and register via DI to act after
/// evaluation.
/// </summary>
public interface IScriptPostProcessor
{
    ValueTask PostProcess(
        IJavaScriptExecutionContext executionContext,
        IExpressionExecutionContext expressionContext,
        IExpressionEvaluatorOptions? options,
        CancellationToken cancellationToken);
}