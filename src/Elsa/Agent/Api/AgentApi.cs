using Elsa.Agent.Api.Constants;
using Elsa.Agent.Api.Endpoints;
using Elsa.Agent.Api.Models;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Elsa.Agent.Api;

/// <summary>Maps the provider-agnostic Agent REST and SSE surface using ordinary ASP.NET Core endpoints.</summary>
public static class AgentApi
{
    private const string OwnerId = "Elsa.Agent.Api";
    private const string RoutePrefix = "/_elsa/agent";

    public static void MapAgentApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var descriptionMethod = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        Configure(endpoints.MapGet($"{RoutePrefix}/bootstrap", (RequestDelegate)HandleBootstrapAsync),
                "ElsaAgentApiEndpointsBootstrap", AgentPermissionKeys.Use,
                typeof(AgentBootstrapResponse), descriptionMethod)
            .WithMetadata(Response(StatusCodes.Status200OK, typeof(AgentApiResponse<AgentBootstrapResponse>)), Unauthorized(), Forbidden());

        Configure(endpoints.MapPost($"{RoutePrefix}/sessions", (RequestDelegate)HandleCreateSessionAsync),
                "ElsaAgentApiEndpointsCreateSession", AgentPermissionKeys.Use,
                typeof(AgentCreateSessionRequest), typeof(AgentApiResponse<AgentCreateSessionResponse>), descriptionMethod)
            .WithMetadata(Response(StatusCodes.Status200OK, typeof(AgentApiResponse<AgentCreateSessionResponse>)), Unauthorized(), Forbidden());

        Configure(endpoints.MapGet($"{RoutePrefix}/sessions/{{sessionId}}", (RequestDelegate)HandleGetSessionAsync),
                "ElsaAgentApiEndpointsGetSession", AgentPermissionKeys.Use,
                typeof(AgentSessionRouteRequest), typeof(AgentApiResponse<AgentSessionDetailsResponse>), descriptionMethod)
            .WithMetadata(Response(StatusCodes.Status200OK, typeof(AgentApiResponse<AgentSessionDetailsResponse>)), Unauthorized(), Forbidden());

        Configure(endpoints.MapPost($"{RoutePrefix}/sessions/{{sessionId}}/messages", (RequestDelegate)HandlePostMessageAsync),
                "ElsaAgentApiEndpointsPostMessage", AgentPermissionKeys.Use,
                typeof(AgentMessageRequest), typeof(AgentApiResponse<AgentMessageAcceptedResponse>), descriptionMethod)
            .WithMetadata(Response(StatusCodes.Status200OK, typeof(AgentApiResponse<AgentMessageAcceptedResponse>)), Unauthorized(), Forbidden());

        Configure(endpoints.MapPost($"{RoutePrefix}/sessions/{{sessionId}}/turns/{{turnId}}/cancel", (RequestDelegate)HandleCancelTurnAsync),
                "ElsaAgentApiEndpointsCancelTurn", AgentPermissionKeys.Use,
                typeof(AgentTurnCancelRequest), typeof(AgentApiResponse<AgentTurnCancelResponse>), descriptionMethod)
            .WithMetadata(Response(StatusCodes.Status200OK, typeof(AgentApiResponse<AgentTurnCancelResponse>)), Unauthorized(), Forbidden());

        Configure(endpoints.MapGet($"{RoutePrefix}/sessions/{{sessionId}}/stream", (RequestDelegate)HandleStreamSessionAsync),
                "ElsaAgentApiEndpointsStreamSession", AgentPermissionKeys.Use,
                typeof(AgentSessionRouteRequest), typeof(void), descriptionMethod)
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status204NoContent, typeof(void), []), Unauthorized(), Forbidden());

        Configure(endpoints.MapPost($"{RoutePrefix}/feedback", (RequestDelegate)HandleFeedbackAsync),
                "ElsaAgentApiEndpointsFeedback", AgentPermissionKeys.Use,
                typeof(AgentFeedbackApiRequest), typeof(AgentApiResponse<AgentFeedback>), descriptionMethod)
            .WithMetadata(Response(StatusCodes.Status200OK, typeof(AgentApiResponse<AgentFeedback>)), Unauthorized(), Forbidden());

        Configure(endpoints.MapPost($"{RoutePrefix}/proposals/{{proposalId}}/approve", (RequestDelegate)HandleApproveProposalAsync),
                "ElsaAgentApiEndpointsApproveProposal", AgentPermissionKeys.Proposals,
                typeof(AgentProposalDecisionRequest), typeof(AgentApiResponse<AgentActionProposal>), descriptionMethod)
            .WithMetadata(Response(StatusCodes.Status200OK, typeof(AgentApiResponse<AgentActionProposal>)), Unauthorized(), Forbidden());

        Configure(endpoints.MapPost($"{RoutePrefix}/proposals/{{proposalId}}/deny", (RequestDelegate)HandleDenyProposalAsync),
                "ElsaAgentApiEndpointsDenyProposal", AgentPermissionKeys.Proposals,
                typeof(AgentProposalDecisionRequest), typeof(AgentApiResponse<AgentActionProposal>), descriptionMethod)
            .WithMetadata(Response(StatusCodes.Status200OK, typeof(AgentApiResponse<AgentActionProposal>)), Unauthorized(), Forbidden());

        Configure(endpoints.MapPost($"{RoutePrefix}/proposals/{{proposalId}}/execute", (RequestDelegate)HandleExecuteProposalAsync),
                "ElsaAgentApiEndpointsExecuteProposal", AgentPermissionKeys.Proposals,
                typeof(AgentProposalDecisionRequest), typeof(AgentApiResponse<AgentProposalExecutionResult>), descriptionMethod)
            .WithMetadata(Response(StatusCodes.Status200OK, typeof(AgentApiResponse<AgentProposalExecutionResult>)), Unauthorized(), Forbidden());

        Configure(endpoints.MapGet($"{RoutePrefix}/audit", (RequestDelegate)HandleAuditAsync),
                "ElsaAgentApiEndpointsAudit", AgentPermissionKeys.Audit,
                typeof(AgentAuditQueryRequest), typeof(AgentApiResponse<IReadOnlyCollection<AgentAuditEvent>>), descriptionMethod)
            .WithMetadata(Response(StatusCodes.Status200OK, typeof(AgentApiResponse<IReadOnlyCollection<AgentAuditEvent>>)), Unauthorized(), Forbidden());
    }

    private static IEndpointConventionBuilder Configure(
        IEndpointConventionBuilder builder,
        string operationId,
        string permission,
        Type responseType,
        System.Reflection.MethodInfo descriptionMethod)
        => ConfigureCore(builder, operationId, permission, responseType, descriptionMethod);

    private static IEndpointConventionBuilder Configure(
        IEndpointConventionBuilder builder,
        string operationId,
        string permission,
        Type requestType,
        Type responseType,
        System.Reflection.MethodInfo descriptionMethod)
    {
        builder.WithMetadata(new AcceptsMetadata(
            requestType == typeof(AgentSessionRouteRequest) || requestType == typeof(AgentAuditQueryRequest)
                ? ["*/*", "application/json"]
                : ["application/json"], requestType, false));
        return ConfigureCore(builder, operationId, permission, responseType, descriptionMethod);
    }

    private static IEndpointConventionBuilder ConfigureCore(
        IEndpointConventionBuilder builder,
        string operationId,
        string permission,
        Type responseType,
        System.Reflection.MethodInfo descriptionMethod)
        => builder
            .WithName(operationId)
            .WithTags(OwnerId)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(permission)
            .WithMetadata(descriptionMethod);

    private static ProducesResponseTypeMetadata Response(int statusCode, Type bodyType) =>
        new(statusCode, bodyType, ["application/json"]);

    private static ProducesResponseTypeMetadata Unauthorized() =>
        new(StatusCodes.Status401Unauthorized, typeof(void), []);

    private static ProducesResponseTypeMetadata Forbidden() =>
        new(StatusCodes.Status403Forbidden, typeof(void), []);

    private static async Task HandleBootstrapAsync(HttpContext context)
    {
        var providers = context.RequestServices.GetRequiredService<IAgentProviderRegistry>();
        var capabilities = context.RequestServices.GetRequiredService<IAgentCapabilityCatalog>();
        var evaluator = context.RequestServices.GetRequiredService<IAgentPolicyEvaluator>();
        var diagnostics = providers.Active is null ? null : await providers.Active.GetDiagnosticsAsync(context.RequestAborted);
        var availability = await evaluator.EvaluateAvailabilityAsync(AgentPolicy.Default, context.RequestAborted);
        var listed = (await capabilities.ListAsync(context.RequestAborted)).ToList();
        var enabled = availability.Allowed && diagnostics?.IsAvailable == true;
        var modes = enabled ? BuildModes(listed) : [];
        var response = new AgentBootstrapResponse(
            enabled,
            diagnostics?.IsAvailable == true ? "available" : "unavailable",
            modes,
            listed.Select(x => x.ToResponse()).ToList(),
            diagnostics?.ToResponse(),
            new(AgentPolicy.Default.ContextVisibility,
                AgentPolicy.Default.AutonomyMode.ToContractString(),
                AgentPolicy.Default.MaxAutonomyMode.ToContractString(),
                AgentPolicy.Default.AllowedAutonomyModes.Select(x => x.ToContractString()).ToList(),
                AgentPolicy.Default.RetentionLabel));
        await WriteAsync(context, AgentApiResponse<AgentBootstrapResponse>.Success(response), AgentJsonContext.Default.AgentApiResponseAgentBootstrapResponse);
    }

    private static async Task HandleCreateSessionAsync(HttpContext context)
    {
        var request = await ReadAsync(context, AgentJsonContext.Default.AgentCreateSessionRequest);
        if (request is null)
            return;
        var activeSurface = request.ActiveSurface ?? new AgentSurfaceRequest();
        var clientContext = request.ClientContext ?? new AgentClientContextRequest();
        var metadata = request.Metadata ?? new Dictionary<string, string>();
        var actorId = AgentEndpointActor.Resolve(context.User);
        var tenantId = AgentEndpointActor.ResolveTenant(context.User);
        if (actorId is null || tenantId is null)
        {
            await WriteAsync(context, AgentApiResponse<AgentCreateSessionResponse>.Failure(new("agent.actor.unresolved", "The current principal does not carry a resolvable actor or tenant identity.", 403)), AgentJsonContext.Default.AgentApiResponseAgentCreateSessionResponse, 403);
            return;
        }

        var providers = context.RequestServices.GetRequiredService<IAgentProviderRegistry>();
        if (providers.Active is null)
        {
            await WriteAsync(context, AgentApiResponse<AgentCreateSessionResponse>.Failure(new("agent.provider.not_found", "No agent harness is enabled.", 404)), AgentJsonContext.Default.AgentApiResponseAgentCreateSessionResponse, 404);
            return;
        }
        var diagnostics = await providers.Active.GetDiagnosticsAsync(context.RequestAborted);
        if (!diagnostics.IsAvailable)
        {
            await WriteAsync(context, AgentApiResponse<AgentCreateSessionResponse>.Failure(new("agent.provider.unavailable", diagnostics.Status, 503)), AgentJsonContext.Default.AgentApiResponseAgentCreateSessionResponse, 503);
            return;
        }

        var sessions = context.RequestServices.GetRequiredService<IAgentSessionService>();
        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? string.IsNullOrWhiteSpace(activeSurface.ResourceId) ? activeSurface.Route : activeSurface.ResourceId
            : request.ConversationId;
        var sessionMetadata = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["route"] = activeSurface.Route,
            ["resourceType"] = activeSurface.ResourceType ?? metadata.GetValueOrDefault("resourceType", string.Empty),
            ["resourceId"] = activeSurface.ResourceId ?? metadata.GetValueOrDefault("resourceId", string.Empty),
            ["studioVersion"] = clientContext.StudioVersion,
            ["sdkVersion"] = clientContext.SdkVersion,
            ["modules"] = string.Join(",", clientContext.ModuleIds)
        };
        var requestedAutonomy = AgentApiMapping.ParseAutonomyMode(request.AutonomyMode) ?? AgentPolicy.Default.AutonomyMode;
        var policy = AgentPolicy.Default with { AutonomyMode = AgentPolicy.Default.Clamp(requestedAutonomy) };
        var title = activeSurface.ResourceType == "workflow-definition" && !string.IsNullOrWhiteSpace(activeSurface.ResourceId)
            ? $"{activeSurface.ResourceId} workflow"
            : "Studio assistant";
        var mode = metadata.TryGetValue("mode", out var metadataMode) && !string.IsNullOrWhiteSpace(metadataMode)
            ? metadataMode
            : request.Mode;
        var session = await sessions.CreateAsync(new(tenantId, actorId, conversationId, providers.Active.ProviderId, mode, title, policy, sessionMetadata), context.RequestAborted);
        await providers.Active.CreateSessionAsync(session, context.RequestAborted);
        var attachments = await CollectInitialContextAsync(context, session, request);
        await sessions.AddContextAsync(session.Id, attachments, context.RequestAborted);
        await WriteAsync(context, AgentApiResponse<AgentCreateSessionResponse>.Success(new(session.Id, session.Status.ToContractString(), session.Title, attachments.Select(x => x.ToResponse()).ToList())), AgentJsonContext.Default.AgentApiResponseAgentCreateSessionResponse);
    }

    private static async Task HandleGetSessionAsync(HttpContext context)
    {
        var sessionId = RouteValue(context, "sessionId");
        var (session, error) = await AgentSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IAgentSessionService>(), context.User, sessionId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, AgentApiResponse<AgentSessionDetailsResponse>.Failure(error), AgentJsonContext.Default.AgentApiResponseAgentSessionDetailsResponse, error.StatusCode);
            return;
        }
        var sessions = context.RequestServices.GetRequiredService<IAgentSessionService>();
        var data = new AgentSessionDetailsResponse(session!.Id, session.Status.ToContractString(), session.Title,
            (await sessions.ListContextAsync(sessionId, context.RequestAborted)).Select(x => x.ToResponse()).ToList(),
            (await sessions.ListMessagesAsync(sessionId, context.RequestAborted)).Select(x => x.ToViewModel()).ToList());
        await WriteAsync(context, AgentApiResponse<AgentSessionDetailsResponse>.Success(data), AgentJsonContext.Default.AgentApiResponseAgentSessionDetailsResponse);
    }

    private static async Task HandlePostMessageAsync(HttpContext context)
    {
        var request = await ReadAsync(context, AgentJsonContext.Default.AgentMessageRequest);
        if (request is null)
            return;
        var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? RouteValue(context, "sessionId") : request.SessionId;
        var requestedCapabilitiesInput = request.RequestedCapabilities ?? [];
        var contextAttachmentIds = request.ContextAttachmentIds ?? [];
        var contextAttachments = request.ContextAttachments ?? [];
        var sessions = context.RequestServices.GetRequiredService<IAgentSessionService>();
        var (session, error) = await AgentSessionAuthorization.AuthorizeAsync(sessions, context.User, sessionId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, AgentApiResponse<AgentMessageAcceptedResponse>.Failure(error), AgentJsonContext.Default.AgentApiResponseAgentMessageAcceptedResponse, error.StatusCode);
            return;
        }
        var evaluator = context.RequestServices.GetRequiredService<IAgentPolicyEvaluator>();
        var availability = await evaluator.EvaluateAvailabilityAsync(session!.Policy, context.RequestAborted);
        if (!availability.Allowed)
        {
            await WriteDeniedAsync(context, sessionId, availability);
            return;
        }
        var requestedCapabilities = requestedCapabilitiesInput.Count > 0 ? requestedCapabilitiesInput : string.IsNullOrWhiteSpace(request.CapabilityId) ? [] : [request.CapabilityId];
        foreach (var capabilityId in requestedCapabilities)
        {
            var decision = await evaluator.EvaluateCapabilityAsync(session.Policy, capabilityId, context.RequestAborted);
            if (!decision.Allowed)
            {
                await WriteDeniedAsync(context, sessionId, decision);
                return;
            }
        }
        var stored = await sessions.ListContextAsync(sessionId, context.RequestAborted);
        var requestedStored = contextAttachmentIds.Count == 0 ? [] : stored.Where(x => contextAttachmentIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase)).ToList();
        var requestContext = contextAttachments.Select(x => x.ToDomain()).ToList();
        var contextItems = await context.RequestServices.GetRequiredService<IAgentContextSanitizer>().SanitizeAsync(requestedStored.Concat(requestContext).ToList(), context.RequestAborted);
        var contextDecision = await evaluator.EvaluateContextAsync(session.Policy, contextItems, context.RequestAborted);
        if (!contextDecision.Allowed)
        {
            await WriteDeniedAsync(context, sessionId, contextDecision);
            return;
        }
        var message = await sessions.AddMessageAsync(sessionId, new(AgentRole.User, string.IsNullOrWhiteSpace(request.Content) ? request.Message : request.Content, AgentMessageStatus.Pending, requestedCapabilities.FirstOrDefault(), contextItems.Select(x => x.Id).ToList(), contextItems), context.RequestAborted);
        await WriteAsync(context, AgentApiResponse<AgentMessageAcceptedResponse>.Success(new(message, [])), AgentJsonContext.Default.AgentApiResponseAgentMessageAcceptedResponse);
    }

    private static async Task HandleCancelTurnAsync(HttpContext context)
    {
        var request = await ReadAsync(context, AgentJsonContext.Default.AgentTurnCancelRequest);
        if (request is null)
            return;
        var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? RouteValue(context, "sessionId") : request.SessionId;
        var (_, error) = await AgentSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IAgentSessionService>(), context.User, sessionId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, AgentApiResponse<AgentTurnCancelResponse>.Failure(error), AgentJsonContext.Default.AgentApiResponseAgentTurnCancelResponse, error.StatusCode);
            return;
        }
        var turnId = string.IsNullOrWhiteSpace(request.TurnId) ? RouteValue(context, "turnId") : request.TurnId;
        var cancelled = context.RequestServices.GetRequiredService<IAgentTurnRegistry>().Cancel(turnId);
        await WriteAsync(context, AgentApiResponse<AgentTurnCancelResponse>.Success(new(turnId, cancelled)), AgentJsonContext.Default.AgentApiResponseAgentTurnCancelResponse);
    }

    private static async Task HandleStreamSessionAsync(HttpContext context)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        var sessionId = RouteValue(context, "sessionId");
        var (_, error) = await AgentSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IAgentSessionService>(), context.User, sessionId, context.RequestAborted);
        if (error is not null)
        {
            await WriteEventAsync(context, new(AgentProviderPrimitives.NewId(), AgentStreamEventKind.Error, null, null, error, DateTimeOffset.UtcNow));
            return;
        }
        await foreach (var item in context.RequestServices.GetRequiredService<IAgentStreamingService>().StreamAsync(sessionId, context.RequestAborted))
            await WriteEventAsync(context, item);
    }

    private static async Task HandleFeedbackAsync(HttpContext context)
    {
        var request = await ReadAsync(context, AgentJsonContext.Default.AgentFeedbackApiRequest);
        if (request is null)
            return;
        var (session, error) = await AgentSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IAgentSessionService>(), context.User, request.SessionId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, AgentApiResponse<AgentFeedback>.Failure(error), AgentJsonContext.Default.AgentApiResponseAgentFeedback, error.StatusCode);
            return;
        }
        var actorId = AgentEndpointActor.Resolve(context.User);
        if (actorId is null)
        {
            await WriteAsync(context, AgentApiResponse<AgentFeedback>.Failure(new("agent.actor.unresolved", "The current principal does not carry a resolvable actor identity.", 403)), AgentJsonContext.Default.AgentApiResponseAgentFeedback, 403);
            return;
        }
        if (!string.IsNullOrWhiteSpace(request.MessageId) && await context.RequestServices.GetRequiredService<IAgentSessionService>().FindMessageAsync(request.SessionId, request.MessageId, context.RequestAborted) is null)
        {
            await WriteAsync(context, AgentApiResponse<AgentFeedback>.Failure(new("agent.message.not_found", $"Agent message '{request.MessageId}' was not found in session '{request.SessionId}'.", 404)), AgentJsonContext.Default.AgentApiResponseAgentFeedback, 404);
            return;
        }
        var feedback = new AgentFeedback(AgentProviderPrimitives.NewId(), session!.Id, request.MessageId, request.Rating > 0 ? "positive" : "negative", request.Comment, actorId, DateTimeOffset.UtcNow);
        await WriteAsync(context, AgentApiResponse<AgentFeedback>.Success(await context.RequestServices.GetRequiredService<IAgentFeedbackService>().AddAsync(feedback, context.RequestAborted)), AgentJsonContext.Default.AgentApiResponseAgentFeedback);
    }

    private static async Task HandleApproveProposalAsync(HttpContext context)
    {
        var request = await ReadAsync(context, AgentJsonContext.Default.AgentProposalDecisionRequest);
        if (request is null)
            return;
        var proposalId = string.IsNullOrWhiteSpace(request.ProposalId) ? RouteValue(context, "proposalId") : request.ProposalId;
        var proposals = context.RequestServices.GetRequiredService<IAgentProposalService>();
        var sessions = context.RequestServices.GetRequiredService<IAgentSessionService>();
        var error = await AgentProposalAuthorization.AuthorizeAsync(proposals, sessions, context.User, proposalId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, AgentApiResponse<AgentActionProposal>.Failure(error), AgentJsonContext.Default.AgentApiResponseAgentActionProposal, error.StatusCode);
            return;
        }
        var actorId = AgentEndpointActor.Resolve(context.User);
        if (actorId is null)
        {
            await WriteAsync(context, AgentApiResponse<AgentActionProposal>.Failure(new("agent.actor.unresolved", "The current principal does not carry a resolvable actor identity.", 403)), AgentJsonContext.Default.AgentApiResponseAgentActionProposal, 403);
            return;
        }
        var result = await proposals.ApproveAsync(proposalId, actorId, request.Revision, request.Comment, context.RequestAborted);
        await WriteAsync(context, result.Succeeded ? AgentApiResponse<AgentActionProposal>.Success(result.Value!) : AgentApiResponse<AgentActionProposal>.Failure(result.Error!), AgentJsonContext.Default.AgentApiResponseAgentActionProposal, result.Succeeded ? 200 : result.Error!.StatusCode);
    }

    private static async Task HandleDenyProposalAsync(HttpContext context)
    {
        var request = await ReadAsync(context, AgentJsonContext.Default.AgentProposalDecisionRequest);
        if (request is null)
            return;
        var proposalId = string.IsNullOrWhiteSpace(request.ProposalId) ? RouteValue(context, "proposalId") : request.ProposalId;
        var proposals = context.RequestServices.GetRequiredService<IAgentProposalService>();
        var error = await AgentProposalAuthorization.AuthorizeAsync(proposals, context.RequestServices.GetRequiredService<IAgentSessionService>(), context.User, proposalId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, AgentApiResponse<AgentActionProposal>.Failure(error), AgentJsonContext.Default.AgentApiResponseAgentActionProposal, error.StatusCode);
            return;
        }
        var actorId = AgentEndpointActor.Resolve(context.User);
        if (actorId is null)
        {
            await WriteAsync(context, AgentApiResponse<AgentActionProposal>.Failure(new("agent.actor.unresolved", "The current principal does not carry a resolvable actor identity.", 403)), AgentJsonContext.Default.AgentApiResponseAgentActionProposal, 403);
            return;
        }
        var result = await proposals.DenyAsync(proposalId, actorId, request.Comment ?? request.Reason, context.RequestAborted);
        await WriteAsync(context, result.Succeeded ? AgentApiResponse<AgentActionProposal>.Success(result.Value!) : AgentApiResponse<AgentActionProposal>.Failure(result.Error!), AgentJsonContext.Default.AgentApiResponseAgentActionProposal, result.Succeeded ? 200 : result.Error!.StatusCode);
    }

    private static async Task HandleExecuteProposalAsync(HttpContext context)
    {
        var request = await ReadAsync(context, AgentJsonContext.Default.AgentProposalDecisionRequest);
        if (request is null)
            return;
        var proposalId = string.IsNullOrWhiteSpace(request.ProposalId) ? RouteValue(context, "proposalId") : request.ProposalId;
        var proposals = context.RequestServices.GetRequiredService<IAgentProposalService>();
        var error = await AgentProposalAuthorization.AuthorizeAsync(proposals, context.RequestServices.GetRequiredService<IAgentSessionService>(), context.User, proposalId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, AgentApiResponse<AgentProposalExecutionResult>.Failure(error), AgentJsonContext.Default.AgentApiResponseAgentProposalExecutionResult, error.StatusCode);
            return;
        }
        var actorId = AgentEndpointActor.Resolve(context.User);
        if (actorId is null)
        {
            await WriteAsync(context, AgentApiResponse<AgentProposalExecutionResult>.Failure(new("agent.actor.unresolved", "The current principal does not carry a resolvable actor identity.", 403)), AgentJsonContext.Default.AgentApiResponseAgentProposalExecutionResult, 403);
            return;
        }
        var result = await proposals.ExecuteAsync(proposalId, actorId, request.Revision, context.RequestAborted);
        await WriteAsync(context, result.Succeeded ? AgentApiResponse<AgentProposalExecutionResult>.Success(result.Value!) : AgentApiResponse<AgentProposalExecutionResult>.Failure(result.Error!), AgentJsonContext.Default.AgentApiResponseAgentProposalExecutionResult, result.Succeeded ? 200 : result.Error!.StatusCode);
    }

    private static async Task HandleAuditAsync(HttpContext context)
    {
        var sessionId = context.Request.Query["sessionId"].FirstOrDefault();
        var rawTake = context.Request.Query["take"].FirstOrDefault();
        var take = ParseTake(rawTake, out var bindingError);
        if (bindingError is not null)
        {
            await WriteBindingErrorAsync(context, "take", bindingError);
            return;
        }
        var events = await context.RequestServices.GetRequiredService<IAgentAuditReader>().ListAsync(sessionId, take, context.RequestAborted);
        await WriteAsync(context, AgentApiResponse<IReadOnlyCollection<AgentAuditEvent>>.Success(events), AgentJsonContext.Default.AgentApiResponseIReadOnlyCollectionAgentAuditEvent);
    }

    private static async Task<IReadOnlyCollection<AgentContextAttachment>> CollectInitialContextAsync(HttpContext context, AgentSession session, AgentCreateSessionRequest request)
    {
        var activeSurface = request.ActiveSurface;
        var workflowId = activeSurface?.ResourceId ?? TryGetWorkflowId(activeSurface?.Route);
        if (workflowId is null)
            return [];
        var result = await context.RequestServices.GetRequiredService<IAgentContextCollector>().CollectAsync(session.Policy, new(session.Id, "workflow", new Dictionary<string, string> { ["workflowDefinitionId"] = workflowId, ["workflowVersionId"] = "draft" }), context.RequestAborted);
        return result.Value ?? [];
    }

    private static async Task WriteDeniedAsync(HttpContext context, string sessionId, AgentPolicyDecision decision)
    {
        await context.RequestServices.GetRequiredService<IAgentAuditSink>().EmitAsync(new(AgentProviderPrimitives.NewId(), AgentAuditEventKind.ContextDenied, sessionId, null, "Agent request denied by policy.", DateTimeOffset.UtcNow, new Dictionary<string, string>()), context.RequestAborted);
        await WriteAsync(context, AgentApiResponse<AgentMessageAcceptedResponse>.Failure(new("agent.policyDenied", string.Join(" ", decision.Violations.Select(x => x.Message)), 403)), AgentJsonContext.Default.AgentApiResponseAgentMessageAcceptedResponse, 403);
    }

    private static async Task<T?> ReadAsync<T>(HttpContext context, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return await context.Request.ReadFromJsonAsync(typeInfo, context.RequestAborted);
        }
        catch (JsonException exception)
        {
            await WriteBindingErrorAsync(context, "serializerErrors", exception.Message.Replace(" Path: $ |", string.Empty, StringComparison.Ordinal));
            return default;
        }
    }

    private static Task WriteAsync<T>(HttpContext context, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, int statusCode = StatusCodes.Status200OK) =>
        Results.Json(value, typeInfo, contentType: "application/json", statusCode: statusCode).ExecuteAsync(context);

    private static async Task WriteEventAsync(HttpContext context, AgentStreamEvent item)
    {
        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(item, AgentSseJsonContext.Default.AgentStreamEvent)}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }

    private static Task WriteBindingErrorAsync(HttpContext context, string field, string message) =>
        Results.Json(
            new AgentBindingErrorResponse(400, "One or more errors occurred!", new Dictionary<string, string[]> { [field] = [message] }),
            AgentJsonContext.Default.AgentBindingErrorResponse,
            contentType: "application/problem+json; charset=utf-8",
            statusCode: StatusCodes.Status400BadRequest)
            .ExecuteAsync(context);

    private static int? ParseTake(string? value, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = null;
            return null;
        }

        if (int.TryParse(value, out var parsed))
        {
            error = null;
            return parsed;
        }

        error = $"Value [{value}] is not valid for a [Int32] property!";
        return null;
    }

    private static string? TryGetWorkflowId(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return null;

        var segments = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var workflowsIndex = Array.FindIndex(segments, x => string.Equals(x, "workflows", StringComparison.OrdinalIgnoreCase));
        return workflowsIndex >= 0 && workflowsIndex + 1 < segments.Length ? segments[workflowsIndex + 1] : null;
    }

    private static string RouteValue(HttpContext context, string name) => context.Request.RouteValues.TryGetValue(name, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

    private static IReadOnlyCollection<string> BuildModes(IReadOnlyCollection<AgentCapability> capabilities)
    {
        var modes = new List<string>();
        if (capabilities.Any(x => string.Equals(x.Id, "workflow.explain", StringComparison.OrdinalIgnoreCase)))
            modes.Add("explain");
        if (capabilities.Any(x => string.Equals(x.Id, "workflow.troubleshoot", StringComparison.OrdinalIgnoreCase)))
            modes.Add("troubleshoot");
        if (capabilities.Any(x => string.Equals(x.Id, "workflow.propose-change", StringComparison.OrdinalIgnoreCase)))
            modes.Add("build");
        return modes;
    }
}
