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
            // Only propagate versions the shell feature explicitly overrode. StudioApiOptions defaults
            // both to the assembly version, so a value that still equals that default means the shell
            // didn't set one — in that case leave whatever the Studio:Api configuration binding produced
            // (which itself falls back to the assembly version) rather than clobbering it.
            var assemblyVersion = StudioApiOptions.ResolveAssemblyVersion();

            if (!string.Equals(Options.HostVersion, assemblyVersion, StringComparison.Ordinal))
                options.HostVersion = Options.HostVersion;

            if (!string.Equals(Options.SdkVersion, assemblyVersion, StringComparison.Ordinal))
                options.SdkVersion = Options.SdkVersion;

            foreach (var moduleId in Options.DisabledModuleIds)
                options.DisabledModuleIds.Add(moduleId);
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
    }
}
