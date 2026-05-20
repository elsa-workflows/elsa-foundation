using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Expressions.JavaScript.EventHandlers
{
    internal sealed class AddGetArgumentFunctions : IDomainEventHandler<OnEvaluatingScript>
    {
        public ValueTask Handle(OnEvaluatingScript domainEvent, CancellationToken cancellationToken)
        {
            if (domainEvent.Options is null)
                return ValueTask.CompletedTask;

            domainEvent.Options.Arguments.ToList().ForEach(arg =>
            {
                var function = new JavaScriptFunction($"get{arg.Key}", () => arg.Value);
                domainEvent.ExecutionContext.RegisterFunction(function);
            });

            return ValueTask.CompletedTask;
        }
    }
}
