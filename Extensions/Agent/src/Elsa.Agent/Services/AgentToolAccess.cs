using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Services;

/// <summary>Host authority is required; an installed tool may add its owner's current resource rules.</summary>
internal static class AgentToolAccess
{
    internal static async ValueTask<AgentPolicyDecision> AuthorizeAsync(IAgentTool tool, IAgentToolAuthorizer host,
        string actorId, string tenantId, AgentResourceTarget? target, CancellationToken cancellationToken)
    {
        var admission = await host.AuthorizeAsync(tool.Descriptor, actorId, tenantId, target, cancellationToken);
        if (!admission.Allowed || tool is not IAgentToolAuthorization owner) return admission;
        var owned = await owner.AuthorizeAsync(actorId, tenantId, target, cancellationToken);
        return new(owned.Allowed, admission.Violations.Concat(owned.Violations).ToArray());
    }
}
