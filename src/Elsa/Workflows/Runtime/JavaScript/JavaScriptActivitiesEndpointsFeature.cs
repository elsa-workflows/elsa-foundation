using CShells.Features;
using CShells.AspNetCore.Features;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Workflows.Runtime.JavaScript;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "JavaScriptEndpoints",
    DisplayName = "JavaScript endpoints",
    Description = "Exposes runtime API endpoints for JavaScript workflow activities.",
    DependsOn = new object[] { "JavaScriptJintEngine" }
)]
public sealed class JavaScriptActivitiesEndpointsFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddPermissionContributor<JavaScriptExecutionPermissionContributor>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        JavaScriptExecutionApi.MapJavaScriptExecutionApi(endpoints);
}
