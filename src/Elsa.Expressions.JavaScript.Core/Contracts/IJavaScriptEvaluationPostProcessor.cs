using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    public interface IJavaScriptEvaluationPostProcessor
    {
        ValueTask Process(IJavaScriptExecutionContext javascriptExecutionContext, IExpressionExecutionContext expressionExecutionContext, string Expression, object? Result);
    }
}
