using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Expressions.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Options;
using Elsa.Expressions.Services;
using Elsa.Expressions.Sources;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Expressions")]
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
            .AddSingleton<IExpressionDescriptorProvider, DefaultExpressionDescriptorProvider>()
            .Configure<ExpressionEvaluatorOptions>(o =>
            {
                o.Arguments = EvaluatorOptions.Arguments;
            })
            .AddSingleton<IVariableTypeDescriptorProvider, DefaultVariableTypeDescriptorProvider>()
            .AddSingleton<IVariableTypeDescriptorCatalog, VariableTypeDescriptorCatalog>()
            .AddSingleton<IVariableDefaultValueFormatter, VariableDefaultValueFormatter>()
            .AddSingleton<IVariableMapper, VariableMapper>()
            .AddScoped<IPortableExpressionEvaluator, PortableExpressionEvaluator>()
            .AddScoped<IVariableFactory, VariableFactory>();

        // Contributes the Variable<T> converter to the JSON payload serializer by registering an
        // IJsonConverterSource; the Serialization feature's single
        // RegisterJsonConverters handler aggregates all sources (Elsa §E3.3).
        services.AddScoped<IJsonConverterSource, ExpressionsJsonConverterSource>();
    }
}
