using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Ownership;

namespace Elsa.Foundation.Identity.Api.Models;

public sealed record IdentityBootstrapResponse(
    OwnershipMode OwnershipMode,
    IReadOnlyCollection<IdentityProviderResponse> Providers);

public sealed record IdentityProviderResponse(
    string Id,
    string Kind,
    string DisplayName,
    bool IsDefault,
    bool Enabled,
    AuthenticationChallengeMetadata? Challenge);

public sealed record IdentityCapabilitiesResponse(
    OwnershipMode OwnershipMode,
    IReadOnlyCollection<IdentityProviderCapabilitiesResponse> Providers,
    IReadOnlyCollection<IdentityPermissionResponse> Permissions);

public sealed record IdentityProviderCapabilitiesResponse(
    string Id,
    string Kind,
    string DisplayName,
    bool IsDefault,
    bool Enabled,
    EffectiveProviderCapabilities Capabilities);

/// <summary>
/// A single catalogued permission projected for clients, including feature-contributed host-control
/// permissions. <see cref="Implies"/> lets clients (such as the Studio management bridge) reason about
/// coarse grants like <c>manage</c> implying <c>read</c>.
/// </summary>
public sealed record IdentityPermissionResponse(
    string Key,
    string DisplayName,
    string Category,
    string Description,
    IReadOnlyCollection<string> Implies);

public sealed record RefreshTokenRequest(string RefreshToken);

/// <summary>
/// The <c>GET /_elsa/identity/token</c> response. The Studio client reads <c>accessToken</c> (camelCase);
/// <c>expiresAt</c> is additive.
/// </summary>
public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

public sealed record RevokeTokenRequest(string Token, string? Reason);

public sealed record ProviderRouteRequest
{
    public string Provider { get; init; } = string.Empty;
}
