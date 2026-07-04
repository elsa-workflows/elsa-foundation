using Elsa.Agent.Api.Constants;
using Elsa.Agent.Api.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Api.Endpoints;

internal sealed class DenyProposal(IAgentProposalService proposals, IAgentSessionService sessions)
    : ElsaEndpoint<AgentProposalDecisionRequest, AgentApiResponse<AgentActionProposal>>
{
    public override void Configure()
    {
        Post(AgentRouteConstants.GetRoute("proposals/{proposalId}/deny"));
        ConfigurePermissions(AgentPermissionKeys.Proposals);
    }

    public override async Task HandleAsync(AgentProposalDecisionRequest req, CancellationToken ct)
    {
        var access = await AgentProposalAuthorization.AuthorizeAsync(proposals, sessions, User, req.ProposalId, ct);
        if (access is not null)
        {
            await Send.ResponseAsync(AgentApiResponse<AgentActionProposal>.Failure(access), access.StatusCode, cancellation: ct);
            return;
        }

        var actorId = AgentEndpointActor.Resolve(User);
        if (actorId is null)
        {
            var error = new AgentError("agent.actor.unresolved", "The current principal does not carry a resolvable actor identity.", 403);
            await Send.ResponseAsync(AgentApiResponse<AgentActionProposal>.Failure(error), 403, cancellation: ct);
            return;
        }

        var result = await proposals.DenyAsync(req.ProposalId, actorId, req.Comment ?? req.Reason, ct);
        if (!result.Succeeded)
        {
            await Send.ResponseAsync(AgentApiResponse<AgentActionProposal>.Failure(result.Error!), result.Error!.StatusCode, cancellation: ct);
            return;
        }

        await Send.OkAsync(AgentApiResponse<AgentActionProposal>.Success(result.Value!), ct);
    }
}
