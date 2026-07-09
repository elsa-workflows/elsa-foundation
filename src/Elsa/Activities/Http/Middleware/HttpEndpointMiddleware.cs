using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Http.Options;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Exceptions;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Http.Middleware;

/// <summary>
/// The request middleware that turns an inbound HTTP request into an <see cref="HttpEndpoint"/> start stimulus
/// (async/202 baseline; spec 089 B routing). A request whose path is under
/// <see cref="HttpEndpointOptions.BasePath"/> (matched on a whole path segment, never a bare prefix) has its
/// endpoint-relative path resolved against the per-shell <see cref="IRouteTable"/> via <see cref="IRouteMatcher"/>
/// (ASP.NET route templates, e.g. <c>orders/{id}</c>); the matched template plus the request method form the
/// stimulus identity (<see cref="HttpEndpointStimulus.Hash(string,string)"/>) dispatched through
/// <see cref="IStimulusRouter"/> in <see cref="StimulusRoutingMode.StartOnly"/> mode. Unmatched templates or
/// methods yield 404; a (template, method) claimed by more than one workflow definition yields 409 and starts
/// nothing. Any other request passes through to the next middleware.
/// </summary>
/// <remarks>
/// <para>
/// <b>Async/202.</b> The router's start path is asynchronous (it enqueues starts through the actor mailbox); the
/// middleware does not wait for the workflow to run. When at least one trigger matched it replies
/// <c>202 Accepted</c> with the started execution ids; when none matched it replies <c>404 Not Found</c>.
/// Synchronous request/response correlation is spec 089 sub-unit E.
/// </para>
/// <para>
/// <b>Live input.</b> The full <see cref="HttpRequestModel"/> (path, method, headers, query, body) is serialized
/// as the stimulus input; the router's start path carries it on the dedicated stimulus-input channel (spec 089
/// sub-unit A), where <see cref="HttpEndpoint"/> surfaces it as its Result. Bodies larger than
/// <see cref="HttpEndpointOptions.MaxRequestBodyBytes"/> are rejected with <c>413</c> before dispatch — the
/// payload becomes durable state on the started instance, so the transport guards its size.
/// </para>
/// <para>
/// Registered as an <see cref="IMiddleware"/> (resolved from DI per request) so it can take the scoped
/// <see cref="IStimulusRouter"/>; <see cref="ActivitiesHttpFeature"/> mounts it into the shell pipeline through
/// the CShells middleware seam.
/// </para>
/// </remarks>
public sealed class HttpEndpointMiddleware(
    IStimulusRouter router,
    IRouteTable routeTable,
    IRouteMatcher routeMatcher,
    IWorkflowTriggerBindingStore triggerBindingStore,
    IOptions<HttpEndpointOptions> options) : IMiddleware
{
    private const string RequestedBy = "http-endpoint";
    private readonly HttpEndpointOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var requestPath = context.Request.Path.Value ?? string.Empty;
        var basePath = _options.BasePath.TrimEnd('/');

        // An empty/root base path would make every request an endpoint candidate and turn unmatched routes into
        // 404s host-wide; workflow endpoints require a dedicated base path (see HttpEndpointOptions.BasePath).
        if (basePath.Length == 0)
        {
            await next(context);
            return;
        }

        // Segment-boundary match: '/workflows/http/orders' is ours; '/workflows/httpstatus' is a sibling route.
        if (!requestPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
            || (requestPath.Length > basePath.Length && requestPath[basePath.Length] != '/'))
        {
            await next(context);
            return;
        }

        var endpointPath = requestPath[basePath.Length..].Trim('/');
        if (string.IsNullOrWhiteSpace(endpointPath))
        {
            await next(context);
            return;
        }

        // From here on the request is ours to handle. A client disconnect cancels RequestAborted; any operation
        // observing it (the claimant lookup, the dispatch, a response write) throws OperationCanceledException.
        // There is no live connection to write a response to, so swallow it and return rather than letting it
        // escape as an unhandled pipeline exception (#592 item 12). A cancellation the request did NOT ask for
        // still propagates.
        try
        {
            await HandleEndpointAsync(context, endpointPath);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
    }

    private async Task HandleEndpointAsync(HttpContext context, string endpointPath)
    {
        // Resolve the concrete endpoint-relative path to a published route template (spec 089 B). The route
        // table holds endpoint-relative templates; TemplateMatcher wants rooted paths, so both sides get a
        // leading slash for the match. The route table enumerates most-specific-first (issue #592 item 1), so
        // "first match wins" is deterministic: a literal template (orders/list) beats a parameter template
        // (orders/{id}) regardless of publish/insertion order.
        var (template, routeValues) = ResolveTemplate(endpointPath);
        if (template is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var stimulusHash = HttpEndpointStimulus.Hash(template, context.Request.Method);

        // Fetch the claimants once (also the source of the endpoint options). The ambiguity guard below shares
        // this fetch rather than re-querying.
        var claimants = await triggerBindingStore.ListByStimulusAsync(HttpEndpointStimulus.StimulusType, stimulusHash, context.RequestAborted);

        // Endpoint options ride the claimant binding's non-identity metadata (spec 089 C, FR-012..FR-014). Sibling
        // claimants of one definition share options; on an ambiguous route (rejected below) any claimant's
        // Authorize flag is enough to know the endpoint is protected, so read the strongest: if ANY claimant
        // authorizes, authorization must pass before we disclose anything else.
        var endpointOptions = ResolveEndpointOptions(claimants);

        // Authorization runs before the body is read, before ambiguity/existence is disclosed, and before any
        // dispatch (FR-012, #592 item 10 — auth before disclosure). Fail closed: an Authorize endpoint with no
        // handler composed (WorkflowsRuntimeHttp feature absent) denies. A configuration fault (missing scheme /
        // unregistered policy) surfaces as 500, distinct from the 401 an anonymous caller gets (#592 item 11).
        if (endpointOptions.Authorize)
        {
            bool authorized;
            try
            {
                var authorizationHandler = context.RequestServices?.GetService(typeof(IHttpEndpointAuthorizationHandler)) as IHttpEndpointAuthorizationHandler;
                authorized = authorizationHandler is not null
                    && await authorizationHandler.AuthorizeAsync(new AuthorizeHttpEndpointContext(context, endpointOptions.Policy));
            }
            catch (HttpEndpointAuthorizationConfigurationException)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return;
            }

            if (!authorized)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        // Ambiguity guard (spec FR-009): a (template, method) claimed by more than one workflow definition is an
        // authoring error, not fan-out — reject before any dispatch so neither workflow starts. Evaluated only
        // after authorization, so an anonymous caller cannot distinguish an ambiguous route from a valid one. The
        // body is slimmed to a stable code — it must not echo the method/template to an (authorized) caller
        // (#592 item 10).
        if (claimants.Select(binding => binding.DefinitionId).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new { error = "ambiguous-endpoint" }),
                context.RequestAborted);
            return;
        }

        var maxBodyBytes = endpointOptions.RequestSizeLimit ?? _options.MaxRequestBodyBytes;
        var requestModel = await BuildRequestModelAsync(context, endpointPath, routeValues, maxBodyBytes);
        if (requestModel is null)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var input = JsonSerializer.SerializeToElement(requestModel);

        // Reuse the claimant set already fetched for the ambiguity guard + options: the router's start path would
        // otherwise issue an identical ListByStimulusAsync(type, hash) for the same request (spec 089 efficiency #7).
        var dispatch = new StimulusDispatchRequest(
            stimulusType: HttpEndpointStimulus.StimulusType,
            stimulusHash: stimulusHash,
            input: input,
            mode: StimulusRoutingMode.StartOnly,
            requestedBy: RequestedBy,
            matchedTriggerBindings: claimants);

        // Per-endpoint RequestTimeout bounds dispatch (which drains inline on the in-process actor, so it can
        // genuinely take time); faults map to statuses via the endpoint fault handler seam (FR-013/FR-014).
        // Non-positive values are rejected at publish (review C2); the > Zero guard here is defense in depth
        // against hand-seeded binding metadata — CancelAfter would throw on a negative TimeSpan.
        StimulusRoutingResult result;
        using var timeoutSource = endpointOptions.RequestTimeout is { } timeout && timeout > TimeSpan.Zero
            ? CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted)
            : null;
        timeoutSource?.CancelAfter(endpointOptions.RequestTimeout!.Value);
        try
        {
            result = await router.RouteAsync(dispatch, timeoutSource?.Token ?? context.RequestAborted);
        }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            // A genuine dispatch fault (not a client abort — that path is swallowed by the top-level guard in
            // InvokeAsync): map it to a status. A per-endpoint timeout trip is an OperationCanceledException on
            // the linked token while RequestAborted stays live, so it lands here (mapped to 408), not the guard.
            await HandleDispatchFaultAsync(context, exception, timedOut: timeoutSource?.IsCancellationRequested == true);
            return;
        }

        if (result.StartedCount == 0)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var startedIds = result.Starts
            .Where(start => start.WorkflowExecutionId is not null)
            .Select(start => start.WorkflowExecutionId!)
            .ToArray();

        context.Response.StatusCode = StatusCodes.Status202Accepted;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { started = startedIds }), context.RequestAborted);
    }

    /// <summary>
    /// Resolves the per-endpoint options from the claimant bindings' non-identity metadata. Sibling claimants of
    /// one definition share options, so the first claimant's metadata drives timeout/size/policy. Authorization is
    /// treated as the strongest claim across claimants: if ANY claimant requires authorization the endpoint is
    /// protected, so authorization runs (and an anonymous caller is denied) before route ambiguity is disclosed on
    /// an ambiguous route (#592 item 10). Empty claimants (unknown route already handled upstream) yield defaults.
    /// </summary>
    private static HttpEndpointStimulusOptions ResolveEndpointOptions(IEnumerable<WorkflowTriggerBinding> claimants)
    {
        HttpEndpointStimulusOptions? first = null;
        var authorizeAny = false;

        foreach (var claimant in claimants)
        {
            var options = HttpEndpointStimulusOptions.FromMetadata(claimant.Metadata);
            first ??= options;
            authorizeAny |= options.Authorize;
        }

        return (first ?? HttpEndpointStimulusOptions.None) with { Authorize = authorizeAny };
    }

    /// <summary>Maps a dispatch fault to a response status via the endpoint fault handler seam; inline fallback (the shared <see cref="Elsa.Http.Core.HttpEndpointFaultMapping"/>) when the policy feature is absent.</summary>
    private static async Task HandleDispatchFaultAsync(HttpContext context, Exception exception, bool timedOut)
    {
        var faultException = timedOut && exception is OperationCanceledException
            ? new TimeoutException("The endpoint's request timeout elapsed before dispatch completed.", exception)
            : exception;

        if (context.RequestServices?.GetService(typeof(IHttpEndpointFaultHandler)) is IHttpEndpointFaultHandler faultHandler)
        {
            await faultHandler.HandleAsync(new HttpEndpointFaultContext(context, [faultException], context.RequestAborted));
            return;
        }

        // No handler composed: apply the same default mapping the policy module's handler uses (shared owner).
        context.Response.StatusCode = Elsa.Http.Core.HttpEndpointFaultMapping.ToStatusCode(faultException);
    }

    /// <summary>
    /// Resolves the endpoint-relative path against the per-shell route table. Returns the matched template
    /// (endpoint-relative, as stored) plus its extracted route values, or (null, empty) when nothing matches.
    /// </summary>
    private (string? Template, IReadOnlyDictionary<string, string> RouteValues) ResolveTemplate(string endpointPath)
    {
        var rootedPath = "/" + endpointPath;

        foreach (var routeData in routeTable)
        {
            var template = routeData.Route;
            if (string.IsNullOrWhiteSpace(template))
                continue;

            // Reuse the route table's precompiled matcher (issue #592 item 6) — no per-request template parse.
            var values = routeMatcher.Match(routeData, rootedPath);
            if (values is null)
                continue;

            var routeValues = values.ToDictionary(
                item => item.Key,
                item => item.Value?.ToString() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
            return (template.Trim('/'), routeValues);
        }

        return (null, EmptyRouteValues);
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyRouteValues =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds the request model; null when the body exceeds <paramref name="maxBodyBytes"/>.</summary>
    private static async Task<HttpRequestModel?> BuildRequestModelAsync(
        HttpContext context,
        string endpointPath,
        IReadOnlyDictionary<string, string> routeValues,
        long maxBodyBytes)
    {
        if (context.Request.ContentLength is { } declaredLength && declaredLength > maxBodyBytes)
            return null;

        string? body = null;
        if (context.Request.Body.CanRead)
        {
            body = await ReadBodyBoundedAsync(context.Request.Body, maxBodyBytes, context.RequestAborted);
            if (body is null)
                return null; // Exceeded the cap during streaming (Content-Length absent or lied).
            if (body.Length == 0)
                body = null;
        }

        var headers = context.Request.Headers
            .ToDictionary(header => header.Key, header => header.Value.Select(v => v ?? string.Empty).ToArray(), StringComparer.OrdinalIgnoreCase);
        var query = context.Request.Query
            .ToDictionary(item => item.Key, item => item.Value.Select(v => v ?? string.Empty).ToArray(), StringComparer.OrdinalIgnoreCase);

        // Parsed content is NOT persisted here (spec 089 efficiency #9): the HttpEndpoint activity derives it from
        // Body + the Content-Type header via the deterministic IHttpRequestBodyParser seam, so the stimulus payload
        // carries the raw body exactly once instead of the body plus a re-encoded copy of it.
        return new HttpRequestModel(
            Path: HttpEndpointStimulus.NormalizeTemplate(endpointPath),
            Method: context.Request.Method.ToUpperInvariant(),
            Headers: headers,
            Query: query,
            Body: body,
            RouteData: new Dictionary<string, string>(routeValues, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Reads the body up to the byte cap (enforced during streaming, not only via Content-Length); null when exceeded.</summary>
    private static async Task<string?> ReadBodyBoundedAsync(Stream body, long maxBodyBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBodyBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
