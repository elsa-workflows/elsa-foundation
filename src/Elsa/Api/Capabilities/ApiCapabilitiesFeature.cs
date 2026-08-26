using CShells.Features;
using CShells.AspNetCore.Features;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Api.Capabilities;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "ApiCapabilities",
    DisplayName = "API Capabilities",
    Description = "Aggregates stable management-client contracts exposed by the active shell.")]
public sealed class ApiCapabilitiesFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddApiCapabilities();
        services.AddDynamicEndpointApiExplorerRefresh();
        services.AddPermissionContributor<ApiCapabilitiesPermissionContributor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        ApiCapabilitiesApi.MapApiCapabilitiesApi(endpoints);
}
