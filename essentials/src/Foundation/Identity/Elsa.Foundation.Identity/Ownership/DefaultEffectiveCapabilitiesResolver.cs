using Elsa.Foundation.Identity.Core.Ownership;

namespace Elsa.Foundation.Identity.Ownership;

public sealed class DefaultEffectiveCapabilitiesResolver : IEffectiveCapabilitiesResolver
{
    public EffectiveProviderCapabilities Resolve(OwnershipConfiguration ownership, ProviderCapabilities providerCapabilities)
    {
        var canManageUsers = ownership.UserAuthority == OwnershipAuthority.Foundation && providerCapabilities.SupportsLocalUserManagement;
        var canManageRoles = ownership.RoleAuthority == OwnershipAuthority.Foundation && providerCapabilities.SupportsLocalRoleManagement;
        var canManageApplications = ownership.ApplicationAuthority == OwnershipAuthority.Foundation && providerCapabilities.SupportsApplicationManagement;
        var canManageTokens = ownership.ApplicationAuthority == OwnershipAuthority.Foundation;
        var propagation = ownership.PermissionPropagation == PermissionPropagationMode.TokenRefreshBoundary ||
                          providerCapabilities.PermissionPropagation == PermissionPropagationMode.TokenRefreshBoundary
            ? PermissionPropagationMode.TokenRefreshBoundary
            : PermissionPropagationMode.ImmediateServerSide;

        return new EffectiveProviderCapabilities(
            canManageUsers,
            canManageRoles,
            canManageApplications,
            providerCapabilities.SupportsGroupSync,
            canManageTokens && providerCapabilities.SupportsTokenIssuance,
            canManageTokens && providerCapabilities.SupportsRefresh,
            canManageTokens && providerCapabilities.SupportsRevocation,
            propagation);
    }
}
