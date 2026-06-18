using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Ownership;

namespace Elsa.Foundation.Identity.Abstractions.Authentication;

public interface IAuthenticationProviderModule
{
    string ProviderId { get; }

    string DisplayName { get; }

    string Kind { get; }

    ProviderCapabilities Capabilities { get; }

    ValueTask<AuthenticationProviderDescriptor> DescribeAsync(CancellationToken cancellationToken = default);
}

public interface IAuthenticationProviderManager
{
    ValueTask<IReadOnlyList<AuthenticationProviderDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    ValueTask<AuthenticationProviderDescriptor?> FindAsync(string providerId, string? tenantId = null, CancellationToken cancellationToken = default);
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

public sealed record AuthenticationProviderDescriptor(
    string Id,
    string DisplayName,
    string Kind,
    ProviderCapabilities Capabilities,
    string? TenantId = null,
    bool Enabled = true,
    bool IsDefault = false);

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

public sealed class DefaultAuthenticationProviderManager(IEnumerable<IAuthenticationProviderModule> modules) : IAuthenticationProviderManager
{
    public async ValueTask<IReadOnlyList<AuthenticationProviderDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        var descriptors = new List<AuthenticationProviderDescriptor>();

        foreach (var module in modules)
            descriptors.Add(await module.DescribeAsync(cancellationToken));

        return descriptors
            .Where(x => x.Enabled)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async ValueTask<AuthenticationProviderDescriptor?> FindAsync(string providerId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var descriptors = await ListAsync(cancellationToken);

        return descriptors
            .Where(x => string.Equals(x.Id, providerId, StringComparison.OrdinalIgnoreCase))
            .Where(x => tenantId is null || x.TenantId is null || string.Equals(x.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => string.Equals(x.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }
}
