using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Services;

/// <summary>Rechecks a frozen preparation without executing a module or custom proposal executor.</summary>
internal sealed class AgentPreparedInvocationResolver(
    IAgentToolRegistry registry,
    IAgentSessionService sessions,
    IAgentToolAuthorizer authorizer,
    IAgentPolicyEvaluator policyEvaluator,
    TimeProvider clock)
{
    internal async Task<(IAgentTool? Tool, AgentError? Error)> ResolveAsync(AgentActionProposal proposal, CancellationToken cancellationToken)
    {
        var prepared = proposal.PreparedInvocation;
        if (prepared is null)
            return (null, new("agent.proposal.executor_unavailable", "This proposal has no concrete prepared execution contract.", 409));
        if (prepared.ExpiresAt <= clock.GetUtcNow())
            return (null, new("agent.proposal.expired", "The prepared operation has expired.", 409));
        if (!Enum.IsDefined(prepared.Risk) || !Enum.IsDefined(prepared.ExecutionLocation))
            return (null, new("agent.proposal.binding_invalid", "The stored execution contract is invalid.", 409));
        if (prepared.SessionId != proposal.SessionId || prepared.ModuleId != prepared.Target?.ModuleId
            || prepared.RequiresApproval != proposal.RequiresApproval
            || prepared.RequiresApproval && (proposal.ApprovedAt is null || proposal.ApprovedBy != prepared.ActorId)
            || !AgentInvocationIntegrity.SameTarget(prepared.Target, new(prepared.ModuleId, proposal.ResourceType ?? string.Empty, proposal.ResourceId, proposal.BaseRevision))
            || AgentInvocationIntegrity.PreparedDigest(prepared) != prepared.ApprovalDigest)
            return (null, new("agent.proposal.binding_invalid", "The stored proposal does not match its immutable prepared operation.", 409));
        var session = await sessions.FindAsync(proposal.SessionId, cancellationToken);
        if (session is null || session.ActorId != prepared.ActorId || session.TenantId != prepared.TenantId
            || session.Status != AgentSessionStatus.Active || session.ExpiresAt is { } expires && expires <= clock.GetUtcNow())
            return (null, new("agent.proposal.forbidden", "The prepared session actor or tenant is no longer available.", 403));
        if (!(await policyEvaluator.EvaluateAvailabilityAsync(session.Policy, cancellationToken)).Allowed)
            return (null, new("agent.disabled", "Current host policy disables this operation.", 403));
        if (session.Policy.RequiresApprovalForMutations && (proposal.ApprovedAt is null || proposal.ApprovedBy != session.ActorId))
            return (null, new("agent.proposal.approval_required", "Current session policy requires approval of the exact operation.", 409));
        var tool = await registry.FindAsync(prepared.ToolName, cancellationToken);
        if (tool is null || tool.Descriptor.ModuleId != prepared.ModuleId || tool.Descriptor.ContractVersion != prepared.ToolContractVersion
            || tool.Descriptor.ExecutionLocation != prepared.ExecutionLocation || tool.Descriptor.Mutability != AgentToolMutability.Mutating)
            return (null, new("agent.proposal.executor_unavailable", "The prepared tool is no longer installed with its reviewed contract.", 409));
        if (prepared.ExecutionLocation == AgentExecutionLocation.ClientWorkingState
            && (prepared.Client is null || !proposal.RequiresApproval || proposal.ApprovedAt is null || tool is not IAgentClientTool))
            return (null, new("agent.proposal.client_unavailable", "The reviewed originating client execution contract is no longer available.", 409));
        if (tool.Descriptor.Risk > prepared.Risk)
            return (null, new("agent.proposal.contract_changed", "The operation's current risk requires a new review.", 409));
        if (session.Policy.DeniedCapabilityIds.Contains(tool.Descriptor.Name, StringComparer.OrdinalIgnoreCase))
            return (null, new("agent.tool.denied", "Current host policy disables this tool.", 403));
        if (!(await authorizer.AuthorizeAsync(tool.Descriptor, session.ActorId, session.TenantId, prepared.Target, cancellationToken)).Allowed)
            return (null, new("agent.proposal.forbidden", "Current server authority denies the prepared target.", 403));
        return (tool, null);
    }

}
