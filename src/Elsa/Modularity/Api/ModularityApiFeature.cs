using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Modularity.Api.Extensions;
using Elsa.Modularity.Api.Options;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Modularity.Api.Endpoints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Elsa.Modularity.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Modularity")]
[ManifestFeatureCategory("Management")]
[ShellFeature(
    name: "ModularityApi",
    DisplayName = "Modularity API",
    Description = "Shell-scoped feature catalog and feature-configuration management endpoints."
)]
public class ModularityApiFeature : IWebShellFeature
{
    public string ShellsJsonPath { get; set; } = "shells.json";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddModularityApi(options =>
        {
            options.ShellsJsonPath = ShellsJsonPath;
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        ModularityApi.MapModularityApi(endpoints);
}
