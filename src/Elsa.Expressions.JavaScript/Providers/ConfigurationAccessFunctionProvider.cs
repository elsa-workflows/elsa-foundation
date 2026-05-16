using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Constants;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Elsa.Expressions.JavaScript.Providers
{
    public record ConfigurationAccessFunctionProviderOptions(bool AllowConfigurationAccess, IEnumerable<string> DisallowedSections);

    internal sealed class ConfigurationAccessFunctionProvider(IOptions<ConfigurationAccessFunctionProviderOptions> featureOptions, IConfiguration configuration)
        : IJavaScriptFunctionProvider,
        IJavaScriptFunctionDeclarationProvider
    {
        public const string GetConfigurationFunctionName = "getConfig";

        public ValueTask<IEnumerable<JavaScriptFunctionDeclaration>> GetDeclarations(CancellationToken cancellationToken = default)
        {
            var result = new JavaScriptFunctionDeclaration(
                    GetConfigurationFunctionName,
                    returnType: WellKnownTypeNames.String,
                    parameter: new JavaScriptParameterDeclaration("name", WellKnownTypeNames.String)
                );

            return new([result]);
        }

        public ValueTask<IEnumerable<IJavaScriptFunction>> GetFunctions(IExpressionExecutionContext expressionExecutionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken = default)
        {
            if (!featureOptions.Value.AllowConfigurationAccess)
            {
                return new([]);
            }

            var result = new IJavaScriptFunction[]
            {
                new JavaScriptFunction<string>(
                    GetConfigurationFunctionName,
                    (name) =>
                    {
                        if(featureOptions.Value.DisallowedSections?.Contains(name) == true)
                        {
                            throw new ArgumentException($"Configuration section '{name}' is restricted from access");
                        }

                        return configuration.GetSection(name).Value;
                    }
                )
            };

            return new(result);
        }
    }
}
