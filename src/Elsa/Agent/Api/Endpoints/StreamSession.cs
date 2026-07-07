using System.Text.Json;
using Elsa.Agent.Api.Constants;
using Elsa.Agent.Api.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Microsoft.AspNetCore.Http;

namespace Elsa.Agent.Api.Endpoints;

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

        var (_, error) = await AgentSessionAuthorization.AuthorizeAsync(sessions, User, req.SessionId, ct);
        if (error is not null)
        {
            await WriteAsync(response, new AgentStreamEvent(AgentProviderPrimitives.NewId(), AgentStreamEventKind.Error, null, null, error, DateTimeOffset.UtcNow), ct);
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
