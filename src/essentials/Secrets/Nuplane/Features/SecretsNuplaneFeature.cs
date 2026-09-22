using CShells.Features;
using Elsa.Secrets.Nuplane.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Secrets.Nuplane.Features;

/// <summary>
/// Composes the <c>elsa</c> secret-reference provider, so a package feed configured with
/// <c>Credentials: secrets://elsa/&lt;name&gt;</c> is authenticated with a value out of the Secrets module.
/// </summary>
/// <remarks>
/// Depends on <c>Secrets</c> because the provider reads through that feature's
/// <c>ISecretValueResolver</c>: without it the composition has a provider claiming <c>elsa</c> that cannot
/// perform a lookup at all, which is worse than having none. Whether the provider is consulted still depends
/// on <em>where</em> Nuplane was composed — it reads its providers from the container that called
/// <c>AddNuplane</c> — so a host that composes Nuplane on its own container registers the provider there
/// with <c>AddSecretsFeedCredentials</c> instead of, or as well as, enabling this feature on a shell. See
/// <c>docs/foundation-host-feeds.md</c>, "Feed credentials".
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Secrets")]
[ManifestFeatureCategory("Modularity")]
[ShellFeature(
    name: "SecretsNuplane",
    DisplayName = "Secrets Package Feed Credentials",
    Description = "Resolves a Nuplane package feed's secrets://elsa/<name> credential reference through the Secrets module.",
    DependsOn = new object[] { "Secrets" }
)]
public class SecretsNuplaneFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Tenant id",
        Description = "The tenant feed credentials are read under. A reconcile runs with no request and no principal, so no tenant can be inherited from a caller and one has to be named; defaults to the same 'default' tenant the identity seeder writes under.",
        Category = "Secrets")]
    public string TenantId { get; set; } = SecretsFeedCredentialProvider.DefaultTenantId;

    public void ConfigureServices(IServiceCollection services) => services.AddSecretsFeedCredentials(TenantId);
}
