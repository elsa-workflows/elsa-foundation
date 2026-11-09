using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Ownership;

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

    /// <summary>Revokes every access and refresh token issued to <paramref name="subject"/>, as sign-out requires.</summary>
    ValueTask RevokeSubjectAsync(TokenSubject subject, CancellationToken cancellationToken = default);
}

/// <summary>
/// The identity tokens are stored and revoked under. An Elsa user is one subject per tenant, whichever provider
/// signed it in; a subject that names no Elsa user is one per tenant and provider. So two providers' equal
/// <c>sub</c> values, one <c>sub</c> in two tenants, or a provider <c>sub</c> equal to an Elsa user id never share
/// tokens, and one sign-out, disable or delete revokes one user's tokens only.
/// </summary>
public sealed record TokenSubject
{
    private TokenSubject(string tenantId, string? userId, string? provider, string? providerSubject)
    {
        TenantId = tenantId;
        UserId = userId;
        Provider = provider;
        ProviderSubject = providerSubject;
    }

    public string TenantId { get; }

    /// <summary>The Elsa user, when the subject is one.</summary>
    public string? UserId { get; }

    /// <summary>The provider that authenticated <see cref="ProviderSubject"/>, when the subject names no Elsa user.</summary>
    public string? Provider { get; }

    /// <summary>The provider's own subject, when the subject names no Elsa user.</summary>
    public string? ProviderSubject { get; }

    public static TokenSubject ForUser(string tenantId, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new(tenantId, userId, null, null);
    }

    public static TokenSubject ForProviderSubject(string tenantId, string provider, string providerSubject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSubject);
        return new(tenantId, null, provider, providerSubject);
    }
}

/// <summary>
/// Answers, from the identity provider's current records, whether the Elsa user a token names may still hold
/// tokens. Token refresh asks it before re-issuing, so a user who was disabled or deleted after signing in gets no
/// new access token from a refresh token, and that user's outstanding refresh tokens are revoked. It is the same
/// check password sign-in and cookie validation apply. The default knows no users and refuses every one, so a
/// host that issues user tokens fails closed until the provider that owns the users replaces it. Only a verdict on
/// the user is an answer: a store read failure, or a request whose persistence scope is bound to another tenant,
/// propagates as an exception, so the refresh fails without passing and without revoking anything.
/// </summary>
[ReplacementContract]
public interface IUserTokenEligibility
{
    /// <summary>True when the user exists in <paramref name="tenantId"/> and is not disabled.</summary>
    ValueTask<bool> IsEligibleAsync(string tenantId, string userId, CancellationToken cancellationToken = default);
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
    string? Provider);

public sealed record AuthenticationSessionInvalidationContext(ClaimsPrincipal Principal);

public sealed record PrincipalFactoryContext(
    string TenantId,
    string Provider,
    string ProviderSubject,
    ClaimsPrincipal ExternalPrincipal,
    IReadOnlyCollection<ClaimMappingRule> MappingRules,
    IReadOnlyCollection<Permission> CatalogPermissions);

public sealed record TokenIssueRequest(string SubjectId, string TenantId, IReadOnlyCollection<string> Scopes)
{
    /// <summary>Canonical Elsa user established by the issuing authority; absent for non-user subjects.</summary>
    public string? CanonicalUserId { get; init; }

    /// <summary>
    /// The provider that authenticated <see cref="SubjectId"/>. Required when there is no <see cref="CanonicalUserId"/>:
    /// together with <see cref="TenantId"/> it scopes the <see cref="TokenSubject"/> the tokens are stored under.
    /// </summary>
    public string? Provider { get; init; }
}

public sealed record TokenIssueResult(string AccessToken, DateTimeOffset ExpiresAt, string? RefreshToken = null);

public sealed record TokenRefreshRequest(string RefreshToken);

public sealed record TokenRefreshResult(string AccessToken, DateTimeOffset ExpiresAt, string? RefreshToken = null);

public sealed record TokenValidationRequest(string Token);

public sealed record TokenValidationResult(bool Succeeded, ClaimsPrincipal? Principal = null, string? Failure = null);

public sealed record TokenRevocationRequest(string Token, string? Reason = null);
