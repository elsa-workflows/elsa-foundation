using System.Security.Claims;

namespace Elsa.Workflows.Publishing.Api.Services;

internal static class PublicationRequestTenant
{
    public static string? Resolve(ClaimsPrincipal principal) =>
        principal.FindFirst("elsa.identity.tenant_id")?.Value
        ?? principal.FindFirst("tenant_id")?.Value;
}
