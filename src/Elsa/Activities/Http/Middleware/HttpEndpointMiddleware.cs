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

        // Resolve the concrete endpoint-relative path to a published route template (spec 089 B). The route
        // table holds endpoint-relative templates; TemplateMatcher wants rooted paths, so both sides get a
        // leading slash for the match. First deterministic match wins (overlapping templates — e.g.
        // orders/{id} vs orders/list — are matched in route-table order, elsa-core parity).
        var (template, routeValues) = ResolveTemplate(endpointPath);
        if (template is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var stimulusHash = HttpEndpointStimulus.Hash(template, context.Request.Method);

        // Ambiguity guard (spec FR-009): a (template, method) claimed by more than one workflow definition is
        // authoring error, not fan-out — reject before any dispatch so neither workflow starts.
        var claimants = await triggerBindingStore.ListByStimulusAsync(HttpEndpointStimulus.StimulusType, stimulusHash, context.RequestAborted);
        if (claimants.Select(binding => binding.DefinitionId).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new { error = "ambiguous-endpoint", detail = $"More than one workflow claims {context.Request.Method} {template}." }),
                context.RequestAborted);
            return;
        }

        // Endpoint options ride the claimant binding's non-identity metadata (spec 089 C, FR-012..FR-014). The
        // ambiguity guard above proved all claimants share one definition, so any binding's metadata will do.
        var endpointOptions = EndpointOptions.FromMetadata(claimants.FirstOrDefault()?.Metadata);

        // Authorization runs before the body is read or anything is dispatched (FR-012). Fail closed: an
        // Authorize endpoint with no handler composed (WorkflowsRuntimeHttp feature absent) denies.
        if (endpointOptions.Authorize)
        {
            var authorizationHandler = context.RequestServices?.GetService(typeof(IHttpEndpointAuthorizationHandler)) as IHttpEndpointAuthorizationHandler;
            var authorized = authorizationHandler is not null
                && await authorizationHandler.AuthorizeAsync(new AuthorizeHttpEndpointContext(context, endpointOptions.Policy));
            if (!authorized)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        var maxBodyBytes = endpointOptions.RequestSizeLimit ?? _options.MaxRequestBodyBytes;
        var requestModel = await BuildRequestModelAsync(context, endpointPath, routeValues, maxBodyBytes, RequestServicesBodyParser(context));
        if (requestModel is null)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var input = JsonSerializer.SerializeToElement(requestModel);

        var dispatch = new StimulusDispatchRequest(
            stimulusType: HttpEndpointStimulus.StimulusType,
            stimulusHash: stimulusHash,
            input: input,
            mode: StimulusRoutingMode.StartOnly,
            requestedBy: RequestedBy);

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
    /// The per-endpoint options parsed from claimant-binding metadata (values written by
    /// <see cref="HttpEndpointTriggerStimulusProvider"/>; formats per data-model.md). Absent keys mean defaults.
    /// </summary>
    private sealed record EndpointOptions(bool Authorize, string? Policy, TimeSpan? RequestTimeout, long? RequestSizeLimit)
    {
        public static EndpointOptions FromMetadata(IReadOnlyDictionary<string, string>? metadata)
        {
            if (metadata is null)
                return new EndpointOptions(false, null, null, null);

            return new EndpointOptions(
                Authorize: metadata.TryGetValue(Elsa.Http.Core.HttpEndpointRouting.AuthorizeMetadataKey, out var authorize)
                    && bool.TryParse(authorize, out var parsedAuthorize) && parsedAuthorize,
                Policy: metadata.GetValueOrDefault(Elsa.Http.Core.HttpEndpointRouting.PolicyMetadataKey),
                RequestTimeout: metadata.TryGetValue(Elsa.Http.Core.HttpEndpointRouting.RequestTimeoutMetadataKey, out var timeout)
                    && TimeSpan.TryParseExact(timeout, "c", System.Globalization.CultureInfo.InvariantCulture, out var parsedTimeout)
                        ? parsedTimeout
                        : null,
                RequestSizeLimit: metadata.TryGetValue(Elsa.Http.Core.HttpEndpointRouting.RequestSizeLimitMetadataKey, out var sizeLimit)
                    && long.TryParse(sizeLimit, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedSizeLimit)
                        ? parsedSizeLimit
                        : null);
        }
    }

    /// <summary>Maps a dispatch fault to a response status via the endpoint fault handler seam; inline fallback when the policy feature is absent.</summary>
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

        // No handler composed: apply the same default mapping the policy module's handler uses.
        context.Response.StatusCode = faultException switch
        {
            TimeoutException or OperationCanceledException => StatusCodes.Status408RequestTimeout,
            HttpBadRequestException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
    }

    private static IHttpRequestBodyParser? RequestServicesBodyParser(HttpContext context) =>
        context.RequestServices?.GetService(typeof(IHttpRequestBodyParser)) as IHttpRequestBodyParser;

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

            var values = routeMatcher.Match("/" + template.TrimStart('/'), rootedPath);
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
        long maxBodyBytes,
        IHttpRequestBodyParser? bodyParser)
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

        // Parsed content (spec 089 C, FR-011): content-type-dispatched, wire-safe JsonElement; null when the
        // parser seam is absent (Http feature not composed), the type is unrecognized, or the body is empty.
        JsonElement? parsedContent = body is not null && bodyParser is not null
            ? bodyParser.Parse(context.Request.ContentType, body)
            : null;

        return new HttpRequestModel(
            Path: HttpEndpointStimulus.NormalizeTemplate(endpointPath),
            Method: context.Request.Method.ToUpperInvariant(),
            Headers: headers,
            Query: query,
            Body: body,
            RouteData: new Dictionary<string, string>(routeValues, StringComparer.OrdinalIgnoreCase),
            ParsedContent: parsedContent);
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
