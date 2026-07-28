using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Preferences.Persistence.Groundwork;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "StudioPreferencesGroundworkPersistence",
    DisplayName = "Studio Preferences Groundwork Persistence",
    Description = "Replaces the in-memory Studio preference store with Groundwork persistence.",
    DependsOn = new object[] { "StudioPreferences" })]
public sealed class StudioPreferencesGroundworkPersistenceFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddGroundworkStudioPreferences();
}
