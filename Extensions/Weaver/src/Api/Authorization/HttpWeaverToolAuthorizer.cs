using Elsa.Weaver.Api.Endpoints;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.AspNetCore.Http;

namespace Elsa.Weaver.Api.Authorization;

/// <summary>Admits tools under the same normalized current principal and permission service as domain endpoints.</summary>
internal sealed class HttpWeaverToolAuthorizer(
    IHttpContextAccessor accessor,
    IEnumerable<IPermissionAuthorizationService> authorizationServices) : IWeaverToolAuthorizer
{
    public async Task<WeaverPolicyDecision> AuthorizeAsync(WeaverToolDescriptor tool, string actorId, string tenantId,
        WeaverResourceTarget? target, CancellationToken cancellationToken = default)
    {
        var context = accessor.HttpContext;
        if (context is null || !WeaverEndpointActor.CanAccess(actorId, tenantId, context.User))
            return Denied("weaver.tool.actor_denied", "The current authenticated actor and tenant do not own this invocation.");
        if (target is not null && target.ModuleId != tool.ModuleId)
            return Denied("weaver.tool.owner_mismatch", "The tool does not own the prepared resource target.");
        var authorities = authorizationServices.ToList();
        if (authorities.Count != 1)
            return Denied("weaver.tool.authorization_unavailable", "Exactly one current permission authorization service is required.");
        foreach (var permission in tool.RequiredPermissions)
        {
            var result = await authorities[0].AuthorizeAsync(new(context.User, permission, tenantId, target), cancellationToken);
            if (!result.Succeeded)
                return Denied("weaver.tool.permission_denied", "The current principal lacks a required permission for this resource.");
        }
        return new(true, []);
    }

    private static WeaverPolicyDecision Denied(string code, string message) => new(false, [new(code, message)]);
}
