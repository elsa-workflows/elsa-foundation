using CShells.Features;
using Elsa.Foundation.Identity.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Persistence.Groundwork;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "IdentityGroundworkPersistence",
    DisplayName = "Identity Groundwork Persistence",
    Description = "Replaces the default in-memory identity stores with Groundwork-backed, durable stores so users, roles, external identities, and tenant memberships survive restarts.",
    Metadata = [GroundworkSchemaFeatureMetadata.Key, GroundworkSchemaFeatureMetadata.Identity]
)]
public class IdentityGroundworkPersistenceFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddGroundworkIdentityStores();
}
