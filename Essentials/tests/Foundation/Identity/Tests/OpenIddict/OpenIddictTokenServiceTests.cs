using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.OpenIddict;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using OpenIddict.Server;
using Microsoft.IdentityModel.Tokens;

namespace Elsa.Foundation.Identity.Tests.OpenIddict;

/// <summary>
/// B4: the token service over the real OpenIddict pipeline — issue → validate → expire → revoke, JWT claim
/// content, and refresh rotation.
/// </summary>
public sealed class OpenIddictTokenServiceTests : IAsyncDisposable
{
    private readonly OpenIddictIdentityFixture _fixture = new();

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-scope")]
    public async Task Cryptographically_valid_legacy_or_wrong_scope_access_tokens_are_refused(string? scopeKey)
    {
        var issued = await _fixture.IssueAsync();
        var original = new JsonWebToken(issued.AccessToken);
        var claims = original.Claims.Where(claim => claim.Type is not "iss" and not "exp" and not "nbf" and not "iat" &&
            claim.Type != IdentityClaimTypes.TokenSubjectKey).ToList();
        if (scopeKey is not null)
            claims.Add(new Claim(IdentityClaimTypes.TokenSubjectKey, scopeKey));
        var server = _fixture.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
        var rewritten = server.JsonWebTokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims), Issuer = server.Issuer!.AbsoluteUri,
            IssuedAt = DateTime.UtcNow, NotBefore = DateTime.UtcNow.AddMinutes(-1), Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = server.SigningCredentials[0], TokenType = "at+jwt"
        });

        Assert.False((await _fixture.TokenService.ValidateAsync(new(rewritten))).Succeeded);
        Assert.True((await _fixture.TokenService.ValidateAsync(new(issued.AccessToken))).Succeeded);
    }

    [Fact]
    public async Task Scoped_revocation_preserves_equal_subjects_in_other_providers_issuers_tenants_and_user_kind()
    {
        var revokedSubject = new TokenSubject("tenant-a", "same-subject", "provider-a", "https://issuer.example/Realm");
        TokenSubject[] unrelated =
        [
            new("tenant-a", "same-subject", "provider-b", "https://issuer.example/Realm"),
            new("tenant-a", "same-subject", "provider-a", "https://other-issuer.example/Realm"),
            new("tenant-b", "same-subject", "provider-a", "https://issuer.example/Realm"),
            new("tenant-a", "Same-Subject", "provider-a", "https://issuer.example/Realm"),
            new("tenant-a", "same-subject", "provider-a", "https://issuer.example/realm"),
            new("tenant-a", "same-subject")
        ];
        var revoked = await _fixture.TokenService.IssueAsync(new(revokedSubject, ["identity/users:read"]));
        var surviving = new List<TokenIssueResult>();
        foreach (var subject in unrelated)
            surviving.Add(await _fixture.TokenService.IssueAsync(new(subject, ["identity/users:read"])));

        Assert.Equal(surviving.Count + 1, surviving.Select(token => new JsonWebToken(token.AccessToken).Subject)
            .Append(new JsonWebToken(revoked.AccessToken).Subject).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("same-subject", new JsonWebToken(revoked.AccessToken).GetClaim(IdentityClaimTypes.ExternalSubject).Value);

        await _fixture.TokenService.RevokeSubjectAsync(revokedSubject);

        Assert.False((await _fixture.TokenService.ValidateAsync(new(revoked.AccessToken))).Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _fixture.TokenService.RefreshAsync(new(revoked.RefreshToken!)));
        foreach (var token in surviving)
        {
            Assert.True((await _fixture.TokenService.ValidateAsync(new(token.AccessToken))).Succeeded);
            var refreshed = await _fixture.TokenService.RefreshAsync(new(token.RefreshToken!));
            Assert.True((await _fixture.TokenService.ValidateAsync(new(refreshed.AccessToken))).Succeeded);
        }
    }

    [Fact]
    public async Task Refresh_preserves_external_scope_and_delimiter_values_do_not_alias()
    {
        var first = new TokenSubject("tenant:a", "subject", "provider", "https://issuer/");
        var second = new TokenSubject("tenant", "a:subject", "provider", "https://issuer/");
        var a = await _fixture.TokenService.IssueAsync(new(first, []));
        var b = await _fixture.TokenService.IssueAsync(new(second, []));
        var refreshed = await _fixture.TokenService.RefreshAsync(new(a.RefreshToken!));
        var principal = (await _fixture.TokenService.ValidateAsync(new(refreshed.AccessToken))).Principal!;
        Assert.True(_fixture.Services.GetRequiredService<ITokenSubjectResolver>().TryResolve(principal, out var recovered));
        Assert.Equal(first, recovered);
        await _fixture.TokenService.RevokeSubjectAsync(first);
        Assert.False((await _fixture.TokenService.ValidateAsync(new(refreshed.AccessToken))).Succeeded);
        Assert.NotNull((await _fixture.TokenService.RefreshAsync(new(b.RefreshToken!))).RefreshToken);
    }

    [Fact]
    public async Task Equal_canonical_user_ids_in_different_tenants_have_independent_revocation()
    {
        var first = new TokenSubject("tenant-a", "same-user-id");
        var second = new TokenSubject("tenant-b", "same-user-id");
        var a = await _fixture.TokenService.IssueAsync(new(first, []));
        var b = await _fixture.TokenService.IssueAsync(new(second, []));

        await _fixture.TokenService.RevokeSubjectAsync(first);

        Assert.False((await _fixture.TokenService.ValidateAsync(new(a.AccessToken))).Succeeded);
        var refreshed = await _fixture.TokenService.RefreshAsync(new(b.RefreshToken!));
        var principal = (await _fixture.TokenService.ValidateAsync(new(refreshed.AccessToken))).Principal!;
        Assert.Equal("same-user-id", principal.FindFirstValue(IdentityClaimTypes.UserId));
        Assert.Equal("tenant-b", principal.FindFirstValue(IdentityClaimTypes.TenantId));
    }

    [Theory]
    [InlineData("{\"TenantId\":\"tenant-a\",\"Scopes\":[]}")]
    [InlineData("{\"Subject\":{\"TenantId\":\"\",\"SubjectId\":\"user\"},\"Scopes\":[]}")]
    [InlineData("{\"Subject\":{\"TenantId\":\"tenant-b\",\"SubjectId\":\"other-user\"},\"Scopes\":[]}")]
    [InlineData("{\"Subject\":{\"TenantId\":\"tenant-a\",\"SubjectId\":\"user-1\"},\"Scopes\":null}")]
    [InlineData("{")]
    public async Task Refresh_refuses_legacy_malformed_or_mismatched_scope_payloads(string payload)
    {
        var issued = await _fixture.IssueAsync();
        var manager = _fixture.Services.GetRequiredService<IOpenIddictTokenManager>();
        var entry = (global::OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken)
            (await manager.FindByReferenceIdAsync(issued.RefreshToken!))!;
        entry.Payload = payload;
        await manager.UpdateAsync(entry);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _fixture.TokenService.RefreshAsync(new(issued.RefreshToken!)));
    }

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
        var issued = await _fixture.TokenService.IssueAsync(new(new TokenSubject("tenant-a", "canonical-elsa-user"), ["user-tasks:read"]));
        foreach (var accessToken in new[] { issued.AccessToken, (await _fixture.TokenService.RefreshAsync(new(issued.RefreshToken!))).AccessToken })
        {
            var principal = (await _fixture.TokenService.ValidateAsync(new(accessToken))).Principal!;
            Assert.Equal("canonical-elsa-user", Assert.Single(principal.FindAll(IdentityClaimTypes.UserId)).Value);
            Assert.Equal("canonical-elsa-user", principal.FindFirst("sub")!.Value);
        }
        var nonUser = await _fixture.TokenService.IssueAsync(new(new TokenSubject("tenant-a", "external-subject", "provider-a", "https://issuer.example/"), ["user-tasks:read"]));
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

        await expiring.TokenService.RevokeSubjectAsync(new TokenSubject("tenant-a", "user-expired"));

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
