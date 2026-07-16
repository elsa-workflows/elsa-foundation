using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Persistence.Groundwork.Composition;

namespace Elsa.Foundation.Identity.Persistence.Groundwork;

/// <summary>
/// Contributes the existing identity manifest without creating a second user, role, or external
/// identity authority beside the adapter seam owned by #644.
/// </summary>
public sealed class IdentityGroundworkStorageManifestSource : IGroundworkStorageManifestSource
{
    public string FeatureIdentity => "elsa-identity";

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = LegacyGroundworkStorageManifestPhysicalizer.Physicalize(IdentityStorageManifest.Create());

        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity,
            manifest,
            [typeof(IUserStore), typeof(IRoleStore), typeof(IExternalIdentityStore), typeof(ITenantMembershipStore)],
            [],
            [],
            [
                "iam-user",
                "iam-role",
                "iam-application",
                "iam-credential",
                "iam-external-identity",
                "iam-claim-mapping",
                "iam-provider-configuration-tenant",
                "iam-provider-configuration-global",
                "iam-tenant-membership"
            ]));
    }
}
