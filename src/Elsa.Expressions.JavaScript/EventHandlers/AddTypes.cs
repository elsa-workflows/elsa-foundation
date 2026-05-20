using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Core.Options;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Options;

namespace Elsa.Expressions.JavaScript.EventHandlers
{
    public sealed class AddTypes(IOptions<JavaScriptOptions> options) : IDomainEventHandler<OnEvaluatingScript>
    {
        public ValueTask Handle(OnEvaluatingScript domainEvent, CancellationToken cancellationToken)
        {
            options.Value.TypeDescriptors
                .ToList()
                .ForEach(d => domainEvent.ExecutionContext.RegisterType(d.GetDescriptorType()));

            return ValueTask.CompletedTask;
        }
    }
}
