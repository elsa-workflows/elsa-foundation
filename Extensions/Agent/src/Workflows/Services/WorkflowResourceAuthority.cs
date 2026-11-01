using Elsa.Agent.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Agent.Workflows.Services;

internal static class WorkflowResourceAuthority
{
    internal static async ValueTask<AgentPolicyDecision> AuthorizeAsync(IEnumerable<IWorkflowDefinitionAuthorizer>? authorizers,
        string actorId, string tenantId, AgentResourceTarget? target, bool manage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(tenantId)
            || target is not { ModuleId: WorkflowAgentSnapshot.ModuleId, ResourceType: WorkflowAgentSnapshot.ResourceType, ResourceId: { Length: > 0 } definitionId })
            return Denied("agent.workflow.resource_required", "A concrete owned workflow definition is required for this tool.");
        var current = authorizers?.ToArray() ?? [];
        if (current.Length != 1)
            return Denied("agent.workflow.authority_unavailable", "Exactly one owning workflow definition authorizer is required.");
        var allowed = manage ? await current[0].CanManageAsync(definitionId, cancellationToken)
            : await current[0].CanReadAsync(definitionId, cancellationToken);
        return allowed ? new(true, []) : Denied("agent.workflow.permission_denied", "The owning workflow definition authority withholds this resource.");
    }

    private static AgentPolicyDecision Denied(string code, string message) => new(false, [new(code, message)]);
}
