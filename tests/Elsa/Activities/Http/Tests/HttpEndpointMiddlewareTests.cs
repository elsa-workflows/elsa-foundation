using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Middleware;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Testing;
using Elsa.Http.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Unit coverage for the inbound <see cref="HttpEndpointMiddleware"/> (spec 089 B, method-aware/templated routes).
/// It drives the middleware with a <see cref="DefaultHttpContext"/>, a fake <see cref="IStimulusRouter"/>, a
/// list-backed <see cref="FakeRouteTable"/> seeded with the published templates, a real-semantics
/// <see cref="TestRouteMatcher"/>, and the real <see cref="InMemoryWorkflowTriggerBindingStore"/> seeded with the
/// claimant bindings. Together they prove: template resolution + route-value extraction, the (template, method)
/// hashing identity, the ambiguity (409) guard, and the response contract — 202 with the started ids when a
/// trigger matched, 404 when the template is unknown or nothing started, and pass-through outside the base path.
/// </summary>
public sealed class HttpEndpointMiddlewareTests
{
    private const string DefinitionId = "definition-1";

    [Fact]
    public async Task MatchingRequest_DispatchesStartOnlyStimulus_AndRepliesAccepted()
    {
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-42"));
        var store = await StoreWith(Binding("artifact-1", "orders/webhook", "POST"));
        var middleware = Middleware(router, store, "orders/webhook");
        var context = NewContext("/workflows/http/orders/webhook", "POST", body: """{"id":7}""");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);

        // The request maps to the matched template + method via the shared hashing scheme, dispatched in StartOnly
        // mode so it never resumes a waiting instance. A concrete (parameter-free) template carries no RouteData.
        var dispatched = Assert.Single(router.Requests);
        Assert.Equal(HttpEndpointStimulus.StimulusType, dispatched.StimulusType);
        Assert.Equal(HttpEndpointStimulus.Hash("orders/webhook", "POST"), dispatched.StimulusHash);
        Assert.Equal(StimulusRoutingMode.StartOnly, dispatched.Mode);
        Assert.NotNull(dispatched.Input);
        Assert.Empty(dispatched.Input!.Value.GetProperty("RouteData").EnumerateObject());

        var payload = JsonDocument.Parse(await ReadResponse(context)).RootElement;
        var started = payload.GetProperty("started");
        Assert.Equal("wf-exec-42", started[0].GetString());
    }

    [Fact]
    public async Task TemplatedRoute_ExtractsRouteValues_AndHashesTemplateWithMethod()
    {
        // orders/{id} in the table; GET /workflows/http/orders/42 matches, extracting id=42. The dispatched hash
        // uses the TEMPLATE (not the concrete path) and the request method — Hash lowercases the method itself.
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(Binding("artifact-1", "orders/{id}", "GET"));
        var middleware = Middleware(router, store, "orders/{id}");
        var context = NewContext("/workflows/http/orders/42", "GET");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        var dispatched = Assert.Single(router.Requests);
        Assert.Equal(HttpEndpointStimulus.Hash("orders/{id}", "GET"), dispatched.StimulusHash);
        Assert.Equal("42", dispatched.Input!.Value.GetProperty("RouteData").GetProperty("id").GetString());
    }

    [Fact]
    public async Task UnmatchedConcretePath_RepliesNotFound_WithoutCallingTheRouter()
    {
        // orders/{id} is the only published template; /orders/42/details matches nothing, so the router is never
        // consulted (404 before dispatch, distinct from "matched but nothing started").
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(Binding("artifact-1", "orders/{id}", "GET"));
        var middleware = Middleware(router, store, "orders/{id}");
        var context = NewContext("/workflows/http/orders/42/details", "GET");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Empty(router.Requests);
    }

    [Fact]
    public async Task MatchingTemplate_WithNoStartedTriggers_RepliesNotFound()
    {
        // The template is in the table (so it resolves and reaches dispatch) but the router starts nothing.
        var router = new RecordingStimulusRouter();
        var store = await StoreWith(Binding("artifact-1", "unknown", "GET"));
        var middleware = Middleware(router, store, "unknown");
        var context = NewContext("/workflows/http/unknown", "GET");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Single(router.Requests);
    }

    [Fact]
    public async Task AmbiguousEndpoint_TwoDefinitionsSameTemplateAndMethod_Replies409_WithoutCallingTheRouter()
    {
        // Two workflows (distinct DefinitionId) claim the same (template, method): authoring error, not fan-out.
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(
            Binding("artifact-1", "orders/{id}", "GET", definitionId: "definition-a"),
            Binding("artifact-2", "orders/{id}", "GET", definitionId: "definition-b"));
        var middleware = Middleware(router, store, "orders/{id}");
        var context = NewContext("/workflows/http/orders/42", "GET");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Empty(router.Requests);
        var payload = JsonDocument.Parse(await ReadResponse(context)).RootElement;
        Assert.Equal("ambiguous-endpoint", payload.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SameDefinitionMultipleBindings_IsNotAmbiguous_AndDispatches()
    {
        // Two bindings for the SAME definition on the same (template, method) hash — e.g. republish remnants or a
        // duplicate node — span one DefinitionId, so the ambiguity guard does not trip and dispatch proceeds.
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(
            Binding("artifact-1", "orders/{id}", "GET", nodeId: "node-a"),
            Binding("artifact-1", "orders/{id}", "GET", nodeId: "node-b"));
        var middleware = Middleware(router, store, "orders/{id}");
        var context = NewContext("/workflows/http/orders/42", "GET");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.Single(router.Requests);
    }

    [Fact]
    public async Task RequestOutsideBasePath_PassesThroughToNext()
    {
        var router = new RecordingStimulusRouter();
        var middleware = Middleware(router, new InMemoryWorkflowTriggerBindingStore(), "orders/webhook");
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
        var middleware = Middleware(router, new InMemoryWorkflowTriggerBindingStore(), "orders/webhook");
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
        var middleware = Middleware(router, new InMemoryWorkflowTriggerBindingStore(), "foo");
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
        // cleanly before any template resolution runs.
        var router = new RecordingStimulusRouter();
        var middleware = Middleware(router, new InMemoryWorkflowTriggerBindingStore(), "foo");
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
        var middleware = Middleware(router, new InMemoryWorkflowTriggerBindingStore(), "health", basePath: "/");
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
        var store = await StoreWith(Binding("artifact-1", "orders/webhook", "POST"));
        var middleware = Middleware(router, store, "orders/webhook");
        var context = NewContext("/workflows/http/orders/webhook", "post");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        var dispatched = Assert.Single(router.Requests);
        Assert.Equal("POST", dispatched.Input!.Value.GetProperty("Method").GetString());
    }

    [Fact]
    public async Task BodyLargerThanTheCap_IsRejectedWith413_BeforeAnyDispatch()
    {
        // Spec 089 review V9: the stimulus payload becomes durable state on the started instance, so the
        // transport bounds it. The template must be in the table so the request reaches the body/model stage.
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(Binding("artifact-1", "orders/webhook", "POST"));
        var middleware = Middleware(router, store, "orders/webhook", maxBodyBytes: 16);
        var context = NewContext("/workflows/http/orders/webhook", "POST", body: new string('x', 64));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Empty(router.Requests);
    }

    private static HttpEndpointMiddleware Middleware(
        IStimulusRouter router,
        IWorkflowTriggerBindingStore store,
        params string[] templates) =>
        new(router, new FakeRouteTable(templates), new TestRouteMatcher(), store, Options());

    private static HttpEndpointMiddleware Middleware(
        IStimulusRouter router,
        IWorkflowTriggerBindingStore store,
        string template,
        string? basePath = null,
        long? maxBodyBytes = null) =>
        new(router, new FakeRouteTable(template), new TestRouteMatcher(), store, Options(basePath, maxBodyBytes));

    private static async Task<InMemoryWorkflowTriggerBindingStore> StoreWith(params WorkflowTriggerBinding[] bindings)
    {
        var store = new InMemoryWorkflowTriggerBindingStore();
        foreach (var binding in bindings)
            await store.SaveAsync(binding);
        return store;
    }

    private static WorkflowTriggerBinding Binding(
        string artifactId,
        string template,
        string method,
        string? definitionId = null,
        string nodeId = "node-a")
    {
        var hash = HttpEndpointStimulus.Hash(template, method);
        return new WorkflowTriggerBinding(
            TriggerBindingId: WorkflowTriggerBinding.BuildId(artifactId, nodeId, hash),
            ArtifactId: artifactId,
            DefinitionId: definitionId ?? DefinitionId,
            ArtifactVersion: "1.0.0",
            ArtifactHash: $"sha256:{artifactId}",
            ExecutableNodeId: nodeId,
            StimulusType: HttpEndpointStimulus.StimulusType,
            StimulusHash: hash,
            CorrelationScope: null,
            Metadata: new Dictionary<string, string>
            {
                [Elsa.Http.Core.HttpEndpointRouting.TemplateMetadataKey] = HttpEndpointStimulus.NormalizeTemplate(template),
                [Elsa.Http.Core.HttpEndpointRouting.MethodMetadataKey] = method.ToLowerInvariant()
            },
            CreatedAt: DateTimeOffset.UnixEpoch);
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
