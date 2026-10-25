using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Integrations.Connectors.OAuth2;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Integrations")]
[ManifestFeatureCategory("Connections")]
[ShellFeature(
    name: "IntegrationsOAuth2",
    DisplayName = "OAuth2 connector",
    Description = "Adds the oauth2 authentication scheme to the foundation http connection.",
    DependsOn = new object[] { "Connections" }
)]
public sealed class IntegrationsOAuth2Feature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddOAuth2Connector();
}
