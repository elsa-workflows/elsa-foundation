using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Http.Options;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Http.Middleware;

/// <summary>
/// The request middleware that turns an inbound HTTP request into an <see cref="HttpEndpoint"/> start stimulus
/// (W16, async/202 baseline). A request whose path is under <see cref="HttpEndpointOptions.BasePath"/> (matched
/// on a whole path segment, never a bare prefix) is mapped to the endpoint route, converted to the opaque
/// <c>(StimulusType, StimulusHash)</c> routing pair via <see cref="HttpEndpointStimulus"/>, and dispatched
/// through <see cref="IStimulusRouter"/> in <see cref="StimulusRoutingMode.StartOnly"/> mode. Any other request
/// passes through to the next middleware.
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
public sealed class HttpEndpointMiddleware(IStimulusRouter router, IOptions<HttpEndpointOptions> options) : IMiddleware
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

        var requestModel = await BuildRequestModelAsync(context, endpointPath, _options.MaxRequestBodyBytes);
        if (requestModel is null)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var input = JsonSerializer.SerializeToElement(requestModel);

        var dispatch = new StimulusDispatchRequest(
            stimulusType: HttpEndpointStimulus.StimulusType,
            stimulusHash: HttpEndpointStimulus.Hash(endpointPath),
            input: input,
            mode: StimulusRoutingMode.StartOnly,
            requestedBy: RequestedBy);

        var result = await router.RouteAsync(dispatch, context.RequestAborted);

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

    /// <summary>Builds the request model; null when the body exceeds <paramref name="maxBodyBytes"/>.</summary>
    private static async Task<HttpRequestModel?> BuildRequestModelAsync(HttpContext context, string endpointPath, long maxBodyBytes)
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

        return new HttpRequestModel(
            Path: HttpEndpointStimulus.NormalizePath(endpointPath),
            Method: context.Request.Method.ToUpperInvariant(),
            Headers: headers,
            Query: query,
            Body: body);
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
