using System.Security.Claims;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Foundation.Identity.Tests;

public sealed class ClaimsPermissionEvaluatorTests
{
    private readonly ClaimsPermissionEvaluator _evaluator = new();

    [Fact]
    public async Task GrantsAnExactPermissionUsingCanonicalOrdinalKeys()
    {
        Assert.True(await GrantsAsync("résumes:read", "RÉSUMES:READ"));
    }

    [Theory]
    [InlineData("identity/*:read", "identity/users:read", true)]
    [InlineData("identity/*:read", "identity/roles/members:read", true)]
    [InlineData("identity/*:read", "identity:read", true)]
    [InlineData("identity/*:*", "identity/providers:manage", true)]
    [InlineData("workflows/runtime/*:execute", "workflows/runtime/javascript:execute", true)]
    [InlineData("identity/*:read", "identity-audit/users:read", false)]
    [InlineData("identity/*:read", "identityx:read", false)]
    [InlineData("identity/*:read", "workflows/design:read", false)]
    [InlineData("identity/*:read", "identity/users:delete", false)]
    [InlineData("identity/users/*:read", "identity/roles:read", false)]
    public async Task ATrailingWildcardResourceCoversTheResourceAndEveryChild(string grant, string required, bool expected)
    {
        Assert.Equal(expected, await GrantsAsync(grant, required));
    }

    [Theory]
    [InlineData("identity/users:*", "identity/users:read", true)]
    [InlineData("identity/users:*", "identity/users:delete", true)]
    [InlineData("identity/users:*", "identity/roles:read", false)]
    [InlineData("*:read", "workflows/design:read", true)]
    [InlineData("*:read", "workflows/design:manage", false)]
    public async Task AWildcardVerbOrResourceCoversOnlyItsOwnAxis(string grant, string required, bool expected)
    {
        Assert.Equal(expected, await GrantsAsync(grant, required));
    }

    [Theory]
    [InlineData("*", "identity/users:manage", true)]
    [InlineData("*", "workflows/runtime/javascript:execute", true)]
    [InlineData("*:*", "secrets:delete", true)]
    [InlineData("*", "*", true)]
    [InlineData("*:*", "*", true)]
    [InlineData("identity/*:*", "*", false)]
    [InlineData("identity/users:manage", "*", false)]
    public async Task TheBareWildcardIsTheSuperuserAndOnlyItSatisfiesAWildcardRequest(string grant, string required, bool expected)
    {
        Assert.Equal(expected, await GrantsAsync(grant, required));
    }

    [Theory]
    [InlineData("identity/users:manage", "identity/users:read", true)]
    [InlineData("identity/*:manage", "identity/users:read", true)]
    [InlineData("*:manage", "secrets:read", true)]
    [InlineData("identity/users:manage", "identity/roles:read", false)]
    [InlineData("identity/users:read", "identity/users:manage", false)]
    [InlineData("identity/users:manage", "identity/users:delete", false)]
    [InlineData("identity/users:write", "identity/users:read", false)]
    [InlineData("studio/preferences/defaults:manage", "studio/preferences:read", false)]
    public async Task ManageImpliesReadOnTheSameResourceAndNothingElseImpliesAnything(string grant, string required, bool expected)
    {
        Assert.Equal(expected, await GrantsAsync(grant, required));
    }

    [Theory]
    [InlineData("identity.users.read")]
    [InlineData("identity/users")]
    [InlineData("identity/*")]
    [InlineData("identity*:read")]
    [InlineData("*/users:read")]
    [InlineData("identity/*/users:read")]
    [InlineData("identity//users:read")]
    [InlineData("identity/users:re*d")]
    [InlineData("identity/users:read:extra")]
    [InlineData("identity/users:read/all")]
    [InlineData("identity users:read")]
    [InlineData(" identity/users:read")]
    [InlineData("")]
    public async Task AGrantThatIsNotAPermissionMatchesNothing(string grant)
    {
        Assert.False(await GrantsAsync(grant, "identity/users:read"));
    }

    [Theory]
    [InlineData("identity.users.read")]
    [InlineData("identity/*:read")]
    [InlineData("identity/users:*")]
    [InlineData(" identity/users:read")]
    public async Task ARequestThatIsNotOneConcretePermissionIsAProgrammingError(string required)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => GrantsAsync("*", required));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyRequestIsDenied(string? required)
    {
        Assert.False(await GrantsAsync("*", required!));
    }

    private async Task<bool> GrantsAsync(string grant, string required)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(IdentityClaimTypes.Permission, grant)], "test"));
        return (await _evaluator.EvaluateAsync(new PermissionEvaluationContext(principal, required))).Succeeded;
    }
}
