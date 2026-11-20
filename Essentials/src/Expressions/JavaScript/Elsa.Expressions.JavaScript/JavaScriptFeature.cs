using CShells.Features;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CShells.Lifecycle;

namespace Elsa.Expressions.JavaScript;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Expressions")]
[ManifestFeatureCategory("JavaScript")]
[ShellFeature(
    name: "JavaScriptExpressions",
    DisplayName = "JavaScript Expressions",
    Description = "Provides portable JavaScript expression evaluation")]
public class JavaScriptFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddScoped<IPortableExpressionHandler, PortableJavaScriptExpressionHandler>()
            .AddScoped<IExpressionDescriptorProvider, JavaScriptExpressionDescriptorProvider>()
            .AddSingleton<IExpressionToolingProvider, JavaScriptExpressionToolingProvider>()
            .AddSingleton<IJavaScriptScriptOutcomeAnalyzer, JavaScriptScriptOutcomeAnalyzer>()
            .AddSingleton<IScriptVariableReadAnalyzer, JavaScriptVariableReadAnalyzer>();
        // The default JSON-only strategy's .NET-usage analysis, shared by publish and the Elsa 3 converter (spec 1223
        // FR-030); replaceable with the strategy, and only one may be composed. A singleton like its sibling analyzers:
        // it holds no state and no scoped dependency (the registered function declarations are singletons) and only
        // parses.
        services.TryAddSingleton<IJavaScriptDotNetUsageAnalyzer, JavaScriptDotNetUsageAnalyzer>();
        services.AddSingleton<IShellInitializer, JavaScriptDotNetUsageAnalyzerCheck>();
    }
}
