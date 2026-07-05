using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;

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
}
