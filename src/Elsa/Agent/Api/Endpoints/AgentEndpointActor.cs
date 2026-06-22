using System.Security.Claims;

namespace Elsa.Agent.Api.Endpoints;

public static class AgentEndpointActor
{
    public const string DefaultActorId = "anonymous";
    public const string DefaultTenantId = "default";

    public static string Resolve(ClaimsPrincipal principal)
        => principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? principal.FindFirst("sub")?.Value
           ?? principal.Identity?.Name
           ?? DefaultActorId;

    public static string ResolveTenant(ClaimsPrincipal principal)
        => principal.FindFirst("elsa.identity.tenant_id")?.Value
           ?? principal.FindFirst("tenant_id")?.Value
           ?? DefaultTenantId;

    public static bool CanAccess(string actorId, string tenantId, ClaimsPrincipal principal)
        => string.Equals(actorId, Resolve(principal), StringComparison.Ordinal)
           && string.Equals(tenantId, ResolveTenant(principal), StringComparison.Ordinal);
}
