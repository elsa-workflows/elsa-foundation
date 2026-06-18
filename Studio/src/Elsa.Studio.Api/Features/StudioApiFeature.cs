using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Api.Extensions;
using Elsa.Studio.Api.Options;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Api.Features;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("API")]
[ManifestFeatureCategory("Modules")]
[ShellFeature(
    name: "StudioApi",
    DisplayName = "Elsa Studio API",
    Description = "Aggregates Studio module manifests and exposes the module discovery endpoint."
)]
public sealed class StudioApiFeature : IWebShellFeature
{
    public StudioApiOptions Options { get; set; } = new();

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaStudioApi(options =>
        {
            options.HostVersion = Options.HostVersion;
            options.SdkVersion = Options.SdkVersion;

            foreach (var moduleId in Options.DisabledModuleIds)
                options.DisabledModuleIds.Add(moduleId);
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
    }
}
