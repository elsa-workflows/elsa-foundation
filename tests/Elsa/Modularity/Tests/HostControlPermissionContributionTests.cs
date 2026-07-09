using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Modularity.Api.Authorization;
using Elsa.Modularity.ExtensionBuilder.Authorization;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// Verifies that the module-management and Extension Builder features contribute their coarse
/// host-control permissions to the shared permission catalog per ADR 0037, with <c>manage</c> implying
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
    public void FeatureContributesReadAndManageWithManageImplyingRead(string readKey, string manageKey)
    {
        var read = _catalog.Find(readKey);
        var manage = _catalog.Find(manageKey);

        Assert.NotNull(read);
        Assert.NotNull(manage);
        Assert.Empty(read!.Implies ?? new HashSet<string>());
        Assert.Contains(readKey, manage!.Implies!);
    }

    [Fact]
    public void ContributedPermissionsUseExpectedCoarseKeys()
    {
        var keys = _catalog.List().Select(x => x.Key).ToHashSet();

        Assert.Contains("module-management.read", keys);
        Assert.Contains("module-management.manage", keys);
        Assert.Contains("extension-builder.read", keys);
        Assert.Contains("extension-builder.manage", keys);
    }

    [Fact]
    public void CatalogRetainsDefaultIdentityPermissionsAlongsideContributions()
    {
        Assert.NotNull(_catalog.Find(DefaultIdentityPermissionKeys.IdentityUsersRead));
        Assert.NotNull(_catalog.Find(DefaultIdentityPermissionKeys.IdentityRolesManage));
    }
}
