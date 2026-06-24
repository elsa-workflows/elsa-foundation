using System.Text.Json;
using Elsa.Agent.Api.Constants;
using Elsa.Agent.Api.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Elsa.Agent.Api.Endpoints;

internal sealed class StreamSession(IAgentStreamingService streaming, IAgentSessionService sessions, ILogger<StreamSession> logger)
    : ElsaEndpoint<AgentSessionRouteRequest>
{
    public override void Configure()
    {
        Get(AgentRouteConstants.GetRoute("sessions/{sessionId}/stream"));
        ConfigurePermissions(AgentPermissionKeys.Use);
    }

    public override async Task HandleAsync(AgentSessionRouteRequest req, CancellationToken ct)
    {
        var session = await sessions.FindAsync(req.SessionId, ct);
        if (session is null)
        {
            await Send.ResponseAsync(AgentApiResponse<object>.Failure(new("agent.session.not_found", $"Agent session '{req.SessionId}' was not found.", 404)), 404, cancellation: ct);
            return;
        }

        if (!AgentEndpointActor.CanAccess(session.ActorId, session.TenantId, User))
        {
            await Send.ResponseAsync(AgentApiResponse<object>.Failure(new("agent.session.forbidden", "The agent session is not available to the current principal.", 403)), 403, cancellation: ct);
            return;
        }

        var response = HttpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var item in streaming.StreamAsync(req.SessionId, ct))
                await WriteAsync(response, item, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Agent session {SessionId} stream failed.", req.SessionId);
            var error = new AgentError("agent.stream.failed", "The agent stream failed before completion.", 500);
            var latestMessage = await sessions.FindLatestMessageAsync(req.SessionId, AgentRole.User, CancellationToken.None);
            if (latestMessage is { Status: AgentMessageStatus.Pending or AgentMessageStatus.Streaming })
                await sessions.UpdateMessageAsync(req.SessionId, latestMessage.Id, AgentMessageStatus.Failed, error, CancellationToken.None);

            try
            {
                await WriteAsync(response, new AgentStreamEvent(Guid.NewGuid().ToString("N"), AgentStreamEventKind.Error, null, null, error, DateTimeOffset.UtcNow), CancellationToken.None);
            }
            catch (Exception writeEx)
            {
                logger.LogWarning(writeEx, "Agent session {SessionId} stream error event could not be written.", req.SessionId);
            }
        }
    }

    private static async Task WriteAsync(HttpResponse response, AgentStreamEvent item, CancellationToken ct)
    {
        await response.WriteAsync($"data: {JsonSerializer.Serialize(item)}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
