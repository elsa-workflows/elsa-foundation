using System.Security.Claims;
using System.Text.Json.Serialization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.Core.Iam;

namespace Elsa.Foundation.Identity.Core.Authentication;

public interface IAuthenticationProviderModule
{
    string ProviderId { get; }

    string DisplayName { get; }

    string Kind { get; }

    ProviderCapabilities Capabilities { get; }

    ValueTask<AuthenticationProviderDescriptor> DescribeAsync(CancellationToken cancellationToken = default);
}

public interface IAuthenticationProviderResolver
{
    ValueTask<IReadOnlyList<AuthenticationProviderDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    ValueTask<AuthenticationProviderDescriptor?> FindAsync(string providerId, string? tenantId = null, bool allowGlobalFallback = false, CancellationToken cancellationToken = default);
}

/// <summary>Live, bounded provider descriptors contributed by a managed connection authority.</summary>
public interface IAuthenticationProviderSource
{
    ValueTask<IReadOnlyList<AuthenticationProviderDescriptor>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Issues the ordinary interactive session for an already admitted canonical user. Server-only input:
/// callers must first validate authentication evidence and admit the identity; never bind this from a request.
/// </summary>
[ReplacementContract]
public interface ICanonicalUserSignInService
{
    string AuthenticationScheme { get; }
    bool SupportsDurableSessions => false;
    /// <summary>Read-only account/session eligibility. Missing support never counts as a recovery path.</summary>
    ValueTask<bool> CanSignInAsync(PrincipalFactoryContext context, bool requiresPassword = false, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    ValueTask<AuthSession?> SignInAsync(PrincipalFactoryContext context, CancellationToken cancellationToken = default);
    /// <summary>Rechecks live account eligibility and the opaque account-version binding of an issued session.</summary>
    ValueTask<bool> ValidateSessionAsync(AuthenticationSessionRecord session, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    /// <summary>Reads and validates in a tenant scope owned by the signed identity, before resource-scope selection.</summary>
    ValueTask<AuthenticationSessionRecord?> ReadSessionAsync(TokenSubject subject, string id, CancellationToken cancellationToken = default) => ValueTask.FromResult<AuthenticationSessionRecord?>(null);
}

public interface IPrincipalFactory
{
    ValueTask<ClaimsPrincipal> CreateAsync(PrincipalFactoryContext context, CancellationToken cancellationToken = default);
}

public interface ITokenService
{
    ValueTask<TokenIssueResult> IssueAsync(TokenIssueRequest request, CancellationToken cancellationToken = default);

    ValueTask<TokenRefreshResult> RefreshAsync(TokenRefreshRequest request, CancellationToken cancellationToken = default);

    ValueTask<TokenValidationResult> ValidateAsync(TokenValidationRequest request, CancellationToken cancellationToken = default);

    ValueTask RevokeAsync(TokenRevocationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Revokes every access and refresh token in the explicit identity scope, as sign-out requires.</summary>
    ValueTask RevokeSubjectAsync(TokenSubject subject, CancellationToken cancellationToken = default);
}

public interface IAuthSessionService
{
    ValueTask<AuthSession> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}

/// <summary>
/// Optionally invalidates server-side authentication state owned by a specific provider before the generic
/// identity API clears that provider's client-side authentication scheme. Providers without server-side
/// session state do not register an implementation.
/// </summary>
public interface IAuthenticationSessionInvalidator
{
    string ProviderId { get; }

    /// <summary>Optional shared interactive scheme; managed logical providers can use the same cookie authority.</summary>
    string? AuthenticationScheme => null;

    ValueTask InvalidateAsync(
        AuthenticationSessionInvalidationContext context,
        CancellationToken cancellationToken = default);
}

public sealed record AuthenticationProviderDescriptor(
    string Id,
    string DisplayName,
    string Kind,
    ProviderCapabilities Capabilities,
    string? TenantId = null,
    bool Enabled = true,
    bool IsDefault = false,
    AuthenticationChallengeMetadata? Challenge = null);

public sealed record AuthenticationChallengeMetadata(
    string Url,
    string Method = "GET",
    string? Scheme = null,
    IReadOnlyDictionary<string, string>? Parameters = null);

public sealed record AuthSession(
    string Status,
    string? Subject,
    string? DisplayName,
    string? TenantId,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> Permissions,
    string TokenFreshness,
    string? Provider)
{
    /// <summary>Server-only binding returned by canonical sign-in; never part of a client session projection.</summary>
    [JsonIgnore]
    public string? AuthenticationSessionId { get; init; }
}

public sealed record AuthenticationSessionInvalidationContext(ClaimsPrincipal Principal);

/// <summary>Projects an already admitted canonical Elsa user; this factory never provisions or imports claims.</summary>
public sealed record PrincipalFactoryContext(string TenantId, string UserId, string? Provider = null)
{
    public ExternalIdentityKey? ExternalIdentity { get; init; }
    public string? ConnectionRecordId { get; init; }
    public string? ConnectionMaterialRevision { get; init; }
    public string? ConnectionCredentialBinding { get; init; }
    public string? ConnectionRedirectUri { get; init; }
}

public sealed record TokenIssueRequest(TokenSubject Subject, IReadOnlyCollection<string> Scopes)
{
    /// <summary>Trusted canonical interactive session; never accepted from an upstream identity claim.</summary>
    public string? SessionId { get; init; }
}

public sealed record TokenIssueResult(string AccessToken, DateTimeOffset ExpiresAt, string? RefreshToken = null);

public sealed record TokenRefreshRequest(string RefreshToken);

public sealed record TokenRefreshResult(string AccessToken, DateTimeOffset ExpiresAt, string? RefreshToken = null);

public sealed record TokenValidationRequest(string Token);

public sealed record TokenValidationResult(bool Succeeded, ClaimsPrincipal? Principal = null, string? Failure = null);

public sealed record TokenRevocationRequest(string Token, string? Reason = null);
