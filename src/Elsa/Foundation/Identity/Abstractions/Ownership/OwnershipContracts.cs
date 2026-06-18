using Elsa.Foundation.Identity.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Abstractions.Ownership;

public enum OwnershipMode
{
    FoundationOwned,
    ExternalOwned,
    Hybrid
}

public enum OwnershipAuthority
{
    Foundation,
    External
}

public enum PermissionPropagationMode
{
    ImmediateServerSide,
    TokenRefreshBoundary
}

public interface IOwnershipModeProvider
{
    ValueTask<OwnershipConfiguration> GetAsync(string? tenantId = null, CancellationToken cancellationToken = default);
}

public interface IEffectiveCapabilitiesResolver
{
    EffectiveProviderCapabilities Resolve(OwnershipConfiguration ownership, ProviderCapabilities providerCapabilities);
}

public sealed record ProviderCapabilities(
    bool SupportsLocalUserManagement,
    bool SupportsLocalRoleManagement,
    bool SupportsApplicationManagement,
    bool SupportsGroupSync,
    bool SupportsTokenIssuance,
    bool SupportsRefresh,
    bool SupportsRevocation,
    PermissionPropagationMode PermissionPropagation = PermissionPropagationMode.ImmediateServerSide)
{
    public static ProviderCapabilities ExternalOidcDefault { get; } = new(
        SupportsLocalUserManagement: false,
        SupportsLocalRoleManagement: false,
        SupportsApplicationManagement: false,
        SupportsGroupSync: true,
        SupportsTokenIssuance: false,
        SupportsRefresh: true,
        SupportsRevocation: false,
        PermissionPropagation: PermissionPropagationMode.TokenRefreshBoundary);

    public static ProviderCapabilities FoundationReference { get; } = new(
        SupportsLocalUserManagement: true,
        SupportsLocalRoleManagement: true,
        SupportsApplicationManagement: true,
        SupportsGroupSync: true,
        SupportsTokenIssuance: true,
        SupportsRefresh: true,
        SupportsRevocation: true);
}

public sealed record OwnershipConfiguration(
    OwnershipMode Mode,
    OwnershipAuthority UserAuthority,
    OwnershipAuthority RoleAuthority,
    OwnershipAuthority ApplicationAuthority,
    PermissionPropagationMode PermissionPropagation)
{
    public static OwnershipConfiguration FromMode(OwnershipMode mode, PermissionPropagationMode permissionPropagation = PermissionPropagationMode.ImmediateServerSide) =>
        mode switch
        {
            OwnershipMode.FoundationOwned => new(mode, OwnershipAuthority.Foundation, OwnershipAuthority.Foundation, OwnershipAuthority.Foundation, permissionPropagation),
            OwnershipMode.ExternalOwned => new(mode, OwnershipAuthority.External, OwnershipAuthority.External, OwnershipAuthority.External, permissionPropagation),
            OwnershipMode.Hybrid => new(mode, OwnershipAuthority.External, OwnershipAuthority.Foundation, OwnershipAuthority.Foundation, permissionPropagation),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
}

public sealed record EffectiveProviderCapabilities(
    bool CanManageUsers,
    bool CanManageRoles,
    bool CanManageApplications,
    bool CanMapGroups,
    bool CanIssueTokens,
    bool CanRefreshTokens,
    bool CanRevokeTokens,
    PermissionPropagationMode PermissionPropagation);

public sealed class OptionsOwnershipModeProvider(IOptions<FoundationIdentityOptions> options) : IOwnershipModeProvider
{
    public ValueTask<OwnershipConfiguration> GetAsync(string? tenantId = null, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(OwnershipConfiguration.FromMode(options.Value.OwnershipMode, options.Value.PermissionPropagation));
}

public sealed class DefaultEffectiveCapabilitiesResolver : IEffectiveCapabilitiesResolver
{
    public EffectiveProviderCapabilities Resolve(OwnershipConfiguration ownership, ProviderCapabilities providerCapabilities)
    {
        var canManageUsers = ownership.UserAuthority == OwnershipAuthority.Foundation && providerCapabilities.SupportsLocalUserManagement;
        var canManageRoles = ownership.RoleAuthority == OwnershipAuthority.Foundation && providerCapabilities.SupportsLocalRoleManagement;
        var canManageApplications = ownership.ApplicationAuthority == OwnershipAuthority.Foundation && providerCapabilities.SupportsApplicationManagement;
        var propagation = ownership.PermissionPropagation == PermissionPropagationMode.ImmediateServerSide
            ? PermissionPropagationMode.ImmediateServerSide
            : providerCapabilities.PermissionPropagation;

        return new EffectiveProviderCapabilities(
            canManageUsers,
            canManageRoles,
            canManageApplications,
            providerCapabilities.SupportsGroupSync,
            providerCapabilities.SupportsTokenIssuance,
            providerCapabilities.SupportsRefresh,
            providerCapabilities.SupportsRevocation,
            propagation);
    }
}
