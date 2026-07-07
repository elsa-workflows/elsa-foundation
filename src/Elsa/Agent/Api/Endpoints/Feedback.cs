using Elsa.Agent.Api.Constants;
using Elsa.Agent.Api.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;

namespace Elsa.Agent.Api.Endpoints;

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
        var (_, authError) = await AgentSessionAuthorization.AuthorizeAsync(sessions, User, req.SessionId, ct);
        if (authError is not null)
        {
            await Send.ResponseAsync(AgentApiResponse<AgentFeedback>.Failure(authError), authError.StatusCode, cancellation: ct);
            return;
        }

        var actorId = AgentEndpointActor.Resolve(User);
        if (actorId is null)
        {
            var error = new AgentError("agent.actor.unresolved", "The current principal does not carry a resolvable actor identity.", 403);
            await Send.ResponseAsync(AgentApiResponse<AgentFeedback>.Failure(error), 403, cancellation: ct);
            return;
        }

        if (!string.IsNullOrWhiteSpace(req.MessageId) && await sessions.FindMessageAsync(req.SessionId, req.MessageId, ct) is null)
        {
            var error = new AgentError("agent.message.not_found", $"Agent message '{req.MessageId}' was not found in session '{req.SessionId}'.", 404);
            await Send.ResponseAsync(AgentApiResponse<AgentFeedback>.Failure(error), 404, cancellation: ct);
            return;
        }

        var item = new AgentFeedback(
            AgentProviderPrimitives.NewId(),
            req.SessionId,
            req.MessageId,
            req.Rating > 0 ? "positive" : "negative",
            req.Comment,
            actorId,
            DateTimeOffset.UtcNow);

        await Send.OkAsync(AgentApiResponse<AgentFeedback>.Success(await feedback.AddAsync(item, ct)), ct);
    }
}
