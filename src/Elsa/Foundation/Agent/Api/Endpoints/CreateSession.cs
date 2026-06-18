using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Models;
using Elsa.Foundation.Agent.Api.Constants;
using Elsa.Foundation.Agent.Api.Models;

namespace Elsa.Foundation.Agent.Api.Endpoints;

internal sealed class CreateSession(IAgentSessionService sessions, IAgentContextCollector contextCollector)
    : ElsaEndpoint<AgentCreateSessionRequest, AgentApiResponse<AgentSession>>
{
    public override void Configure()
    {
        Post(AgentRouteConstants.GetRoute("sessions"));
        ConfigurePermissions(AgentPermissionKeys.Use);
    }

    public override async Task HandleAsync(AgentCreateSessionRequest req, CancellationToken ct)
    {
        var session = await sessions.CreateAsync(new(
            req.TenantId,
            string.IsNullOrWhiteSpace(req.ConversationId)
                ? string.IsNullOrWhiteSpace(req.ActiveSurface.ResourceId) ? req.ActiveSurface.Route : req.ActiveSurface.ResourceId
                : req.ConversationId,
            req.ProviderId,
            GetMode(req),
            BuildTitle(req),
            req.Policy ?? AgentPolicy.Default,
            BuildMetadata(req)), ct);

        _ = await CollectInitialContextAsync(session, req, ct);

        await Send.OkAsync(AgentApiResponse<AgentSession>.Success(session), ct);
    }

    private async Task<IReadOnlyCollection<AgentContextAttachment>> CollectInitialContextAsync(AgentSession session, AgentCreateSessionRequest req, CancellationToken ct)
    {
        var workflowId = req.ActiveSurface.ResourceId ?? TryGetWorkflowId(req.ActiveSurface.Route);
        if (workflowId is null)
            return [];

        var result = await contextCollector.CollectAsync(session.Policy, new(
            session.Id,
            "workflow",
            new Dictionary<string, string>
            {
                ["workflowDefinitionId"] = workflowId,
                ["workflowVersionId"] = "draft"
            }), ct);

        return result.Value ?? [];
    }

    private static string BuildTitle(AgentCreateSessionRequest request)
        => request.ActiveSurface.ResourceType == "workflow-definition" && !string.IsNullOrWhiteSpace(request.ActiveSurface.ResourceId)
            ? $"{request.ActiveSurface.ResourceId} workflow"
            : "Studio assistant";

    private static string GetMode(AgentCreateSessionRequest request)
        => request.Metadata.TryGetValue("mode", out var mode) && !string.IsNullOrWhiteSpace(mode) ? mode : request.Mode;

    private static IReadOnlyDictionary<string, string> BuildMetadata(AgentCreateSessionRequest request)
    {
        var metadata = new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["route"] = request.ActiveSurface.Route,
            ["resourceType"] = request.ActiveSurface.ResourceType ?? request.Metadata.GetValueOrDefault("resourceType", string.Empty),
            ["resourceId"] = request.ActiveSurface.ResourceId ?? request.Metadata.GetValueOrDefault("resourceId", string.Empty),
            ["studioVersion"] = request.ClientContext.StudioVersion,
            ["sdkVersion"] = request.ClientContext.SdkVersion,
            ["modules"] = string.Join(",", request.ClientContext.ModuleIds)
        };

        return metadata;
    }

    private static string? TryGetWorkflowId(string route)
    {
        var segments = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var workflowsIndex = Array.FindIndex(segments, x => string.Equals(x, "workflows", StringComparison.OrdinalIgnoreCase));
        return workflowsIndex >= 0 && workflowsIndex + 1 < segments.Length ? segments[workflowsIndex + 1] : null;
    }
}
