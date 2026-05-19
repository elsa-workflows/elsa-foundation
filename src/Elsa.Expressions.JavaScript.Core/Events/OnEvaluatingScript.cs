using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Mediator.Core;

namespace Elsa.Expressions.JavaScript.Core.Events
{
    public sealed record OnEvaluatingScript(string Script, IJavaScriptEvaluationContext EvaluationContext, IExpressionExecutionContext ExpressionContext, IExpressionEvaluatorOptions? Options)
        : IDomainEvent;
}
