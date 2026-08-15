using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Api.FastEndpoints.Tests;

/// <summary>
/// Covers issue #414 item 5: the permission composition applied by every Elsa endpoint base class
/// (always grant <see cref="PermissionNames.All"/> alongside the endpoint's own permissions) lives in a
/// single helper, so a composition change lands in exactly one place. The composition itself is frozen
/// behavior: the wildcard permission always comes first, followed by the endpoint's permissions in order.
/// </summary>
public sealed class ElsaEndpointPermissionsTests
{
    [Fact]
    public void Compose_prepends_the_wildcard_permission()
    {
        Assert.Equal(new[] { PermissionNames.All, "a", "b" }, ElsaEndpointPermissions.Compose(["a", "b"]));
    }

    [Fact]
    public void Compose_with_no_permissions_grants_only_the_wildcard()
    {
        Assert.Equal(new[] { PermissionNames.All }, ElsaEndpointPermissions.Compose([]));
    }

    [Fact]
    public void ComposePolicy_with_no_permissions_uses_a_canonical_single_wildcard_policy()
    {
        var policy = ElsaEndpointPermissions.ComposePolicy([]);

        Assert.Equal("Elsa.Permission:v1:s:Kg", policy);
        var result = new PermissionPolicyCodec().Parse(policy);
        Assert.Equal(PermissionPolicyParseStatus.Valid, result.Status);
        Assert.Equal(PermissionRequirementMode.Single, result.Descriptor!.Mode);
        Assert.Equal([PermissionNames.All], result.Descriptor.Permissions);
    }

    [Fact]
    public void ComposePolicy_with_actions_uses_one_canonical_any_policy_for_wildcard_or_actions()
    {
        var policy = ElsaEndpointPermissions.ComposePolicy(["write", "read", "READ"]);

        Assert.Equal("Elsa.Permission:v1:a:Kg.UkVBRA.V1JJVEU", policy);
        var result = new PermissionPolicyCodec().Parse(policy);
        Assert.Equal(PermissionPolicyParseStatus.Valid, result.Status);
        Assert.Equal(PermissionRequirementMode.Any, result.Descriptor!.Mode);
        Assert.Equal([PermissionNames.All, "READ", "WRITE"], result.Descriptor.Permissions);
    }
}
