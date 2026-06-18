using System.Text.Json;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Models;
using Elsa.Foundation.Agent.Api.Constants;
using Elsa.Foundation.Agent.Api.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Foundation.Agent.Api.Endpoints;

internal sealed class StreamSession(IAgentStreamingService streaming)
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

        await foreach (var item in streaming.StreamAsync(req.SessionId, ct))
        {
            await response.WriteAsync($"data: {JsonSerializer.Serialize(item)}\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
    }
}
