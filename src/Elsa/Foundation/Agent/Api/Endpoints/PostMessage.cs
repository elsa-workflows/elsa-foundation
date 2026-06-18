using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Models;
using Elsa.Foundation.Agent.Api.Constants;
using Elsa.Foundation.Agent.Api.Models;

namespace Elsa.Foundation.Agent.Api.Endpoints;

internal sealed class PostMessage(
    IAgentSessionService sessions,
    IAgentPolicyEvaluator policyEvaluator,
    IAgentAuditSink auditSink)
    : ElsaEndpoint<AgentMessageRequest, AgentApiResponse<AgentMessageAcceptedResponse>>
{
    public override void Configure()
    {
        Post(AgentRouteConstants.GetRoute("sessions/{sessionId}/messages"));
        ConfigurePermissions(AgentPermissionKeys.Use);
    }

    public override async Task HandleAsync(AgentMessageRequest req, CancellationToken ct)
    {
        var session = await sessions.FindAsync(req.SessionId, ct);
        if (session is null)
        {
            var error = new AgentError("agent.session.not_found", $"Agent session '{req.SessionId}' was not found.", 404);
            await Send.ResponseAsync(AgentApiResponse<AgentMessageAcceptedResponse>.Failure(error), 404, cancellation: ct);
            return;
        }

        var availability = await policyEvaluator.EvaluateAvailabilityAsync(session.Policy, ct);
        if (!availability.Allowed)
        {
            await SendDeniedAsync(req.SessionId, availability, ct);
            return;
        }

        var requestedCapabilities = req.RequestedCapabilities.Count > 0
            ? req.RequestedCapabilities
            : string.IsNullOrWhiteSpace(req.CapabilityId) ? [] : [req.CapabilityId];

        foreach (var capabilityId in requestedCapabilities)
        {
            var capabilityDecision = await policyEvaluator.EvaluateCapabilityAsync(session.Policy, capabilityId, ct);
            if (!capabilityDecision.Allowed)
            {
                await SendDeniedAsync(req.SessionId, capabilityDecision, ct);
                return;
            }
        }

        var message = await sessions.AddMessageAsync(req.SessionId, new(
            AgentRole.User,
            string.IsNullOrWhiteSpace(req.Content) ? req.Message : req.Content,
            AgentMessageStatus.Pending,
            requestedCapabilities.FirstOrDefault(),
            req.ContextAttachmentIds.Count > 0 ? req.ContextAttachmentIds : req.ContextAttachments.Select(x => x.Id).ToList(),
            req.ContextAttachments.Select(x => x.ToDomain()).ToList()), ct);

        await Send.OkAsync(AgentApiResponse<AgentMessageAcceptedResponse>.Success(new(message, [])), ct);
    }

    private async Task SendDeniedAsync(string sessionId, AgentPolicyDecision decision, CancellationToken ct)
    {
        await auditSink.EmitAsync(new(
            Guid.NewGuid().ToString("N"),
            AgentAuditEventKind.ContextDenied,
            sessionId,
            null,
            "Agent request denied by policy.",
            DateTimeOffset.UtcNow,
            decision.Violations.Select((x, i) => new { Key = $"{i}:{x.Code}", x.Message }).ToDictionary(x => x.Key, x => x.Message)), ct);

        var message = string.Join(" ", decision.Violations.Select(x => x.Message));
        await Send.ResponseAsync(
            AgentApiResponse<AgentMessageAcceptedResponse>.Failure(new("agent.policyDenied", message, 403)),
            403,
            cancellation: ct);
    }
}
