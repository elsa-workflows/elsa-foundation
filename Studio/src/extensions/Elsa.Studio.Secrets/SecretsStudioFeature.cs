using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Secrets;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Secrets")]
[StudioModule("secrets", "Secrets", "1.0.0", "navigation", "routes", "http", "expression-editors", "secrets")]
[ShellFeature(
    name: "SecretsStudio",
    DisplayName = "Secrets Studio module",
    Description = "Contributes secret management routes and secret picker property editors to Elsa Studio."
)]
public sealed class SecretsStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
