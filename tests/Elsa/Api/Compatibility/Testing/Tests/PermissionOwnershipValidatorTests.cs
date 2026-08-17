using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.Security;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Xunit;

namespace Elsa.Api.Compatibility.Testing.Tests;

public sealed class PermissionOwnershipValidatorTests
{
    [Fact]
    public void Allows_cross_owner_consumption_when_catalog_owner_is_unique()
    {
        var result = PermissionOwnershipValidator.Validate(
            [new Contributor("catalog-owner", "orders.read")],
            [new PermissionConsumption(new EndpointIdentity("/orders", "GET"), "route-owner", "orders.read")]);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Reports_missing_conflicting_and_wildcard_permissions()
    {
        var result = PermissionOwnershipValidator.Validate(
            [new Contributor("one", "orders.read"), new Contributor("two", "orders.read")],
            [
                new PermissionConsumption(new EndpointIdentity("/missing", "GET"), "owner", "missing.read"),
                new PermissionConsumption(new EndpointIdentity("/wildcard", "GET"), "owner", "*")
            ]);

        Assert.Contains(result.Issues, issue => issue.Code == "ConflictingCatalogOwners");
        Assert.Contains(result.Issues, issue => issue.Code == "MissingCatalogOwner");
        Assert.Contains(result.Issues, issue => issue.Code == "WildcardEndpointPermission");
    }

    [Fact]
    public void Ignores_wildcard_grant_when_policy_also_contains_an_action_permission()
    {
        var policy = new PermissionPolicyCodec().Format(PermissionPolicyDescriptor.Any("orders.read", "*"));
        var entry = new EndpointManifestEntry(
            new NormalizedRoute("/orders"), ["GET"], "orders", "route-owner", EndpointAuthoringModels.MinimalApi,
            EndpointSecurityDispositionMetadata.Permission(policy), [], [], null);

        var result = new PermissionOwnershipValidator([new Contributor("catalog-owner", "orders.read")]).Validate([entry]);

        Assert.True(result.IsValid);
    }

    private sealed class Contributor(string owner, params string[] keys) : IPermissionContributor
    {
        public string OwnerId => owner;
        public IEnumerable<Permission> Contribute() => keys.Select(key => new Permission(key, key, "test", key));
    }
}
