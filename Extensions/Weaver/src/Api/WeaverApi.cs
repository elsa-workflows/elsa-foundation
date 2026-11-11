using Elsa.Weaver.Api.Constants;
using Elsa.Weaver.Api.Endpoints;
using Elsa.Weaver.Api.Models;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Core.Services;
using Elsa.Api.AspNetCore;
using MinimalEndpoints;
using Elsa.Foundation.Identity.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Security.Cryptography;

namespace Elsa.Weaver.Api;

/// <summary>Maps the provider-agnostic Agent REST and SSE surface using ordinary ASP.NET Core endpoints.</summary>
public static class WeaverApi
{
    private const string OwnerId = "Elsa.Weaver.Api";
    private const string RoutePrefix = "/_elsa/weaver";

    public static void MapWeaverApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The Agent operations keep their own reads, writes, failure-carrying response envelopes,
        // and the SSE stream, so every operation stays on the group's raw seam; the group supplies
        // the shared metadata the mapper used to inline per endpoint.
        var api = endpoints.MapEndpointGroup(OwnerId, WeaverJsonContext.Default, jsonContentType: "application/json");

        Map(api, "GET", $"{RoutePrefix}/bootstrap", "Bootstrap", WeaverPermissionKeys.Use,
            null, typeof(WeaverApiResponse<WeaverBootstrapResponse>), HandleBootstrapAsync);
        Map(api, "POST", $"{RoutePrefix}/sessions", "CreateSession", WeaverPermissionKeys.Use,
            typeof(WeaverCreateSessionRequest), typeof(WeaverApiResponse<WeaverCreateSessionResponse>), HandleCreateSessionAsync);
        Map(api, "GET", $"{RoutePrefix}/sessions", "ListSessions", WeaverPermissionKeys.Use,
            typeof(WeaverSessionListQueryRequest), typeof(WeaverApiResponse<WeaverSessionListResponse>), HandleListSessionsAsync);
        Map(api, "GET", $"{RoutePrefix}/sessions/{{sessionId}}", "GetSession", WeaverPermissionKeys.Use,
            typeof(WeaverSessionRouteRequest), typeof(WeaverApiResponse<WeaverSessionDetailsResponse>), HandleGetSessionAsync);
        Map(api, "PUT", $"{RoutePrefix}/sessions/{{sessionId}}/autonomy", "ChangeAutonomy", WeaverPermissionKeys.Use,
            typeof(WeaverSessionAutonomyRequest), typeof(WeaverApiResponse<WeaverSessionPolicyResponse>), HandleChangeAutonomyAsync);
        Map(api, "POST", $"{RoutePrefix}/sessions/{{sessionId}}/messages", "PostMessage", WeaverPermissionKeys.Use,
            typeof(WeaverMessageRequest), typeof(WeaverApiResponse<WeaverMessageAcceptedResponse>), HandlePostMessageAsync);
        Map(api, "POST", $"{RoutePrefix}/sessions/{{sessionId}}/messages/{{messageId}}/withdraw", "WithdrawMessage", WeaverPermissionKeys.Use,
            typeof(WeaverSessionRouteRequest), typeof(WeaverApiResponse<WeaverMessageViewModelResponse>), HandleWithdrawMessageAsync);
        Map(api, "GET", $"{RoutePrefix}/sessions/{{sessionId}}/proposals", "ListSessionProposals", WeaverPermissionKeys.Use,
            typeof(WeaverSessionRouteRequest), typeof(WeaverApiResponse<IReadOnlyCollection<WeaverActionProposal>>), HandleListProposalsAsync);
        Map(api, "POST", $"{RoutePrefix}/sessions/{{sessionId}}/turns/{{turnId}}/cancel", "CancelTurn", WeaverPermissionKeys.Use,
            typeof(WeaverTurnCancelRequest), typeof(WeaverApiResponse<WeaverTurnCancelResponse>), HandleCancelTurnAsync);
        Map(api, "GET", $"{RoutePrefix}/sessions/{{sessionId}}/stream", "StreamSession", WeaverPermissionKeys.Use,
            typeof(WeaverSessionRouteRequest), null, HandleStreamSessionAsync, successStatus: StatusCodes.Status204NoContent);
        Map(api, "POST", $"{RoutePrefix}/feedback", "Feedback", WeaverPermissionKeys.Use,
            typeof(WeaverFeedbackApiRequest), typeof(WeaverApiResponse<WeaverFeedback>), HandleFeedbackAsync);
        Map(api, "POST", $"{RoutePrefix}/proposals/{{proposalId}}/approve", "ApproveProposal", WeaverPermissionKeys.Proposals,
            typeof(WeaverProposalDecisionRequest), typeof(WeaverApiResponse<WeaverActionProposal>), HandleApproveProposalAsync);
        Map(api, "POST", $"{RoutePrefix}/proposals/{{proposalId}}/deny", "DenyProposal", WeaverPermissionKeys.Proposals,
            typeof(WeaverProposalDecisionRequest), typeof(WeaverApiResponse<WeaverActionProposal>), HandleDenyProposalAsync);
        Map(api, "POST", $"{RoutePrefix}/proposals/{{proposalId}}/execute", "ExecuteProposal", WeaverPermissionKeys.Proposals,
            typeof(WeaverProposalDecisionRequest), typeof(WeaverApiResponse<WeaverProposalExecutionResult>), HandleExecuteProposalAsync);
        Map(api, "POST", $"{RoutePrefix}/sessions/{{sessionId}}/client-invocations/{{invocationId}}/acknowledgements", "AcknowledgeClientInvocation", WeaverPermissionKeys.Proposals,
            typeof(WeaverClientAcknowledgement), typeof(WeaverApiResponse<WeaverProposalExecutionResult>), HandleClientAcknowledgementAsync);
        Map(api, "POST", $"{RoutePrefix}/sessions/{{sessionId}}/client-invocations/{{invocationId}}/resolution", "ResolveClientInvocation", WeaverPermissionKeys.Proposals,
            typeof(WeaverClientInvocationResolutionRequest), typeof(WeaverApiResponse<WeaverActionProposal>), HandleClientResolutionAsync);
        Map(api, "GET", $"{RoutePrefix}/audit", "Audit", WeaverPermissionKeys.Audit,
            typeof(WeaverAuditQueryRequest), typeof(WeaverApiResponse<IReadOnlyCollection<WeaverAuditEvent>>), HandleAuditAsync);
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
                requestType == typeof(WeaverSessionRouteRequest) || requestType == typeof(WeaverAuditQueryRequest) || requestType == typeof(WeaverSessionListQueryRequest)
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
        catch (Exception failure) when (WeaverSessionOwnershipException.Find(failure) is { } ownership && !context.Response.HasStarted)
        {
            if (RetryAfterSeconds(context, ownership) is { } seconds)
                context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await WriteAsync(context, WeaverApiResponse<WeaverSessionPolicyResponse>.Failure(OwnershipError(ownership)),
                WeaverJsonContext.Default.WeaverApiResponseWeaverSessionPolicyResponse, StatusCodes.Status409Conflict);
        }
        catch (Exception failure) when (WeaverSessionUnavailableException.Find(failure) is { } unavailable && !context.Response.HasStarted)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverSessionPolicyResponse>.Failure(UnavailableError(unavailable)),
                WeaverJsonContext.Default.WeaverApiResponseWeaverSessionPolicyResponse, StatusCodes.Status409Conflict);
        }
    }

    private static WeaverError OwnershipError(WeaverSessionOwnershipException ownership) => new(ownership.Code, ownership.Message, StatusCodes.Status409Conflict);

    private static WeaverError UnavailableError(WeaverSessionUnavailableException unavailable)
        => new(WeaverSessionUnavailableException.Code, unavailable.Message, StatusCodes.Status409Conflict);

    private static long? RetryAfterSeconds(HttpContext context, WeaverSessionOwnershipException ownership)
    {
        if (ownership.RetryAt is not { } retryAt) return null;
        var now = (context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        return Math.Max(1, (long)Math.Ceiling((retryAt - now).TotalSeconds));
    }

    private static async Task HandleBootstrapAsync(HttpContext context)
    {
        var providers = context.RequestServices.GetRequiredService<IWeaverProviderRegistry>();
        var capabilities = context.RequestServices.GetRequiredService<IWeaverCapabilityCatalog>();
        var evaluator = context.RequestServices.GetRequiredService<IWeaverPolicyEvaluator>();
        var diagnostics = providers.Active is null ? null : await providers.Active.GetDiagnosticsAsync(context.RequestAborted);
        var availability = await evaluator.EvaluateAvailabilityAsync(WeaverPolicy.Default, context.RequestAborted);
        var listed = (await capabilities.ListAsync(context.RequestAborted)).ToList();
        var enabled = availability.Allowed && diagnostics?.IsAvailable == true;
        var modes = enabled ? BuildModes(listed) : [];
        var response = new WeaverBootstrapResponse(
            enabled,
            diagnostics?.IsAvailable == true ? "available" : "unavailable",
            modes,
            listed.Select(x => x.ToResponse()).ToList(),
            diagnostics?.ToResponse(),
            new(WeaverPolicy.Default.ContextVisibility,
                WeaverPolicy.Default.AutonomyMode.ToContractString(),
                WeaverPolicy.Default.MaxAutonomyMode.ToContractString(),
                WeaverPolicy.Default.AllowedAutonomyModes.Select(x => x.ToContractString()).ToList(),
                WeaverPolicy.Default.RetentionLabel))
        {
            Recovery = DescribeRecovery(context)
        };
        await WriteAsync(context, WeaverApiResponse<WeaverBootstrapResponse>.Success(response), WeaverJsonContext.Default.WeaverApiResponseWeaverBootstrapResponse);
    }

    /// <summary>
    /// The composition's durable-recovery support. A composition whose lifecycle services cannot be resolved reports
    /// unsupported, as the activation report does, so bootstrap still answers with its diagnostics.
    /// </summary>
    private static string DescribeRecovery(HttpContext context)
    {
        try { return context.RequestServices.GetService<IWeaverRecoverySupport>()?.Describe() ?? WeaverRecoveryModes.Unsupported; }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(OwnerId)
                .LogDebug("Durable agent recovery support could not be evaluated ({FailureType}).", failure.GetType().Name);
            return WeaverRecoveryModes.Unsupported;
        }
    }

    private static async Task HandleCreateSessionAsync(HttpContext context)
    {
        var request = await ReadAsync(context, WeaverJsonContext.Default.WeaverCreateSessionRequest);
        if (request is null)
            return;
        if (request.Workspace is { Length: > WeaverSessionCreateRequest.MaxWorkspaceLength } || request.ContinuedFrom is { Length: > WeaverSessionCreateRequest.MaxContinuedFromLength })
        {
            await WriteAsync(context, WeaverApiResponse<WeaverCreateSessionResponse>.Failure(new("weaver.session.invalid_request",
                $"A workspace is at most {WeaverSessionCreateRequest.MaxWorkspaceLength} characters and a continued session identity at most {WeaverSessionCreateRequest.MaxContinuedFromLength}.", 400)),
                WeaverJsonContext.Default.WeaverApiResponseWeaverCreateSessionResponse, 400);
            return;
        }
        var activeSurface = request.ActiveSurface ?? new WeaverSurfaceRequest();
        var clientContext = request.ClientContext ?? new WeaverClientContextRequest();
        var metadata = request.Metadata ?? new Dictionary<string, string>();
        var actorId = WeaverEndpointActor.Resolve(context.User);
        var tenantId = WeaverEndpointActor.ResolveTenant(context.User);
        if (actorId is null || tenantId is null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverCreateSessionResponse>.Failure(new("weaver.actor.unresolved", "The current principal does not carry a resolvable actor or tenant identity.", 403)), WeaverJsonContext.Default.WeaverApiResponseWeaverCreateSessionResponse, 403);
            return;
        }

        var providers = context.RequestServices.GetRequiredService<IWeaverProviderRegistry>();
        if (providers.Active is null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverCreateSessionResponse>.Failure(new("weaver.provider.not_found", "No agent harness is enabled.", 404)), WeaverJsonContext.Default.WeaverApiResponseWeaverCreateSessionResponse, 404);
            return;
        }
        var diagnostics = await providers.Active.GetDiagnosticsAsync(context.RequestAborted);
        if (!diagnostics.IsAvailable)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverCreateSessionResponse>.Failure(new("weaver.provider.unavailable", diagnostics.Status, 503)), WeaverJsonContext.Default.WeaverApiResponseWeaverCreateSessionResponse, 503);
            return;
        }

        var sessions = context.RequestServices.GetRequiredService<IWeaverSessionService>();
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
        var requestedAutonomy = WeaverApiMapping.ParseAutonomyMode(request.AutonomyMode) ?? WeaverPolicy.Default.AutonomyMode;
        var policy = WeaverPolicy.Default with { AutonomyMode = WeaverPolicy.Default.Clamp(requestedAutonomy) };
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
        IReadOnlyCollection<WeaverContextAttachment> attachments = [];
        var created = new WeaverCreateSessionResponse(session.Id, session.Status.ToContractString(), session.Title, attachments.Select(x => x.ToResponse()).ToList())
        {
            Policy = await ReadPolicyAsync(context, session.Id, actorId, tenantId)
        };
        await WriteAsync(context, WeaverApiResponse<WeaverCreateSessionResponse>.Success(created), WeaverJsonContext.Default.WeaverApiResponseWeaverCreateSessionResponse);
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
        var actorId = WeaverEndpointActor.Resolve(context.User);
        var tenantId = WeaverEndpointActor.ResolveTenant(context.User);
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(tenantId))
        {
            await WriteSessionListFailureAsync(context, new("weaver.actor.unresolved", "The current principal does not carry a resolvable actor or tenant identity.", 403));
            return;
        }
        var workspace = context.Request.Query["workspace"].FirstOrDefault();
        var query = new WeaverSessionOwnerQuery(actorId, tenantId, string.IsNullOrWhiteSpace(workspace) ? null : workspace,
            Math.Clamp(take ?? WeaverSessionOwnerQuery.DefaultTake, 1, WeaverSessionOwnerQuery.MaxTake), context.Request.Query["before"].FirstOrDefault());
        WeaverSessionListResult result;
        try { result = await context.RequestServices.GetRequiredService<IWeaverSessionService>().ListOwnedAsync(query, context.RequestAborted); }
        catch (ArgumentException) when (query.Before is not null)
        {
            await WriteBindingErrorAsync(context, "before", "The cursor is not one this server issued.");
            return;
        }
        if (!result.Succeeded || result.Page is not { } page)
        {
            await WriteSessionListFailureAsync(context, new("weaver.session.query_unsupported", "The installed agent session service cannot answer owner-scoped session queries.", 409));
            return;
        }
        await WriteAsync(context, WeaverApiResponse<WeaverSessionListResponse>.Success(new(page.Items.Select(x => x.ToResponse()).ToList(), page.Next)),
            WeaverJsonContext.Default.WeaverApiResponseWeaverSessionListResponse);
    }

    private static Task WriteSessionListFailureAsync(HttpContext context, WeaverError error) =>
        WriteAsync(context, WeaverApiResponse<WeaverSessionListResponse>.Failure(error), WeaverJsonContext.Default.WeaverApiResponseWeaverSessionListResponse, error.StatusCode);

    private static async Task HandleGetSessionAsync(HttpContext context)
    {
        var sessionId = RouteValue(context, "sessionId");
        var (session, error) = await WeaverSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IWeaverSessionService>(), context.User, sessionId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverSessionDetailsResponse>.Failure(error), WeaverJsonContext.Default.WeaverApiResponseWeaverSessionDetailsResponse, error.StatusCode);
            return;
        }
        var sessions = context.RequestServices.GetRequiredService<IWeaverSessionService>();
        var data = new WeaverSessionDetailsResponse(session!.Id, session.Status.ToContractString(), session.Title,
            (await sessions.ListContextAsync(sessionId, context.RequestAborted)).Select(x => x.ToResponse()).ToList(),
            (await sessions.ListMessagesAsync(sessionId, context.RequestAborted))
                .Where(x => x.Role != WeaverRole.Assistant || x.Content.Length > 0 || x.DisclosureHistory is null)
                .Select(x => x.ToViewModel()).ToList())
        {
            // Authorization above proved these resolve to the session owner.
            Policy = await ReadPolicyAsync(context, session.Id, WeaverEndpointActor.Resolve(context.User)!, WeaverEndpointActor.ResolveTenant(context.User)!),
            LimitReached = session.LimitReached
        };
        await WriteAsync(context, WeaverApiResponse<WeaverSessionDetailsResponse>.Success(data), WeaverJsonContext.Default.WeaverApiResponseWeaverSessionDetailsResponse);
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
        WeaverSessionAutonomyRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync(WeaverJsonContext.Default.WeaverSessionAutonomyRequest, context.RequestAborted);
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
        var requestedMode = WeaverApiMapping.ParseAutonomyMode(request?.AutonomyMode);
        if (request?.ExpectedRevision is not { } expectedRevision || requestedMode is not { } mode)
        {
            await WritePolicyFailureAsync(context, new(AutonomyInvalidCode, "Send a defined autonomy mode (manual, auto-read-only or full-auto) and the last acknowledged policy revision.", 400));
            return;
        }

        var sessionId = RouteValue(context, "sessionId");
        var (session, error) = await WeaverSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IWeaverSessionService>(), context.User, sessionId, context.RequestAborted);
        if (error is not null)
        {
            await WritePolicyFailureAsync(context, error);
            return;
        }
        var policies = context.RequestServices.GetService<IWeaverSessionPolicyService>();
        var result = policies is null
            ? new WeaverSessionPolicyResult(WeaverSessionPolicyOutcome.Unsupported, null)
            : await policies.ChangeAutonomyAsync(new(session!.Id, WeaverEndpointActor.Resolve(context.User)!, WeaverEndpointActor.ResolveTenant(context.User)!,
                mode, expectedRevision), context.RequestAborted);
        if (result is { Outcome: WeaverSessionPolicyOutcome.Succeeded, State: { } state })
        {
            await WriteAsync(context, WeaverApiResponse<WeaverSessionPolicyResponse>.Success(state.ToResponse()), WeaverJsonContext.Default.WeaverApiResponseWeaverSessionPolicyResponse);
            return;
        }
        await WritePolicyFailureAsync(context, result.Outcome switch
        {
            WeaverSessionPolicyOutcome.StaleRevision => new("weaver.session.policy_stale", "The session policy changed since the expected revision. Read the session back before retrying.", 409),
            WeaverSessionPolicyOutcome.Busy => new("weaver.session.busy", "Finish or resolve the current work before changing the session autonomy.", 409),
            WeaverSessionPolicyOutcome.SessionClosed => new("weaver.session.closed", "The agent session is closed or expired.", 409),
            WeaverSessionPolicyOutcome.InvalidMode => new(AutonomyInvalidCode, "The requested autonomy mode is not defined.", 400),
            WeaverSessionPolicyOutcome.NotFound => new("weaver.session.not_found", $"Agent session '{sessionId}' was not found.", 404),
            _ => new("weaver.session.autonomy_unsupported", "The installed agent session services cannot change the session autonomy.", 409)
        });
    }

    private const string AutonomyInvalidCode = "weaver.session.autonomy_invalid";

    private static Task WritePolicyFailureAsync(HttpContext context, WeaverError error) =>
        WriteAsync(context, WeaverApiResponse<WeaverSessionPolicyResponse>.Failure(error), WeaverJsonContext.Default.WeaverApiResponseWeaverSessionPolicyResponse, error.StatusCode);

    /// <summary>Reads the owner's acknowledged policy; null only when the installed composition cannot read it back.</summary>
    private static async Task<WeaverSessionPolicyResponse?> ReadPolicyAsync(HttpContext context, string sessionId, string actorId, string tenantId)
    {
        var policies = context.RequestServices.GetService<IWeaverSessionPolicyService>();
        if (policies is null)
            return null;
        var result = await policies.GetAsync(sessionId, actorId, tenantId, context.RequestAborted);
        return result is { Outcome: WeaverSessionPolicyOutcome.Succeeded, State: { } state } ? state.ToResponse() : null;
    }

    private static async Task HandlePostMessageAsync(HttpContext context)
    {
        var request = await ReadAsync(context, WeaverJsonContext.Default.WeaverMessageRequest);
        if (request is null)
            return;
        var sessionId = RouteValueOrFallback(context, "sessionId", request.SessionId);
        var requestedCapabilitiesInput = request.RequestedCapabilities ?? [];
        var contextAttachmentIds = request.ContextAttachmentIds ?? [];
        var contextAttachments = request.ContextAttachments ?? [];
        var sessions = context.RequestServices.GetRequiredService<IWeaverSessionService>();
        var (session, error) = await WeaverSessionAuthorization.AuthorizeAsync(sessions, context.User, sessionId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverMessageAcceptedResponse>.Failure(error), WeaverJsonContext.Default.WeaverApiResponseWeaverMessageAcceptedResponse, error.StatusCode);
            return;
        }
        sessionId = session!.Id;
        var evaluator = context.RequestServices.GetRequiredService<IWeaverPolicyEvaluator>();
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, WeaverJsonContext.Default.WeaverMessageRequest)));
        if (!string.IsNullOrWhiteSpace(request.RequestId))
        {
            var existing = (await sessions.ListMessagesAsync(sessionId, context.RequestAborted)).FirstOrDefault(x => x.Role == WeaverRole.User && x.RequestId == request.RequestId);
            if (existing is not null)
            {
                if (existing.DeliveryFingerprint != fingerprint)
                {
                    await WriteAsync(context, WeaverApiResponse<WeaverMessageAcceptedResponse>.Failure(new("weaver.message.delivery_conflict", "This prompt delivery identity was reused with different input.", 409)), WeaverJsonContext.Default.WeaverApiResponseWeaverMessageAcceptedResponse, 409);
                    return;
                }
                // A failure after accepted insertion can precede context retention. Recovery retains only the
                // original accepted message's own context; the store deduplicates by attachment identity.
                await sessions.AddContextAsync(sessionId, existing.Context, context.RequestAborted);
                await WriteAsync(context, WeaverApiResponse<WeaverMessageAcceptedResponse>.Success(existing.ToAcceptedResponse([new("weaver.message.duplicate", "This delivery was already accepted; it does not authorize another turn.", request.RequestId)])), WeaverJsonContext.Default.WeaverApiResponseWeaverMessageAcceptedResponse);
                return;
            }
        }
        if (context.RequestServices.GetRequiredService<IWeaverTurnRegistry>().IsActive(sessionId)
            || await context.RequestServices.GetRequiredService<IWeaverTurnStateStore>().FindAsync(sessionId, context.RequestAborted) is not null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverMessageAcceptedResponse>.Failure(new("weaver.session.busy", "Finish or resolve the current turn before sending another prompt.", 409)), WeaverJsonContext.Default.WeaverApiResponseWeaverMessageAcceptedResponse, 409);
            return;
        }
        var availability = await evaluator.EvaluateAvailabilityAsync(session!.Policy, context.RequestAborted);
        if (!availability.Allowed)
        {
            await WriteDeniedAsync(context, sessionId, availability);
            return;
        }
        var requestedCapabilities = requestedCapabilitiesInput.Count > 0 ? requestedCapabilitiesInput : string.IsNullOrWhiteSpace(request.CapabilityId) ? [] : [request.CapabilityId];
        var installedCapabilities = await context.RequestServices.GetRequiredService<IWeaverCapabilityCatalog>().ListAsync(context.RequestAborted);
        foreach (var capabilityId in requestedCapabilities)
        {
            if (!installedCapabilities.Any(x => x.Id == capabilityId))
            {
                await WriteDeniedAsync(context, sessionId, new(false, [new("weaver.capability.unavailable", "No installed module contributes the requested capability.", capabilityId)]));
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
            await WriteAsync(context, WeaverApiResponse<WeaverMessageAcceptedResponse>.Failure(new("weaver.context.required_unavailable",
                "A requested retained context attachment is unavailable. Refresh the current context before retrying.", 409)),
                WeaverJsonContext.Default.WeaverApiResponseWeaverMessageAcceptedResponse, 409);
            return;
        }
        var requestedStored = requestedContextIds.Count == 0 ? [] : stored.Where(x => requestedContextIds.Contains(x.Id)).ToList();
        var requestContext = requestedStored.Concat(contextAttachments.Select(x => x.ToDomain())).ToList();
        WeaverResult<WeaverContextContribution> collected;
        var surface = request.ActiveSurface ?? new WeaverSurfaceRequest
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
            collected = await context.RequestServices.GetRequiredService<IWeaverContextCollector>().CollectAsync(session.Policy,
                new(sessionId, surface.Route, surfaceInputs) { CallerAttachments = requestContext }, context.RequestAborted);
        }
        catch (WeaverContributionException)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverMessageAcceptedResponse>.Failure(new("weaver.context.composition_invalid", "Installed module context contributions conflict or cannot be transported.", 409)), WeaverJsonContext.Default.WeaverApiResponseWeaverMessageAcceptedResponse, 409);
            return;
        }
        if (!collected.Succeeded || collected.Value is null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverMessageAcceptedResponse>.Failure(collected.Error!), WeaverJsonContext.Default.WeaverApiResponseWeaverMessageAcceptedResponse, collected.Error!.StatusCode);
            return;
        }
        var contextItems = collected.Value.Attachments;
        var bindings = contextItems.Where(x => x.Client is not null).Select(x => x.Client!).Distinct().ToList();
        var messageRequest = new WeaverMessageCreateRequest(WeaverRole.User, string.IsNullOrWhiteSpace(request.Content) ? request.Message : request.Content,
            WeaverMessageStatus.Pending, requestedCapabilities.FirstOrDefault(), contextItems.Select(x => x.Id).ToList(), contextItems)
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
        WeaverMessage message;
        try { message = await sessions.AddMessageAsync(sessionId, messageRequest, context.RequestAborted); }
        catch (WeaverSessionPolicyChangedException)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverMessageAcceptedResponse>.Failure(new(WeaverSessionPolicyChangedException.Code, "The session policy changed while this prompt was being authorized. Review the current policy and send it again.", 409)), WeaverJsonContext.Default.WeaverApiResponseWeaverMessageAcceptedResponse, 409);
            return;
        }
        catch (WeaverContributionException)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverMessageAcceptedResponse>.Failure(new("weaver.message.delivery_conflict", "Another prompt is pending, or this delivery identity conflicts with an accepted prompt.", 409)), WeaverJsonContext.Default.WeaverApiResponseWeaverMessageAcceptedResponse, 409);
            return;
        }
        await sessions.AddContextAsync(sessionId, contextItems, context.RequestAborted);
        var warnings = collected.Value.Diagnostics.Select(x => new WeaverViolation(x.Code, x.Message, x.ContributorId)).ToList();
        await WriteAsync(context, WeaverApiResponse<WeaverMessageAcceptedResponse>.Success(message.ToAcceptedResponse(warnings)), WeaverJsonContext.Default.WeaverApiResponseWeaverMessageAcceptedResponse);
    }

    /// <summary>Preview: the session's proposals with their statuses, previews and deadlines, for the session's owner only.</summary>
    private static async Task HandleListProposalsAsync(HttpContext context)
    {
        var (session, error) = await WeaverSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IWeaverSessionService>(),
            context.User, RouteValue(context, "sessionId"), context.RequestAborted);
        var result = error is null
            ? await context.RequestServices.GetRequiredService<IWeaverProposalService>().ListAsync(session!.Id, context.RequestAborted)
            : new(null, error);
        await WriteAsync(context, result.Succeeded
                ? WeaverApiResponse<IReadOnlyCollection<WeaverActionProposal>>.Success(result.Value!.ToList())
                : WeaverApiResponse<IReadOnlyCollection<WeaverActionProposal>>.Failure(result.Error!),
            WeaverJsonContext.Default.WeaverApiResponseIReadOnlyCollectionWeaverActionProposal, result.Succeeded ? 200 : result.Error!.StatusCode);
    }

    /// <summary>
    /// Preview: withdraws the owner's prompt that never started (Pending to Cancelled, reason <c>withdrawn</c>); refused
    /// with 409 <c>agent.message.not_pending</c> once it started, or while a turn is registered for the session.
    /// </summary>
    private static async Task HandleWithdrawMessageAsync(HttpContext context)
    {
        var sessions = context.RequestServices.GetRequiredService<IWeaverSessionService>();
        var (session, error) = await WeaverSessionAuthorization.AuthorizeAsync(sessions, context.User, RouteValue(context, "sessionId"), context.RequestAborted);
        var result = error is null ? await sessions.WithdrawMessageAsync(session!.Id, RouteValue(context, "messageId"), context.RequestAborted) : new(null, error);
        await WriteAsync(context, result.Succeeded
                ? WeaverApiResponse<WeaverMessageViewModelResponse>.Success(result.Value!.ToViewModel())
                : WeaverApiResponse<WeaverMessageViewModelResponse>.Failure(result.Error!),
            WeaverJsonContext.Default.WeaverApiResponseWeaverMessageViewModelResponse, result.Succeeded ? 200 : result.Error!.StatusCode);
    }

    private static async Task HandleCancelTurnAsync(HttpContext context)
    {
        var request = await ReadAsync(context, WeaverJsonContext.Default.WeaverTurnCancelRequest);
        if (request is null)
            return;
        var sessionId = RouteValueOrFallback(context, "sessionId", request.SessionId);
        var (session, error) = await WeaverSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IWeaverSessionService>(), context.User, sessionId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverTurnCancelResponse>.Failure(error), WeaverJsonContext.Default.WeaverApiResponseWeaverTurnCancelResponse, error.StatusCode);
            return;
        }
        var turnId = RouteValueOrFallback(context, "turnId", request.TurnId);
        var cancelled = context.RequestServices.GetRequiredService<IWeaverTurnRegistry>().Cancel(session!.Id, turnId);
        await WriteAsync(context, WeaverApiResponse<WeaverTurnCancelResponse>.Success(new(turnId, cancelled)), WeaverJsonContext.Default.WeaverApiResponseWeaverTurnCancelResponse);
    }

    private static async Task HandleStreamSessionAsync(HttpContext context)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        var sessionId = RouteValue(context, "sessionId");
        var (_, error) = await WeaverSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IWeaverSessionService>(), context.User, sessionId, context.RequestAborted);
        if (error is not null)
        {
            await WriteEventAsync(context, new(WeaverProviderPrimitives.NewId(), WeaverStreamEventKind.Error, null, null, error, DateTimeOffset.UtcNow));
            return;
        }
        try
        {
            await foreach (var item in context.RequestServices.GetRequiredService<IWeaverStreamingService>().StreamAsync(sessionId, context.RequestAborted))
                await WriteEventAsync(context, item);
        }
        catch (Exception failure) when (WeaverSessionOwnershipException.Find(failure) is { } ownership)
        {
            // Ownership is reported to the session's owner explicitly; the turn's recorded state is unchanged here.
            await WriteEventAsync(context, new(WeaverProviderPrimitives.NewId(), WeaverStreamEventKind.Error, null, null, OwnershipError(ownership), DateTimeOffset.UtcNow));
        }
        catch (Exception failure) when (WeaverSessionUnavailableException.Find(failure) is { } unavailable)
        {
            await WriteEventAsync(context, new(WeaverProviderPrimitives.NewId(), WeaverStreamEventKind.Error, null, null, UnavailableError(unavailable), DateTimeOffset.UtcNow));
        }
        catch (WeaverContributionException failure)
        {
            // The feature exposes its domain cause to this transport caller. Keep the causal
            // exception on the server; the browser receives only the fixed recovery disposition.
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(OwnerId)
                .LogError(failure, "Agent disclosure {Operation} failed for session {SessionId}.", "stream", sessionId);
            await WriteEventAsync(context, new(WeaverProviderPrimitives.NewId(), WeaverStreamEventKind.Error, null, null,
                new("weaver.history.provenance_unavailable", "Complete disclosure history could not be retained or authorized. Recorded effects remain unchanged; retry when storage and authority are available.", 503), DateTimeOffset.UtcNow));
        }
    }

    private static async Task HandleFeedbackAsync(HttpContext context)
    {
        var request = await ReadAsync(context, WeaverJsonContext.Default.WeaverFeedbackApiRequest);
        if (request is null)
            return;
        var (session, error) = await WeaverSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IWeaverSessionService>(), context.User, request.SessionId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverFeedback>.Failure(error), WeaverJsonContext.Default.WeaverApiResponseWeaverFeedback, error.StatusCode);
            return;
        }
        var actorId = WeaverEndpointActor.Resolve(context.User);
        if (actorId is null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverFeedback>.Failure(new("weaver.actor.unresolved", "The current principal does not carry a resolvable actor identity.", 403)), WeaverJsonContext.Default.WeaverApiResponseWeaverFeedback, 403);
            return;
        }
        if (!string.IsNullOrWhiteSpace(request.MessageId) && await context.RequestServices.GetRequiredService<IWeaverSessionService>().FindMessageAsync(request.SessionId, request.MessageId, context.RequestAborted) is null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverFeedback>.Failure(new("weaver.message.not_found", $"Agent message '{request.MessageId}' was not found in session '{request.SessionId}'.", 404)), WeaverJsonContext.Default.WeaverApiResponseWeaverFeedback, 404);
            return;
        }
        var feedback = new WeaverFeedback(WeaverProviderPrimitives.NewId(), session!.Id, request.MessageId, request.Rating > 0 ? "positive" : "negative", request.Comment, actorId, DateTimeOffset.UtcNow);
        await WriteAsync(context, WeaverApiResponse<WeaverFeedback>.Success(await context.RequestServices.GetRequiredService<IWeaverFeedbackService>().AddAsync(feedback, context.RequestAborted)), WeaverJsonContext.Default.WeaverApiResponseWeaverFeedback);
    }

    private static async Task HandleApproveProposalAsync(HttpContext context)
    {
        var request = await ReadAsync(context, WeaverJsonContext.Default.WeaverProposalDecisionRequest);
        if (request is null)
            return;
        var proposalId = RouteValueOrFallback(context, "proposalId", request.ProposalId);
        var proposals = context.RequestServices.GetRequiredService<IWeaverProposalService>();
        var sessions = context.RequestServices.GetRequiredService<IWeaverSessionService>();
        var error = await WeaverProposalAuthorization.AuthorizeAsync(proposals, sessions, context.User, proposalId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverActionProposal>.Failure(error), WeaverJsonContext.Default.WeaverApiResponseWeaverActionProposal, error.StatusCode);
            return;
        }
        var actorId = WeaverEndpointActor.Resolve(context.User);
        if (actorId is null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverActionProposal>.Failure(new("weaver.actor.unresolved", "The current principal does not carry a resolvable actor identity.", 403)), WeaverJsonContext.Default.WeaverApiResponseWeaverActionProposal, 403);
            return;
        }
        var result = await proposals.ApproveAsync(proposalId, actorId, request.Revision, request.Comment, context.RequestAborted);
        if (!result.Succeeded && await FindTerminalExecutionFailureAsync(proposals, proposalId, context.RequestAborted) is { } terminalOutcome)
        {
            await WriteAsync(context, new WeaverApiResponse<WeaverProposalExecutionResult>(terminalOutcome, result.Error),
                WeaverJsonContext.Default.WeaverApiResponseWeaverProposalExecutionResult, result.Error!.StatusCode);
            return;
        }
        await WriteAsync(context, result.Succeeded ? WeaverApiResponse<WeaverActionProposal>.Success(result.Value!) : WeaverApiResponse<WeaverActionProposal>.Failure(result.Error!), WeaverJsonContext.Default.WeaverApiResponseWeaverActionProposal, result.Succeeded ? 200 : result.Error!.StatusCode);
    }

    private static async Task HandleDenyProposalAsync(HttpContext context)
    {
        var request = await ReadAsync(context, WeaverJsonContext.Default.WeaverProposalDecisionRequest);
        if (request is null)
            return;
        var proposalId = RouteValueOrFallback(context, "proposalId", request.ProposalId);
        var proposals = context.RequestServices.GetRequiredService<IWeaverProposalService>();
        var error = await WeaverProposalAuthorization.AuthorizeAsync(proposals, context.RequestServices.GetRequiredService<IWeaverSessionService>(), context.User, proposalId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverActionProposal>.Failure(error), WeaverJsonContext.Default.WeaverApiResponseWeaverActionProposal, error.StatusCode);
            return;
        }
        var actorId = WeaverEndpointActor.Resolve(context.User);
        if (actorId is null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverActionProposal>.Failure(new("weaver.actor.unresolved", "The current principal does not carry a resolvable actor identity.", 403)), WeaverJsonContext.Default.WeaverApiResponseWeaverActionProposal, 403);
            return;
        }
        var result = await proposals.DenyAsync(proposalId, actorId, request.Comment ?? request.Reason, context.RequestAborted);
        if (!result.Succeeded && await FindTerminalExecutionFailureAsync(proposals, proposalId, context.RequestAborted) is { } terminalOutcome)
        {
            await WriteAsync(context, new WeaverApiResponse<WeaverProposalExecutionResult>(terminalOutcome, result.Error),
                WeaverJsonContext.Default.WeaverApiResponseWeaverProposalExecutionResult, result.Error!.StatusCode);
            return;
        }
        await WriteAsync(context, result.Succeeded ? WeaverApiResponse<WeaverActionProposal>.Success(result.Value!) : WeaverApiResponse<WeaverActionProposal>.Failure(result.Error!), WeaverJsonContext.Default.WeaverApiResponseWeaverActionProposal, result.Succeeded ? 200 : result.Error!.StatusCode);
    }

    private static async Task HandleExecuteProposalAsync(HttpContext context)
    {
        var request = await ReadAsync(context, WeaverJsonContext.Default.WeaverProposalDecisionRequest);
        if (request is null)
            return;
        var proposalId = RouteValueOrFallback(context, "proposalId", request.ProposalId);
        var proposals = context.RequestServices.GetRequiredService<IWeaverProposalService>();
        var error = await WeaverProposalAuthorization.AuthorizeAsync(proposals, context.RequestServices.GetRequiredService<IWeaverSessionService>(), context.User, proposalId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverProposalExecutionResult>.Failure(error), WeaverJsonContext.Default.WeaverApiResponseWeaverProposalExecutionResult, error.StatusCode);
            return;
        }
        var actorId = WeaverEndpointActor.Resolve(context.User);
        if (actorId is null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverProposalExecutionResult>.Failure(new("weaver.actor.unresolved", "The current principal does not carry a resolvable actor identity.", 403)), WeaverJsonContext.Default.WeaverApiResponseWeaverProposalExecutionResult, 403);
            return;
        }
        var result = await proposals.ExecuteAsync(proposalId, actorId, request.Revision, context.RequestAborted);
        // A refusal recorded as a terminal outcome, such as 409 agent.proposal.policy_stale or agent.proposal.expired, keeps
        // its own error code and status and also carries the authoritative non-success outcome.
        var failureData = result.Succeeded ? null : await FindTerminalExecutionFailureAsync(proposals, proposalId, context.RequestAborted);
        var response = result.Succeeded ? WeaverApiResponse<WeaverProposalExecutionResult>.Success(result.Value!)
            : new WeaverApiResponse<WeaverProposalExecutionResult>(failureData, result.Error);
        await WriteAsync(context, response, WeaverJsonContext.Default.WeaverApiResponseWeaverProposalExecutionResult,
            result.Succeeded ? result.Value!.ClientInvocation is not null ? 202 : 200 : result.Error!.StatusCode);
    }

    private static async Task<WeaverProposalExecutionResult?> FindTerminalExecutionFailureAsync(
        IWeaverProposalService proposals, string proposalId, CancellationToken cancellationToken)
    {
        var proposal = await proposals.FindAsync(proposalId, cancellationToken);
        if (proposal is null || proposal.Status is not (WeaverActionProposalStatus.Expired or WeaverActionProposalStatus.Failed
            or WeaverActionProposalStatus.Denied or WeaverActionProposalStatus.Cancelled))
            return null;
        var retained = await proposals.FindResultAsync(proposalId, cancellationToken);
        var summary = retained?.Content ?? (proposal.Status switch
        {
            WeaverActionProposalStatus.Expired => "The prepared operation expired before dispatch.",
            WeaverActionProposalStatus.Denied => "The reviewed operation was denied before dispatch.",
            WeaverActionProposalStatus.Cancelled => "The prepared operation was cancelled before dispatch.",
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
        var request = await ReadAsync(context, WeaverJsonContext.Default.WeaverClientAcknowledgement);
        if (request is null)
            return;
        var sessionId = RouteValue(context, "sessionId");
        var (session, error) = await WeaverSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IWeaverSessionService>(), context.User, sessionId, context.RequestAborted);
        if (error is not null)
        {
            await WriteAsync(context, WeaverApiResponse<WeaverProposalExecutionResult>.Failure(error), WeaverJsonContext.Default.WeaverApiResponseWeaverProposalExecutionResult, error.StatusCode);
            return;
        }
        var result = await context.RequestServices.GetRequiredService<IWeaverProposalService>().AcknowledgeAsync(session!.Id,
            RouteValue(context, "invocationId"), session!.ActorId, request, context.RequestAborted);
        await WriteAsync(context, result.Succeeded ? WeaverApiResponse<WeaverProposalExecutionResult>.Success(result.Value!) : WeaverApiResponse<WeaverProposalExecutionResult>.Failure(result.Error!),
            WeaverJsonContext.Default.WeaverApiResponseWeaverProposalExecutionResult, result.Succeeded ? 200 : result.Error!.StatusCode);
    }

    /// <summary>
    /// Preview: the owner's statement about an uncertain client operation. It is recorded on the proposal, never as a
    /// receipt, and releases only that operation's claim; repeating the same statement is idempotent.
    /// </summary>
    private static async Task HandleClientResolutionAsync(HttpContext context)
    {
        var request = await ReadAsync(context, WeaverJsonContext.Default.WeaverClientInvocationResolutionRequest);
        if (request is null)
            return;
        var (session, error) = await WeaverSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IWeaverSessionService>(),
            context.User, RouteValue(context, "sessionId"), context.RequestAborted);
        WeaverResult<WeaverActionProposal> result;
        if (error is not null)
            result = new(null, error);
        else if (WeaverApiMapping.ParseResolutionClaim(request.Claim) is not { } claim)
            result = WeaverResult<WeaverActionProposal>.Failure("weaver.invocation.resolution_invalid", "Send claim applied, notApplied or unknown.", 400);
        else
            result = await context.RequestServices.GetRequiredService<IWeaverProposalService>().ResolveClientInvocationAsync(session!.Id,
                RouteValue(context, "invocationId"), session.ActorId, claim, context.RequestAborted);
        await WriteAsync(context, result.Succeeded ? WeaverApiResponse<WeaverActionProposal>.Success(result.Value!) : WeaverApiResponse<WeaverActionProposal>.Failure(result.Error!),
            WeaverJsonContext.Default.WeaverApiResponseWeaverActionProposal, result.Succeeded ? 200 : result.Error!.StatusCode);
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
            var (_, error) = await WeaverSessionAuthorization.AuthorizeAsync(context.RequestServices.GetRequiredService<IWeaverSessionService>(),
                context.User, sessionId, context.RequestAborted);
            if (error is not null)
            {
                await WriteAsync(context, WeaverApiResponse<IReadOnlyCollection<WeaverAuditEvent>>.Failure(error), WeaverJsonContext.Default.WeaverApiResponseIReadOnlyCollectionWeaverAuditEvent, error.StatusCode);
                return;
            }
        }
        var actorId = WeaverEndpointActor.Resolve(context.User);
        var tenantId = WeaverEndpointActor.ResolveTenant(context.User);
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(tenantId))
        {
            var unresolved = new WeaverError("weaver.actor.unresolved", "The current principal does not carry a resolvable actor or tenant identity.", 403);
            await WriteAsync(context, WeaverApiResponse<IReadOnlyCollection<WeaverAuditEvent>>.Failure(unresolved), WeaverJsonContext.Default.WeaverApiResponseIReadOnlyCollectionWeaverAuditEvent, unresolved.StatusCode);
            return;
        }
        // The owning reader filters by ownership and orders before the limit. There is deliberately no
        // global-read or per-event session lookup fallback when the installed reader lacks the query.
        var result = await context.RequestServices.GetRequiredService<IWeaverAuditReader>().ListOwnedAsync(
            new(actorId, tenantId, sessionId, Math.Clamp(take ?? 100, 1, WeaverAuditOwnerQuery.MaxTake)), context.RequestAborted);
        if (!result.Succeeded)
        {
            var unsupported = new WeaverError("weaver.audit.query_unsupported", "The installed agent audit reader cannot answer owner-scoped audit queries.", 409);
            await WriteAsync(context, WeaverApiResponse<IReadOnlyCollection<WeaverAuditEvent>>.Failure(unsupported), WeaverJsonContext.Default.WeaverApiResponseIReadOnlyCollectionWeaverAuditEvent, unsupported.StatusCode);
            return;
        }
        await WriteAsync(context, WeaverApiResponse<IReadOnlyCollection<WeaverAuditEvent>>.Success(result.Events), WeaverJsonContext.Default.WeaverApiResponseIReadOnlyCollectionWeaverAuditEvent);
    }

    private static async Task WriteDeniedAsync(HttpContext context, string sessionId, WeaverPolicyDecision decision)
    {
        await context.RequestServices.GetRequiredService<IWeaverAuditSink>().EmitAsync(new(
            WeaverProviderPrimitives.NewId(),
            WeaverAuditEventKind.ContextDenied,
            sessionId,
            null,
            "Agent request denied by policy.",
            DateTimeOffset.UtcNow,
            decision.Violations.Select((violation, index) => new { Key = $"{index}:{violation.Code}", violation.Message }).ToDictionary(x => x.Key, x => x.Message)),
            context.RequestAborted);
        await WriteAsync(context, WeaverApiResponse<WeaverMessageAcceptedResponse>.Failure(new("weaver.policyDenied", string.Join(" ", decision.Violations.Select(x => x.Message)), 403)), WeaverJsonContext.Default.WeaverApiResponseWeaverMessageAcceptedResponse, 403);
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

    private static async Task WriteEventAsync(HttpContext context, WeaverStreamEvent item)
    {
        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(item, WeaverSseJsonContext.Default.WeaverStreamEvent)}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }

    private static Task WriteBindingErrorAsync(HttpContext context, string field, string message) =>
        Results.Json(
            new WeaverBindingErrorResponse(400, "One or more errors occurred!", new Dictionary<string, string[]> { [field] = [message] }),
            WeaverJsonContext.Default.WeaverBindingErrorResponse,
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

    private static IReadOnlyCollection<string> BuildModes(IReadOnlyCollection<WeaverCapability> capabilities)
    {
        var modes = capabilities.SelectMany(x => x.Modes).Distinct(StringComparer.Ordinal).ToList();
        return modes.Count == 0 ? ["explain"] : modes;
    }
}
