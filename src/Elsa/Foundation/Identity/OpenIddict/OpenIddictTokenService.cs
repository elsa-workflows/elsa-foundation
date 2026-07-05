using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation;
using TokenValidationResult = Elsa.Foundation.Identity.Abstractions.Authentication.TokenValidationResult;

namespace Elsa.Foundation.Identity.OpenIddict;

/// <summary>
/// <see cref="ITokenService"/> over the real OpenIddict pipeline:
/// <list type="bullet">
/// <item><b>Issue</b> mints a signed JWT access token (readable <c>at+jwt</c>, RS256 with the server's
/// signing credentials, carrying sub/tenant/provider/permission claims) plus an opaque, single-use refresh
/// token. Both are backed by OpenIddict token-store entries so they can be revoked and rotated.</item>
/// <item><b>Validate</b> runs OpenIddict's full local validation pipeline (signature, issuer, lifetime, and
/// token-entry status — so revocation takes effect immediately).</item>
/// <item><b>Refresh</b> redeems the stored refresh entry (single use; reuse after redemption or revocation
/// fails) and issues a fresh access/refresh pair.</item>
/// <item><b>Revoke</b> revokes the matching token entry, for access JWTs (via their embedded entry id) and
/// opaque refresh tokens alike.</item>
/// </list>
/// Tokens are minted directly with the server's <see cref="OpenIddictServerOptions.JsonWebTokenHandler"/> and
/// credentials rather than through the HTTP <c>/connect/token</c> pipeline, because issuance here is a
/// first-party cookie→bearer exchange invoked from an endpoint (Workstream C), not an OAuth grant.
/// </summary>
public sealed class OpenIddictTokenService(
    IOpenIddictTokenManager tokenManager,
    OpenIddictValidationService validationService,
    IOptionsMonitor<OpenIddictServerOptions> serverOptions,
    IOptions<OpenIddictIdentityOptions> options) : ITokenService
{
    public async ValueTask<TokenIssueResult> IssueAsync(TokenIssueRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var accessExpiresAt = now.Add(options.Value.AccessTokenLifetime);

        var entryId = await CreateTokenEntryAsync(
            request.SubjectId, OpenIddictConstants.TokenTypeHints.AccessToken, now, accessExpiresAt,
            referenceId: null, payload: null, cancellationToken);

        var accessToken = CreateAccessTokenJwt(request, entryId, now, accessExpiresAt);
        var refreshToken = await CreateRefreshTokenAsync(request, now, cancellationToken);

        return new TokenIssueResult(accessToken, accessExpiresAt, refreshToken);
    }

    public async ValueTask<TokenRefreshResult> RefreshAsync(TokenRefreshRequest request, CancellationToken cancellationToken = default)
    {
        var entry = await tokenManager.FindByReferenceIdAsync(request.RefreshToken, cancellationToken) ?? throw InvalidRefreshToken();

        if (!await tokenManager.HasStatusAsync(entry, OpenIddictConstants.Statuses.Valid, cancellationToken))
            throw InvalidRefreshToken();

        var expiresAt = await tokenManager.GetExpirationDateAsync(entry, cancellationToken);
        if (expiresAt is null || expiresAt <= DateTimeOffset.UtcNow)
            throw InvalidRefreshToken();

        // Rotation: a refresh token is single-use. Redeeming marks the entry, so replaying it fails above.
        if (!await tokenManager.TryRedeemAsync(entry, cancellationToken))
            throw InvalidRefreshToken();

        var subject = await tokenManager.GetSubjectAsync(entry, cancellationToken) ?? throw InvalidRefreshToken();
        var payloadJson = await tokenManager.GetPayloadAsync(entry, cancellationToken) ?? throw InvalidRefreshToken();
        var payload = JsonSerializer.Deserialize<RefreshTokenPayload>(payloadJson) ?? throw InvalidRefreshToken();

        var issued = await IssueAsync(new TokenIssueRequest(subject, payload.TenantId, payload.Scopes), cancellationToken);
        return new TokenRefreshResult(issued.AccessToken, issued.ExpiresAt, issued.RefreshToken);
    }

    public async ValueTask<TokenValidationResult> ValidateAsync(TokenValidationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var principal = await validationService.ValidateAccessTokenAsync(request.Token, cancellationToken);
            return new TokenValidationResult(true, principal);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Deliberately broad (cancellation already rethrown above): this is a fail-closed validation
            // boundary — any non-cancellation failure means the token is not valid. OpenIddict's validation
            // service raises several exception families (protocol, security-token, invalid-operation) by version;
            // narrowing risks converting a clean rejection into a 500 on the bearer path.
            return new TokenValidationResult(false, Failure: exception.Message);
        }
    }

    public async ValueTask RevokeAsync(TokenRevocationRequest request, CancellationToken cancellationToken = default)
    {
        // Opaque refresh tokens are stored by reference id; access JWTs carry their entry id as a claim.
        var entry = await tokenManager.FindByReferenceIdAsync(request.Token, cancellationToken);

        if (entry is null && TryGetTokenEntryId(request.Token, out var entryId))
            entry = await tokenManager.FindByIdAsync(entryId, cancellationToken);

        if (entry is not null)
            await tokenManager.TryRevokeAsync(entry, cancellationToken);
    }

    private string CreateAccessTokenJwt(TokenIssueRequest request, string entryId, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        // Resolving the server options also runs OpenIddict's configuration validation (credentials present).
        var server = serverOptions.CurrentValue;

        var identity = CreateClaimsIdentity(request);
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Private.TokenId, entryId));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.JwtId, Guid.NewGuid().ToString("n")));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = identity,
            Issuer = server.Issuer!.AbsoluteUri,
            IssuedAt = now.UtcDateTime,
            // Keep NotBefore < Expires even when a non-positive lifetime is configured (e.g. expiry tests).
            NotBefore = expiresAt > now ? now.UtcDateTime : expiresAt.AddMinutes(-5).UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = server.SigningCredentials[0],
            TokenType = "at+jwt"
        };

        return server.JsonWebTokenHandler.CreateToken(descriptor);
    }

    private ClaimsIdentity CreateClaimsIdentity(TokenIssueRequest request)
    {
        var identity = new ClaimsIdentity(options.Value.ProviderId);
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, request.SubjectId));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, request.SubjectId));
        identity.AddClaim(new Claim(IdentityClaimTypes.TenantId, request.TenantId));
        identity.AddClaim(new Claim(IdentityClaimTypes.Provider, options.Value.ProviderId));

        foreach (var scope in request.Scopes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            identity.AddClaim(new Claim(IdentityClaimTypes.Permission, scope));

        return identity;
    }

    private async ValueTask<string> CreateRefreshTokenAsync(TokenIssueRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var referenceId = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var payload = JsonSerializer.Serialize(new RefreshTokenPayload(request.TenantId, request.Scopes.ToArray()));

        await CreateTokenEntryAsync(
            request.SubjectId, OpenIddictConstants.TokenTypeHints.RefreshToken, now, now.Add(options.Value.RefreshTokenLifetime),
            referenceId, payload, cancellationToken);

        return referenceId;
    }

    private async ValueTask<string> CreateTokenEntryAsync(
        string subject, string type, DateTimeOffset now, DateTimeOffset expiresAt, string? referenceId, string? payload,
        CancellationToken cancellationToken)
    {
        var entry = await tokenManager.CreateAsync(new OpenIddictTokenDescriptor
        {
            Subject = subject,
            Type = type,
            CreationDate = now,
            ExpirationDate = expiresAt,
            Status = OpenIddictConstants.Statuses.Valid,
            ReferenceId = referenceId,
            Payload = payload
        }, cancellationToken);

        return await tokenManager.GetIdAsync(entry, cancellationToken)
            ?? throw new InvalidOperationException("The created token entry has no identifier.");
    }

    private static bool TryGetTokenEntryId(string token, out string entryId)
    {
        try
        {
            var jwt = new JsonWebToken(token);
            if (jwt.TryGetPayloadValue<string>(OpenIddictConstants.Claims.Private.TokenId, out var id) && !string.IsNullOrEmpty(id))
            {
                entryId = id;
                return true;
            }
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            // Not a JWT (malformed/null) — fall through.
        }

        entryId = string.Empty;
        return false;
    }

    private static InvalidOperationException InvalidRefreshToken() => new("Refresh token is invalid or expired.");

    private sealed record RefreshTokenPayload(string TenantId, string[] Scopes);
}
