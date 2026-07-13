using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Secrets.Api.Endpoints.Secrets;

internal static class SecretEndpointTenant
{
    public static string? Resolve(ClaimsPrincipal principal)
    {
        var tenantId = principal.FindFirst(IdentityClaimTypes.TenantId)?.Value;
        return string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;
    }
}
