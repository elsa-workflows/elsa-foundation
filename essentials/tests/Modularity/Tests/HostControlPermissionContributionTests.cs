using System.Security.Claims;
using Elsa.ExtensionBuilder.Api.Authorization;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Modularity.Api.Authorization;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// Verifies that the module-management and Extension Builder features contribute their coarse
/// host-control permissions to the shared permission catalog, that the evaluator lets <c>manage</c> satisfy
/// <c>read</c>, and that the aggregated catalog resolves them alongside the default identity permissions.
/// </summary>
public sealed class HostControlPermissionContributionTests
{
    private readonly CompositePermissionCatalog _catalog = new(
    [
        new DefaultIdentityPermissionCatalog(),
        new ModuleManagementPermissionContributor(),
        new ExtensionBuilderPermissionContributor()
    ]);

    [Theory]
    [InlineData(ModuleManagementPermissionKeys.Read, ModuleManagementPermissionKeys.Manage)]
    [InlineData(ExtensionBuilderPermissionKeys.Read, ExtensionBuilderPermissionKeys.Manage)]
    public async Task FeatureContributesReadAndManageWithManageSatisfyingRead(string readKey, string manageKey)
    {
        var evaluator = new ClaimsPermissionEvaluator();

        Assert.NotNull(_catalog.Find(readKey));
        Assert.NotNull(_catalog.Find(manageKey));
        Assert.True((await evaluator.EvaluateAsync(new(Holding(manageKey), readKey))).Succeeded);
        Assert.False((await evaluator.EvaluateAsync(new(Holding(readKey), manageKey))).Succeeded);
    }

    private static ClaimsPrincipal Holding(string permission) =>
        new(new ClaimsIdentity([new Claim(IdentityClaimTypes.Permission, permission)], "test"));

    [Fact]
    public void ContributedPermissionsUseExpectedCoarseKeys()
    {
        var keys = _catalog.List().Select(x => x.Key).ToHashSet();

        Assert.Contains("module-management:read", keys);
        Assert.Contains("module-management:manage", keys);
        Assert.Contains("extension-builder:read", keys);
        Assert.Contains("extension-builder:manage", keys);
    }

    [Fact]
    public void ContributedPermissionsCarryTheirFeaturesOwner()
    {
        Assert.Equal(ModuleManagementPermissionKeys.OwnerId, _catalog.Find(ModuleManagementPermissionKeys.Manage)!.OwnerId);
        Assert.Equal(ExtensionBuilderPermissionKeys.OwnerId, _catalog.Find(ExtensionBuilderPermissionKeys.Manage)!.OwnerId);
    }

    [Fact]
    public void CatalogRetainsDefaultIdentityPermissionsAlongsideContributions()
    {
        Assert.NotNull(_catalog.Find(DefaultIdentityPermissionKeys.IdentityUsersRead));
        Assert.NotNull(_catalog.Find(DefaultIdentityPermissionKeys.IdentityRolesManage));
    }
}
