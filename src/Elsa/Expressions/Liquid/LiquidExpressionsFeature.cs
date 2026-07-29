using CShells.Features;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Liquid.Services;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Fluid;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.Liquid;

/// <summary>
/// Registers the binding-pure Liquid evaluator and its authoring descriptor.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Expressions")]
[ManifestFeatureCategory("Liquid")]
[ShellFeature(
    name: "Liquid",
    DisplayName = "Liquid Expressions",
    Description = "Provides portable Liquid expression evaluation")]
public class LiquidExpressionsFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddScoped<IPortableExpressionHandler, PortableLiquidExpressionHandler>()
            .AddScoped<FluidParser>()
            .AddSingleton<IExpressionDescriptorProvider, LiquidExpressionDescriptorProvider>()
            .AddSingleton<IExpressionToolingProvider, LiquidExpressionToolingProvider>();
    }
}
