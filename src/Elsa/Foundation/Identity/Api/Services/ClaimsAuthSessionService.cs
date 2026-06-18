using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Foundation.Identity.Api.Services;

public sealed class ClaimsAuthSessionService : IAuthSessionService
{
    public ValueTask<AuthSession> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return ValueTask.FromResult(new AuthSession("anonymous", null, null, null, new HashSet<string>(), new HashSet<string>(), "none", null));

        var roles = principal.Claims
            .Where(x => x.Type is IdentityClaimTypes.Role or ClaimTypes.Role)
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var permissions = principal.Claims
            .Where(x => x.Type == IdentityClaimTypes.Permission)
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tenantId = principal.FindFirst(IdentityClaimTypes.TenantId)?.Value;
        var provider = principal.FindFirst(IdentityClaimTypes.Provider)?.Value;
        var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
        var displayName = principal.FindFirst(ClaimTypes.Name)?.Value ?? principal.Identity?.Name;

        return ValueTask.FromResult(new AuthSession(
            "authenticated",
            subject,
            displayName,
            tenantId,
            roles,
            permissions,
            "fresh",
            provider));
    }
}
