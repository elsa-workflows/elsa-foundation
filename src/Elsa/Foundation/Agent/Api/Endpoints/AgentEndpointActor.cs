using System.Security.Claims;

namespace Elsa.Foundation.Agent.Api.Endpoints;

internal static class AgentEndpointActor
{
    public static string Resolve(ClaimsPrincipal principal)
        => principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? principal.FindFirst("sub")?.Value
           ?? principal.Identity?.Name
           ?? "anonymous";
}
