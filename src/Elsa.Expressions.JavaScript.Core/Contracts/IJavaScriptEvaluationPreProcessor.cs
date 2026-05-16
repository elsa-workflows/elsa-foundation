using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    public interface IJavaScriptEvaluationPreProcessor
    {
        ValueTask Process(IJavaScriptExecutionContext javascriptExecutionContext, IExpressionExecutionContext expressionExecutionContext, string expression);
    }
}
