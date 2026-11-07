using Elsa.Agent.Api.Constants;
using Elsa.Agent.Api.Endpoints;
using Elsa.Agent.Api.Models;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Api.AspNetCore;
using MinimalEndpoints;
using Elsa.Foundation.Identity.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Security.Cryptography;

namespace Elsa.Agent.Api;

/// <summary>Maps the provider-agnostic Agent REST and SSE surface using ordinary ASP.NET Core endpoints.</summary>
public static class AgentApi
{
    private const string OwnerId = "Elsa.Agent.Api";
    private const string RoutePrefix = "/_elsa/agent";

    public static void MapAgentApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The Agent operations keep their own reads, writes, failure-carrying response envelopes,
        // and the SSE stream, so every operation stays on the group's raw seam; the group supplies
        // the shared metadata the mapper used to inline per endpoint.
        var api = endpoints.MapEndpointGroup(OwnerId, AgentJsonContext.Default, jsonContentType: "application/json");

        Map(api, "GET", $"{RoutePrefix}/bootstrap", "Bootstrap", AgentPermissionKeys.Use,
            null, typeof(AgentApiResponse<AgentBootstrapResponse>), HandleBootstrapAsync);
        Map(api, "POST", $"{RoutePrefix}/sessions", "CreateSession", AgentPermissionKeys.Use,
            typeof(AgentCreateSessionRequest), typeof(AgentApiResponse<AgentCreateSessionResponse>), HandleCreateSessionAsync);
        Map(api, "GET", $"{RoutePrefix}/sessions", "ListSessions", AgentPermissionKeys.Use,
            typeof(AgentSessionListQueryRequest), typeof(AgentApiResponse<AgentSessionListResponse>), HandleListSessionsAsync);
        Map(api, "GET", $"{RoutePrefix}/sessions/{{sessionId}}", "GetSession", AgentPermissionKeys.Use,
            typeof(AgentSessionRouteRequest), typeof(AgentApiResponse<AgentSessionDetailsResponse>), HandleGetSessionAsync);
        Map(api, "PUT", $"{RoutePrefix}/sessions/{{sessionId}}/autonomy", "ChangeAutonomy", AgentPermissionKeys.Use,
            typeof(AgentSessionAutonomyRequest), typeof(AgentApiResponse<AgentSessionPolicyResponse>), HandleChangeAutonomyAsync);
        Map(api, "POST", $"{RoutePrefix}/sessions/{{sessionId}}/messages", "PostMessage", AgentPermissionKeys.Use,
            typeof(AgentMessageRequest), typeof(AgentApiResponse<AgentMessageAcceptedResponse>), HandlePostMessageAsync);
        Map(api, "POST", $"{RoutePrefix}/sessions/{{sessionId}}/messages/{{messageId}}/withdraw", "WithdrawMessage", AgentPermissionKeys.Use,
            typeof(AgentSessionRouteRequest), typeof(AgentApiResponse<AgentMessageViewModelResponse>), HandleWithdrawMessageAsync);
        Map(api, "GET", $"{RoutePrefix}/sessions/{{sessionId}}/proposals", "ListSessionProposals", AgentPermissionKeys.Use,
            typeof(AgentSessionRouteRequest), typeof(AgentApiResponse<IReadOnlyCollection<AgentActionProposal>>), HandleListProposalsAsync);
        Map(api, "POST", $"{RoutePrefix}/sessions/{{sessionId}}/turns/{{turnId}}/cancel", "CancelTurn", AgentPermissionKeys.Use,
            typeof(AgentTurnCancelRequest), typeof(AgentApiResponse<AgentTurnCancelResponse>), HandleCancelTurnAsync);
        Map(api, "GET", $"{RoutePrefix}/sessions/{{sessionId}}/stream", "StreamSession", AgentPermissionKeys.Use,
            typeof(AgentSessionRouteRequest), null, HandleStreamSessionAsync, successStatus: StatusCodes.Status204NoContent);
        Map(api, "POST", $"{RoutePrefix}/feedback", "Feedback", AgentPermissionKeys.Use,
            typeof(AgentFeedbackApiRequest), typeof(AgentApiResponse<AgentFeedback>), HandleFeedbackAsync);
        Map(api, "POST", $"{RoutePrefix}/proposals/{{proposalId}}/approve", "ApproveProposal", AgentPermissionKeys.Proposals,
            typeof(AgentProposalDecisionRequest), typeof(AgentApiResponse<AgentActionProposal>), HandleApproveProposalAsync);
        Map(api, "POST", $"{RoutePrefix}/proposals/{{proposalId}}/deny", "DenyProposal", AgentPermissionKeys.Proposals,
            typeof(AgentProposalDecisionRequest), typeof(AgentApiResponse<AgentActionProposal>), HandleDenyProposalAsync);
        Map(api, "POST", $"{RoutePrefix}/proposals/{{proposalId}}/execute", "ExecuteProposal", AgentPermissionKeys.Proposals,
            typeof(AgentProposalDecisionRequest), typeof(AgentApiResponse<AgentProposalExecutionResult>), HandleExecuteProposalAsync);
        Map(api, "POST", $"{RoutePrefix}/sessions/{{sessionId}}/client-invocations/{{invocationId}}/acknowledgements", "AcknowledgeClientInvocation", AgentPermissionKeys.Proposals,
            typeof(AgentClientAcknowledgement), typeof(AgentApiResponse<AgentProposalExecutionResult>), HandleClientAcknowledgementAsync);
        Map(api, "POST", $"{RoutePrefix}/sessions/{{sessionId}}/client-invocations/{{invocationId}}/resolution", "ResolveClientInvocation", AgentPermissionKeys.Proposals,
            typeof(AgentClientInvocationResolutionRequest), typeof(AgentApiResponse<AgentActionProposal>), HandleClientResolutionAsync);
        Map(api, "GET", $"{RoutePrefix}/audit", "Audit", AgentPermissionKeys.Audit,
            typeof(AgentAuditQueryRequest), typeof(AgentApiResponse<IReadOnlyCollection<AgentAuditEvent>>), HandleAuditAsync);
    }

    private static void Map(
        EndpointGroup api,
        string method,
        string pattern,
        string operation,
        string permission,
        Type? requestType,
        Type? responseType,
        Func<HttpContext, Task> dispatch,
        int successStatus = StatusCodes.Status200OK)
    {
        var builder = api.MapUnboundOperation(method, pattern, operation, responseType, successStatus, null, context => DispatchAsync(context, dispatch))
            .RequirePermission(permission);
        if (requestType is not null)
        {
            // The published documents accept any media type on the route- and query-bound reads,
            // exactly as the hand-written mapper declared them.
            builder.WithMetadata(new AcceptsMetadata(
                requestType == typeof(AgentSessionRouteRequest) || requestType == typeof(AgentAuditQueryRequest) || requestType == typeof(AgentSessionListQueryRequest)
                    ? ["*/*", "application/json"]
                    : ["application/json"], requestType, false));
        }
    }

    /// <summary>
    /// Any route on a session another process owns answers 409 agent.session.owned_elsewhere with a retry hint; a session
    /// this process lost during the call answers 409 agent.session.ownership_lost; a session whose recorded state is
    /// unreadable or fails verification answers 409 agent.session.unavailable_state with its reason. Nothing was applied in
    /// any case. Session authorization runs on the plain owner columns first, so only the owner reaches these outcomes.
    /// </summary>
    private static async Task DispatchAsync(HttpContext context, Func<HttpContext, Task> dispatch)
    {
        try { await dispatch(context); }
        catch (Exception failure) when (AgentSessionOwnershipException.Find(failure) is { } ownership && !context.Response.HasStarted)
        {
            if (RetryAfterSeconds(context, ownership) is { } seconds)
                context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await WriteAsync(context, AgentApiResponse<AgentSessionPolicyResponse>.Failure(OwnershipError(ownership)),
                AgentJsonContext.Default.AgentApiResponseAgentSessionPolicyResponse, StatusCodes.Status409Conflict);
        }
        catch (Exception failure) when (AgentSessionUnavailableException.Find(failure) is { } unavailable && !context.Response.HasStarted)
        {
            await WriteAsync(context, AgentApiResponse<AgentSessionPolicyResponse>.Failure(UnavailableError(unavailable)),
                AgentJsonContext.Default.AgentApiResponseAgentSessionPolicyResponse, StatusCodes.Status409Conflict);
        }
    }

    private static AgentError OwnershipError(AgentSessionOwnershipException ownership) => new(ownership.Code, ownership.Message, StatusCodes.Status409Conflict);

    private static AgentError UnavailableError(AgentSessionUnavailableException unavailable)
        => new(AgentSessionUnavailableException.Code, unavailable.Message, StatusCodes.Status409Conflict);

    private static long? RetryAfterSeconds(HttpContext context, AgentSessionOwnershipException ownership)
    {
        if (ownership.RetryAt is not { } retryAt) return null;
        var now = (context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        return Math.Max(1, (long)Math.Ceiling((retryAt - now).TotalSeconds));
    }

    private static async Task HandleBootstrapAsync(HttpContext context)
    {
        var providers = context.RequestServices.GetRequiredService<IAgentProviderRegistry>();
        var capabilities = context.RequestServices.GetRequiredService<IAgentCapabilityCatalog>();
        var evaluator = context.RequestServices.GetRequiredService<IAgentPolicyEvaluator>();
        var diagnostics = providers.Active is null ? null : await providers.Active.GetDiagnosticsAsync(context.RequestAborted);
        var availability = await evaluator.EvaluateAvailabilityAsync(AgentPolicy.Default, context.RequestAborted);
        var listed = (await capabilities.ListAsync(context.RequestAborted)).ToList();
        var privileges = context.RequestServices.GetRequiredService<IOptionsMonitor<AgentPrivilegePolicyOptions>>().CurrentValue;
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
                AgentPolicy.Default.RetentionLabel))
        {
            Recovery = DescribeRecovery(context),
            Privileges = new(privileges.Administrative, privileges.Irrecoverable, privileges.Activation)
        };
        await WriteAsync(context, AgentApiResponse<AgentBootstrapResponse>.Success(response), AgentJsonContext.Default.AgentApiResponseAgentBootstrapResponse);
    }

    /// <summary>
    /// The composition's durable-recovery support. A composition whose lifecycle services cannot be resolved reports
    /// unsupported, as the activation report does, so bootstrap still answers with its diagnostics.
    /// </summary>
    private static string DescribeRecovery(HttpContext context)
    {
        try { return context.RequestServices.GetService<IAgentRecoverySupport>()?.Describe() ?? AgentRecoveryModes.Unsupported; }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(OwnerId)
                .LogDebug("Durable agent recovery support could not be evaluated ({FailureType}).", failure.GetType().Name);
            return AgentRecoveryModes.Unsupported;
        }
    }

    private static async Task HandleCreateSessionAsync(HttpContext context)
    {
        var request = await ReadAsync(context, AgentJsonContext.Default.AgentCreateSessionRequest);
        if (request is null)
            return;
        if (request.Workspace is { Length: > AgentSessionCreateRequest.MaxWorkspaceLength } || request.ContinuedFrom is { Length: > AgentSessionCreateRequest.MaxContinuedFromLength })
        {
            await WriteAsync(context, AgentApiResponse<AgentCreateSessionResponse>.Failure(new("agent.session.invalid_request",
                $"A workspace is at most {AgentSessionCreateRequest.MaxWorkspaceLength} characters and a continued session identity at most {AgentSessionCreateRequest.MaxContinuedFromLength}.", 400)),
                AgentJsonContext.Default.AgentApiResponseAgentCreateSessionResponse, 400);
            return;
        }
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
        const string title = "Studio assistant";
        var mode = metadata.TryGetValue("mode", out var metadataMode) && !string.IsNullOrWhiteSpace(metadataMode)
            ? metadataMode
            : request.Mode;
        var session = await sessions.CreateAsync(new(tenantId, actorId, conversationId, providers.Active.ProviderId, mode, title, policy, sessionMetadata)
        {
            Workspace = string.IsNullOrWhiteSpace(request.Workspace) ? null : request.Workspace,
            ContinuedFrom = string.IsNullOrWhiteSpace(request.ContinuedFrom) ? null : request.ContinuedFrom
        }, context.RequestAborted);
        await providers.Active.CreateSessionAsync(session, context.RequestAborted);
        // Routes and presentation metadata never establish an authoritative resource target.
        IReadOnlyCollection<AgentContextAttachment> attachments = [];
        var created = new AgentCreateSessionResponse(session.Id, session.Status.ToContractString(), session.Title, attachments.Select(x => x.ToResponse()).ToList())
        {
            Policy = await ReadPolicyAsync(context, session.Id, actorId, tenantId)
        };
        await WriteAsync(context, AgentApiResponse<AgentCreateSessionResponse>.Success(created), AgentJsonContext.Default.AgentApiResponseAgentCreateSessionResponse);
    }

    /// <summary>
    /// Preview: the caller's own sessions. The owning store filters by the authenticated actor and tenant before it orders
    /// and limits; the workspace only narrows the result. Nothing in the request can select another owner.
    /// </summary>
    private static async Task HandleListSessionsAsync(HttpContext context)
    {
        var take = ParseTake(context.Request.Query["take"].FirstOrDefault(), out var bindingError);
        if (bindingError is not null)
        {
            await WriteBindingErrorAsync(context, "take", bindingError);
            return;
        }
        var actorId = AgentEndpointActor.Resolve(context.User);
        var tenantId = AgentEndpointActor.ResolveTenant(context.User);
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(tenantId))
        {
            await WriteSessionListFailureAsync(context, new("agent.actor.unresolved", "The current principal does not carry a resolvable actor or tenant identity.", 403));
            return;
        }
        var workspace = context.Request.Query["workspace"].FirstOrDefault();
        var query = new AgentSessionOwnerQuery(actorId, tenantId, string.IsNullOrWhiteSpace(workspace) ? null : workspace,
            Math.Clamp(take ?? AgentSessionOwnerQuery.DefaultTake, 1, AgentSessionOwnerQuery.MaxTake), context.Request.Query["before"].FirstOrDefault());
        AgentSessionListResult result;
        try { result = await context.RequestServices.GetRequiredService<IAgentSessionService>().ListOwnedAsync(query, context.RequestAborted); }
        catch (ArgumentException) when (query.Before is not null)
        {
            await WriteBindingErrorAsync(context, "before", "The cursor is not one this server issued.");
            return;
        }
        if (!result.Succeeded || result.Page is not { } page)
        {
            await WriteSessionListFailureAsync(context, new("agent.session.query_unsupported", "The installed agent session service cannot answer owner-scoped session queries.", 409));
            return;
        }
        await WriteAsync(context, AgentApiResponse<AgentSessionListResponse>.Success(new(page.Items.Select(x => x.ToResponse()).ToList(), page.Next)),
            AgentJsonContext.Default.AgentApiResponseAgentSessionListResponse);
    }

    private static Task WriteSessionListFailureAsync(HttpContext context, AgentError error) =>
        WriteAsync(context, AgentApiResponse<AgentSessionListResponse>.Failure(error), AgentJsonContext.Default.AgentApiResponseAgentSessionListResponse, error.StatusCode);

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
            (await sessions.ListMessagesAsync(sessionId, context.RequestAborted))
                .Where(x => x.Role != AgentRole.Assistant || x.Content.Length > 0 || x.DisclosureHistory is null)
                .Select(x => x.ToViewModel()).ToList())
        {
            // Authorization above proved these resolve to the session owner.
            Policy = await ReadPolicyAsync(context, session.Id, AgentEndpointActor.Resolve(context.User)!, AgentEndpointActor.ResolveTenant(context.User)!),
            LimitReached = session.LimitReached
        };
        await WriteAsync(context, AgentApiResponse<AgentSessionDetailsResponse>.Success(data), AgentJsonContext.Default.AgentApiResponseAgentSessionDetailsResponse);
    }

    /// <summary>
    /// Preview: an autonomy-only conditional change through the session policy owner.
    /// The owner is the authenticated actor and tenant; nothing in the request can select it.
    /// </summary>
    private static async Task HandleChangeAutonomyAsync(HttpContext context)
    {
        if (!context.Request.HasJsonContentType())
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }
        AgentSessionAutonomyRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync(AgentJsonContext.Default.AgentSessionAutonomyRequest, context.RequestAborted);
        }
        catch (NotSupportedException)
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }
        catch (JsonException)
        {
            request = null;
        }
        var requestedMode = AgentApiMapping.ParseAutonomyMode(request?.AutonomyMode);
        if (request?.ExpectedRevision is not { } expectedRevision || requestedMode is not { } mode)
        {
            await WritePolicyFailureAsync(context, new(AutonomyInvalidCode, "Send a defined autonomy mode (manual, auto-read-only or full-auto) and the last acknowledged policy revision.", 400));
            return;
        }

        var sessionId = RouteValue(context, "sessionId");
        var (session, error) = await AgentSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IAgentSessionService>(), context.User, sessionId, context.RequestAborted);
        if (error is not null)
        {
            await WritePolicyFailureAsync(context, error);
            return;
        }
        var policies = context.RequestServices.GetService<IAgentSessionPolicyService>();
        var result = policies is null
            ? new AgentSessionPolicyResult(AgentSessionPolicyOutcome.Unsupported, null)
            : await policies.ChangeAutonomyAsync(new(session!.Id, AgentEndpointActor.Resolve(context.User)!, AgentEndpointActor.ResolveTenant(context.User)!,
                mode, expectedRevision), context.RequestAborted);
        if (result is { Outcome: AgentSessionPolicyOutcome.Succeeded, State: { } state })
        {
            await WriteAsync(context, AgentApiResponse<AgentSessionPolicyResponse>.Success(state.ToResponse()), AgentJsonContext.Default.AgentApiResponseAgentSessionPolicyResponse);
            return;
        }
        await WritePolicyFailureAsync(context, result.Outcome switch
        {
            AgentSessionPolicyOutcome.StaleRevision => new("agent.session.policy_stale", "The session policy changed since the expected revision. Read the session back before retrying.", 409),
            AgentSessionPolicyOutcome.Busy => new("agent.session.busy", "Finish or resolve the current work before changing the session autonomy.", 409),
            AgentSessionPolicyOutcome.SessionClosed => new("agent.session.closed", "The agent session is closed or expired.", 409),
            AgentSessionPolicyOutcome.InvalidMode => new(AutonomyInvalidCode, "The requested autonomy mode is not defined.", 400),
            AgentSessionPolicyOutcome.NotFound => new("agent.session.not_found", $"Agent session '{sessionId}' was not found.", 404),
            _ => new("agent.session.autonomy_unsupported", "The installed agent session services cannot change the session autonomy.", 409)
        });
    }

    private const string AutonomyInvalidCode = "agent.session.autonomy_invalid";

    private static Task WritePolicyFailureAsync(HttpContext context, AgentError error) =>
        WriteAsync(context, AgentApiResponse<AgentSessionPolicyResponse>.Failure(error), AgentJsonContext.Default.AgentApiResponseAgentSessionPolicyResponse, error.StatusCode);

    /// <summary>Reads the owner's acknowledged policy; null only when the installed composition cannot read it back.</summary>
    private static async Task<AgentSessionPolicyResponse?> ReadPolicyAsync(HttpContext context, string sessionId, string actorId, string tenantId)
    {
        var policies = context.RequestServices.GetService<IAgentSessionPolicyService>();
        if (policies is null)
            return null;
        var result = await policies.GetAsync(sessionId, actorId, tenantId, context.RequestAborted);
        return result is { Outcome: AgentSessionPolicyOutcome.Succeeded, State: { } state } ? state.ToResponse() : null;
    }

    private static async Task HandlePostMessageAsync(HttpContext context)
    {
        var request = await ReadAsync(context, AgentJsonContext.Default.AgentMessageRequest);
        if (request is null)
            return;
        var sessionId = RouteValueOrFallback(context, "sessionId", request.SessionId);
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
        sessionId = session!.Id;
        var evaluator = context.RequestServices.GetRequiredService<IAgentPolicyEvaluator>();
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, AgentJsonContext.Default.AgentMessageRequest)));
        if (!string.IsNullOrWhiteSpace(request.RequestId))
        {
            var existing = (await sessions.ListMessagesAsync(sessionId, context.RequestAborted)).FirstOrDefault(x => x.Role == AgentRole.User && x.RequestId == request.RequestId);
            if (existing is not null)
            {
                if (existing.DeliveryFingerprint != fingerprint)
                {
                    await WriteAsync(context, AgentApiResponse<AgentMessageAcceptedResponse>.Failure(new("agent.message.delivery_conflict", "This prompt delivery identity was reused with different input.", 409)), AgentJsonContext.Default.AgentApiResponseAgentMessageAcceptedResponse, 409);
                    return;
                }
                // A failure after accepted insertion can precede context retention. Recovery retains only the
                // original accepted message's own context; the store deduplicates by attachment identity.
                await sessions.AddContextAsync(sessionId, existing.Context, context.RequestAborted);
                await WriteAsync(context, AgentApiResponse<AgentMessageAcceptedResponse>.Success(existing.ToAcceptedResponse([new("agent.message.duplicate", "This delivery was already accepted; it does not authorize another turn.", request.RequestId)])), AgentJsonContext.Default.AgentApiResponseAgentMessageAcceptedResponse);
                return;
            }
        }
        if (context.RequestServices.GetRequiredService<IAgentTurnRegistry>().IsActive(sessionId)
            || await context.RequestServices.GetRequiredService<IAgentTurnStateStore>().FindAsync(sessionId, context.RequestAborted) is not null)
        {
            await WriteAsync(context, AgentApiResponse<AgentMessageAcceptedResponse>.Failure(new("agent.session.busy", "Finish or resolve the current turn before sending another prompt.", 409)), AgentJsonContext.Default.AgentApiResponseAgentMessageAcceptedResponse, 409);
            return;
        }
        var availability = await evaluator.EvaluateAvailabilityAsync(session!.Policy, context.RequestAborted);
        if (!availability.Allowed)
        {
            await WriteDeniedAsync(context, sessionId, availability);
            return;
        }
        var requestedCapabilities = requestedCapabilitiesInput.Count > 0 ? requestedCapabilitiesInput : string.IsNullOrWhiteSpace(request.CapabilityId) ? [] : [request.CapabilityId];
        var installedCapabilities = await context.RequestServices.GetRequiredService<IAgentCapabilityCatalog>().ListAsync(context.RequestAborted);
        foreach (var capabilityId in requestedCapabilities)
        {
            if (!installedCapabilities.Any(x => x.Id == capabilityId))
            {
                await WriteDeniedAsync(context, sessionId, new(false, [new("agent.capability.unavailable", "No installed module contributes the requested capability.", capabilityId)]));
                return;
            }
            var decision = await evaluator.EvaluateCapabilityAsync(session.Policy, capabilityId, context.RequestAborted);
            if (!decision.Allowed)
            {
                await WriteDeniedAsync(context, sessionId, decision);
                return;
            }
        }
        var stored = await sessions.ListContextAsync(sessionId, context.RequestAborted);
        var requestedContextIds = contextAttachmentIds.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var retainedContextIds = stored.Select(attachment => attachment.Id).ToHashSet(StringComparer.Ordinal);
        if (requestedContextIds.Any(id => !retainedContextIds.Contains(id)))
        {
            await WriteAsync(context, AgentApiResponse<AgentMessageAcceptedResponse>.Failure(new("agent.context.required_unavailable",
                "A requested retained context attachment is unavailable. Refresh the current context before retrying.", 409)),
                AgentJsonContext.Default.AgentApiResponseAgentMessageAcceptedResponse, 409);
            return;
        }
        var requestedStored = requestedContextIds.Count == 0 ? [] : stored.Where(x => requestedContextIds.Contains(x.Id)).ToList();
        var requestContext = requestedStored.Concat(contextAttachments.Select(x => x.ToDomain())).ToList();
        AgentResult<AgentContextContribution> collected;
        var surface = request.ActiveSurface ?? new AgentSurfaceRequest
        {
            Route = session.Metadata.GetValueOrDefault("route", "/"),
            ResourceType = session.Metadata.GetValueOrDefault("resourceType"),
            ResourceId = session.Metadata.GetValueOrDefault("resourceId")
        };
        var surfaceInputs = new Dictionary<string, string>
        {
            ["route"] = surface.Route,
            ["resourceType"] = surface.ResourceType ?? string.Empty,
            ["resourceId"] = surface.ResourceId ?? string.Empty,
            ["selection"] = JsonSerializer.Serialize(surface.Selection)
        };
        try
        {
            collected = await context.RequestServices.GetRequiredService<IAgentContextCollector>().CollectAsync(session.Policy,
                new(sessionId, surface.Route, surfaceInputs) { CallerAttachments = requestContext }, context.RequestAborted);
        }
        catch (AgentContributionException)
        {
            await WriteAsync(context, AgentApiResponse<AgentMessageAcceptedResponse>.Failure(new("agent.context.composition_invalid", "Installed module context contributions conflict or cannot be transported.", 409)), AgentJsonContext.Default.AgentApiResponseAgentMessageAcceptedResponse, 409);
            return;
        }
        if (!collected.Succeeded || collected.Value is null)
        {
            await WriteAsync(context, AgentApiResponse<AgentMessageAcceptedResponse>.Failure(collected.Error!), AgentJsonContext.Default.AgentApiResponseAgentMessageAcceptedResponse, collected.Error!.StatusCode);
            return;
        }
        var contextItems = collected.Value.Attachments;
        var bindings = contextItems.Where(x => x.Client is not null).Select(x => x.Client!).Distinct().ToList();
        var messageRequest = new AgentMessageCreateRequest(AgentRole.User, string.IsNullOrWhiteSpace(request.Content) ? request.Message : request.Content,
            AgentMessageStatus.Pending, requestedCapabilities.FirstOrDefault(), contextItems.Select(x => x.Id).ToList(), contextItems)
        {
            RequestId = request.RequestId,
            Source = request.Source,
            Client = bindings.Count == 1 ? bindings[0] : null,
            PromptMode = request.PromptMode,
            DeliveryFingerprint = fingerprint,
            HasDisclosureProvenance = true,
            // Evaluators and collectors above authorized this delivery under the revision read at authorization.
            ExpectedPolicyRevision = session.PolicyRevision
        };
        AgentMessage message;
        try { message = await sessions.AddMessageAsync(sessionId, messageRequest, context.RequestAborted); }
        catch (AgentSessionPolicyChangedException)
        {
            await WriteAsync(context, AgentApiResponse<AgentMessageAcceptedResponse>.Failure(new(AgentSessionPolicyChangedException.Code, "The session policy changed while this prompt was being authorized. Review the current policy and send it again.", 409)), AgentJsonContext.Default.AgentApiResponseAgentMessageAcceptedResponse, 409);
            return;
        }
        catch (AgentContributionException)
        {
            await WriteAsync(context, AgentApiResponse<AgentMessageAcceptedResponse>.Failure(new("agent.message.delivery_conflict", "Another prompt is pending, or this delivery identity conflicts with an accepted prompt.", 409)), AgentJsonContext.Default.AgentApiResponseAgentMessageAcceptedResponse, 409);
            return;
        }
        await sessions.AddContextAsync(sessionId, contextItems, context.RequestAborted);
        var warnings = collected.Value.Diagnostics.Select(x => new AgentViolation(x.Code, x.Message, x.ContributorId)).ToList();
        await WriteAsync(context, AgentApiResponse<AgentMessageAcceptedResponse>.Success(message.ToAcceptedResponse(warnings)), AgentJsonContext.Default.AgentApiResponseAgentMessageAcceptedResponse);
    }

    /// <summary>Preview: the session's proposals with their statuses, previews and deadlines, for the session's owner only.</summary>
    private static async Task HandleListProposalsAsync(HttpContext context)
    {
        var (session, error) = await AgentSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IAgentSessionService>(),
            context.User, RouteValue(context, "sessionId"), context.RequestAborted);
        var result = error is null
            ? await context.RequestServices.GetRequiredService<IAgentProposalService>().ListAsync(session!.Id, context.RequestAborted)
            : new(null, error);
        await WriteAsync(context, result.Succeeded
                ? AgentApiResponse<IReadOnlyCollection<AgentActionProposal>>.Success(result.Value!.ToList())
                : AgentApiResponse<IReadOnlyCollection<AgentActionProposal>>.Failure(result.Error!),
            AgentJsonContext.Default.AgentApiResponseIReadOnlyCollectionAgentActionProposal, result.Succeeded ? 200 : result.Error!.StatusCode);
    }

    /// <summary>
    /// Preview: withdraws the owner's prompt that never started (Pending to Cancelled, reason <c>withdrawn</c>); refused
    /// with 409 <c>agent.message.not_pending</c> once it started, or while a turn is registered for the session.
    /// </summary>
    private static async Task HandleWithdrawMessageAsync(HttpContext context)
    {
        var sessions = context.RequestServices.GetRequiredService<IAgentSessionService>();
        var (session, error) = await AgentSessionAuthorization.AuthorizeAsync(sessions, context.User, RouteValue(context, "sessionId"), context.RequestAborted);
        var result = error is null ? await sessions.WithdrawMessageAsync(session!.Id, RouteValue(context, "messageId"), context.RequestAborted) : new(null, error);
        await WriteAsync(context, result.Succeeded
                ? AgentApiResponse<AgentMessageViewModelResponse>.Success(result.Value!.ToViewModel())
                : AgentApiResponse<AgentMessageViewModelResponse>.Failure(result.Error!),
            AgentJsonContext.Default.AgentApiResponseAgentMessageViewModelResponse, result.Succeeded ? 200 : result.Error!.StatusCode);
    }

    private static async Task HandleCancelTurnAsync(HttpContext context)
    {
        var request = await ReadAsync(context, AgentJsonContext.Default.AgentTurnCancelRequest);
        if (request is null)
            return;
        var sessionId = RouteValueOrFallback(context, "sessionId", request.SessionId);
        var (session, error) = await AgentSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IAgentSessionService>(), context.User, sessionId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, AgentApiResponse<AgentTurnCancelResponse>.Failure(error), AgentJsonContext.Default.AgentApiResponseAgentTurnCancelResponse, error.StatusCode);
            return;
        }
        var turnId = RouteValueOrFallback(context, "turnId", request.TurnId);
        var cancelled = context.RequestServices.GetRequiredService<IAgentTurnRegistry>().Cancel(session!.Id, turnId);
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
        try
        {
            await foreach (var item in context.RequestServices.GetRequiredService<IAgentStreamingService>().StreamAsync(sessionId, context.RequestAborted))
                await WriteEventAsync(context, item);
        }
        catch (Exception failure) when (AgentSessionOwnershipException.Find(failure) is { } ownership)
        {
            // Ownership is reported to the session's owner explicitly; the turn's recorded state is unchanged here.
            await WriteEventAsync(context, new(AgentProviderPrimitives.NewId(), AgentStreamEventKind.Error, null, null, OwnershipError(ownership), DateTimeOffset.UtcNow));
        }
        catch (Exception failure) when (AgentSessionUnavailableException.Find(failure) is { } unavailable)
        {
            await WriteEventAsync(context, new(AgentProviderPrimitives.NewId(), AgentStreamEventKind.Error, null, null, UnavailableError(unavailable), DateTimeOffset.UtcNow));
        }
        catch (AgentContributionException failure)
        {
            // The feature exposes its domain cause to this transport caller. Keep the causal
            // exception on the server; the browser receives only the fixed recovery disposition.
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(OwnerId)
                .LogError(failure, "Agent disclosure {Operation} failed for session {SessionId}.", "stream", sessionId);
            await WriteEventAsync(context, new(AgentProviderPrimitives.NewId(), AgentStreamEventKind.Error, null, null,
                new("agent.history.provenance_unavailable", "Complete disclosure history could not be retained or authorized. Recorded effects remain unchanged; retry when storage and authority are available.", 503), DateTimeOffset.UtcNow));
        }
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
        var proposalId = RouteValueOrFallback(context, "proposalId", request.ProposalId);
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
        if (!result.Succeeded && await FindTerminalExecutionFailureAsync(proposals, proposalId, context.RequestAborted) is { } terminalOutcome)
        {
            await WriteAsync(context, new AgentApiResponse<AgentProposalExecutionResult>(terminalOutcome, result.Error),
                AgentJsonContext.Default.AgentApiResponseAgentProposalExecutionResult, result.Error!.StatusCode);
            return;
        }
        await WriteAsync(context, result.Succeeded ? AgentApiResponse<AgentActionProposal>.Success(result.Value!) : AgentApiResponse<AgentActionProposal>.Failure(result.Error!), AgentJsonContext.Default.AgentApiResponseAgentActionProposal, result.Succeeded ? 200 : result.Error!.StatusCode);
    }

    private static async Task HandleDenyProposalAsync(HttpContext context)
    {
        var request = await ReadAsync(context, AgentJsonContext.Default.AgentProposalDecisionRequest);
        if (request is null)
            return;
        var proposalId = RouteValueOrFallback(context, "proposalId", request.ProposalId);
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
        if (!result.Succeeded && await FindTerminalExecutionFailureAsync(proposals, proposalId, context.RequestAborted) is { } terminalOutcome)
        {
            await WriteAsync(context, new AgentApiResponse<AgentProposalExecutionResult>(terminalOutcome, result.Error),
                AgentJsonContext.Default.AgentApiResponseAgentProposalExecutionResult, result.Error!.StatusCode);
            return;
        }
        await WriteAsync(context, result.Succeeded ? AgentApiResponse<AgentActionProposal>.Success(result.Value!) : AgentApiResponse<AgentActionProposal>.Failure(result.Error!), AgentJsonContext.Default.AgentApiResponseAgentActionProposal, result.Succeeded ? 200 : result.Error!.StatusCode);
    }

    private static async Task HandleExecuteProposalAsync(HttpContext context)
    {
        var request = await ReadAsync(context, AgentJsonContext.Default.AgentProposalDecisionRequest);
        if (request is null)
            return;
        var proposalId = RouteValueOrFallback(context, "proposalId", request.ProposalId);
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
        // A refusal recorded as a terminal outcome, such as 409 agent.proposal.policy_stale or agent.proposal.expired, keeps
        // its own error code and status and also carries the authoritative non-success outcome.
        var failureData = result.Succeeded ? null : await FindTerminalExecutionFailureAsync(proposals, proposalId, context.RequestAborted);
        var response = result.Succeeded ? AgentApiResponse<AgentProposalExecutionResult>.Success(result.Value!)
            : new AgentApiResponse<AgentProposalExecutionResult>(failureData, result.Error);
        await WriteAsync(context, response, AgentJsonContext.Default.AgentApiResponseAgentProposalExecutionResult,
            result.Succeeded ? result.Value!.ClientInvocation is not null ? 202 : 200 : result.Error!.StatusCode);
    }

    private static async Task<AgentProposalExecutionResult?> FindTerminalExecutionFailureAsync(
        IAgentProposalService proposals, string proposalId, CancellationToken cancellationToken)
    {
        var proposal = await proposals.FindAsync(proposalId, cancellationToken);
        if (proposal is null || proposal.Status is not (AgentActionProposalStatus.Expired or AgentActionProposalStatus.Failed
            or AgentActionProposalStatus.Denied or AgentActionProposalStatus.Cancelled))
            return null;
        var retained = await proposals.FindResultAsync(proposalId, cancellationToken);
        var summary = retained?.Content ?? (proposal.Status switch
        {
            AgentActionProposalStatus.Expired => "The prepared operation expired before dispatch.",
            AgentActionProposalStatus.Denied => "The reviewed operation was denied before dispatch.",
            AgentActionProposalStatus.Cancelled => "The prepared operation was cancelled before dispatch.",
            _ => "The operation ended with a failed outcome."
        });
        return new(proposal.Id, false, proposal.ResourceTarget?.ResourceType ?? proposal.ResourceType ?? "unavailable",
            proposal.ResourceTarget?.ResourceId ?? proposal.ResourceId ?? string.Empty, summary)
        {
            Status = proposal.Status.ToContractString(),
            ToolResult = retained
        };
    }

    private static async Task HandleClientAcknowledgementAsync(HttpContext context)
    {
        var request = await ReadAsync(context, AgentJsonContext.Default.AgentClientAcknowledgement);
        if (request is null)
            return;
        var sessionId = RouteValue(context, "sessionId");
        var (session, error) = await AgentSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IAgentSessionService>(), context.User, sessionId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, AgentApiResponse<AgentProposalExecutionResult>.Failure(error), AgentJsonContext.Default.AgentApiResponseAgentProposalExecutionResult, error.StatusCode);
            return;
        }
        var result = await context.RequestServices.GetRequiredService<IAgentProposalService>().AcknowledgeAsync(session!.Id,
            RouteValue(context, "invocationId"), session!.ActorId, request, context.RequestAborted);
        await WriteAsync(context, result.Succeeded ? AgentApiResponse<AgentProposalExecutionResult>.Success(result.Value!) : AgentApiResponse<AgentProposalExecutionResult>.Failure(result.Error!),
            AgentJsonContext.Default.AgentApiResponseAgentProposalExecutionResult, result.Succeeded ? 200 : result.Error!.StatusCode);
    }

    /// <summary>
    /// Preview: the owner's statement about an uncertain client operation. It is recorded on the proposal, never as a
    /// receipt, and releases only that operation's claim; repeating the same statement is idempotent.
    /// </summary>
    private static async Task HandleClientResolutionAsync(HttpContext context)
    {
        var request = await ReadAsync(context, AgentJsonContext.Default.AgentClientInvocationResolutionRequest);
        if (request is null)
            return;
        var (session, error) = await AgentSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IAgentSessionService>(),
            context.User, RouteValue(context, "sessionId"), context.RequestAborted);
        AgentResult<AgentActionProposal> result;
        if (error is not null)
            result = new(null, error);
        else if (AgentApiMapping.ParseResolutionClaim(request.Claim) is not { } claim)
            result = AgentResult<AgentActionProposal>.Failure("agent.invocation.resolution_invalid", "Send claim applied, notApplied or unknown.", 400);
        else
            result = await context.RequestServices.GetRequiredService<IAgentProposalService>().ResolveClientInvocationAsync(session!.Id,
                RouteValue(context, "invocationId"), session.ActorId, claim, context.RequestAborted);
        await WriteAsync(context, result.Succeeded ? AgentApiResponse<AgentActionProposal>.Success(result.Value!) : AgentApiResponse<AgentActionProposal>.Failure(result.Error!),
            AgentJsonContext.Default.AgentApiResponseAgentActionProposal, result.Succeeded ? 200 : result.Error!.StatusCode);
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
        if (sessionId is not null)
        {
            var (_, error) = await AgentSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IAgentSessionService>(),
                context.User, sessionId, context.RequestAborted);
            if (error is not null)
            {
                await WriteAsync(context, AgentApiResponse<IReadOnlyCollection<AgentAuditEvent>>.Failure(error), AgentJsonContext.Default.AgentApiResponseIReadOnlyCollectionAgentAuditEvent, error.StatusCode);
                return;
            }
        }
        var actorId = AgentEndpointActor.Resolve(context.User);
        var tenantId = AgentEndpointActor.ResolveTenant(context.User);
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(tenantId))
        {
            var unresolved = new AgentError("agent.actor.unresolved", "The current principal does not carry a resolvable actor or tenant identity.", 403);
            await WriteAsync(context, AgentApiResponse<IReadOnlyCollection<AgentAuditEvent>>.Failure(unresolved), AgentJsonContext.Default.AgentApiResponseIReadOnlyCollectionAgentAuditEvent, unresolved.StatusCode);
            return;
        }
        // The owning reader filters by ownership and orders before the limit. There is deliberately no
        // global-read or per-event session lookup fallback when the installed reader lacks the query.
        var result = await context.RequestServices.GetRequiredService<IAgentAuditReader>().ListOwnedAsync(
            new(actorId, tenantId, sessionId, Math.Clamp(take ?? 100, 1, AgentAuditOwnerQuery.MaxTake)), context.RequestAborted);
        if (!result.Succeeded)
        {
            var unsupported = new AgentError("agent.audit.query_unsupported", "The installed agent audit reader cannot answer owner-scoped audit queries.", 409);
            await WriteAsync(context, AgentApiResponse<IReadOnlyCollection<AgentAuditEvent>>.Failure(unsupported), AgentJsonContext.Default.AgentApiResponseIReadOnlyCollectionAgentAuditEvent, unsupported.StatusCode);
            return;
        }
        await WriteAsync(context, AgentApiResponse<IReadOnlyCollection<AgentAuditEvent>>.Success(result.Events), AgentJsonContext.Default.AgentApiResponseIReadOnlyCollectionAgentAuditEvent);
    }

    private static async Task WriteDeniedAsync(HttpContext context, string sessionId, AgentPolicyDecision decision)
    {
        await context.RequestServices.GetRequiredService<IAgentAuditSink>().EmitAsync(new(
            AgentProviderPrimitives.NewId(),
            AgentAuditEventKind.ContextDenied,
            sessionId,
            null,
            "Agent request denied by policy.",
            DateTimeOffset.UtcNow,
            decision.Violations.Select((violation, index) => new { Key = $"{index}:{violation.Code}", violation.Message }).ToDictionary(x => x.Key, x => x.Message)),
            context.RequestAborted);
        await WriteAsync(context, AgentApiResponse<AgentMessageAcceptedResponse>.Failure(new("agent.policyDenied", string.Join(" ", decision.Violations.Select(x => x.Message)), 403)), AgentJsonContext.Default.AgentApiResponseAgentMessageAcceptedResponse, 403);
    }

    private static async Task<T?> ReadAsync<T>(HttpContext context, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        if (string.IsNullOrWhiteSpace(context.Request.ContentType))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return default;
        }

        try
        {
            var value = await context.Request.ReadFromJsonAsync(typeInfo, context.RequestAborted);
            return value ?? JsonSerializer.Deserialize("{}", typeInfo);
        }
        catch (NotSupportedException)
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return default;
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

    private static string RouteValue(HttpContext context, string name) => context.Request.RouteValues.TryGetValue(name, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

    private static string RouteValueOrFallback(HttpContext context, string name, string fallback)
    {
        var routeValue = RouteValue(context, name);
        return string.IsNullOrWhiteSpace(routeValue) ? fallback : routeValue;
    }

    private static IReadOnlyCollection<string> BuildModes(IReadOnlyCollection<AgentCapability> capabilities)
    {
        var modes = capabilities.SelectMany(x => x.Modes).Distinct(StringComparer.Ordinal).ToList();
        return modes.Count == 0 ? ["explain"] : modes;
    }
}
