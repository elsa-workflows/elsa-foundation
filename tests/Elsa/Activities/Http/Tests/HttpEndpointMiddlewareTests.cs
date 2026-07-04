using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Middleware;
using Elsa.Activities.Http.Options;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Unit coverage for the inbound <see cref="HttpEndpointMiddleware"/> (W16, async/202 baseline). It drives the
/// middleware with a <see cref="DefaultHttpContext"/> and a fake <see cref="IStimulusRouter"/> to prove the
/// request → stimulus → <c>StartOnly</c> dispatch mapping and the response contract: <c>202 Accepted</c> with the
/// started ids when a trigger matched, <c>404 Not Found</c> when none matched, and pass-through to the next
/// middleware for requests outside the configured base path.
/// </summary>
public sealed class HttpEndpointMiddlewareTests
{
    [Fact]
    public async Task MatchingRequest_DispatchesStartOnlyStimulus_AndRepliesAccepted()
    {
        var router = new FakeStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-42"));
        var middleware = new HttpEndpointMiddleware(router, Options());
        var context = NewContext("/workflows/http/orders/webhook", "POST", body: """{"id":7}""");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);

        // The request maps to the endpoint route (base path stripped, normalized) via the shared hashing scheme,
        // and is dispatched in StartOnly mode so it never resumes a waiting instance.
        var dispatched = Assert.Single(router.Requests);
        Assert.Equal(Activities.HttpEndpointStimulus.StimulusType, dispatched.StimulusType);
        Assert.Equal(Activities.HttpEndpointStimulus.Hash("orders/webhook"), dispatched.StimulusHash);
        Assert.Equal(StimulusRoutingMode.StartOnly, dispatched.Mode);
        Assert.NotNull(dispatched.Input);

        var payload = JsonDocument.Parse(await ReadResponse(context)).RootElement;
        var started = payload.GetProperty("started");
        Assert.Equal("wf-exec-42", started[0].GetString());
    }

    [Fact]
    public async Task MatchingRequest_WithNoStartedTriggers_RepliesNotFound()
    {
        var router = new FakeStimulusRouter();
        var middleware = new HttpEndpointMiddleware(router, Options());
        var context = NewContext("/workflows/http/unknown", "GET");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Single(router.Requests);
    }

    [Fact]
    public async Task RequestOutsideBasePath_PassesThroughToNext()
    {
        var router = new FakeStimulusRouter();
        var middleware = new HttpEndpointMiddleware(router, Options());
        var context = NewContext("/api/orders", "GET");
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        Assert.True(nextCalled);
        Assert.Empty(router.Requests);
    }

    [Fact]
    public async Task RequestAtBasePathRoot_PassesThroughToNext()
    {
        var router = new FakeStimulusRouter();
        var middleware = new HttpEndpointMiddleware(router, Options());
        var context = NewContext("/workflows/http", "GET");
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        Assert.True(nextCalled);
        Assert.Empty(router.Requests);
    }

    private static IOptions<HttpEndpointOptions> Options() => Microsoft.Extensions.Options.Options.Create(new HttpEndpointOptions());

    private static DefaultHttpContext NewContext(string path, string method, string? body = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        if (body is not null)
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadResponse(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private sealed class FakeStimulusRouter : IStimulusRouter
    {
        private readonly StimulusStartOutcome[] _starts;

        public FakeStimulusRouter(params StimulusStartOutcome[] starts) => _starts = starts;

        public List<StimulusDispatchRequest> Requests { get; } = new();

        public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new StimulusRoutingResult(_starts, Array.Empty<StimulusResumeOutcome>()));
        }
    }
}
