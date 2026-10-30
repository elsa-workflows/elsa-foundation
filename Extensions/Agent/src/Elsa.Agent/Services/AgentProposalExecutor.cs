using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Services;

/// <summary>Routes the stored preparation to the same installed module, with current server authorization.</summary>
internal sealed class DefaultAgentActionProposalExecutor(AgentPreparedInvocationResolver resolver) : IAgentActionProposalExecutor
{
    public async Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default)
    {
        var resolved = await resolver.ResolveAsync(proposal, cancellationToken);
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
        if (proposal.PreparedInvocation is not { ExecutionLocation: AgentExecutionLocation.ClientWorkingState, Client: not null } prepared)
            return AgentResult<AgentProposalExecutionResult>.Failure("agent.proposal.client_unavailable", "This proposal has no bound client completion contract.", 409);
        var resolved = await resolver.ResolveAsync(proposal, cancellationToken);
        if (resolved.Error is not null)
            return AgentResult<AgentProposalExecutionResult>.Success(Uncertain(proposal,
                "A client result was reported after its execution contract, authority or lifetime changed. The effect is unverified and will not be replayed."));
        if (resolved.Tool is not IAgentClientTool clientTool)
            return AgentResult<AgentProposalExecutionResult>.Success(Uncertain(proposal,
                "The installed tool no longer supports validation of the dispatched client operation. Its reported effect remains uncertain and will not be replayed."));
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
