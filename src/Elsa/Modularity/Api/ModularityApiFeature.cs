using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Modularity.Api.Extensions;
using Elsa.Modularity.Api.Options;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Modularity.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Modularity")]
[ManifestFeatureCategory("Management")]
[ShellFeature(
    name: "ModularityApi",
    DisplayName = "Modularity API",
    Description = "Shell-scoped feature catalog and feature-configuration management endpoints."
)]
public class ModularityApiFeature : FastEndpointsFeatureBase
{
    public string ShellsJsonPath { get; set; } = "shells.json";

    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        services.AddModularityApi(options =>
        {
            options.ShellsJsonPath = ShellsJsonPath;
        });
    }
}
