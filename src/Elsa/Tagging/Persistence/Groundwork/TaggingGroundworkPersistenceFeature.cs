using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Tagging.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Tagging.Persistence.Groundwork;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Tagging")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "TaggingGroundworkPersistence",
    DisplayName = "Tagging Groundwork Persistence",
    Description = "Persists the tag definition catalog and its append-only audit records in Groundwork.")]
public class TaggingGroundworkPersistenceFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddGroundworkTaggingStore();
}
