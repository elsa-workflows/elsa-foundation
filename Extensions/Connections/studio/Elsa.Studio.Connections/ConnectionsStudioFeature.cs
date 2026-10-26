using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Connections;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Connections")]
[StudioModule("connections", "Connections", "navigation", "routes", "http", "expression-editors", "connections")]
[ShellFeature(
    name: "ConnectionsStudio",
    DisplayName = "Connections Studio module",
    Description = "Contributes the Connections page and the connection picker expression editor to Elsa Studio."
)]
public sealed class ConnectionsStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
