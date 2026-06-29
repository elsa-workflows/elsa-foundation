using Elsa.Foundation.Identity.Abstractions.Ownership;

namespace Elsa.Foundation.Identity.Tests;

public sealed class OwnershipContractsTests
{
    [Fact]
    public void FoundationOwnedCapabilitiesRequireProviderSupport()
    {
        var resolver = new DefaultEffectiveCapabilitiesResolver();

        var capabilities = resolver.Resolve(
            OwnershipConfiguration.FromMode(OwnershipMode.FoundationOwned),
            ProviderCapabilities.ExternalOidcDefault);

        Assert.False(capabilities.CanManageUsers);
        Assert.False(capabilities.CanManageRoles);
        Assert.False(capabilities.CanManageApplications);
        Assert.True(capabilities.CanMapGroups);
        Assert.Equal(PermissionPropagationMode.TokenRefreshBoundary, capabilities.PermissionPropagation);
    }

    [Fact]
    public void HybridModeKeepsRolesAndApplicationsFoundationOwned()
    {
        var resolver = new DefaultEffectiveCapabilitiesResolver();

        var capabilities = resolver.Resolve(
            OwnershipConfiguration.FromMode(OwnershipMode.Hybrid, PermissionPropagationMode.TokenRefreshBoundary),
            ProviderCapabilities.FoundationReference with { PermissionPropagation = PermissionPropagationMode.TokenRefreshBoundary });

        Assert.False(capabilities.CanManageUsers);
        Assert.True(capabilities.CanManageRoles);
        Assert.True(capabilities.CanManageApplications);
        Assert.Equal(PermissionPropagationMode.TokenRefreshBoundary, capabilities.PermissionPropagation);
    }

    [Fact]
    public void ExternalOwnedModeDisablesFoundationTokenOperations()
    {
        var resolver = new DefaultEffectiveCapabilitiesResolver();

        var capabilities = resolver.Resolve(
            OwnershipConfiguration.FromMode(OwnershipMode.ExternalOwned),
            ProviderCapabilities.FoundationReference);

        Assert.False(capabilities.CanIssueTokens);
        Assert.False(capabilities.CanRefreshTokens);
        Assert.False(capabilities.CanRevokeTokens);
    }
}
