using System.Net;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;

namespace Elsa.Studio.Web;

/// <summary>
/// The Extension Builder half of the Studio management bridge (#256, ADR 0037): browser-facing Studio routes that
/// relay Extension Builder operations to the backend Elsa host. The browser calls Studio with only its normal
/// credentials; Studio attaches the server-side management key on the Studio→backend call, so the key never leaves the
/// server and the SPA never issues doomed, 401-noisy requests against backend host-control endpoints.
///
/// <para><see cref="Operations"/> is a declarative table that is simultaneously the allowlist (nothing outside it is
/// relayed) and the forwarding map: every route template is shape-identical relative to both the Studio route group
/// and the backend Extension Builder root, so the incoming path suffix is the outbound path suffix. Mutations ship
/// with <c>Bridged = false</c> in this PR and answer 501 without any outbound call; a follow-up flips them on.</para>
/// </summary>
internal static class StudioExtensionBuilderBridge
{
    /// <summary>The Studio-owned route group the browser calls. Nested under the management bridge group.</summary>
    public const string RouteGroup = StudioBackendManagementBridge.RouteGroup + "/extension-builder";

    /// <summary>The backend Extension Builder root the relay forwards to. A Studio→backend implementation detail.</summary>
    internal const string BackendRoot = "/_elsa/extension-builder";

    internal enum BridgeAccess
    {
        Read,
        Manage
    }

    internal enum BridgePayloadKind
    {
        Json,
        Text,
        Stream
    }

    /// <summary>
    /// One allowlisted Extension Builder operation. <see cref="Route"/> is relative to BOTH <see cref="RouteGroup"/>
    /// and <see cref="BackendRoot"/> (shape-identical by design). <see cref="TimeoutSeconds"/> is the per-request
    /// budget on the backend headers/JSON-body phase; Text/Stream body copies run outside it. <see cref="Bridged"/> is
    /// false for operations that are allowlisted but not yet relayed (they answer 501).
    /// </summary>
    internal sealed record BridgeOperation(
        string Name,
        string Method,
        string Route,
        BridgeAccess Access,
        BridgePayloadKind Payload = BridgePayloadKind.Json,
        int TimeoutSeconds = 8,
        bool Bridged = true);

    // The full allowlist. Deliberately NOT bridged at all: the backend's POST /projects/{projectId}/builds — Studio has
    // no caller for it. Reads relay now; mutations are table-complete but answer 501 until the mutation PR flips
    // Bridged to true row by row.
    internal static readonly IReadOnlyList<BridgeOperation> Operations =
    [
        // Read relays (extension-builder.read; manage implies read via the policy).
        new("list-templates", "GET", "/templates", BridgeAccess.Read),
        new("list-repositories", "GET", "/repositories", BridgeAccess.Read),
        new("list-workspaces", "GET", "/workspaces", BridgeAccess.Read),
        new("get-workspace", "GET", "/workspaces/{workspaceId}", BridgeAccess.Read),
        new("list-working-copies", "GET", "/workspaces/{workspaceId}/working-copies", BridgeAccess.Read),
        new("get-repository-tree", "GET", "/workspaces/{workspaceId}/repository-tree", BridgeAccess.Read),
        new("get-workspace-file", "GET", "/workspaces/{workspaceId}/files/{**path}", BridgeAccess.Read),
        new("get-source-control-status", "GET", "/workspaces/{workspaceId}/source-control/status", BridgeAccess.Read),
        new("get-source-control-diff", "GET", "/workspaces/{workspaceId}/source-control/diff/{**path}", BridgeAccess.Read),
        new("get-project", "GET", "/projects/{projectId}", BridgeAccess.Read),
        new("list-project-files", "GET", "/projects/{projectId}/files", BridgeAccess.Read),
        new("get-project-file", "GET", "/projects/{projectId}/files/{**path}", BridgeAccess.Read),
        new("get-project-runtime-status", "GET", "/projects/{projectId}/runtime-status", BridgeAccess.Read),
        new("get-build", "GET", "/builds/{buildId}", BridgeAccess.Read),
        new("get-build-log", "GET", "/builds/{buildId}/log", BridgeAccess.Read, BridgePayloadKind.Text, TimeoutSeconds: 30),
        new("get-build-artifact", "GET", "/builds/{buildId}/artifact", BridgeAccess.Read, BridgePayloadKind.Stream, TimeoutSeconds: 120),

        // Mutation relays (extension-builder.manage). Bridged = false in this PR: allowlisted, gated, and answering 501.
        new("create-workspace", "POST", "/workspaces", BridgeAccess.Manage, Bridged: false),
        new("delete-workspace", "DELETE", "/workspaces/{workspaceId}", BridgeAccess.Manage, TimeoutSeconds: 30, Bridged: false),
        new("add-server-local-repository", "POST", "/repositories/server-local", BridgeAccess.Manage, TimeoutSeconds: 30, Bridged: false),
        new("clone-repository", "POST", "/repositories/clone", BridgeAccess.Manage, TimeoutSeconds: 120, Bridged: false),
        new("select-working-copy", "POST", "/workspaces/{workspaceId}/working-copies/select", BridgeAccess.Manage, TimeoutSeconds: 30, Bridged: false),
        new("put-workspace-file", "PUT", "/workspaces/{workspaceId}/files/{**path}", BridgeAccess.Manage, Bridged: false),
        new("delete-workspace-file", "DELETE", "/workspaces/{workspaceId}/files/{**path}", BridgeAccess.Manage, Bridged: false),
        new("move-workspace-file", "POST", "/workspaces/{workspaceId}/files/move", BridgeAccess.Manage, Bridged: false),
        new("apply-template", "POST", "/workspaces/{workspaceId}/templates/apply", BridgeAccess.Manage, TimeoutSeconds: 60, Bridged: false),
        new("stage", "POST", "/workspaces/{workspaceId}/source-control/stage", BridgeAccess.Manage, Bridged: false),
        new("unstage", "POST", "/workspaces/{workspaceId}/source-control/unstage", BridgeAccess.Manage, Bridged: false),
        new("stage-all", "POST", "/workspaces/{workspaceId}/source-control/stage-all", BridgeAccess.Manage, Bridged: false),
        new("commit", "POST", "/workspaces/{workspaceId}/source-control/commit", BridgeAccess.Manage, TimeoutSeconds: 30, Bridged: false),
        new("push", "POST", "/workspaces/{workspaceId}/source-control/push", BridgeAccess.Manage, TimeoutSeconds: 120, Bridged: false),
        new("pull", "POST", "/workspaces/{workspaceId}/source-control/pull", BridgeAccess.Manage, TimeoutSeconds: 120, Bridged: false),
        new("start-build", "POST", "/workspaces/{workspaceId}/builds", BridgeAccess.Manage, Bridged: false),
        new("create-project", "POST", "/workspaces/{workspaceId}/projects", BridgeAccess.Manage, TimeoutSeconds: 60, Bridged: false),
        new("delete-project", "DELETE", "/projects/{projectId}", BridgeAccess.Manage, TimeoutSeconds: 30, Bridged: false),
        new("put-project-file", "PUT", "/projects/{projectId}/files/{**path}", BridgeAccess.Manage, Bridged: false),
        new("delete-project-file", "DELETE", "/projects/{projectId}/files/{**path}", BridgeAccess.Manage, Bridged: false),
        new("promote-build", "POST", "/builds/{buildId}/promote", BridgeAccess.Manage, TimeoutSeconds: 120, Bridged: false),
        new("promote-build-artifact", "POST", "/builds/{buildId}/artifacts/{artifactId}/promote", BridgeAccess.Manage, TimeoutSeconds: 120, Bridged: false),
        new("rollback-project", "POST", "/projects/{projectId}/rollback", BridgeAccess.Manage, TimeoutSeconds: 120, Bridged: false),
        new("retry-reconcile", "POST", "/projects/{projectId}/retry-reconcile", BridgeAccess.Manage, TimeoutSeconds: 120, Bridged: false)
    ];

    public static IEndpointRouteBuilder MapStudioExtensionBuilderBridge(this IEndpointRouteBuilder endpoints)
    {
        // Host-control permission gating (#249, ADR 0037): reads require `extension-builder.read` (manage implies read,
        // expanded locally in the policy); mutations require `extension-builder.manage`. When Studio auth is disabled
        // every policy allows anonymously (demo shell). The browser carries no Elsa host management key.
        var group = endpoints.MapGroup(RouteGroup);

        foreach (var op in Operations)
        {
            group.MapMethods(op.Route, [op.Method],
                    (HttpContext context, StudioExtensionBuilderRelayClient client) => client.RelayAsync(op, context))
                .RequireAuthorization(op.Access == BridgeAccess.Manage
                    ? StudioBridgeAuth.ExtensionBuilderManagePolicyName
                    : StudioBridgeAuth.ExtensionBuilderReadPolicyName);
        }

        return endpoints;
    }
}

/// <summary>
/// The Studio-owned error body every non-relayed bridge answer carries (503/504/501). <see cref="Management"/> reuses
/// the bridge's explicit backend-management status envelope (null on 501, where the backend state is not in question).
/// The 503/504 + this shape is the browser-side discriminator: relayed backend domain responses never produce these
/// status codes, so the SPA can tell "Studio infrastructure said no" from "the backend's Extension Builder said no".
/// </summary>
internal sealed record StudioExtensionBuilderBridgeError(string Detail, StudioBackendManagementStatus? Management);

/// <summary>
/// Relays a single allowlisted Extension Builder operation to the backend Elsa host. Only the management key
/// (<see cref="StudioBackendManagementOptions.ManagementApiKeyHeaderName"/>), an <c>Accept</c>, and — for bodied
/// requests — the request <c>Content-Type</c> + streamed body cross to the backend; the browser's
/// <c>Authorization</c> header, cookies, and all other request headers never do. Fails closed exactly like the probe
/// client: unconfigured means ZERO outbound calls.
/// </summary>
internal sealed class StudioExtensionBuilderRelayClient(
    HttpClient httpClient,
    StudioBackendManagementOptions options,
    TimeProvider timeProvider,
    ILogger<StudioExtensionBuilderRelayClient> logger)
{
    private const string NotBridgedDetail = "This action is not yet available through Studio. Workspace mutations are being routed through the Studio management bridge (issue #256).";
    private const string UnconfiguredDetail = "Backend management is not configured on the Studio host. Set Studio:BackendBaseUrl and Studio:BackendModuleManagementApiKey to enable Extension Builder.";
    private const string UnauthorizedDetail = "The backend rejected the Studio management key (or the Extension Builder surface is disabled). Verify Studio:BackendModuleManagementApiKey matches the backend host management key.";
    private const string UnreachableDetail = "The backend Extension Builder surface could not be reached. Check that the backend host is running and Studio:BackendBaseUrl is correct.";
    private const string TimedOutDetail = "The backend Extension Builder surface did not answer within the operation's time budget. The operation may still have completed on the backend.";
    private const string UnrecognizedPayloadDetail = "The backend responded but did not return a recognizable Extension Builder payload.";

    public async Task RelayAsync(StudioExtensionBuilderBridge.BridgeOperation operation, HttpContext context)
    {
        // Fail closed first: without a backend base URL or a management key ZERO outbound calls are issued (ADR 0037),
        // even for not-yet-bridged operations — the infrastructure plane dominates.
        if (!options.IsConfigured)
        {
            await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, StudioBackendManagementStatus.Unconfigured, UnconfiguredDetail);
            return;
        }

        if (!operation.Bridged)
        {
            await WriteErrorAsync(context, StatusCodes.Status501NotImplemented, managementStatus: null, NotBridgedDetail);
            return;
        }

        // The incoming Studio path suffix IS the backend path suffix (the table's templates are shape-identical), and
        // GetEncodedPathAndQuery preserves segment encoding and relays the query string as-is. The suffix is located by
        // searching for the route group rather than slicing at its length because GetEncodedPathAndQuery prepends any
        // PathBase the Studio host is mounted under.
        var encodedPathAndQuery = context.Request.GetEncodedPathAndQuery();
        var routeGroupIndex = encodedPathAndQuery.IndexOf(StudioExtensionBuilderBridge.RouteGroup, StringComparison.Ordinal);
        var backendPathAndQuery = string.Concat(
            StudioExtensionBuilderBridge.BackendRoot,
            encodedPathAndQuery.AsSpan(routeGroupIndex + StudioExtensionBuilderBridge.RouteGroup.Length));

        // Per-operation budget over the browser-abort token. The budget covers connect + response headers + buffered
        // (JSON) body reads; Text/Stream body copies run outside it so a long log/artifact download is bounded only by
        // the browser connection.
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(operation.TimeoutSeconds), timeProvider);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, timeoutSource.Token);

        HttpResponseMessage? response = null;
        try
        {
            using var request = BuildBackendRequest(operation, context, backendPathAndQuery);
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            await MapAndRelayResponseAsync(operation, context, response, budget.Token);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The browser went away; nothing to answer.
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("The Extension Builder relay operation {Operation} exceeded its {TimeoutSeconds}s budget against {BackendBaseUrl}.",
                operation.Name, operation.TimeoutSeconds, options.BackendBaseUrl);
            await WriteErrorAsync(context, StatusCodes.Status504GatewayTimeout, StudioBackendManagementStatus.Unreachable, TimedOutDetail);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Studio could not reach the backend Extension Builder surface at {BackendBaseUrl} for operation {Operation}.",
                options.BackendBaseUrl, operation.Name);
            await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, StudioBackendManagementStatus.Unreachable, UnreachableDetail);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private HttpRequestMessage BuildBackendRequest(
        StudioExtensionBuilderBridge.BridgeOperation operation,
        HttpContext context,
        string backendPathAndQuery)
    {
        var request = new HttpRequestMessage(new HttpMethod(operation.Method), backendPathAndQuery);
        request.Headers.TryAddWithoutValidation(StudioBackendManagementOptions.ManagementApiKeyHeaderName, options.ManagementApiKey);
        request.Headers.TryAddWithoutValidation("Accept", AcceptFor(operation.Payload));

        // Bodied methods stream the browser body through with its Content-Type; nothing else is forwarded. Body
        // presence comes from the server's body-detection feature because HTTP/2+ requests can carry a body with
        // neither a Content-Length nor a Transfer-Encoding header.
        var canHaveBody = context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody
            ?? context.Request.ContentLength > 0;
        if (canHaveBody)
        {
            request.Content = new StreamContent(context.Request.Body);
            if (!string.IsNullOrEmpty(context.Request.ContentType))
                request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
        }

        return request;
    }

    private static string AcceptFor(StudioExtensionBuilderBridge.BridgePayloadKind payload) => payload switch
    {
        StudioExtensionBuilderBridge.BridgePayloadKind.Text => "text/plain",
        StudioExtensionBuilderBridge.BridgePayloadKind.Stream => "application/octet-stream",
        _ => "application/json"
    };

    private async Task MapAndRelayResponseAsync(
        StudioExtensionBuilderBridge.BridgeOperation operation,
        HttpContext context,
        HttpResponseMessage response,
        CancellationToken budgetToken)
    {
        var isJson = HasJsonContentType(response);

        // Plane A (infrastructure, Studio-owned bodies — backend error bodies are never echoed): 401/403 mean the
        // management key was rejected; a bare (non-JSON) 404 is the backend hiding a disabled management surface —
        // both collapse to `unauthorized`, same as the probe. A 404 WITH a JSON body is a domain not-found and relays
        // on plane B below.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ||
            (response.StatusCode == HttpStatusCode.NotFound && !isJson))
        {
            await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, StudioBackendManagementStatus.Unauthorized, UnauthorizedDetail);
            return;
        }

        if (response.IsSuccessStatusCode)
        {
            // A 2xx that isn't JSON on a JSON operation means we hit something other than the surface (e.g. an SPA
            // fallback page). Content-type sniff only — arrays and any other JSON shape are valid relay payloads.
            if (operation.Payload == StudioExtensionBuilderBridge.BridgePayloadKind.Json && !isJson)
            {
                await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, StudioBackendManagementStatus.Degraded, UnrecognizedPayloadDetail);
                return;
            }

            await RelayBodyAsync(operation, context, response, budgetToken);
            return;
        }

        // Plane B (domain relay): backend 400/404/409 with JSON bodies are Extension Builder domain answers the SPA
        // must see verbatim (status code + body + Content-Type).
        if (isJson && response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            await RelayBufferedAsync(context, response, budgetToken);
            return;
        }

        // 5xx (and any other unexpected non-success): the surface exists but is unhealthy.
        logger.LogWarning("The backend Extension Builder surface at {BackendBaseUrl} responded with {StatusCode} for operation {Operation}.",
            options.BackendBaseUrl, (int)response.StatusCode, operation.Name);
        await WriteErrorAsync(
            context,
            StatusCodes.Status503ServiceUnavailable,
            StudioBackendManagementStatus.Degraded,
            $"The backend Extension Builder surface responded with an unexpected status ({(int)response.StatusCode}).");
    }

    private static async Task RelayBodyAsync(
        StudioExtensionBuilderBridge.BridgeOperation operation,
        HttpContext context,
        HttpResponseMessage response,
        CancellationToken budgetToken)
    {
        if (operation.Payload == StudioExtensionBuilderBridge.BridgePayloadKind.Json)
        {
            await RelayBufferedAsync(context, response, budgetToken);
            return;
        }

        // Text/Stream: relay headers, then copy the body straight to the browser OUTSIDE the CTS budget — a long
        // build-log or artifact download must not be killed by the headers budget.
        context.Response.StatusCode = (int)response.StatusCode;
        CopyContentType(context, response);
        if (operation.Payload == StudioExtensionBuilderBridge.BridgePayloadKind.Stream)
        {
            if (response.Content.Headers.ContentDisposition is { } contentDisposition)
                context.Response.Headers.ContentDisposition = contentDisposition.ToString();
            if (response.Content.Headers.ContentLength is { } contentLength)
                context.Response.ContentLength = contentLength;
        }

        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private static async Task RelayBufferedAsync(HttpContext context, HttpResponseMessage response, CancellationToken budgetToken)
    {
        // Buffer under the budget so a stalled body still maps to 504 instead of hanging after headers were relayed.
        var body = await response.Content.ReadAsByteArrayAsync(budgetToken);
        context.Response.StatusCode = (int)response.StatusCode;
        CopyContentType(context, response);
        await context.Response.Body.WriteAsync(body, context.RequestAborted);
    }

    private static void CopyContentType(HttpContext context, HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentType is { } contentType)
            context.Response.ContentType = contentType.ToString();
    }

    private static bool HasJsonContentType(HttpResponseMessage response)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        return mediaType is not null && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase);
    }

    private async Task WriteErrorAsync(HttpContext context, int statusCode, string? managementStatus, string detail)
    {
        // A body-copy failure after headers went out cannot be turned into a Studio error answer anymore.
        if (context.Response.HasStarted)
        {
            context.Abort();
            return;
        }

        var management = managementStatus is null
            ? null
            : new StudioBackendManagementStatus(managementStatus, detail, options.NormalizedBackendBaseUrl, timeProvider.GetUtcNow());

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new StudioExtensionBuilderBridgeError(detail, management), context.RequestAborted);
    }
}
