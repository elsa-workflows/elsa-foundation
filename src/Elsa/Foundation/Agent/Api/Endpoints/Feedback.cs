using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Models;
using Elsa.Foundation.Agent.Api.Constants;
using Elsa.Foundation.Agent.Api.Models;

namespace Elsa.Foundation.Agent.Api.Endpoints;

internal sealed class Feedback(IAgentFeedbackService feedback)
    : ElsaEndpoint<AgentFeedbackApiRequest, AgentApiResponse<AgentFeedback>>
{
    public override void Configure()
    {
        Post(AgentRouteConstants.GetRoute("feedback"));
        ConfigurePermissions(AgentPermissionKeys.Use);
    }

    public override async Task HandleAsync(AgentFeedbackApiRequest req, CancellationToken ct)
    {
        var item = new AgentFeedback(
            Guid.NewGuid().ToString("N"),
            req.SessionId,
            req.MessageId,
            req.Rating > 0 ? "positive" : "negative",
            req.Comment,
            req.ActorId,
            DateTimeOffset.UtcNow);

        await Send.OkAsync(AgentApiResponse<AgentFeedback>.Success(await feedback.AddAsync(item, ct)), ct);
    }
}
