using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Models;
using Elsa.Foundation.Agent.Api.Constants;
using Elsa.Foundation.Agent.Api.Models;

namespace Elsa.Foundation.Agent.Api.Endpoints;

internal sealed class GetSession(IAgentSessionService sessions)
    : ElsaEndpoint<AgentSessionRouteRequest, AgentApiResponse<AgentSessionDetailsResponse>>
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
            await Send.ResponseAsync(AgentApiResponse<AgentSessionDetailsResponse>.Failure(error), 404, cancellation: ct);
            return;
        }

        if (!AgentEndpointActor.CanAccess(session.ActorId, session.TenantId, User))
        {
            var error = new AgentError("agent.session.forbidden", "The agent session is not available to the current principal.", 403);
            await Send.ResponseAsync(AgentApiResponse<AgentSessionDetailsResponse>.Failure(error), 403, cancellation: ct);
            return;
        }

        var context = await sessions.ListContextAsync(req.SessionId, ct);
        var messages = await sessions.ListMessagesAsync(req.SessionId, ct);
        await Send.OkAsync(AgentApiResponse<AgentSessionDetailsResponse>.Success(new(
            session.Id,
            session.Status.ToContractString(),
            session.Title,
            context.Select(x => x.ToResponse()).ToList(),
            messages.Select(x => x.ToViewModel()).ToList())), ct);
    }
}
