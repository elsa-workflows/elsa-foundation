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
}
