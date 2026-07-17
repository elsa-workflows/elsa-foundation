using CShells.Features;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Services;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

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
            .AddScoped<IExpressionDescriptorProvider, JavaScriptExpressionDescriptorProvider>();
    }
}
