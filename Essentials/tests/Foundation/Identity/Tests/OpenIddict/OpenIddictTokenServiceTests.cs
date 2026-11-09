using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Authentication;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Foundation.Identity.OpenIddict;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace Elsa.Foundation.Identity.Tests.OpenIddict;

/// <summary>
/// B4: the token service over the real OpenIddict pipeline — issue → validate → expire → revoke, JWT claim
/// content, refresh rotation, and the refresh-time user check (#2840).
/// </summary>
public sealed class OpenIddictTokenServiceTests : IAsyncDisposable
{
    private readonly UserLedger _users = new();
    private readonly OpenIddictIdentityFixture _fixture;

    // The replacement contract must go through its Replace* method; the ledger behind it is shared with the test.
    public OpenIddictTokenServiceTests() =>
        _fixture = new OpenIddictIdentityFixture(configureServices: services =>
            services.AddSingleton(_users).ReplaceUserTokenEligibility<StubUserTokenEligibility>());

    [Fact]
    public async Task Issue_Produces_Signed_Readable_Jwt_With_Subject_Tenant_And_Permission_Claims()
    {
        var issued = await _fixture.IssueAsync("user-7", "tenant-x", "identity/users:read", "identity/roles:read");

        var jwt = new JsonWebToken(issued.AccessToken);

        Assert.Equal("at+jwt", jwt.Typ);
        Assert.Equal("RS256", jwt.Alg);
        Assert.Equal(new OpenIddictIdentityOptions().Issuer, jwt.Issuer);
        Assert.Equal("user-7", jwt.Subject);
        Assert.Equal("tenant-x", jwt.GetClaim(IdentityClaimTypes.TenantId).Value);
        Assert.Equal("openiddict", jwt.GetClaim(IdentityClaimTypes.Provider).Value);
        Assert.Equal("v1", jwt.GetClaim(IdentityClaimTypes.Normalized).Value);
        Assert.Single(jwt.Claims, x => x.Type == IdentityClaimTypes.Normalized);
        Assert.True(jwt.TryGetClaim("oi_tkn_id", out _), "the JWT should reference its token-store entry");

        var permissions = jwt.Claims.Where(x => x.Type == IdentityClaimTypes.Permission).Select(x => x.Value).ToList();
        Assert.Contains("identity/users:read", permissions);
        Assert.Contains("identity/roles:read", permissions);

        Assert.NotNull(issued.RefreshToken);
        Assert.InRange(issued.ExpiresAt, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(16));
    }

    [Fact]
    public async Task Explicit_canonical_user_survives_issue_validation_and_refresh_without_inference_from_subject()
    {
        var issued = await _fixture.TokenService.IssueAsync(new("external-subject", "tenant-a", ["user-tasks:read"]) { CanonicalUserId = "canonical-elsa-user" });
        foreach (var accessToken in new[] { issued.AccessToken, (await _fixture.TokenService.RefreshAsync(new(issued.RefreshToken!))).AccessToken })
        {
            var principal = (await _fixture.TokenService.ValidateAsync(new(accessToken))).Principal!;
            Assert.Equal("canonical-elsa-user", Assert.Single(principal.FindAll(IdentityClaimTypes.UserId)).Value);
            Assert.Equal("external-subject", principal.FindFirst("sub")!.Value);
        }
        var nonUser = await _fixture.IssueAsync("external-subject", "tenant-a", "user-tasks:read");
        Assert.Empty((await _fixture.TokenService.ValidateAsync(new(nonUser.AccessToken))).Principal!.FindAll(IdentityClaimTypes.UserId));
    }

    [Fact]
    public async Task Issued_Token_Validates_To_An_Authenticated_Principal_With_Claims()
    {
        var issued = await _fixture.IssueAsync("user-8", "tenant-y", "identity/users:manage");

        var result = await _fixture.TokenService.ValidateAsync(new TokenValidationRequest(issued.AccessToken));

        Assert.True(result.Succeeded, result.Failure);
        Assert.True(result.Principal!.Identity!.IsAuthenticated);
        Assert.Equal("tenant-y", result.Principal.FindFirst(IdentityClaimTypes.TenantId)?.Value);
        Assert.Contains("identity/users:manage", result.Principal.FindAll(IdentityClaimTypes.Permission).Select(x => x.Value));
        Assert.Contains("user-8", result.Principal.FindAll("sub").Select(x => x.Value));
        Assert.Equal(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, result.Principal.Identity!.AuthenticationType);
    }

    [Fact]
    public void ProviderTrust_RegistersValidationRuntimeType_NotTokenConstructionType()
    {
        var options = _fixture.Services.GetRequiredService<IOptions<FoundationIdentityOptions>>().Value;

        Assert.Contains(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, options.NormalizedAuthenticationTypes);
        Assert.DoesNotContain("openiddict", options.NormalizedAuthenticationTypes);
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
    public async Task RevokeSubject_Revokes_Live_Entries_And_Leaves_Expired_Ones_Unwritten()
    {
        // The access entry is born expired while its refresh entry is live, so one subject holds both kinds.
        await using var expiring = new OpenIddictIdentityFixture(options => options.AccessTokenLifetime = TimeSpan.FromMinutes(-15));
        var issued = await expiring.IssueAsync("user-expired");
        var manager = expiring.Services.GetRequiredService<IOpenIddictTokenManager>();
        var accessEntry = (await manager.FindByIdAsync(new JsonWebToken(issued.AccessToken).GetClaim("oi_tkn_id").Value))!;
        var refreshEntry = (await manager.FindByReferenceIdAsync(issued.RefreshToken!))!;

        await expiring.TokenService.RevokeSubjectAsync("user-expired");

        Assert.True(await manager.HasStatusAsync(accessEntry, OpenIddictConstants.Statuses.Valid), "an expired entry must not be written");
        Assert.True(await manager.HasStatusAsync(refreshEntry, OpenIddictConstants.Statuses.Revoked));
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
        var issued = await _fixture.IssueAsync("user-9", "tenant-z", "identity/roles:manage");

        var refreshed = await _fixture.TokenService.RefreshAsync(new TokenRefreshRequest(issued.RefreshToken!));

        Assert.NotEqual(issued.RefreshToken, refreshed.RefreshToken);
        var jwt = new JsonWebToken(refreshed.AccessToken);
        Assert.Equal("user-9", jwt.Subject);
        Assert.Equal("tenant-z", jwt.GetClaim(IdentityClaimTypes.TenantId).Value);
        Assert.Contains("identity/roles:manage", jwt.Claims.Where(x => x.Type == IdentityClaimTypes.Permission).Select(x => x.Value));
        Assert.True((await _fixture.TokenService.ValidateAsync(new TokenValidationRequest(refreshed.AccessToken))).Succeeded);
    }

    [Fact]
    public async Task Refresh_For_A_Disabled_Or_Deleted_User_Is_Refused_Issues_Nothing_And_Revokes_The_Users_Tokens()
    {
        // Two clients hold refresh tokens for the same user; a third subject is a bystander.
        var first = await IssueUserAsync("user-gone");
        var second = await IssueUserAsync("user-gone");
        var bystander = await IssueUserAsync("user-stays");
        var manager = _fixture.Services.GetRequiredService<IOpenIddictTokenManager>();
        var entriesBefore = await manager.CountAsync();

        // Disabled and deleted users are both "not eligible" to the token service; the provider decides which is which.
        _users.Ineligible.Add(("tenant-a", "user-gone"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _fixture.TokenService.RefreshAsync(new TokenRefreshRequest(first.RefreshToken!)));

        Assert.Equal(("tenant-a", "user-gone"), Assert.Single(_users.Asked));
        Assert.Equal(entriesBefore, await manager.CountAsync());
        // Every token the user still held is dead: the other client's refresh token and both bearers.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _fixture.TokenService.RefreshAsync(new TokenRefreshRequest(second.RefreshToken!)));
        Assert.False((await _fixture.TokenService.ValidateAsync(new TokenValidationRequest(first.AccessToken))).Succeeded);
        Assert.False((await _fixture.TokenService.ValidateAsync(new TokenValidationRequest(second.AccessToken))).Succeeded);
        // Re-enabling the user does not revive a revoked refresh token.
        _users.Ineligible.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _fixture.TokenService.RefreshAsync(new TokenRefreshRequest(second.RefreshToken!)));
        // The bystander is untouched.
        Assert.True((await _fixture.TokenService.ValidateAsync(new TokenValidationRequest(bystander.AccessToken))).Succeeded);
        var refreshed = await _fixture.TokenService.RefreshAsync(new TokenRefreshRequest(bystander.RefreshToken!));
        Assert.Equal("user-stays", new JsonWebToken(refreshed.AccessToken).Subject);
    }

    [Fact]
    public async Task Refresh_Whose_User_Check_Fails_Is_Refused_Without_Revoking_Anything()
    {
        var first = await IssueUserAsync("user-unknown");
        var second = await IssueUserAsync("user-unknown");
        _users.Unavailable.Add(("tenant-a", "user-unknown"));

        // No verdict on the user: the refresh fails (401 at the endpoint) but nothing is revoked, so a retry can succeed.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _fixture.TokenService.RefreshAsync(new TokenRefreshRequest(first.RefreshToken!)));
        Assert.True((await _fixture.TokenService.ValidateAsync(new TokenValidationRequest(first.AccessToken))).Succeeded);
        Assert.True((await _fixture.TokenService.ValidateAsync(new TokenValidationRequest(second.AccessToken))).Succeeded);

        _users.Unavailable.Clear();
        Assert.NotNull((await _fixture.TokenService.RefreshAsync(new TokenRefreshRequest(first.RefreshToken!))).AccessToken);
        Assert.NotNull((await _fixture.TokenService.RefreshAsync(new TokenRefreshRequest(second.RefreshToken!))).AccessToken);
    }

    [Fact]
    public async Task Refresh_For_An_Enabled_User_Re_Checks_The_User_And_Rotates()
    {
        var issued = await IssueUserAsync("user-active", "tenant-b");

        var refreshed = await _fixture.TokenService.RefreshAsync(new TokenRefreshRequest(issued.RefreshToken!));

        Assert.Equal(("tenant-b", "user-active"), Assert.Single(_users.Asked));
        Assert.NotEqual(issued.RefreshToken, refreshed.RefreshToken);
        var principal = (await _fixture.TokenService.ValidateAsync(new TokenValidationRequest(refreshed.AccessToken))).Principal!;
        Assert.Equal("user-active", Assert.Single(principal.FindAll(IdentityClaimTypes.UserId)).Value);
    }

    [Fact]
    public async Task Refresh_For_A_Subject_That_Is_No_Elsa_User_Does_Not_Consult_The_User_Provider()
    {
        var issued = await _fixture.IssueAsync("external-subject", "tenant-a", "user-tasks:read");

        var refreshed = await _fixture.TokenService.RefreshAsync(new TokenRefreshRequest(issued.RefreshToken!));

        Assert.Empty(_users.Asked);
        Assert.Equal("external-subject", new JsonWebToken(refreshed.AccessToken).Subject);
    }

    [Fact]
    public async Task Hosts_Without_A_User_Provider_Refuse_User_Refreshes_And_Still_Serve_Non_User_Subjects()
    {
        await using var plain = new OpenIddictIdentityFixture();
        Assert.IsType<UnsupportedUserTokenEligibility>(plain.Services.CreateScope().ServiceProvider.GetRequiredService<IUserTokenEligibility>());
        var user = await plain.TokenService.IssueAsync(new("user-1", "tenant-a", ["identity/users:read"]) { CanonicalUserId = "user-1" });
        var nonUser = await plain.IssueAsync("external-subject", "tenant-a", "user-tasks:read");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await plain.TokenService.RefreshAsync(new TokenRefreshRequest(user.RefreshToken!)));
        Assert.NotNull((await plain.TokenService.RefreshAsync(new TokenRefreshRequest(nonUser.RefreshToken!))).AccessToken);
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

    /// <summary>Issues a pair for an Elsa user: the subject is the user id, as the first-party exchange issues it.</summary>
    private ValueTask<TokenIssueResult> IssueUserAsync(string userId, string tenant = "tenant-a") =>
        _fixture.TokenService.IssueAsync(new TokenIssueRequest(userId, tenant, ["identity/users:read"]) { CanonicalUserId = userId });

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

    /// <summary>Which users may not hold tokens, and every question the token service asked.</summary>
    internal sealed class UserLedger
    {
        public HashSet<(string Tenant, string User)> Ineligible { get; } = [];

        /// <summary>Users whose record cannot be read: the check fails instead of answering.</summary>
        public HashSet<(string Tenant, string User)> Unavailable { get; } = [];

        public List<(string Tenant, string User)> Asked { get; } = [];
    }

    /// <summary>Admits every user except those the ledger lists, and records every question asked.</summary>
    internal sealed class StubUserTokenEligibility(UserLedger ledger) : IUserTokenEligibility
    {
        public ValueTask<bool> IsEligibleAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
        {
            ledger.Asked.Add((tenantId, userId));
            if (ledger.Unavailable.Contains((tenantId, userId)))
                throw new InvalidOperationException("The user store is unavailable.");
            return ValueTask.FromResult(!ledger.Ineligible.Contains((tenantId, userId)));
        }
    }
}
