using System.Security.Claims;
using Elsa.Agent.Api.Endpoints;
using Elsa.Agent.Core;
using Microsoft.AspNetCore.Http;

namespace Elsa.Agent.Api.Authorization;

/// <summary>Reads the current request each time, without retaining or manufacturing a principal.</summary>
internal sealed class HttpAgentPrincipalSource(IHttpContextAccessor accessor) : IAgentPrincipalSource
{
    public ClaimsPrincipal? GetCurrent(string actorId, string tenantId)
    {
        var principal = accessor.HttpContext?.User;
        return principal?.Identity?.IsAuthenticated == true && AgentEndpointActor.CanAccess(actorId, tenantId, principal)
            ? principal
            : null;
    }
}
