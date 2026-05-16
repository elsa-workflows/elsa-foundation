using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    public interface IJavaScriptExecutionContextFactory
    {
        Task<IJavaScriptExecutionContext> Create(IExpressionExecutionContext expressionExecutionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken = default);
    }
}
