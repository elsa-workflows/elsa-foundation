using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Http.JavaScript.Contributors;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Http.JavaScript;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("HTTP")]
[ManifestFeatureCategory("JavaScript")]
[ShellFeature(
    name: "HttpJavaScript",
    DisplayName = "HTTP JavaScript services",
    Description = "Adds HTTP declarations to JavaScript design services."
)]
public class HttpJavaScriptFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IJavaScriptDeclarationContributor, HttpTypeDeclarationContributor>();
    }
}
