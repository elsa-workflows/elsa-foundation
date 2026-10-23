using System.Security.Claims;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Primitives.Exceptions;

namespace Elsa.Workflows.Publishing.Api.Services;

internal static class PublicationRequestTenant
{
    public static string? Resolve(ClaimsPrincipal principal, NormalizedPrincipalValidator validator)
    {
        if (!validator.TryGetNormalizedPrincipal(principal, out var normalizedPrincipal))
            throw new EntityNotFoundException("The publishing identity was not found.");

        var tenantClaims = normalizedPrincipal.FindAll(IdentityClaimTypes.TenantId).ToArray();
        if (tenantClaims.Length > 1 || tenantClaims.Any(claim => string.IsNullOrWhiteSpace(claim.Value)))
            throw new EntityNotFoundException("The publishing tenant identity was not found.");

        // An absent canonical tenant claim is the explicit global-tenant identity.
        return tenantClaims.SingleOrDefault()?.Value;
    }
}
