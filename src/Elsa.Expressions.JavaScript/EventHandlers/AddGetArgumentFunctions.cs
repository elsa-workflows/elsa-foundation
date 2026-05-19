using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Mediator.Core;

namespace Elsa.Expressions.JavaScript.Jint.EventHandlers
{
    internal sealed class AddGetArgumentFunctions : IDomainEventHandler<OnEvaluatingScript>
    {
        public ValueTask Handle(OnEvaluatingScript domainEvent, CancellationToken cancellationToken)
        {
            if (domainEvent.Options is null)
                return ValueTask.CompletedTask;

            domainEvent.Options.Arguments.ToList().ForEach(arg =>
            {
                var function = new JavaScriptFunction($"get{arg.Key}", (_) => arg.Value);
                domainEvent.EvaluationContext.AddFunction(function);
            });

            return ValueTask.CompletedTask;
        }
    }
}
