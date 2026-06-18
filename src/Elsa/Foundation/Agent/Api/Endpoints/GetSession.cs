using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Models;
using Elsa.Foundation.Agent.Api.Constants;
using Elsa.Foundation.Agent.Api.Models;

namespace Elsa.Foundation.Agent.Api.Endpoints;

internal sealed class GetSession(IAgentSessionService sessions)
    : ElsaEndpoint<AgentSessionRouteRequest, AgentApiResponse<AgentSession>>
{
    public override void Configure()
    {
        Get(AgentRouteConstants.GetRoute("sessions/{sessionId}"));
        ConfigurePermissions(AgentPermissionKeys.Use);
    }

    public override async Task HandleAsync(AgentSessionRouteRequest req, CancellationToken ct)
    {
        var session = await sessions.FindAsync(req.SessionId, ct);
        if (session is null)
        {
            var error = new AgentError("agent.session.not_found", $"Agent session '{req.SessionId}' was not found.", 404);
            await Send.ResponseAsync(AgentApiResponse<AgentSession>.Failure(error), 404, cancellation: ct);
            return;
        }

        await Send.OkAsync(AgentApiResponse<AgentSession>.Success(session), ct);
    }
}
