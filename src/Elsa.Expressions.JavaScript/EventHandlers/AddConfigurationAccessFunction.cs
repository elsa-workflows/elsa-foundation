using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Options;
using Elsa.Expressions.JavaScript.Primitives.Constants;
using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Elsa.Expressions.JavaScript.EventHandlers
{
    public sealed class AddConfigurationAccessFunction(IOptions<ConfigurationAccessFunctionProviderOptions> featureOptions, IConfiguration configuration)
        : IDomainEventHandler<OnEvaluatingScript>
    {
        public ValueTask Handle(OnEvaluatingScript domainEvent, CancellationToken cancellationToken)
        {
            if (!featureOptions.Value.AllowConfigurationAccess)
            {
                return ValueTask.CompletedTask;
            }

            var function = new JavaScriptFunction<string>(
                FunctionNames.GetConfiguration,
                (name) =>
                {
                    if (featureOptions.Value.DisallowedSections?.Contains(name) == true)
                    {
                        throw new ArgumentException($"Configuration section '{name}' is restricted from access");
                    }

                    return configuration.GetSection(name).Value;
                }
            );

            domainEvent.ExecutionContext.RegisterFunction(function);

            return ValueTask.CompletedTask;
        }
    }
}
