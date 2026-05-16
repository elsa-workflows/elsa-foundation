using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    public interface IJavaScriptObjectProvider
    {
        ValueTask<IEnumerable<IJavaScriptObject>> GetObjects(IJavaScriptExecutionContext executionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken);
    }
}
