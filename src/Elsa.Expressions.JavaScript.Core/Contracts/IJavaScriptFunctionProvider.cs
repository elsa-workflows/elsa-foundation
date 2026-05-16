using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    public interface IJavaScriptFunctionProvider
    {
        ValueTask<IEnumerable<IJavaScriptFunction>> GetFunctions(IExpressionExecutionContext expressionExecutionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken = default);
    }
}
