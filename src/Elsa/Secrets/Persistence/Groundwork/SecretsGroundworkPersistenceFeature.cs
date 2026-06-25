using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Secrets.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Secrets.Persistence.Groundwork;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Secrets")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "SecretsGroundworkPersistence",
    DisplayName = "Secrets Groundwork Persistence",
    Description = "Replaces the default in-memory secrets repository with a Groundwork-backed repository."
)]
public class SecretsGroundworkPersistenceFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddGroundworkSecretsStore();
}
