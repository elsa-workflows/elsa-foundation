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

public sealed record TokenIssueRequest(string SubjectId, string TenantId, IReadOnlyCollection<string> Scopes);

public sealed record TokenIssueResult(string AccessToken, DateTimeOffset ExpiresAt, string? RefreshToken = null);

public sealed record TokenRefreshRequest(string RefreshToken);

public sealed record TokenRefreshResult(string AccessToken, DateTimeOffset ExpiresAt, string? RefreshToken = null);

public sealed record TokenValidationRequest(string Token);

public sealed record TokenValidationResult(bool Succeeded, ClaimsPrincipal? Principal = null, string? Failure = null);

public sealed record TokenRevocationRequest(string Token, string? Reason = null);
