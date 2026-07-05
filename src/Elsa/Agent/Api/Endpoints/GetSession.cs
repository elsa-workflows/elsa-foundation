using Elsa.Agent.Api.Constants;
using Elsa.Agent.Api.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Api.Endpoints;

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
        var (session, error) = await AgentSessionAuthorization.AuthorizeAsync(sessions, User, req.SessionId, ct);
        if (error is not null)
        {
            await Send.ResponseAsync(AgentApiResponse<AgentSessionDetailsResponse>.Failure(error), error.StatusCode, cancellation: ct);
            return;
        }

        var context = await sessions.ListContextAsync(req.SessionId, ct);
        var messages = await sessions.ListMessagesAsync(req.SessionId, ct);
        await Send.OkAsync(AgentApiResponse<AgentSessionDetailsResponse>.Success(new(
            session!.Id,
            session.Status.ToContractString(),
            session.Title,
            context.Select(x => x.ToResponse()).ToList(),
            messages.Select(x => x.ToViewModel()).ToList())), ct);
    }
}
