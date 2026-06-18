using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Models;
using Elsa.Foundation.Agent.Api.Constants;
using Elsa.Foundation.Agent.Api.Models;

namespace Elsa.Foundation.Agent.Api.Endpoints;

internal sealed class ApproveProposal(IAgentProposalService proposals)
    : ElsaEndpoint<AgentProposalDecisionRequest, AgentApiResponse<AgentActionProposal>>
{
    public override void Configure()
    {
        Post(AgentRouteConstants.GetRoute("proposals/{proposalId}/approve"));
        ConfigurePermissions(AgentPermissionKeys.Proposals);
    }

    public override async Task HandleAsync(AgentProposalDecisionRequest req, CancellationToken ct)
    {
        var result = await proposals.ApproveAsync(req.ProposalId, AgentEndpointActor.Resolve(User), req.Revision, req.Comment, ct);
        if (!result.Succeeded)
        {
            await Send.ResponseAsync(AgentApiResponse<AgentActionProposal>.Failure(result.Error!), result.Error!.StatusCode, cancellation: ct);
            return;
        }

        await Send.OkAsync(AgentApiResponse<AgentActionProposal>.Success(result.Value!), ct);
    }
}
