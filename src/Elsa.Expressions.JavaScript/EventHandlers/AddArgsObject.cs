using Elsa.Expressions.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Extensions;
using System.Dynamic;

namespace Elsa.Expressions.JavaScript.EventHandlers
{
    public sealed class AddArgsObject : IDomainEventHandler<Core.Events.OnEvaluatingScript>
    {
        private const string ArgsObjectName = "args";

        public async ValueTask Handle(Core.Events.OnEvaluatingScript domainEvent, CancellationToken cancellationToken)
        {
            // Add args object
            domainEvent.ExecutionContext.SetValue(ArgsObjectName, GetArgsFromOptions(domainEvent.Options));
        }

        private static IDictionary<string, object?> GetArgsFromOptions(IExpressionEvaluatorOptions? options)
        {
            var args = new ExpandoObject() as IDictionary<string, object?>;

            if (options is null)
            {
                return args;
            }

            foreach (var (name, value) in options.Arguments)
            {
                args[name.Camelize()] = value;
            }

            return args;
        }
    }
}
