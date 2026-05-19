using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Mediator.Core;

namespace Elsa.Expressions.JavaScript.Core.Events
{
    public sealed record OnScriptEvaluated(IJavaScriptEvaluationContext EvaluationContext, IExpressionExecutionContext ExpressionContext, IExpressionEvaluatorOptions? Options)
        : IDomainEvent;
}
