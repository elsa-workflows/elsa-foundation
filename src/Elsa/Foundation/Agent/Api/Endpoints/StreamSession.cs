using System.Text.Json;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Models;
using Elsa.Foundation.Agent.Api.Constants;
using Elsa.Foundation.Agent.Api.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Foundation.Agent.Api.Endpoints;

internal sealed class StreamSession(IAgentStreamingService streaming, IAgentSessionService sessions)
    : ElsaEndpoint<AgentSessionRouteRequest>
{
    public override void Configure()
    {
        Get(AgentRouteConstants.GetRoute("sessions/{sessionId}/stream"));
        ConfigurePermissions(AgentPermissionKeys.Use);
    }

    public override async Task HandleAsync(AgentSessionRouteRequest req, CancellationToken ct)
    {
        var response = HttpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";

        var session = await sessions.FindAsync(req.SessionId, ct);
        if (session is null)
        {
            await WriteAsync(response, new AgentStreamEvent(Guid.NewGuid().ToString("N"), AgentStreamEventKind.Error, null, null, new("agent.session.not_found", $"Agent session '{req.SessionId}' was not found.", 404), DateTimeOffset.UtcNow), ct);
            return;
        }

        if (!AgentEndpointActor.CanAccess(session.ActorId, session.TenantId, User))
        {
            await WriteAsync(response, new AgentStreamEvent(Guid.NewGuid().ToString("N"), AgentStreamEventKind.Error, null, null, new("agent.session.forbidden", "The agent session is not available to the current principal.", 403), DateTimeOffset.UtcNow), ct);
            return;
        }

        await foreach (var item in streaming.StreamAsync(req.SessionId, ct))
            await WriteAsync(response, item, ct);
    }

    private static async Task WriteAsync(HttpResponse response, AgentStreamEvent item, CancellationToken ct)
    {
        await response.WriteAsync($"data: {JsonSerializer.Serialize(item)}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
