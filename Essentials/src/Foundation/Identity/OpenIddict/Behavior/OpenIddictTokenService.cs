using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation;
using TokenValidationResult = Elsa.Foundation.Identity.Core.Authentication.TokenValidationResult;

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
/// opaque refresh tokens alike. <b>RevokeSubject</b> revokes every entry issued to a subject, which sign-out uses.</item>
/// </list>
/// Tokens are minted directly with the server's <see cref="OpenIddictServerOptions.JsonWebTokenHandler"/> and
/// credentials rather than through the HTTP <c>/connect/token</c> pipeline, because issuance here is a
/// first-party cookie→bearer exchange invoked from an endpoint (Workstream C), not an OAuth grant.
/// </summary>
public sealed class OpenIddictTokenService(
    IOpenIddictTokenManager tokenManager,
    OpenIddictValidationService validationService,
    IOptionsMonitor<OpenIddictServerOptions> serverOptions,
    IOptions<OpenIddictIdentityOptions> options, IAuthenticationSessionStore? sessions = null, ICanonicalUserSignInService? canonical = null) : ITokenService
{
    public async ValueTask<TokenIssueResult> IssueAsync(TokenIssueRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var session = await TokenSessionGuard.ValidateAsync(request.Subject, request.SessionId, sessions, canonical, cancellationToken);
        if (!session.Allowed) throw new InvalidOperationException("The authentication session is unavailable.");
        var accessExpiresAt = now.Add(options.Value.AccessTokenLifetime);
        if (session.ExpiresAt is { } absolute && absolute < accessExpiresAt) accessExpiresAt = absolute;

        var entryId = await CreateTokenEntryAsync(
            TokenSubjectKey.Create(request.Subject), OpenIddictConstants.TokenTypeHints.AccessToken, now, accessExpiresAt,
            referenceId: null, payload: null, cancellationToken);

        var accessToken = CreateAccessTokenJwt(request, entryId, now, accessExpiresAt);
        var refreshToken = await CreateRefreshTokenAsync(request, now, session.ExpiresAt, cancellationToken);

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

        var subjectKey = await tokenManager.GetSubjectAsync(entry, cancellationToken) ?? throw InvalidRefreshToken();
        var payloadJson = await tokenManager.GetPayloadAsync(entry, cancellationToken) ?? throw InvalidRefreshToken();
        RefreshTokenPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<RefreshTokenPayload>(payloadJson) ?? throw InvalidRefreshToken();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw InvalidRefreshToken();
        }
        // Pre-GA unscoped payloads are refused rather than guessed into a tenant/provider scope.
        if (payload.Subject is null || payload.Scopes is null ||
            !string.Equals(subjectKey, TokenSubjectKey.Create(payload.Subject), StringComparison.Ordinal))
            throw InvalidRefreshToken();

        var issued = await IssueAsync(new TokenIssueRequest(payload.Subject, payload.Scopes) { SessionId = payload.SessionId }, cancellationToken);
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

    public async ValueTask RevokeSubjectAsync(TokenSubject subject, CancellationToken cancellationToken = default)
    {
        // Entry by entry, not RevokeBySubjectAsync: that bulk path needs ExecuteUpdate, which the in-memory store
        // (Workbench development/demo mode) refuses. Materialized first so no reader stays open across the updates.
        var entries = new List<object>();
        await foreach (var entry in tokenManager.FindBySubjectAsync(TokenSubjectKey.Create(subject), cancellationToken))
            entries.Add(entry);

        // Live entries only: an expired or no-longer-valid entry is already dead, so it is left unwritten. Filtered
        // here: the untyped token manager has no store-agnostic subject + expiry query (its ListAsync is over object).
        var now = DateTimeOffset.UtcNow;
        var unrevoked = 0;
        foreach (var entry in entries)
        {
            if (await tokenManager.GetExpirationDateAsync(entry, cancellationToken) is { } expiresAt && expiresAt <= now)
                continue;

            // TryRevokeAsync answers false on a non-fatal update failure (a concurrency conflict, for one), leaving
            // the entry valid. Not retried here: the rest are still revoked, then the throw fails the sign-out so
            // the caller can retry the whole revocation with the session intact.
            if (await tokenManager.HasStatusAsync(entry, OpenIddictConstants.Statuses.Valid, cancellationToken) &&
                !await tokenManager.TryRevokeAsync(entry, cancellationToken))
                unrevoked++;
        }

        if (unrevoked > 0)
            throw new InvalidOperationException($"{unrevoked} of the subject's live tokens could not be revoked.");
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
        // Non-user subjects are scoped in the JWT too: consumers that bind an actor through sub must
        // not collapse different providers into one actor. Canonical Elsa users keep their user ID.
        var publicSubject = request.Subject.CanonicalUserId ?? TokenSubjectKey.Create(request.Subject);
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, publicSubject));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, publicSubject));
        identity.AddClaim(new Claim(IdentityClaimTypes.TenantId, request.Subject.TenantId));
        identity.AddClaim(new Claim(IdentityClaimTypes.TokenSubjectKey, TokenSubjectKey.Create(request.Subject)));
        if (request.SessionId is { } sessionId) identity.AddClaim(new(IdentityClaimTypes.SessionId, sessionId));
        if (request.Subject.CanonicalUserId is { } userId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);
            identity.AddClaim(new Claim(IdentityClaimTypes.UserId, userId));
        }
        else
        {
            identity.AddClaim(new Claim(IdentityClaimTypes.SubjectProvider, request.Subject.Provider!));
            identity.AddClaim(new Claim(IdentityClaimTypes.SubjectIssuer, request.Subject.Issuer!));
            identity.AddClaim(new Claim(IdentityClaimTypes.ExternalSubject, request.Subject.SubjectId));
        }
        identity.AddClaim(new Claim(IdentityClaimTypes.Provider, options.Value.ProviderId));

        foreach (var scope in request.Scopes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            identity.AddClaim(new Claim(IdentityClaimTypes.Permission, scope));

        // The token is a trusted provider projection. Emit the normalized marker only after all projected
        // tenant/provider/grant claims have been assembled; validation observes the registered bearer type.
        identity.AddClaim(new Claim(IdentityClaimTypes.Normalized, "v1"));

        return identity;
    }

    private async ValueTask<string> CreateRefreshTokenAsync(TokenIssueRequest request, DateTimeOffset now, DateTimeOffset? sessionExpiresAt, CancellationToken cancellationToken)
    {
        var referenceId = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var payload = JsonSerializer.Serialize(new RefreshTokenPayload(request.Subject, request.Scopes.ToArray(), request.SessionId));
        var expiresAt = now.Add(options.Value.RefreshTokenLifetime);
        if (sessionExpiresAt is { } absolute && absolute < expiresAt) expiresAt = absolute;

        await CreateTokenEntryAsync(
            TokenSubjectKey.Create(request.Subject), OpenIddictConstants.TokenTypeHints.RefreshToken, now, expiresAt,
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

    private sealed record RefreshTokenPayload(TokenSubject? Subject, string[]? Scopes, string? SessionId = null);
}
