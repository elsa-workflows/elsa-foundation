using CShells.Features;
using Elsa.Expressions.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Handlers;
using Elsa.Expressions.Options;
using Elsa.Expressions.Services;
using Elsa.Mediator.Core.Extensions;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions
{
    [ShellFeature(
        name: "Expressions",
        DisplayName = "Expressions",
        Description = "Installs and configures the required services for using expressions."
    )]
    public class ExpressionsFeature : IShellFeature
    {
        public ExpressionEvaluatorOptions EvaluatorOptions { get; set; } = new();

        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddSingleton<IExpressionDescriptorRegistry, ExpressionDescriptorRegistry>()
                .Configure<ExpressionEvaluatorOptions>(o =>
                {
                    o.Arguments = EvaluatorOptions.Arguments;
                })
                .AddSingleton<IVariableDefaultValueFormatter, VariableDefaultValueFormatter>()
                .AddSingleton<IVariableMapper, VariableMapper>()
                .AddScoped<IExpressionEvaluator, ExpressionEvaluator>()
                .AddScoped<IVariableFactory, VariableFactory>();

            // Contributes Variable<T> and FuncExpressionValue converters to the JSON payload
            // serializer via the OnJsonPayloadConvertersInitializing domain event (framework
            // §2.6.1 Registry + StartUp Task sub-pattern; Elsa §E3.3).
            services.AddDomainEventHandler<OnJsonPayloadConvertersInitializing, ExpressionsJsonConvertersHandler>();
        }
    }
}
