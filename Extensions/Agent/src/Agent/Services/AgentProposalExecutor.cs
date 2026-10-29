using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Services;

/// <summary>Routes the stored preparation to the same installed module, with current server authorization.</summary>
internal sealed class DefaultAgentActionProposalExecutor(
    IAgentToolRegistry registry,
    IAgentSessionService sessions,
    IAgentToolAuthorizer authorizer,
    IAgentPolicyEvaluator policyEvaluator,
    TimeProvider clock) : IAgentActionProposalExecutor
{
    public async Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(proposal, cancellationToken);
        if (resolved.Error is { } error)
            return AgentResult<AgentProposalExecutionResult>.Failure(error.Code, error.Message, error.StatusCode);
        var prepared = proposal.PreparedInvocation!;
        if (prepared.ExecutionLocation == AgentExecutionLocation.ClientWorkingState)
        {
            if (!proposal.RequiresApproval || proposal.ApprovedAt is null || prepared.Client is null || resolved.Tool is not IAgentClientTool)
                return AgentResult<AgentProposalExecutionResult>.Failure("agent.proposal.client_unavailable", "No reviewed originating client execution contract is available.", 409);
            return AgentResult<AgentProposalExecutionResult>.Success(Result(proposal, false,
                "Waiting for the originating view to confirm the change.", "awaiting-client") with { ClientInvocation = prepared });
        }

        var execution = await resolved.Tool!.ExecuteAsync(prepared, cancellationToken);
        if (!execution.Succeeded || execution.Value is null)
            return AgentResult<AgentProposalExecutionResult>.Failure(execution.Error?.Code ?? "agent.proposal.refused",
                execution.Error?.Message ?? "The owning module refused application.", execution.Error?.StatusCode ?? 409);
        if (!AgentInvocationIntegrity.ValidReceipt(prepared, execution.Value, AgentEffectScope.ServerPersisted))
            return AgentResult<AgentProposalExecutionResult>.Success(Uncertain(proposal,
                "The module returned without a valid owning-resource effect receipt. Its outcome is uncertain; the operation will not be replayed."));
        var result = FreezeResult(execution.Value);
        return AgentResult<AgentProposalExecutionResult>.Success(Result(proposal, true, result.Content, "executed") with { ToolResult = result });
    }

    public async Task<AgentResult<AgentProposalExecutionResult>> CompleteClientAsync(AgentActionProposal proposal,
        AgentClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(proposal, cancellationToken);
        if (resolved.Error is not null)
            return AgentResult<AgentProposalExecutionResult>.Success(Uncertain(proposal,
                "A client result was reported after its execution contract, authority or lifetime changed. The effect is unverified and will not be replayed."));
        var prepared = proposal.PreparedInvocation!;
        if (prepared.ExecutionLocation != AgentExecutionLocation.ClientWorkingState || resolved.Tool is not IAgentClientTool clientTool)
            return AgentResult<AgentProposalExecutionResult>.Failure("agent.proposal.client_unavailable", "The installed tool does not support this client completion.", 409);
        if (acknowledgement.Outcome == AgentClientOutcome.Uncertain)
            return AgentResult<AgentProposalExecutionResult>.Success(Uncertain(proposal,
                "The originating client could not establish whether the operation completed. It will not be replayed."));
        if (acknowledgement.Outcome == AgentClientOutcome.Refused)
        {
            var refused = LifecycleResult(prepared, "refused", "The originating client refused the operation before application.");
            return AgentResult<AgentProposalExecutionResult>.Success(Result(proposal, false, refused.Content, "refused") with { ToolResult = refused });
        }
        var completed = await clientTool.CompleteAsync(prepared, acknowledgement, cancellationToken);
        if (!completed.Succeeded || completed.Value is null || !AgentInvocationIntegrity.ValidReceipt(prepared, completed.Value, AgentEffectScope.ClientWorkingState))
            return AgentResult<AgentProposalExecutionResult>.Success(Uncertain(proposal,
                "The reported client effect did not validate against the exact prepared operation. Its outcome is uncertain and will not be replayed."));
        var result = FreezeResult(completed.Value);
        return AgentResult<AgentProposalExecutionResult>.Success(Result(proposal, true, result.Content, "executed") with { ToolResult = result });
    }

    private async Task<(IAgentTool? Tool, AgentError? Error)> ResolveAsync(AgentActionProposal proposal, CancellationToken cancellationToken)
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
        if (tool.Descriptor.Risk > prepared.Risk)
            return (null, new("agent.proposal.contract_changed", "The operation's current risk requires a new review.", 409));
        if (session.Policy.DeniedCapabilityIds.Contains(tool.Descriptor.Name, StringComparer.OrdinalIgnoreCase))
            return (null, new("agent.tool.denied", "Current host policy disables this tool.", 403));
        if (!(await authorizer.AuthorizeAsync(tool.Descriptor, session.ActorId, session.TenantId, prepared.Target, cancellationToken)).Allowed)
            return (null, new("agent.proposal.forbidden", "Current server authority denies the prepared target.", 403));
        return (tool, null);
    }

    internal static AgentProposalExecutionResult Result(AgentActionProposal proposal, bool executed, string summary, string status) => new(
        proposal.Id, executed, proposal.PreparedInvocation?.Target?.ResourceType ?? proposal.ResourceType ?? "unavailable",
        proposal.PreparedInvocation?.Target?.ResourceId ?? proposal.ResourceId ?? string.Empty, summary) { Status = status };

    internal static AgentProposalExecutionResult Uncertain(AgentActionProposal proposal, string summary)
        => Result(proposal, false, summary, "uncertain") with
        {
            ToolResult = proposal.PreparedInvocation is { } prepared ? LifecycleResult(prepared, "uncertain", summary) : null
        };

    internal static AgentToolResult LifecycleResult(AgentPreparedToolInvocation prepared, string status, string summary)
        => new(prepared.ToolCallId, prepared.ToolName, false, summary,
            new("agent.operation-outcome", "1", JsonSerializer.SerializeToElement(new { status, prepared.InvocationId })));

    private static AgentToolResult FreezeResult(AgentToolResult value) => value with
    {
        Payload = value.Payload is null ? null : AgentInvocationIntegrity.Freeze(value.Payload),
        Receipt = value.Receipt is null ? null : value.Receipt with { Evidence = AgentInvocationIntegrity.Freeze(value.Receipt.Evidence) }
    };
}
