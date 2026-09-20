using System.Security.Claims;

namespace Elsa.Agent.Api.Endpoints;

public static class AgentEndpointActor
{
    /// <summary>
    /// Resolves the calling principal's actor id from its identity claims, or <c>null</c> when the
    /// principal carries none of the recognized identity claims. A null result must be treated as an
    /// authorization failure by callers — never substituted with a shared default, because multiple
    /// distinct principals would otherwise collide into one identity (issue #374).
    /// </summary>
    public static string? Resolve(ClaimsPrincipal principal)
        => principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? principal.FindFirst("sub")?.Value
           ?? principal.Identity?.Name;

    /// <summary>
    /// Resolves the calling principal's tenant id from its claims, or <c>null</c> when absent. As with
    /// <see cref="Resolve"/>, a null result must fail closed rather than default to a shared tenant.
    /// </summary>
    public static string? ResolveTenant(ClaimsPrincipal principal)
        => principal.FindFirst("elsa.identity.tenant_id")?.Value
           ?? principal.FindFirst("tenant_id")?.Value;

    /// <summary>
    /// Returns whether the principal owns the given actor/tenant. Fails closed: an unresolvable actor
    /// or tenant (missing claims) never matches, so two claim-less principals cannot access each
    /// other's sessions, proposals or audit data.
    /// </summary>
    public static bool CanAccess(string actorId, string tenantId, ClaimsPrincipal principal)
    {
        var resolvedActorId = Resolve(principal);
        var resolvedTenantId = ResolveTenant(principal);

        return resolvedActorId is not null
               && resolvedTenantId is not null
               && string.Equals(actorId, resolvedActorId, StringComparison.Ordinal)
               && string.Equals(tenantId, resolvedTenantId, StringComparison.Ordinal);
    }
}
