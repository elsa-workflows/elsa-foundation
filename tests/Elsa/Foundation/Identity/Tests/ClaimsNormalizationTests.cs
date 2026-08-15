using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.Extensions.Options;

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
            new(IdentityClaimTypes.Normalized, "v1"),
            new(IdentityClaimTypes.Normalized, "forged"),
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
        Assert.Single(result.Principal.Claims, x => x.Type == IdentityClaimTypes.TenantId);
        Assert.Single(result.Principal.Claims, x => x.Type == IdentityClaimTypes.Provider);
        Assert.Equal(2, result.Principal.Claims.Count(x => x.Type == IdentityClaimTypes.Role));
        Assert.Single(result.Principal.Claims, x => x.Type == IdentityClaimTypes.Permission);
        var marker = Assert.Single(result.Principal.Claims, x => x.Type == IdentityClaimTypes.Normalized);
        Assert.Equal("v1", marker.Value);
        Assert.DoesNotContain(result.Principal.Claims, x => x.Type == IdentityClaimTypes.TenantId && x.Value == "stale-tenant");
        Assert.DoesNotContain(result.Principal.Claims, x => x.Type == IdentityClaimTypes.Provider && x.Value == "stale-provider");
    }

    [Fact]
    public void NormalizedPrincipalValidatorRequiresOneExactTrustedMarkedIdentity()
    {
        var validator = CreateValidator("trusted");

        Assert.False(validator.TryGetNormalizedPrincipal(new ClaimsPrincipal(), out _));
        Assert.False(validator.TryGetNormalizedPrincipal(Principal(Identity("TRUSTED", markerValues: ["v1"])), out _));
        Assert.False(validator.TryGetNormalizedPrincipal(Principal(Identity("trusted")), out _));
        Assert.False(validator.TryGetNormalizedPrincipal(Principal(Identity("trusted", markerValues: ["v1", "v1"])), out _));
        Assert.False(validator.TryGetNormalizedPrincipal(Principal(Identity("trusted", markerValues: ["v1", "forged"])), out _));
        Assert.False(validator.TryGetNormalizedPrincipal(Principal(
            Identity("trusted", tenant: "a", markerValues: ["v1"]),
            Identity("trusted", tenant: "b", markerValues: ["v1"])), out _));

        Assert.True(validator.TryGetNormalizedPrincipal(
            Principal(Identity("trusted", tenant: "a", markerValues: ["v1"])),
            out var selected));
        Assert.Equal("a", selected.FindFirst(IdentityClaimTypes.TenantId)?.Value);
    }

    [Fact]
    public void NormalizedPrincipalValidatorAlwaysMatchesAuthenticationTypesUsingOrdinalCaseSensitiveComparison()
    {
        var validator = new NormalizedPrincipalValidator(Options.Create(new FoundationIdentityOptions
        {
            NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "trusted" }
        }));

        Assert.False(validator.TryGetNormalizedPrincipal(
            Principal(Identity("TRUSTED", markerValues: ["v1"])),
            out _));
    }

    [Fact]
    public void NormalizedPrincipalValidatorFiltersUntrustedIdentitiesFromEvaluatorInput()
    {
        var validator = CreateValidator("trusted");
        var trusted = Identity("trusted", tenant: "tenant-a", markerValues: ["v1"]);
        trusted.AddClaim(new Claim(IdentityClaimTypes.Permission, "trusted.read"));
        var raw = Identity("raw", tenant: "tenant-b", markerValues: ["v1"]);
        raw.AddClaim(new Claim(IdentityClaimTypes.Permission, "forged.manage"));

        Assert.True(validator.TryGetNormalizedPrincipal(Principal(raw, trusted), out var selected));
        Assert.Single(selected.Identities);
        Assert.Contains(selected.Claims, x => x.Type == IdentityClaimTypes.Permission && x.Value == "trusted.read");
        Assert.DoesNotContain(selected.Claims, x => x.Value == "forged.manage" || x.Value == "tenant-b");
    }

    private static NormalizedPrincipalValidator CreateValidator(params string[] trustedTypes) =>
        new(Options.Create(new FoundationIdentityOptions
        {
            NormalizedAuthenticationTypes = trustedTypes.ToHashSet(StringComparer.Ordinal)
        }));

    private static ClaimsPrincipal Principal(params ClaimsIdentity[] identities) => new(identities);

    private static ClaimsIdentity Identity(
        string authenticationType,
        string tenant = "tenant-a",
        params string[] markerValues)
    {
        var identity = new ClaimsIdentity(authenticationType: authenticationType);
        identity.AddClaim(new Claim(IdentityClaimTypes.TenantId, tenant));
        foreach (var marker in markerValues)
            identity.AddClaim(new Claim(IdentityClaimTypes.Normalized, marker));
        return identity;
    }
}
