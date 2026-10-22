using CShells.Features;
using Elsa.Connections.Extensions;
using Elsa.Connections.Options;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Connections.Features;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Connections")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "Connections",
    DisplayName = "Connections",
    Description = "Provides service connections: types, repository, manager, resolver, and the built-in http connection type.",
    DependsOn = new object[] { "Secrets" }
)]
public class ConnectionsFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Public base URL",
        Description = "Public origin used to build the OAuth callback URL at /_elsa/connections/oauth/callback.",
        Category = "OAuth")]
    public string? PublicBaseUrl { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddConnections();
        services.Configure<ConnectionsOptions>(options =>
        {
            if (!string.IsNullOrWhiteSpace(PublicBaseUrl))
                options.PublicBaseUrl = PublicBaseUrl;
        });
    }
}
