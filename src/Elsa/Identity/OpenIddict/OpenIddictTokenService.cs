using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Elsa.Foundation.Identity.OpenIddict;

public sealed class OpenIddictTokenService(IOptions<OpenIddictIdentityOptions> options) : ITokenService
{
    private readonly ConcurrentDictionary<string, StoredToken> _accessTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StoredToken> _refreshTokens = new(StringComparer.Ordinal);

    public ValueTask<TokenIssueResult> IssueAsync(TokenIssueRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var accessToken = CreateToken();
        var refreshToken = CreateToken();
        var principal = CreatePrincipal(request);
        var accessExpiresAt = now.Add(options.Value.AccessTokenLifetime);
        var refreshExpiresAt = now.Add(options.Value.RefreshTokenLifetime);

        _accessTokens[accessToken] = new(principal, accessExpiresAt, Revoked: false);
        _refreshTokens[refreshToken] = new(principal, refreshExpiresAt, Revoked: false);

        return ValueTask.FromResult(new TokenIssueResult(accessToken, accessExpiresAt, refreshToken));
    }

    public ValueTask<TokenRefreshResult> RefreshAsync(TokenRefreshRequest request, CancellationToken cancellationToken = default)
    {
        if (!_refreshTokens.TryGetValue(request.RefreshToken, out var storedToken) || storedToken.Revoked || storedToken.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Refresh token is invalid or expired.");

        var accessToken = CreateToken();
        var refreshToken = CreateToken();
        var accessExpiresAt = DateTimeOffset.UtcNow.Add(options.Value.AccessTokenLifetime);
        var refreshExpiresAt = DateTimeOffset.UtcNow.Add(options.Value.RefreshTokenLifetime);

        _refreshTokens[request.RefreshToken] = storedToken with { Revoked = true };
        _accessTokens[accessToken] = new(storedToken.Principal, accessExpiresAt, Revoked: false);
        _refreshTokens[refreshToken] = new(storedToken.Principal, refreshExpiresAt, Revoked: false);

        return ValueTask.FromResult(new TokenRefreshResult(accessToken, accessExpiresAt, refreshToken));
    }

    public ValueTask<TokenValidationResult> ValidateAsync(TokenValidationRequest request, CancellationToken cancellationToken = default)
    {
        if (!_accessTokens.TryGetValue(request.Token, out var storedToken))
            return ValueTask.FromResult(new TokenValidationResult(false, Failure: "Token was not issued by this provider."));

        if (storedToken.Revoked)
            return ValueTask.FromResult(new TokenValidationResult(false, Failure: "Token has been revoked."));

        if (storedToken.ExpiresAt <= DateTimeOffset.UtcNow)
            return ValueTask.FromResult(new TokenValidationResult(false, Failure: "Token has expired."));

        return ValueTask.FromResult(new TokenValidationResult(true, storedToken.Principal));
    }

    public ValueTask RevokeAsync(TokenRevocationRequest request, CancellationToken cancellationToken = default)
    {
        if (_accessTokens.TryGetValue(request.Token, out var accessToken))
            _accessTokens[request.Token] = accessToken with { Revoked = true };

        if (_refreshTokens.TryGetValue(request.Token, out var refreshToken))
            _refreshTokens[request.Token] = refreshToken with { Revoked = true };

        return ValueTask.CompletedTask;
    }

    private ClaimsPrincipal CreatePrincipal(TokenIssueRequest request)
    {
        var identity = new ClaimsIdentity(options.Value.ProviderId);
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, request.SubjectId));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, request.SubjectId));
        identity.AddClaim(new Claim(IdentityClaimTypes.TenantId, request.TenantId));
        identity.AddClaim(new Claim(IdentityClaimTypes.Provider, options.Value.ProviderId));

        foreach (var scope in request.Scopes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            identity.AddClaim(new Claim(IdentityClaimTypes.Permission, scope));

        return new ClaimsPrincipal(identity);
    }

    private static string CreateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private sealed record StoredToken(ClaimsPrincipal Principal, DateTimeOffset ExpiresAt, bool Revoked);
}
