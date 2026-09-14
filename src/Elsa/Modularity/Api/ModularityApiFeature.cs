using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Modularity.Api.Endpoints;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Modularity.Api.Extensions;
using Elsa.Modularity.Api.Options;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NativeEndpoints;

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

    /// <summary>Key for the feature catalog revision; see <see cref="FeatureManagementOptions.RevisionKey"/>.</summary>
    [ManifestSetting(
        DisplayName = "Revision key",
        Description = "Random value of at least 32 bytes that keys the feature catalog revision. Share it across every instance that applies against the same shells.json. When omitted, each process generates its own, so a revision read before a restart or from another instance is rejected as a conflict.",
        Secret = true)]
    public string? RevisionKey { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaEndpoints();
        services.AddModularityApi(options =>
        {
            options.ShellsJsonPath = ShellsJsonPath;
            options.RevisionKey = RevisionKey;
        });
        services.AddDynamicEndpointApiExplorerRefresh();
        // The owner's failure services are keyed so hosts composing several modules keep each
        // module's own error shapes; the endpoint pipeline falls back to unkeyed registrations.
        services.TryAddKeyedSingleton<IEndpointProblemWriter, ModularityProblemWriter>(ModularityApi.OwnerId);
        services.TryAddKeyedSingleton<IEndpointFaultRenderer, ModularityFaultRenderer>(ModularityApi.OwnerId);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        ModularityApi.MapModularityApi(endpoints);
}
