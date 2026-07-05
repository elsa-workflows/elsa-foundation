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
        var (_, error) = await AgentSessionAuthorization.AuthorizeAsync(sessions, User, req.SessionId, ct);
        if (error is not null)
        {
            await Send.ResponseAsync(AgentApiResponse<AgentTurnCancelResponse>.Failure(error), error.StatusCode, cancellation: ct);
            return;
        }

        var cancelled = turns.Cancel(req.TurnId);
        await Send.OkAsync(AgentApiResponse<AgentTurnCancelResponse>.Success(new(req.TurnId, cancelled)), ct);
    }
}
