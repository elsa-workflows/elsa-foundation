using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Foundation.Identity.Tests;

public sealed class ClaimsNormalizationTests
{
    [Fact]
    public async Task NormalizeAddsMappedRolesAndPermissionsInRuleOrder()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new("groups", "admins")
        ]));
        var normalizer = new DefaultClaimsNormalizer(new ClaimMappingRuleEvaluator());

        var result = await normalizer.NormalizeAsync(new(
            principal,
            "tenant-a",
            "entra",
            [
                new("later", "tenant-a", "entra", "groups", "admins", new HashSet<string> { "operators" }, new HashSet<string> { "identity.providers.read" }, 20, false),
                new("first", "tenant-a", "entra", "groups", "admins", new HashSet<string> { "admins" }, new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersManage }, 10, true)
            ]));

        Assert.Contains("admins", result.Roles);
        Assert.DoesNotContain("operators", result.Roles);
        Assert.Contains(DefaultIdentityPermissionKeys.IdentityUsersManage, result.Permissions);
        Assert.Contains(result.Principal.Claims, x => x.Type == IdentityClaimTypes.TenantId && x.Value == "tenant-a");
        Assert.Contains(result.Principal.Claims, x => x.Type == IdentityClaimTypes.Provider && x.Value == "entra");
        Assert.Contains(result.Principal.Claims, x => x.Type == IdentityClaimTypes.Role && x.Value == "admins");
        Assert.Contains(result.Principal.Claims, x => x.Type == IdentityClaimTypes.Permission && x.Value == DefaultIdentityPermissionKeys.IdentityUsersManage);
    }

    [Fact]
    public async Task NormalizeScopesRulesToCurrentTenantAndProvider()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new("groups", "admins")
        ]));
        var normalizer = new DefaultClaimsNormalizer(new ClaimMappingRuleEvaluator());

        var result = await normalizer.NormalizeAsync(new(
            principal,
            "tenant-a",
            "entra",
            [
                new("wrong-tenant", "tenant-b", "entra", "groups", "admins", new HashSet<string> { "tenant-b-admins" }, new HashSet<string> { "identity.roles.manage" }, 1, false),
                new("wrong-provider", "tenant-a", "github", "groups", "admins", new HashSet<string> { "github-admins" }, new HashSet<string> { "identity.providers.manage" }, 2, false),
                new("right", "tenant-a", "entra", "groups", "admins", new HashSet<string> { "admins" }, new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersManage }, 3, false)
            ]));

        Assert.Equal(["admins"], result.Roles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        Assert.Equal([DefaultIdentityPermissionKeys.IdentityUsersManage], result.Permissions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NormalizeStripsIncomingInternalClaimsAndDoesNotDuplicateOutputs()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new(IdentityClaimTypes.TenantId, "stale-tenant"),
            new(IdentityClaimTypes.Provider, "stale-provider"),
            new(IdentityClaimTypes.Role, "forged-role"),
            new(IdentityClaimTypes.Permission, DefaultIdentityPermissionKeys.IdentityProvidersManage),
            new(ClaimTypes.Role, "external-role"),
            new("groups", "admins")
        ]));
        var normalizer = new DefaultClaimsNormalizer(new ClaimMappingRuleEvaluator());

        var result = await normalizer.NormalizeAsync(new(
            principal,
            "tenant-a",
            "entra",
            [
                new("right", "tenant-a", "entra", "groups", "admins", new HashSet<string> { "admins" }, new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersManage }, 1, false)
            ]));

        Assert.DoesNotContain("forged-role", result.Roles);
        Assert.DoesNotContain(DefaultIdentityPermissionKeys.IdentityProvidersManage, result.Permissions);
        Assert.Single(result.Principal.Claims.Where(x => x.Type == IdentityClaimTypes.TenantId));
        Assert.Single(result.Principal.Claims.Where(x => x.Type == IdentityClaimTypes.Provider));
        Assert.Equal(2, result.Principal.Claims.Count(x => x.Type == IdentityClaimTypes.Role));
        Assert.Single(result.Principal.Claims.Where(x => x.Type == IdentityClaimTypes.Permission));
        Assert.DoesNotContain(result.Principal.Claims, x => x.Type == IdentityClaimTypes.TenantId && x.Value == "stale-tenant");
        Assert.DoesNotContain(result.Principal.Claims, x => x.Type == IdentityClaimTypes.Provider && x.Value == "stale-provider");
    }
}
