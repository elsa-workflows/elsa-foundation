using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;

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
            [
                AtomicRoute(IdentityStorageManifest.IdentityUserDocumentKind, "identity-user-authority"),
                AtomicRoute(IdentityStorageManifest.IdentityRoleDocumentKind, "identity-role-authority"),
                AtomicRoute(IdentityStorageManifest.UserClaimDocumentKind, "identity-user-claim"),
                AtomicRoute(IdentityStorageManifest.RoleClaimDocumentKind, "identity-role-claim"),
                AtomicRoute(IdentityStorageManifest.ExternalLoginDocumentKind, "identity-external-login"),
                AtomicRoute(IdentityStorageManifest.UserRoleDocumentKind, "identity-user-role"),
                AtomicRoute(IdentityStorageManifest.UserTokenDocumentKind, "identity-user-token"),
                AtomicRoute(IdentityStorageManifest.IdentityTenantMembershipDocumentKind, "identity-tenant-membership"),
                AtomicRoute(IdentityStorageManifest.UserNameReservationDocumentKind, "identity-user-name-reservation"),
                AtomicRoute(IdentityStorageManifest.EmailReservationDocumentKind, "identity-email-reservation"),
                AtomicRoute(IdentityStorageManifest.RoleNameReservationDocumentKind, "identity-role-name-reservation"),
                AtomicRoute(IdentityStorageManifest.IdentityMutationReceiptDocumentKind, "identity-mutation-receipt")
            ],
            [new GroundworkStorageTopologyRequirement(RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity)],
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

    private static GroundworkStorageRouteRequirement AtomicRoute(string storageUnit, string routeIdentity) =>
        new(
            new StorageUnitIdentity(storageUnit),
            routeIdentity,
            new HashSet<CapabilityId> { WellKnownCapabilities.AtomicCommit });
}
