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
/// (W16, async/202 baseline). A request whose path is under <see cref="HttpEndpointOptions.BasePath"/> is mapped
/// to the endpoint route, converted to the opaque <c>(StimulusType, StimulusHash)</c> routing pair via
/// <see cref="HttpEndpointStimulus"/>, and dispatched through <see cref="IStimulusRouter"/> in
/// <see cref="StimulusRoutingMode.StartOnly"/> mode. Any other request passes through to the next middleware.
/// </summary>
/// <remarks>
/// <para>
/// <b>Async/202.</b> The router's start path is asynchronous (it enqueues starts through the actor mailbox); the
/// middleware does not wait for the workflow to run. When at least one trigger matched it replies
/// <c>202 Accepted</c> with the started execution ids; when none matched it replies <c>404 Not Found</c>.
/// Synchronous request/response correlation (blocking the caller for the workflow's <see cref="WriteHttpResponse"/>
/// artifact) is the separate "HTTP synchronous response correlation" follow-up.
/// </para>
/// <para>
/// <b>Forward-compatible input.</b> The full <see cref="HttpRequestModel"/> (path, method, headers, query, body)
/// is serialized as the stimulus input even though the current runtime start path does not thread stimulus
/// input into started instances — so when start-input delivery lands the live request is available with no wire
/// change. See <see cref="HttpRequestModel"/>.
/// </para>
/// <para>
/// Registered as an <see cref="IMiddleware"/> (resolved from DI per request) so it can take the scoped
/// <see cref="IStimulusRouter"/>; a host adds it with <c>app.UseMiddleware&lt;HttpEndpointMiddleware&gt;()</c>.
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

        if (!requestPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var endpointPath = requestPath[basePath.Length..].Trim('/');
        if (string.IsNullOrEmpty(endpointPath))
        {
            await next(context);
            return;
        }

        var requestModel = await BuildRequestModelAsync(context, endpointPath);
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

    private static async Task<HttpRequestModel> BuildRequestModelAsync(HttpContext context, string endpointPath)
    {
        string? body = null;
        if (context.Request.Body.CanRead)
        {
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            body = await reader.ReadToEndAsync(context.RequestAborted);
            if (string.IsNullOrEmpty(body))
                body = null;
        }

        var headers = context.Request.Headers
            .ToDictionary(header => header.Key, header => header.Value.Select(v => v ?? string.Empty).ToArray(), StringComparer.OrdinalIgnoreCase);
        var query = context.Request.Query
            .ToDictionary(item => item.Key, item => item.Value.Select(v => v ?? string.Empty).ToArray(), StringComparer.OrdinalIgnoreCase);

        return new HttpRequestModel(
            Path: HttpEndpointStimulus.NormalizePath(endpointPath),
            Method: context.Request.Method,
            Headers: headers,
            Query: query,
            Body: body);
    }
}
