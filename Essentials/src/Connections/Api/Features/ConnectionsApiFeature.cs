using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Connections.Api.Authorization;
using Elsa.Connections.Api.Services;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Extensions;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NativeEndpoints;

namespace Elsa.Connections.Api.Features;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Connections")]
[ManifestFeatureCategory("Security")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "ConnectionsApi",
    DisplayName = "Connections API",
    Description = "Provides HTTP endpoints for managing service connections and the OAuth callback.",
    DependsOn = new object[] { "Connections" }
)]
public class ConnectionsApiFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaEndpoints();
        services.AddConnections();
        services.AddHttpContextAccessor();
        services.AddDynamicEndpointApiExplorerRefresh();
        services.AddPermissionContributor<ConnectionsPermissionContributor>();
        services.Replace(ServiceDescriptor.Scoped<IConnectionPublishTenantAccessor, HttpContextConnectionPublishTenantAccessor>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        ConnectionsApi.MapConnectionsApi(endpoints);
}
