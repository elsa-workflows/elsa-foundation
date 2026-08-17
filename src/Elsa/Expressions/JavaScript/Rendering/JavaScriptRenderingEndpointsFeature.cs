using CShells.Features;
using CShells.AspNetCore.Features;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Expressions.JavaScript.Rendering;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Expressions")]
[ManifestFeatureCategory("JavaScript")]
[ManifestFeatureCategory("API")]
[ShellFeature(
      name: "JavaScriptRenderingEndpoints",
      DisplayName = "JavaScript rendering endpoints",
      Description = "Exposes JavaScript rendering endpoints for design-time declaration document generation."
   )]
public sealed class JavaScriptRenderingEndpointsFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddPermissionContributor<JavaScriptRenderingPermissionContributor>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        JavaScriptRenderingApi.MapJavaScriptRenderingApi(endpoints);
}
