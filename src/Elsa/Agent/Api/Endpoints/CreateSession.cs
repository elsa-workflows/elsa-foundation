using Elsa.Agent.Api.Constants;
using Elsa.Agent.Api.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Api.Endpoints;

internal sealed class CreateSession(
    IAgentSessionService sessions,
    IAgentContextCollector contextCollector,
    IAgentProviderRegistry providers)
    : ElsaEndpoint<AgentCreateSessionRequest, AgentApiResponse<AgentCreateSessionResponse>>
{
    public override void Configure()
    {
        Post(AgentRouteConstants.GetRoute("sessions"));
        ConfigurePermissions(AgentPermissionKeys.Use);
    }

    public override async Task HandleAsync(AgentCreateSessionRequest req, CancellationToken ct)
    {
        var actorId = AgentEndpointActor.Resolve(User);
        var tenantId = AgentEndpointActor.ResolveTenant(User);
        var provider = providers.Find(req.ProviderId);
        if (provider is null)
        {
            var error = new AgentError("agent.provider.not_found", $"Agent provider '{req.ProviderId}' is not registered.", 404);
            await Send.ResponseAsync(AgentApiResponse<AgentCreateSessionResponse>.Failure(error), 404, cancellation: ct);
            return;
        }

        var diagnostics = await provider.GetDiagnosticsAsync(ct);
        if (!diagnostics.IsAvailable)
        {
            var error = new AgentError("agent.provider.unavailable", diagnostics.Status, 503);
            await Send.ResponseAsync(AgentApiResponse<AgentCreateSessionResponse>.Failure(error), 503, cancellation: ct);
            return;
        }

        var session = await sessions.CreateAsync(new(
            tenantId,
            actorId,
            string.IsNullOrWhiteSpace(req.ConversationId)
                ? string.IsNullOrWhiteSpace(req.ActiveSurface.ResourceId) ? req.ActiveSurface.Route : req.ActiveSurface.ResourceId
                : req.ConversationId,
            req.ProviderId,
            GetMode(req),
            BuildTitle(req),
            AgentPolicy.Default,
            BuildMetadata(req)), ct);

        _ = await provider.CreateSessionAsync(session, ct);
        var contextAttachments = await CollectInitialContextAsync(session, req, ct);
        await sessions.AddContextAsync(session.Id, contextAttachments, ct);

        await Send.OkAsync(AgentApiResponse<AgentCreateSessionResponse>.Success(new(
            session.Id,
            session.Status.ToContractString(),
            session.Title,
            contextAttachments.Select(x => x.ToResponse()).ToList())), ct);
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
