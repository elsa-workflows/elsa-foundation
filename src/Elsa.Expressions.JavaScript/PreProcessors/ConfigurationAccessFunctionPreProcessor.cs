using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Options;
using Elsa.Expressions.JavaScript.Primitives.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Elsa.Expressions.JavaScript.PreProcessors
{
    public sealed class ConfigurationAccessFunctionPreProcessor(IOptions<ConfigurationAccessFunctionProviderOptions> featureOptions, IConfiguration configuration)
        : IScriptPreProcessor
    {
        public ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
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

            executionContext.RegisterFunction(function);

            return ValueTask.CompletedTask;
        }
    }
}
