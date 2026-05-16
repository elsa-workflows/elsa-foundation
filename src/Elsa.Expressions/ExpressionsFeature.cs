using CShells.Features;
using Elsa.Expressions.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JsonConverters;
using Elsa.Expressions.Options;
using Elsa.Expressions.Services;
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
                .AddSingleton<IVariableFormatter, VariableFormatter>()
                .AddSingleton<IVariableMapper, VariableMapper>()
                .AddSingleton<IPayloadSerializerConverterProvider>(GetVariableConverterProvider)
                .AddSingleton<IPayloadSerializerConverterProvider>(GetFuncExpressionValueConverterProvider)

                .AddScoped<IExpressionEvaluator, ExpressionEvaluator>()
                .AddScoped<IVariableFactory, VariableFactory>()
                ;
        }

        static DefaultPayloadSerializerConverterProvider GetVariableConverterProvider(IServiceProvider sp)
        {
            return new DefaultPayloadSerializerConverterProvider(
                () => new VariableConverterFactory(sp.GetRequiredService<IVariableMapper>()));
        }

        static DefaultPayloadSerializerConverterProvider GetFuncExpressionValueConverterProvider(IServiceProvider sp)
        {
            return new DefaultPayloadSerializerConverterProvider(() => new FuncExpressionValueConverter());
        }
    }
}
