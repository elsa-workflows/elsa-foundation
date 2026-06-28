using Elsa.Agent.Api.Constants;
using Elsa.Agent.Api.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Api.Endpoints;

internal sealed class CancelTurn(IAgentTurnRegistry turns, IAgentSessionService sessions)
    : ElsaEndpoint<AgentTurnCancelRequest, AgentApiResponse<AgentTurnCancelResponse>>
{
    public override void Configure()
    {
        Post(AgentRouteConstants.GetRoute("sessions/{sessionId}/turns/{turnId}/cancel"));
        ConfigurePermissions(AgentPermissionKeys.Use);
    }

    public override async Task HandleAsync(AgentTurnCancelRequest req, CancellationToken ct)
    {
        var session = await sessions.FindAsync(req.SessionId, ct);
        if (session is null)
        {
            await Send.ResponseAsync(AgentApiResponse<AgentTurnCancelResponse>.Failure(new("agent.session.not_found", $"Agent session '{req.SessionId}' was not found.", 404)), 404, cancellation: ct);
            return;
        }

        if (!AgentEndpointActor.CanAccess(session.ActorId, session.TenantId, User))
        {
            await Send.ResponseAsync(AgentApiResponse<AgentTurnCancelResponse>.Failure(new("agent.session.forbidden", "The agent session is not available to the current principal.", 403)), 403, cancellation: ct);
            return;
        }

        var cancelled = turns.Cancel(req.TurnId);
        await Send.OkAsync(AgentApiResponse<AgentTurnCancelResponse>.Success(new(req.TurnId, cancelled)), ct);
    }
}
