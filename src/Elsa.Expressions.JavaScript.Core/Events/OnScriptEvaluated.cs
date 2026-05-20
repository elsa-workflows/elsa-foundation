using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Core.Events
{
    public sealed record OnScriptEvaluated(IJavaScriptExecutionContext ExecutionContext, IExpressionExecutionContext ExpressionContext, IExpressionEvaluatorOptions? Options)
        : IDomainEvent;
}
