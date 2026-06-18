using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Models;
using Elsa.Foundation.Agent.Api.Constants;
using Elsa.Foundation.Agent.Api.Models;

namespace Elsa.Foundation.Agent.Api.Endpoints;

internal sealed class Feedback(IAgentFeedbackService feedback, IAgentSessionService sessions)
    : ElsaEndpoint<AgentFeedbackApiRequest, AgentApiResponse<AgentFeedback>>
{
    public override void Configure()
    {
        Post(AgentRouteConstants.GetRoute("feedback"));
        ConfigurePermissions(AgentPermissionKeys.Use);
    }

    public override async Task HandleAsync(AgentFeedbackApiRequest req, CancellationToken ct)
    {
        var session = await sessions.FindAsync(req.SessionId, ct);
        if (session is null)
        {
            var error = new AgentError("agent.session.not_found", $"Agent session '{req.SessionId}' was not found.", 404);
            await Send.ResponseAsync(AgentApiResponse<AgentFeedback>.Failure(error), 404, cancellation: ct);
            return;
        }

        if (!string.IsNullOrWhiteSpace(req.MessageId) && await sessions.FindMessageAsync(req.SessionId, req.MessageId, ct) is null)
        {
            var error = new AgentError("agent.message.not_found", $"Agent message '{req.MessageId}' was not found in session '{req.SessionId}'.", 404);
            await Send.ResponseAsync(AgentApiResponse<AgentFeedback>.Failure(error), 404, cancellation: ct);
            return;
        }

        var item = new AgentFeedback(
            Guid.NewGuid().ToString("N"),
            req.SessionId,
            req.MessageId,
            req.Rating > 0 ? "positive" : "negative",
            req.Comment,
            AgentEndpointActor.Resolve(User),
            DateTimeOffset.UtcNow);

        await Send.OkAsync(AgentApiResponse<AgentFeedback>.Success(await feedback.AddAsync(item, ct)), ct);
    }
}
