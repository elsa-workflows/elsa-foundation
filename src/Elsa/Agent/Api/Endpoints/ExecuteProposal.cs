using Elsa.Agent.Api.Constants;
using Elsa.Agent.Api.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Api.Endpoints;

internal sealed class ExecuteProposal(IAgentProposalService proposals, IAgentSessionService sessions)
    : ElsaEndpoint<AgentProposalDecisionRequest, AgentApiResponse<AgentProposalExecutionResult>>
{
    public override void Configure()
    {
        Post(AgentRouteConstants.GetRoute("proposals/{proposalId}/execute"));
        ConfigurePermissions(AgentPermissionKeys.Proposals);
    }

    public override async Task HandleAsync(AgentProposalDecisionRequest req, CancellationToken ct)
    {
        var access = await AuthorizeProposalAsync(req.ProposalId, ct);
        if (access is not null)
        {
            await Send.ResponseAsync(AgentApiResponse<AgentProposalExecutionResult>.Failure(access), access.StatusCode, cancellation: ct);
            return;
        }

        var result = await proposals.ExecuteAsync(req.ProposalId, AgentEndpointActor.Resolve(User), req.Revision, ct);
        if (!result.Succeeded)
        {
            await Send.ResponseAsync(AgentApiResponse<AgentProposalExecutionResult>.Failure(result.Error!), result.Error!.StatusCode, cancellation: ct);
            return;
        }

        await Send.OkAsync(AgentApiResponse<AgentProposalExecutionResult>.Success(result.Value!), ct);
    }

    private async Task<AgentError?> AuthorizeProposalAsync(string proposalId, CancellationToken ct)
    {
        var proposal = await proposals.FindAsync(proposalId, ct);
        if (proposal is null)
            return new("agent.proposal.not_found", $"Proposal '{proposalId}' was not found.", 404);

        var session = await sessions.FindAsync(proposal.SessionId, ct);
        if (session is null)
            return new("agent.session.not_found", $"Agent session '{proposal.SessionId}' was not found.", 404);

        return AgentEndpointActor.CanAccess(session.ActorId, session.TenantId, User)
            ? null
            : new("agent.proposal.forbidden", "The agent proposal is not available to the current principal.", 403);
    }
}
