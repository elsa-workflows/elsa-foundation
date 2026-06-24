using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Secrets;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Secrets")]
[ShellFeature(
    name: "SecretsStudio",
    DisplayName = "Secrets Studio module",
    Description = "Contributes secret management routes and secret picker property editors to Elsa Studio."
)]
public sealed class SecretsStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSecretsStudio();
    }
}
