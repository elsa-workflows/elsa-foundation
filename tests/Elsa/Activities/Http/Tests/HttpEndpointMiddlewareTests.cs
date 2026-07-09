using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Middleware;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Testing;
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
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-42"));
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
        var router = new RecordingStimulusRouter();
        var middleware = new HttpEndpointMiddleware(router, Options());
        var context = NewContext("/workflows/http/unknown", "GET");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Single(router.Requests);
    }

    [Fact]
    public async Task RequestOutsideBasePath_PassesThroughToNext()
    {
        var router = new RecordingStimulusRouter();
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
        var router = new RecordingStimulusRouter();
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

    [Fact]
    public async Task SiblingPathSharingThePrefixText_PassesThroughToNext()
    {
        // Spec 089 review V1: '/workflows/httpstatus' shares the prefix TEXT of '/workflows/http' but is a
        // different path segment — it must never be captured as an endpoint route.
        var router = new RecordingStimulusRouter();
        var middleware = new HttpEndpointMiddleware(router, Options());
        var context = NewContext("/workflows/httpstatus/foo", "GET");
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
    public async Task WhitespaceOnlyEndpointPath_PassesThroughToNext()
    {
        // Spec 089 review V2: '/workflows/http/%20' decodes to a whitespace-only route; it must pass through
        // cleanly instead of reaching NormalizePath (which throws on whitespace → 500).
        var router = new RecordingStimulusRouter();
        var middleware = new HttpEndpointMiddleware(router, Options());
        var context = NewContext("/workflows/http/ ", "GET");
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
    public async Task EmptyBasePath_DisablesTheMiddleware_EverythingPassesThrough()
    {
        // Spec 089 review V4: an empty/root base path must never turn the middleware into a host-wide
        // catch-all that 404s unrelated routes.
        var router = new RecordingStimulusRouter();
        var middleware = new HttpEndpointMiddleware(router, Options(basePath: "/"));
        var context = NewContext("/health", "GET");
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
    public async Task LowercaseHttpMethod_IsUppercasedOnTheRequestModel()
    {
        // Spec 089 review V7: the documented contract is an uppercase method; raw clients may send
        // non-standard casing and Kestrel forwards the verb token verbatim.
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var middleware = new HttpEndpointMiddleware(router, Options());
        var context = NewContext("/workflows/http/orders/webhook", "post");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        var dispatched = Assert.Single(router.Requests);
        Assert.Equal("POST", dispatched.Input!.Value.GetProperty("Method").GetString());
    }

    [Fact]
    public async Task BodyLargerThanTheCap_IsRejectedWith413_BeforeAnyDispatch()
    {
        // Spec 089 review V9: the stimulus payload becomes durable state on the started instance, so the
        // transport bounds it. Declared Content-Length and actual streamed size are both enforced.
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var middleware = new HttpEndpointMiddleware(router, Options(maxBodyBytes: 16));
        var context = NewContext("/workflows/http/orders/webhook", "POST", body: new string('x', 64));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Empty(router.Requests);
    }

    private static IOptions<HttpEndpointOptions> Options(string? basePath = null, long? maxBodyBytes = null) =>
        Microsoft.Extensions.Options.Options.Create(new HttpEndpointOptions
        {
            BasePath = basePath ?? new HttpEndpointOptions().BasePath,
            MaxRequestBodyBytes = maxBodyBytes ?? new HttpEndpointOptions().MaxRequestBodyBytes
        });

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
}
