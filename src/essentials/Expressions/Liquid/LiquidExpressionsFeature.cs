using CShells.Features;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Liquid.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Fluid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
            .AddSingleton<IExpressionDescriptorProvider, LiquidExpressionDescriptorProvider>()
            .TryAddSingleton(_ => LiquidExpressionProfile.Default);

        // The parser stays scoped so existing host overrides are honored, while each default scope
        // receives a fresh parser. The handler applies the same immutable profile to either parser.
        services.TryAddScoped<FluidParser>(_ => new FluidParser(new FluidParserOptions { AllowFunctions = false }));
        services.AddScoped<IPortableExpressionHandler, PortableLiquidExpressionHandler>();
        services.AddSingleton<IExpressionToolingProvider, LiquidExpressionToolingProvider>();
    }
}
