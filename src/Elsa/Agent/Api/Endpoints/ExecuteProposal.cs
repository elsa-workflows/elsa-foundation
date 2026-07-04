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
        var access = await AgentProposalAuthorization.AuthorizeAsync(proposals, sessions, User, req.ProposalId, ct);
        if (access is not null)
        {
            await Send.ResponseAsync(AgentApiResponse<AgentProposalExecutionResult>.Failure(access), access.StatusCode, cancellation: ct);
            return;
        }

        var actorId = AgentEndpointActor.Resolve(User);
        if (actorId is null)
        {
            var error = new AgentError("agent.actor.unresolved", "The current principal does not carry a resolvable actor identity.", 403);
            await Send.ResponseAsync(AgentApiResponse<AgentProposalExecutionResult>.Failure(error), 403, cancellation: ct);
            return;
        }

        var result = await proposals.ExecuteAsync(req.ProposalId, actorId, req.Revision, ct);
        if (!result.Succeeded)
        {
            await Send.ResponseAsync(AgentApiResponse<AgentProposalExecutionResult>.Failure(result.Error!), result.Error!.StatusCode, cancellation: ct);
            return;
        }

        await Send.OkAsync(AgentApiResponse<AgentProposalExecutionResult>.Success(result.Value!), ct);
    }
}
