using System.Security.Claims;
using Elsa.Weaver.Api.Endpoints;
using Elsa.Weaver.Core;
using Microsoft.AspNetCore.Http;

namespace Elsa.Weaver.Api.Authorization;

/// <summary>Reads the current request each time, without retaining or manufacturing a principal.</summary>
internal sealed class HttpWeaverPrincipalSource(IHttpContextAccessor accessor) : IWeaverPrincipalSource
{
    public ClaimsPrincipal? GetCurrent(string actorId, string tenantId)
    {
        var principal = accessor.HttpContext?.User;
        return principal?.Identity?.IsAuthenticated == true && WeaverEndpointActor.CanAccess(actorId, tenantId, principal)
            ? principal
            : null;
    }
}
