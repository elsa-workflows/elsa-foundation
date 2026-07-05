using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.OpenIddict;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Elsa.Foundation.Identity.Tests.OpenIddict;

/// <summary>
/// B4: the token service over the real OpenIddict pipeline — issue → validate → expire → revoke, JWT claim
/// content, and refresh rotation.
/// </summary>
public sealed class OpenIddictTokenServiceTests : IAsyncDisposable
{
    private readonly OpenIddictIdentityFixture _fixture = new();

    [Fact]
    public async Task Issue_Produces_Signed_Readable_Jwt_With_Subject_Tenant_And_Permission_Claims()
    {
        var issued = await _fixture.IssueAsync("user-7", "tenant-x", "identity.users.read", "identity.roles.read");

        var jwt = new JsonWebToken(issued.AccessToken);

        Assert.Equal("at+jwt", jwt.Typ);
        Assert.Equal("RS256", jwt.Alg);
        Assert.Equal(new OpenIddictIdentityOptions().Issuer, jwt.Issuer);
        Assert.Equal("user-7", jwt.Subject);
        Assert.Equal("tenant-x", jwt.GetClaim(IdentityClaimTypes.TenantId).Value);
        Assert.Equal("openiddict", jwt.GetClaim(IdentityClaimTypes.Provider).Value);
        Assert.True(jwt.TryGetClaim("oi_tkn_id", out _), "the JWT should reference its token-store entry");

        var permissions = jwt.Claims.Where(x => x.Type == IdentityClaimTypes.Permission).Select(x => x.Value).ToList();
        Assert.Contains("identity.users.read", permissions);
        Assert.Contains("identity.roles.read", permissions);

        Assert.NotNull(issued.RefreshToken);
        Assert.InRange(issued.ExpiresAt, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(16));
    }

    [Fact]
    public async Task Issued_Token_Validates_To_An_Authenticated_Principal_With_Claims()
    {
        var issued = await _fixture.IssueAsync("user-8", "tenant-y", "identity.users.manage");

        var result = await _fixture.TokenService.ValidateAsync(new TokenValidationRequest(issued.AccessToken));

        Assert.True(result.Succeeded, result.Failure);
        Assert.True(result.Principal!.Identity!.IsAuthenticated);
        Assert.Equal("tenant-y", result.Principal.FindFirst(IdentityClaimTypes.TenantId)?.Value);
        Assert.Contains("identity.users.manage", result.Principal.FindAll(IdentityClaimTypes.Permission).Select(x => x.Value));
        Assert.Contains("user-8", result.Principal.FindAll("sub").Select(x => x.Value));
    }

    [Fact]
    public async Task Expired_Token_Fails_Validation()
    {
        // Beyond the default 5-minute clock skew.
        await using var expiring = new OpenIddictIdentityFixture(options => options.AccessTokenLifetime = TimeSpan.FromMinutes(-15));

        var issued = await expiring.IssueAsync();
        var result = await expiring.TokenService.ValidateAsync(new TokenValidationRequest(issued.AccessToken));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Tampered_Token_Fails_Validation()
    {
        var issued = await _fixture.IssueAsync();
        var tampered = Tamper(issued.AccessToken);

        var result = await _fixture.TokenService.ValidateAsync(new TokenValidationRequest(tampered));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Revoked_Access_Token_Fails_Validation_Immediately()
    {
        var issued = await _fixture.IssueAsync();
        Assert.True((await _fixture.TokenService.ValidateAsync(new TokenValidationRequest(issued.AccessToken))).Succeeded);

        await _fixture.TokenService.RevokeAsync(new TokenRevocationRequest(issued.AccessToken));
        var result = await _fixture.TokenService.ValidateAsync(new TokenValidationRequest(issued.AccessToken));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Refresh_Rotates_Tokens_And_Preserves_Subject_Tenant_And_Scopes()
    {
        var issued = await _fixture.IssueAsync("user-9", "tenant-z", "identity.roles.manage");

        var refreshed = await _fixture.TokenService.RefreshAsync(new TokenRefreshRequest(issued.RefreshToken!));

        Assert.NotEqual(issued.RefreshToken, refreshed.RefreshToken);
        var jwt = new JsonWebToken(refreshed.AccessToken);
        Assert.Equal("user-9", jwt.Subject);
        Assert.Equal("tenant-z", jwt.GetClaim(IdentityClaimTypes.TenantId).Value);
        Assert.Contains("identity.roles.manage", jwt.Claims.Where(x => x.Type == IdentityClaimTypes.Permission).Select(x => x.Value));
        Assert.True((await _fixture.TokenService.ValidateAsync(new TokenValidationRequest(refreshed.AccessToken))).Succeeded);
    }

    [Fact]
    public async Task Refresh_Token_Is_Single_Use()
    {
        var issued = await _fixture.IssueAsync();
        await _fixture.TokenService.RefreshAsync(new TokenRefreshRequest(issued.RefreshToken!));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _fixture.TokenService.RefreshAsync(new TokenRefreshRequest(issued.RefreshToken!)));
    }

    [Fact]
    public async Task Revoked_Refresh_Token_Cannot_Be_Used()
    {
        var issued = await _fixture.IssueAsync();
        await _fixture.TokenService.RevokeAsync(new TokenRevocationRequest(issued.RefreshToken!));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _fixture.TokenService.RefreshAsync(new TokenRefreshRequest(issued.RefreshToken!)));
    }

    [Fact]
    public async Task Unknown_Refresh_Token_Is_Rejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _fixture.TokenService.RefreshAsync(new TokenRefreshRequest("not-a-refresh-token")));
    }

    /// <summary>Flips a character in the payload segment so the signature no longer matches.</summary>
    internal static string Tamper(string jwt)
    {
        var parts = jwt.Split('.');
        var payload = parts[1];
        var index = payload.Length / 2;
        parts[1] = payload[..index] + (payload[index] == 'a' ? 'b' : 'a') + payload[(index + 1)..];
        return string.Join('.', parts);
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
