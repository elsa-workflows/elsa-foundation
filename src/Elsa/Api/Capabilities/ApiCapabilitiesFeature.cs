using CShells.Features;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.FastEndpoints;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Api.Capabilities;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "ApiCapabilities",
    DisplayName = "API Capabilities",
    Description = "Aggregates stable management-client contracts exposed by the active shell.")]
public sealed class ApiCapabilitiesFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddApiCapabilities();
    }
}
