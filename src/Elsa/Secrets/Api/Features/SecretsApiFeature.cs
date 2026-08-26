using Elsa.Api.AspNetCore;
using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Secrets.Api;
using Elsa.Secrets.Api.Authorization;
using Elsa.Secrets.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Secrets.Api.Features;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Secrets")]
[ManifestFeatureCategory("Security")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "SecretsApi",
    DisplayName = "Secrets API",
    Description = "Provides HTTP endpoints for managing and resolving secret metadata."
)]
public class SecretsApiFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSecrets();
        services.AddDynamicEndpointApiExplorerRefresh();
        services.AddPermissionContributor<SecretsPermissionContributor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        SecretsApi.MapSecretsApi(endpoints);
}
