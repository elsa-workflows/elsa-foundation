using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Expressions.JavaScript.Core.Options;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Http.JavaScript.Constants;
using Elsa.Http.JavaScript.Contributors;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Http.JavaScript;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("HTTP")]
[ManifestFeatureCategory("JavaScript")]
[ShellFeature(
    name: "HttpJavaScript",
    DisplayName = "HTTP JavaScript services",
    Description = "Adds HTTP type descriptors and declarations to JavaScript expression services."
)]
public class HttpJavaScriptFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.Configure<JavaScriptOptions>(o =>
        {
            HttpTypeDescriptors.GetDescriptors().ToList().ForEach(o.TypeDescriptors.Add);
        });

        services.AddScoped<IJavaScriptDeclarationContributor, HttpTypeDeclarationContributor>();
    }
}
