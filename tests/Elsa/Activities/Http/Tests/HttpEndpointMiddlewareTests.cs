using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Middleware;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Http.Services;
using Elsa.Activities.Testing;
using Elsa.Http.Core;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Unit coverage for the inbound <see cref="HttpEndpointMiddleware"/> (spec 089 B, method-aware/templated routes).
/// It drives the middleware with a <see cref="DefaultHttpContext"/>, a fake <see cref="IStimulusRouter"/>, a
/// production-delegating <see cref="FakeRouteTable"/> seeded with the published templates, a real-semantics
/// <see cref="TestRouteMatcher"/>, and the real <see cref="InMemoryWorkflowTriggerBindingStore"/> seeded with the
/// claimant bindings. Together they prove: template resolution + route-value extraction, the (template, method)
/// hashing identity, the ambiguity (409) guard, and the response contract — 202 with the started ids when a
/// trigger matched, 404 when the template is unknown or nothing started, and pass-through outside the base path.
/// The #592 follow-up items add: authorization is evaluated before route existence/ambiguity is disclosed
/// (item 10), a configuration fault maps to 500 rather than 401 (item 11), and a client abort during dispatch is
/// swallowed rather than surfaced as a pipeline exception (item 12).
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

        // The request maps to the matched template + method via the shared hashing scheme, dispatched in
        // StartAndResume mode (spec 089 D) so it both starts triggers and resumes waiting instances. A concrete
        // (parameter-free) template carries no RouteData.
        var dispatched = Assert.Single(router.Requests);
        Assert.Equal(HttpEndpointStimulus.StimulusType, dispatched.StimulusType);
        Assert.Equal(HttpEndpointStimulus.Hash("orders/webhook", "POST"), dispatched.StimulusHash);
        Assert.Equal(StimulusRoutingMode.StartAndResume, dispatched.Mode);
        Assert.NotNull(dispatched.Input);
        Assert.Empty(dispatched.Input!.Value.GetProperty("RouteData").EnumerateObject());

        var payload = JsonDocument.Parse(await ReadResponse(context)).RootElement;
        var started = payload.GetProperty("started");
        Assert.Equal("wf-exec-42", started[0].GetString());
        // The response now carries a (here empty) resumed array alongside started (spec 089 D, D-D6).
        Assert.Empty(payload.GetProperty("resumed").EnumerateArray());
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

    [Theory]
    [InlineData("orders/list", "orders/{id}")] // more-specific template listed first
    [InlineData("orders/{id}", "orders/list")] // ...and listed last: order must not matter
    public async Task OverlappingTemplates_LiteralBeatsParameter_Deterministically(string first, string second)
    {
        // A request for the literal path must always bind the literal template (orders/list), never the parameter
        // template (orders/{id}), regardless of the order the two templates were published into the route table
        // (issue #592 item 1). The dispatched hash proves which template won.
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(
            Binding("artifact-1", "orders/list", "GET"),
            Binding("artifact-1", "orders/{id}", "GET"));
        var middleware = Middleware(router, store, new[] { first, second });
        var context = NewContext("/workflows/http/orders/list", "GET");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        var dispatched = Assert.Single(router.Requests);
        Assert.Equal(HttpEndpointStimulus.Hash("orders/list", "GET"), dispatched.StimulusHash);
        // The literal template captured no route parameter.
        Assert.Empty(dispatched.Input!.Value.GetProperty("RouteData").EnumerateObject());
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

    // ---- Spec 089 sub-unit C (T009): authorization, per-endpoint size limit, ParsedContent, faults ----

    [Fact]
    public async Task Authorize_WithNoRequestServices_Replies401_FailClosed_RouterUntouched_BodyUnread()
    {
        // authorize=true metadata but the request has no RequestServices (WorkflowsRuntimeHttp feature absent),
        // so no handler can be resolved — fail closed. 401 before the body is read or anything dispatched.
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(Binding("artifact-1", "secure", "POST", authorize: true));
        var middleware = Middleware(router, store, "secure");
        var body = new TrackingStream(Encoding.UTF8.GetBytes("""{"id":1}"""));
        var context = NewContext("/workflows/http/secure", "POST", bodyStream: body);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Empty(router.Requests);
        Assert.False(body.WasRead);
    }

    [Fact]
    public async Task Authorize_WithFailingHandler_Replies401_RouterUntouched()
    {
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(Binding("artifact-1", "secure", "POST", authorize: true));
        var handler = new FakeAuthorizationHandler(authorize: false);
        var middleware = Middleware(router, store, "secure");
        var context = NewContext("/workflows/http/secure", "POST", body: """{"id":1}""",
            services: ServicesWith<IHttpEndpointAuthorizationHandler>(handler));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Empty(router.Requests);
        Assert.True(handler.WasInvoked);
    }

    [Fact]
    public async Task Authorize_WithPassingHandler_Replies202_AndHandlerReceivesPolicyFromMetadata()
    {
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(Binding("artifact-1", "secure", "POST", authorize: true, policy: "admins"));
        var handler = new FakeAuthorizationHandler(authorize: true);
        var middleware = Middleware(router, store, "secure");
        var context = NewContext("/workflows/http/secure", "POST", body: """{"id":1}""",
            services: ServicesWith<IHttpEndpointAuthorizationHandler>(handler));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.Single(router.Requests);
        Assert.NotNull(handler.LastContext);
        Assert.Equal("admins", handler.LastContext!.Policy);
        Assert.Same(context, handler.LastContext.HttpContext);
    }

    [Fact]
    public async Task PerEndpointSizeLimit_SmallerThanGlobal_RejectsOversizedBodyWith413()
    {
        // Global cap 256, per-endpoint 16: a 64-byte body is under the global limit but over the per-endpoint
        // override, so it 413s (the per-endpoint value wins).
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(Binding("artifact-1", "sized", "POST", requestSizeLimit: 16));
        var middleware = Middleware(router, store, "sized", maxBodyBytes: 256);
        var context = NewContext("/workflows/http/sized", "POST", body: new string('x', 64));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Empty(router.Requests);
    }

    [Fact]
    public async Task PerEndpointSizeLimit_LargerThanGlobal_AcceptsBodyBetweenTheTwoWith202()
    {
        // Global cap 16, per-endpoint 256: a 64-byte body exceeds the global limit but is under the per-endpoint
        // override, so the override raises the ceiling and the request dispatches (202).
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(Binding("artifact-1", "sized", "POST", requestSizeLimit: 256));
        var middleware = Middleware(router, store, "sized", maxBodyBytes: 16);
        var context = NewContext("/workflows/http/sized", "POST", body: new string('x', 64));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.Single(router.Requests);
    }

    [Fact]
    public async Task DispatchedInput_CarriesTheRawBody_ButNotAReEncodedParsedContentCopy()
    {
        // Spec 089 efficiency #9: parsed content is no longer persisted on the wire model — the activity derives
        // it from Body at execution time — so the durable stimulus payload carries the body exactly once, with no
        // re-encoded ParsedContent copy doubling it.
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(Binding("artifact-1", "orders/webhook", "POST"));
        var middleware = Middleware(router, store, "orders/webhook");
        var context = NewContext("/workflows/http/orders/webhook", "POST", body: """{"orderId":7}""",
            contentType: "application/json");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        var dispatched = Assert.Single(router.Requests);
        Assert.Equal("""{"orderId":7}""", dispatched.Input!.Value.GetProperty("Body").GetString());
        Assert.False(
            dispatched.Input!.Value.TryGetProperty("ParsedContent", out _),
            "ParsedContent must not be persisted on the stimulus payload.");
    }

    [Fact]
    public async Task RequestTimeout_DelayingDispatch_Replies408_ViaInlineFallback()
    {
        // The router delays past the endpoint's 20ms timeout; the linked CTS trips, the dispatch OperationCanceled
        // is mapped to 408 by the inline fallback (no IHttpEndpointFaultHandler registered).
        var router = BehaviourStimulusRouter.Delaying(TimeSpan.FromSeconds(30));
        var store = await StoreWith(Binding("artifact-1", "slow", "POST", requestTimeout: TimeSpan.FromMilliseconds(20)));
        var middleware = Middleware(router, store, "slow");
        var context = NewContext("/workflows/http/slow", "POST", body: "{}");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status408RequestTimeout, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandSeededNegativeTimeoutMetadata_IsIgnored_NotAServerError()
    {
        // Review C2 defense in depth: the provider rejects non-positive timeouts at publish, but metadata can
        // be hand-seeded; a negative value must not reach CancelAfter (which throws) — it is treated as
        // no-timeout and the request completes normally.
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(Binding("artifact-1", "negative", "POST", requestTimeout: TimeSpan.FromSeconds(-1)));
        var middleware = Middleware(router, store, "negative");
        var context = NewContext("/workflows/http/negative", "POST", body: "{}");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.Single(router.Requests);
    }

    [Fact]
    public async Task DispatchThrowsHttpBadRequestException_Replies400_ViaInlineFallback()
    {
        var router = BehaviourStimulusRouter.Throwing(new HttpBadRequestException("bad", new InvalidOperationException()));
        var store = await StoreWith(Binding("artifact-1", "faulty", "POST"));
        var middleware = Middleware(router, store, "faulty");
        var context = NewContext("/workflows/http/faulty", "POST", body: "{}");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task DispatchThrowsUnexpectedException_Replies500_ViaInlineFallback()
    {
        var router = BehaviourStimulusRouter.Throwing(new InvalidOperationException("boom"));
        var store = await StoreWith(Binding("artifact-1", "faulty", "POST"));
        var middleware = Middleware(router, store, "faulty");
        var context = NewContext("/workflows/http/faulty", "POST", body: "{}");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    [Fact]
    public async Task DispatchFault_WithRegisteredFaultHandler_InvokesHandlerInsteadOfInlineFallback()
    {
        // A registered IHttpEndpointFaultHandler takes over the mapping: it writes a sentinel status (teapot)
        // that the inline fallback would never produce, proving the seam handler ran instead of the fallback.
        const int sentinel = StatusCodes.Status418ImATeapot;
        var router = BehaviourStimulusRouter.Throwing(new InvalidOperationException("boom"));
        var store = await StoreWith(Binding("artifact-1", "faulty", "POST"));
        var faultHandler = new FakeFaultHandler(sentinel);
        var middleware = Middleware(router, store, "faulty");
        var context = NewContext("/workflows/http/faulty", "POST", body: "{}",
            services: ServicesWith<IHttpEndpointFaultHandler>(faultHandler));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(sentinel, context.Response.StatusCode);
        Assert.True(faultHandler.WasInvoked);
        Assert.Contains(faultHandler.LastContext!.Exceptions, e => e is InvalidOperationException);
    }

    // ---- Spec 089 sub-unit D (T011): StartAndResume — resume-only matches, merged body, bookmark-metadata options ----

    [Fact]
    public async Task ResumeOnlyMatch_NoClaimant_Replies202_WithResumedIds()
    {
        // A purely mid-flow endpoint: no trigger claimant, one waiting instance resumed by this request. The route
        // is in the table (from the resolver's bookmark union); dispatch resumes and the body reports it.
        var router = RecordingStimulusRouter.WithOutcomes(
            resumes: new[] { new StimulusResumeOutcome("wf-resumed-1", BookmarkResumeDispatchStatus.Dispatched) });
        var bookmarks = await BookmarkLookupWith(WaitingBookmark("wf-resumed-1", "callbacks/{id}", "POST"));
        var middleware = Middleware(router, new InMemoryWorkflowTriggerBindingStore(), "callbacks/{id}", bookmarkLookup: bookmarks);
        var context = NewContext("/workflows/http/callbacks/42", "POST", body: "{}");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        var dispatched = Assert.Single(router.Requests);
        Assert.Equal(StimulusRoutingMode.StartAndResume, dispatched.Mode);
        var payload = JsonDocument.Parse(await ReadResponse(context)).RootElement;
        Assert.Empty(payload.GetProperty("started").EnumerateArray());
        var resumed = payload.GetProperty("resumed");
        Assert.Equal("wf-resumed-1", resumed[0].GetString());
    }

    [Fact]
    public async Task StartAndResumeMatch_Replies202_WithMergedStartedAndResumedBody()
    {
        // One request both starts a trigger and resumes a waiting instance — the body carries both arrays.
        var router = RecordingStimulusRouter.WithOutcomes(
            starts: new[] { StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-started-1") },
            resumes: new[] { new StimulusResumeOutcome("wf-resumed-1", BookmarkResumeDispatchStatus.Dispatched) });
        var store = await StoreWith(Binding("artifact-1", "orders/webhook", "POST"));
        var middleware = Middleware(router, store, "orders/webhook");
        var context = NewContext("/workflows/http/orders/webhook", "POST", body: "{}");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        var payload = JsonDocument.Parse(await ReadResponse(context)).RootElement;
        Assert.Equal("wf-started-1", payload.GetProperty("started")[0].GetString());
        Assert.Equal("wf-resumed-1", payload.GetProperty("resumed")[0].GetString());
    }

    [Fact]
    public async Task NeitherStartedNorResumed_Replies404()
    {
        // The template resolves (in the table) but the router neither starts nor resumes anything → 404, requiring
        // BOTH zero starts and zero dispatched resumes (spec 089 D, D-D6).
        var router = new RecordingStimulusRouter();
        var store = await StoreWith(Binding("artifact-1", "callbacks", "POST"));
        var middleware = Middleware(router, store, "callbacks");
        var context = NewContext("/workflows/http/callbacks", "POST", body: "{}");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Single(router.Requests);
    }

    [Fact]
    public async Task NonDispatchedResume_DoesNotCountAsMatch_Replies404()
    {
        // A resume outcome that did NOT dispatch (e.g. NotFound after a race) must not be reported as resumed nor
        // keep the request from 404-ing — ResumedCount counts only Dispatched.
        var router = RecordingStimulusRouter.WithOutcomes(
            resumes: new[] { new StimulusResumeOutcome("wf-x", BookmarkResumeDispatchStatus.NotFound, reason: "gone") });
        var store = await StoreWith(Binding("artifact-1", "callbacks", "POST"));
        var middleware = Middleware(router, store, "callbacks");
        var context = NewContext("/workflows/http/callbacks", "POST", body: "{}");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task ResumeOnly_BookmarkMetadataAuthorize_FailingHandler_Replies401_BeforeBodyRead()
    {
        // No trigger claimant, but the waiting bookmark's metadata marks the endpoint authorize=true — enforcement
        // uses the bookmark's options (D-D5) and fails closed before the body is read or anything dispatched.
        var router = RecordingStimulusRouter.WithOutcomes(
            resumes: new[] { new StimulusResumeOutcome("wf-1", BookmarkResumeDispatchStatus.Dispatched) });
        var bookmarks = await BookmarkLookupWith(WaitingBookmark("wf-1", "secure", "POST", authorize: true, policy: "admins"));
        var middleware = Middleware(router, new InMemoryWorkflowTriggerBindingStore(), "secure", bookmarkLookup: bookmarks);
        var handler = new FakeAuthorizationHandler(authorize: false);
        var body = new TrackingStream(Encoding.UTF8.GetBytes("""{"id":1}"""));
        var context = NewContext("/workflows/http/secure", "POST", bodyStream: body,
            services: ServicesWith<IHttpEndpointAuthorizationHandler>(handler));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Empty(router.Requests);
        Assert.False(body.WasRead);
        Assert.True(handler.WasInvoked);
        Assert.Equal("admins", handler.LastContext!.Policy);
    }

    [Fact]
    public async Task ResumeOnly_BookmarkMetadataSizeLimit_OversizedBody_Replies413()
    {
        // No trigger claimant; the waiting bookmark's metadata sets a per-endpoint size limit smaller than the
        // global cap, and an oversized body 413s before dispatch (D-D5 enforcement applies to resume-only matches).
        var router = RecordingStimulusRouter.WithOutcomes(
            resumes: new[] { new StimulusResumeOutcome("wf-1", BookmarkResumeDispatchStatus.Dispatched) });
        var bookmarks = await BookmarkLookupWith(WaitingBookmark("wf-1", "sized", "POST", requestSizeLimit: 16));
        var middleware = Middleware(router, new InMemoryWorkflowTriggerBindingStore(), "sized", maxBodyBytes: 256, bookmarkLookup: bookmarks);
        var context = NewContext("/workflows/http/sized", "POST", body: new string('x', 64));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Empty(router.Requests);
    }

    // ---- D-D5 fail-closed merge: divergent options across multiple waiting bookmarks on one (template, method) ----

    [Fact]
    public async Task ResumeOnly_DivergentAuthorize_AcrossBookmarks_FailsClosed_AnonymousDenied()
    {
        // Two waiting bookmarks share (template, method) but diverge on authorize (one false, one true). Options are
        // excluded from the hash, so both ride the same identity; the merge is fail-closed — ANY authorize=true
        // protects the endpoint, so an anonymous caller is denied 401 even though a sibling bookmark left the flag off.
        var router = RecordingStimulusRouter.WithOutcomes(
            resumes: new[] { new StimulusResumeOutcome("wf-1", BookmarkResumeDispatchStatus.Dispatched) });
        var bookmarks = await BookmarkLookupWith(
            WaitingBookmark("wf-lax", "secure", "POST", authorize: false),
            WaitingBookmark("wf-strict", "secure", "POST", authorize: true));
        var handler = new FakeAuthorizationHandler(authorize: false);
        var middleware = Middleware(router, new InMemoryWorkflowTriggerBindingStore(), "secure", bookmarkLookup: bookmarks);
        var context = NewContext("/workflows/http/secure", "POST", body: "{}",
            services: ServicesWith<IHttpEndpointAuthorizationHandler>(handler));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Empty(router.Requests);
        Assert.True(handler.WasInvoked);
    }

    [Fact]
    public async Task ResumeOnly_DivergentAuthorize_AcrossBookmarks_AuthenticatedCaller_Passes()
    {
        // The same divergent-authorize pair, but the (single, no named policy) authorize check passes → 202. Proves
        // the merge does not spuriously deny once authorization succeeds.
        var router = RecordingStimulusRouter.WithOutcomes(
            resumes: new[] { new StimulusResumeOutcome("wf-1", BookmarkResumeDispatchStatus.Dispatched) });
        var bookmarks = await BookmarkLookupWith(
            WaitingBookmark("wf-lax", "secure", "POST", authorize: false),
            WaitingBookmark("wf-strict", "secure", "POST", authorize: true));
        var handler = new FakeAuthorizationHandler(authorize: true);
        var middleware = Middleware(router, new InMemoryWorkflowTriggerBindingStore(), "secure", bookmarkLookup: bookmarks);
        var context = NewContext("/workflows/http/secure", "POST", body: "{}",
            services: ServicesWith<IHttpEndpointAuthorizationHandler>(handler));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.Single(router.Requests);
    }

    [Fact]
    public async Task ResumeOnly_DivergentSizeLimits_AcrossBookmarks_StrictestWins_413sInBetweenBody()
    {
        // Two waiting bookmarks on one (template, method) declare different size limits (16 and 128). The merge
        // takes the MIN (16), so a 64-byte body — under the looser bookmark's limit — is still rejected 413.
        var router = RecordingStimulusRouter.WithOutcomes(
            resumes: new[] { new StimulusResumeOutcome("wf-1", BookmarkResumeDispatchStatus.Dispatched) });
        var bookmarks = await BookmarkLookupWith(
            WaitingBookmark("wf-loose", "sized", "POST", requestSizeLimit: 128),
            WaitingBookmark("wf-tight", "sized", "POST", requestSizeLimit: 16));
        var middleware = Middleware(router, new InMemoryWorkflowTriggerBindingStore(), "sized", maxBodyBytes: 256, bookmarkLookup: bookmarks);
        var context = NewContext("/workflows/http/sized", "POST", body: new string('x', 64));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Empty(router.Requests);
    }

    [Fact]
    public async Task ResumeOnly_TwoDistinctPolicies_AcrossBookmarks_BothEvaluated_OneFailing_Denies401()
    {
        // Two waiting bookmarks declare DISTINCT policies (admins, auditors) on one (template, method). The merge
        // requires EVERY policy to pass; the handler denies "auditors", so the request is denied 401 — and both
        // policies were evaluated (the handler saw each).
        var router = RecordingStimulusRouter.WithOutcomes(
            resumes: new[] { new StimulusResumeOutcome("wf-1", BookmarkResumeDispatchStatus.Dispatched) });
        var bookmarks = await BookmarkLookupWith(
            WaitingBookmark("wf-a", "secure", "POST", authorize: true, policy: "admins"),
            WaitingBookmark("wf-b", "secure", "POST", authorize: true, policy: "auditors"));
        var handler = FakeAuthorizationHandler.PerPolicy(policy => policy == "admins");
        var middleware = Middleware(router, new InMemoryWorkflowTriggerBindingStore(), "secure", bookmarkLookup: bookmarks);
        var context = NewContext("/workflows/http/secure", "POST", body: "{}",
            services: ServicesWith<IHttpEndpointAuthorizationHandler>(handler));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Empty(router.Requests);
        Assert.Contains("admins", handler.EvaluatedPolicies);
        Assert.Contains("auditors", handler.EvaluatedPolicies);
    }

    [Fact]
    public async Task ResumeOnly_TwoDistinctPolicies_AllPassing_Replies202()
    {
        // The same two distinct policies, both satisfied → the request dispatches (202). Proves the all-must-pass
        // loop admits the request only once every distinct policy succeeds.
        var router = RecordingStimulusRouter.WithOutcomes(
            resumes: new[] { new StimulusResumeOutcome("wf-1", BookmarkResumeDispatchStatus.Dispatched) });
        var bookmarks = await BookmarkLookupWith(
            WaitingBookmark("wf-a", "secure", "POST", authorize: true, policy: "admins"),
            WaitingBookmark("wf-b", "secure", "POST", authorize: true, policy: "auditors"));
        var handler = new FakeAuthorizationHandler(authorize: true);
        var middleware = Middleware(router, new InMemoryWorkflowTriggerBindingStore(), "secure", bookmarkLookup: bookmarks);
        var context = NewContext("/workflows/http/secure", "POST", body: "{}",
            services: ServicesWith<IHttpEndpointAuthorizationHandler>(handler));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.Single(router.Requests);
        Assert.Equal(2, handler.EvaluatedPolicies.Count);
    }

    // ---- #592 item 10: authorization is evaluated BEFORE route existence/ambiguity is disclosed ----

    [Fact]
    public async Task Authorize_AmbiguousRoute_AnonymousCaller_Replies401_NotAmbiguity_RouterUntouched()
    {
        // Two definitions claim the same authorized (template, method). An anonymous caller must get 401, never
        // the 409 that would reveal the route exists and is ambiguous (#592 item 10). Auth runs before the
        // ambiguity guard, so the router is never consulted.
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(
            Binding("artifact-1", "secure/{id}", "GET", definitionId: "definition-a", authorize: true),
            Binding("artifact-2", "secure/{id}", "GET", definitionId: "definition-b", authorize: true));
        var handler = new FakeAuthorizationHandler(authorize: false);
        var middleware = Middleware(router, store, "secure/{id}");
        var context = NewContext("/workflows/http/secure/42", "GET",
            services: ServicesWith<IHttpEndpointAuthorizationHandler>(handler));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Empty(router.Requests);
        Assert.True(handler.WasInvoked);
    }

    [Fact]
    public async Task Authorize_AmbiguousRoute_MixedAuthorizeFlags_StillRequiresAuthorization()
    {
        // Only one of the two ambiguous claimants declares authorize=true. The endpoint is still protected: an
        // anonymous caller must not slip past because a sibling definition left the flag off (#592 item 10).
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(
            Binding("artifact-1", "secure/{id}", "GET", definitionId: "definition-a", authorize: false),
            Binding("artifact-2", "secure/{id}", "GET", definitionId: "definition-b", authorize: true));
        var handler = new FakeAuthorizationHandler(authorize: false);
        var middleware = Middleware(router, store, "secure/{id}");
        var context = NewContext("/workflows/http/secure/42", "GET",
            services: ServicesWith<IHttpEndpointAuthorizationHandler>(handler));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Empty(router.Requests);
        Assert.True(handler.WasInvoked);
    }

    [Fact]
    public async Task Authorize_ValidRoute_AnonymousCaller_Replies401()
    {
        // The unambiguous, valid case: an authorized endpoint denies an anonymous caller with 401 before dispatch.
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(Binding("artifact-1", "secure/{id}", "GET", authorize: true));
        var handler = new FakeAuthorizationHandler(authorize: false);
        var middleware = Middleware(router, store, "secure/{id}");
        var context = NewContext("/workflows/http/secure/42", "GET",
            services: ServicesWith<IHttpEndpointAuthorizationHandler>(handler));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Empty(router.Requests);
    }

    [Fact]
    public async Task ClaimantWins_OverBookmarkMetadata_WhenBothExist()
    {
        // Both a trigger claimant and a waiting bookmark exist for the (template, method), and their options DIFFER:
        // the claimant does NOT authorize, the bookmark WOULD. D-D5 says the claimant wins — so no auth runs and the
        // request dispatches (202) even though the bookmark's metadata alone would have demanded authorization.
        var router = RecordingStimulusRouter.WithOutcomes(
            starts: new[] { StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-started-1") });
        var store = await StoreWith(Binding("artifact-1", "orders/webhook", "POST")); // authorize=false
        var bookmarks = await BookmarkLookupWith(WaitingBookmark("wf-1", "orders/webhook", "POST", authorize: true));
        var middleware = Middleware(router, store, "orders/webhook", bookmarkLookup: bookmarks);
        // A failing auth handler is present; if the bookmark's authorize=true were consulted it would 401.
        var context = NewContext("/workflows/http/orders/webhook", "POST", body: "{}",
            services: ServicesWith<IHttpEndpointAuthorizationHandler>(new FakeAuthorizationHandler(authorize: false)));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.Single(router.Requests);
    }

    [Fact]
    public async Task Ambiguity409Guard_StaysClaimantOnly_UnaffectedByBookmarks()
    {
        // The 409 guard is trigger-claimant-only (bookmark resumes are instance-scoped, never ambiguous across
        // definitions). Two definitions claim the same (template, method) → 409 before dispatch, regardless of any
        // waiting bookmark on the same hash.
        var router = RecordingStimulusRouter.WithOutcomes(
            resumes: new[] { new StimulusResumeOutcome("wf-1", BookmarkResumeDispatchStatus.Dispatched) });
        var store = await StoreWith(
            Binding("artifact-1", "orders/{id}", "GET", definitionId: "definition-a"),
            Binding("artifact-2", "orders/{id}", "GET", definitionId: "definition-b"));
        var bookmarks = await BookmarkLookupWith(WaitingBookmark("wf-1", "orders/{id}", "GET"));
        var middleware = Middleware(router, store, "orders/{id}", bookmarkLookup: bookmarks);
        var context = NewContext("/workflows/http/orders/42", "GET");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Empty(router.Requests);
    }

    [Fact]
    public async Task AmbiguousEndpoint_409Body_DoesNotEchoMethodOrTemplate()
    {
        // #592 item 10: the slimmed 409 body carries only a stable code, never the request method or the
        // resolved template (which would disclose endpoint details).
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(
            Binding("artifact-1", "orders/{id}", "GET", definitionId: "definition-a"),
            Binding("artifact-2", "orders/{id}", "GET", definitionId: "definition-b"));
        var middleware = Middleware(router, store, "orders/{id}");
        var context = NewContext("/workflows/http/orders/42", "GET");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        var body = await ReadResponse(context);
        Assert.DoesNotContain("orders/{id}", body);
        Assert.DoesNotContain("GET", body);
    }

    // ---- #592 item 11: a configuration fault surfaces as 500, distinct from the 401 an anonymous caller gets ----

    [Fact]
    public async Task Authorize_HandlerThrowsConfigurationException_Replies500_NotAuthDenial_RouterUntouched()
    {
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(Binding("artifact-1", "secure", "POST", authorize: true, policy: "missing-policy"));
        var handler = FakeAuthorizationHandler.Throwing(
            new HttpEndpointAuthorizationConfigurationException("no scheme", new InvalidOperationException()));
        var middleware = Middleware(router, store, "secure");
        var context = NewContext("/workflows/http/secure", "POST", body: """{"id":1}""",
            services: ServicesWith<IHttpEndpointAuthorizationHandler>(handler));

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Empty(router.Requests);
    }

    // ---- #592 item 12: a client disconnect during dispatch is swallowed, not surfaced as a pipeline exception ----

    [Fact]
    public async Task ClientAbortDuringDispatch_IsSwallowed_NoResponseWritten_NoRethrow()
    {
        // The request is aborted while dispatch is in flight: the OperationCanceledException the dispatch observes
        // must not escape the middleware (there is no live connection to write to). No throw, status untouched.
        var router = BehaviourStimulusRouter.Throwing(new OperationCanceledException());
        var store = await StoreWith(Binding("artifact-1", "aborted", "POST"));
        var middleware = Middleware(router, store, "aborted");
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var context = NewContext("/workflows/http/aborted", "POST", body: "{}");
        context.RequestAborted = aborted.Token;

        // Must not throw.
        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        // Default status (200) — nothing was written on the dead connection.
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    // ---- Spec 089 sub-unit E (T006): synchronous same-exchange responses + 202 degrade ----

    [Fact]
    public async Task Sync_WorkflowAuthorsResponse_MiddlewareReturnsWithoutWriting_No202Stomp()
    {
        // A sync-mode claimant (http:responseMode=Sync). The router callback simulates the inline drain: it writes
        // the workflow-authored status + body to the live response and marks the sink. After RouteAsync the
        // middleware sees ResponseWritten and returns WITHOUT stomping a 202 — the response is whatever the workflow
        // authored (here 201 + a JSON body), never the {started,resumed} degrade envelope.
        var (services, sink) = SyncServices();
        var router = RecordingStimulusRouter.WithCallback(
            async _ =>
            {
                sink.HttpContext!.Response.StatusCode = StatusCodes.Status201Created;
                sink.HttpContext.Response.ContentType = "application/json";
                await sink.HttpContext.Response.WriteAsync("""{"ok":true}""");
                sink.MarkResponseWritten();
            },
            starts: new[] { StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1") });
        var store = await StoreWith(Binding("artifact-1", "orders/webhook", "POST", responseMode: ResponseMode.Sync));
        var middleware = Middleware(router, store, "orders/webhook");
        var context = NewContext("/workflows/http/orders/webhook", "POST", body: "{}", services: services);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.True(sink.ResponseWritten);
        Assert.Same(context, sink.HttpContext);
        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        var body = await ReadResponse(context);
        Assert.Equal("""{"ok":true}""", body);
        Assert.DoesNotContain("started", body);
    }

    [Fact]
    public async Task Sync_NoLiveWrite_DegradesTo202_WithStartedResumedBody()
    {
        // Sync mode, but the inline drain wrote nothing (suspend-before-response / no WriteHttpResponse / non-local
        // deferred — one degrade path). The sink stays unmarked, so the middleware falls through to the shared 202
        // writer with the {started,resumed} envelope.
        var (services, sink) = SyncServices();
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(Binding("artifact-1", "orders/webhook", "POST", responseMode: ResponseMode.Sync));
        var middleware = Middleware(router, store, "orders/webhook");
        var context = NewContext("/workflows/http/orders/webhook", "POST", body: "{}", services: services);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.False(sink.ResponseWritten);
        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        var payload = JsonDocument.Parse(await ReadResponse(context)).RootElement;
        Assert.Equal("wf-exec-1", payload.GetProperty("started")[0].GetString());
        Assert.Empty(payload.GetProperty("resumed").EnumerateArray());
    }

    [Fact]
    public async Task Sync_Dispatch_CarriesRequestServicesAsAmbientServices_SameInstance()
    {
        // The sync dispatch must carry the request's RequestServices as ambient services (spec-069 chain), so the
        // inline drain builds the activity context with them and WriteHttpResponse resolves THIS sink.
        var (services, sink) = SyncServices();
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(Binding("artifact-1", "orders/webhook", "POST", responseMode: ResponseMode.Sync));
        var middleware = Middleware(router, store, "orders/webhook");
        var context = NewContext("/workflows/http/orders/webhook", "POST", body: "{}", services: services);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        var dispatched = Assert.Single(router.Requests);
        Assert.NotNull(dispatched.DispatchOptions);
        Assert.Same(context.RequestServices, dispatched.DispatchOptions!.AmbientServices);
        // The middleware populated the request scope's sink with the live context.
        Assert.Same(context, sink.HttpContext);
    }

    [Fact]
    public async Task Async_NoMetadata_DispatchOptionsNull_AndSinkUntouched()
    {
        // Async mode (no responseMode metadata) is bit-identical to the pre-E baseline: no ambient services on the
        // dispatch and the sink is never populated even when one is registered on the request scope.
        var (services, sink) = SyncServices();
        var router = new RecordingStimulusRouter(StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1"));
        var store = await StoreWith(Binding("artifact-1", "orders/webhook", "POST")); // Async default
        var middleware = Middleware(router, store, "orders/webhook");
        var context = NewContext("/workflows/http/orders/webhook", "POST", body: "{}", services: services);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        var dispatched = Assert.Single(router.Requests);
        Assert.Null(dispatched.DispatchOptions);
        Assert.Null(sink.HttpContext);
        Assert.False(sink.ResponseWritten);
        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
    }

    [Fact]
    public async Task Sync_ResumeOnlyMatch_BookmarkMetadataSync_TakesSyncPath()
    {
        // A purely mid-flow endpoint whose waiting bookmark authored responseMode=Sync (E-D6 any-Sync). No trigger
        // claimant, so the mode comes from the bookmark merge; the sync path is taken — ambient services attached,
        // sink populated, and the callback's live write returned without a 202 stomp.
        var (services, sink) = SyncServices();
        var router = RecordingStimulusRouter.WithCallback(
            async _ =>
            {
                sink.HttpContext!.Response.StatusCode = StatusCodes.Status200OK;
                await sink.HttpContext.Response.WriteAsync("resumed-body");
                sink.MarkResponseWritten();
            },
            resumes: new[] { new StimulusResumeOutcome("wf-resumed-1", BookmarkResumeDispatchStatus.Dispatched) });
        var bookmarks = await BookmarkLookupWith(
            WaitingBookmark("wf-resumed-1", "callbacks/{id}", "POST", responseMode: ResponseMode.Sync));
        var middleware = Middleware(router, new InMemoryWorkflowTriggerBindingStore(), "callbacks/{id}", bookmarkLookup: bookmarks);
        var context = NewContext("/workflows/http/callbacks/42", "POST", body: "{}", services: services);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        var dispatched = Assert.Single(router.Requests);
        Assert.Same(context.RequestServices, dispatched.DispatchOptions!.AmbientServices);
        Assert.True(sink.ResponseWritten);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("resumed-body", await ReadResponse(context));
    }

    [Fact]
    public async Task Sync_ZeroStartsAndResumes_Replies404_PrecedenceOverDegrade()
    {
        // Sync mode but nothing started or resumed: the 404 branch stays FIRST — a sync request that matched no work
        // is 404, never a 202 degrade, and the sink (though populated for the dispatch) is not consulted.
        var (services, sink) = SyncServices();
        var router = new RecordingStimulusRouter(); // routes nothing
        var store = await StoreWith(Binding("artifact-1", "orders/webhook", "POST", responseMode: ResponseMode.Sync));
        var middleware = Middleware(router, store, "orders/webhook");
        var context = NewContext("/workflows/http/orders/webhook", "POST", body: "{}", services: services);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(sink.ResponseWritten);
        Assert.Empty(await ReadResponse(context));
    }

    [Fact]
    public async Task Sync_ResponseHasStarted_WithoutSinkMark_ReturnsWithout202Stomp()
    {
        // The workflow wrote directly to the live response (Response.HasStarted) but did NOT mark the sink — the
        // middleware must still detect the started response and return without stomping a 202 over it.
        var (services, sink) = SyncServices();
        var startedFeature = new StartedResponseFeature();
        var router = RecordingStimulusRouter.WithCallback(
            _ =>
            {
                startedFeature.MarkStarted();
                return ValueTask.CompletedTask;
            },
            starts: new[] { StimulusStartOutcome.Started("binding-1", "artifact-1", "wf-exec-1") });
        var store = await StoreWith(Binding("artifact-1", "orders/webhook", "POST", responseMode: ResponseMode.Sync));
        var middleware = Middleware(router, store, "orders/webhook");
        var context = NewContext("/workflows/http/orders/webhook", "POST", body: "{}", services: services);
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(startedFeature);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.False(sink.ResponseWritten);
        Assert.True(context.Response.HasStarted);
        // Middleware left the status untouched (200 default) — it did NOT overwrite with 202.
        Assert.NotEqual(StatusCodes.Status202Accepted, context.Response.StatusCode);
    }

    [Fact]
    public async Task Sync_DispatchTimeout_Replies408_FaultPathStillWraps()
    {
        // Sync mode does not bypass the timeout/fault wrapping: a dispatch that delays past the endpoint timeout
        // still trips the linked CTS and maps to 408 (the fault path now bounds the synchronous wait).
        var (services, _) = SyncServices();
        var router = BehaviourStimulusRouter.Delaying(TimeSpan.FromSeconds(30));
        var store = await StoreWith(Binding("artifact-1", "slow", "POST",
            requestTimeout: TimeSpan.FromMilliseconds(20), responseMode: ResponseMode.Sync));
        var middleware = Middleware(router, store, "slow");
        var context = NewContext("/workflows/http/slow", "POST", body: "{}", services: services);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status408RequestTimeout, context.Response.StatusCode);
    }

    private static HttpEndpointMiddleware Middleware(
        IStimulusRouter router,
        IWorkflowTriggerBindingStore store,
        params string[] templates) =>
        new(router, new FakeRouteTable(templates), new TestRouteMatcher(), store, EmptyBookmarkLookup(), Options());

    private static HttpEndpointMiddleware Middleware(
        IStimulusRouter router,
        IWorkflowTriggerBindingStore store,
        string template,
        string? basePath = null,
        long? maxBodyBytes = null,
        IGlobalBookmarkStimulusLookup? bookmarkLookup = null) =>
        new(router, new FakeRouteTable(template), new TestRouteMatcher(), store, bookmarkLookup ?? EmptyBookmarkLookup(), Options(basePath, maxBodyBytes));

    /// <summary>A lookup over an empty bookmark store — the common case where every match is trigger-claimant-backed.</summary>
    private static IGlobalBookmarkStimulusLookup EmptyBookmarkLookup() =>
        new GlobalBookmarkStimulusLookup(new InMemoryBookmarkStateStore());

    /// <summary>A lookup over a store seeded with the given waiting HttpEndpoint bookmarks (resume-only option source).</summary>
    private static async Task<IGlobalBookmarkStimulusLookup> BookmarkLookupWith(params BookmarkState[] bookmarks)
    {
        var store = new InMemoryBookmarkStateStore();
        foreach (var bookmark in bookmarks)
            await store.SaveAsync(bookmark);
        return new GlobalBookmarkStimulusLookup(store);
    }

    /// <summary>Builds a waiting HttpEndpoint bookmark carrying the given (template, method) hash + options metadata.</summary>
    private static BookmarkState WaitingBookmark(
        string workflowExecutionId,
        string template,
        string method,
        bool authorize = false,
        string? policy = null,
        long? requestSizeLimit = null,
        ResponseMode responseMode = ResponseMode.Async)
    {
        var hash = HttpEndpointStimulus.Hash(template, method);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Elsa.Http.Core.HttpEndpointRouting.TemplateMetadataKey] = HttpEndpointStimulus.NormalizeTemplate(template),
            [Elsa.Http.Core.HttpEndpointRouting.MethodMetadataKey] = method.ToLowerInvariant()
        };
        if (authorize)
            metadata[Elsa.Http.Core.HttpEndpointRouting.AuthorizeMetadataKey] = "true";
        if (policy is not null)
            metadata[Elsa.Http.Core.HttpEndpointRouting.PolicyMetadataKey] = policy;
        if (requestSizeLimit is { } sizeLimit)
            metadata[Elsa.Http.Core.HttpEndpointRouting.RequestSizeLimitMetadataKey] =
                sizeLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (responseMode != ResponseMode.Async)
            metadata[Elsa.Http.Core.HttpEndpointRouting.ResponseModeMetadataKey] = responseMode.ToString();

        return new BookmarkState(
            BookmarkId: $"http-endpoint:{workflowExecutionId}:{method.ToLowerInvariant()}",
            WorkflowExecutionId: workflowExecutionId,
            ActivityExecutionId: $"ae-{workflowExecutionId}",
            ExecutableNodeId: $"node-{workflowExecutionId}",
            ResumeTargetId: "OnResume",
            StimulusType: HttpEndpointStimulus.StimulusType,
            StimulusHash: hash,
            Payload: null,
            Metadata: metadata,
            CreatedAt: DateTimeOffset.UnixEpoch,
            ExpiresAt: null);
    }

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
        string nodeId = "node-a",
        bool authorize = false,
        string? policy = null,
        TimeSpan? requestTimeout = null,
        long? requestSizeLimit = null,
        ResponseMode responseMode = ResponseMode.Async)
    {
        var hash = HttpEndpointStimulus.Hash(template, method);

        // Mirror what HttpEndpointTriggerStimulusProvider stamps: identity metadata always, options only when
        // present, in the same formats (per data-model.md).
        var metadata = new Dictionary<string, string>
        {
            [Elsa.Http.Core.HttpEndpointRouting.TemplateMetadataKey] = HttpEndpointStimulus.NormalizeTemplate(template),
            [Elsa.Http.Core.HttpEndpointRouting.MethodMetadataKey] = method.ToLowerInvariant()
        };
        if (authorize)
            metadata[Elsa.Http.Core.HttpEndpointRouting.AuthorizeMetadataKey] = "true";
        if (policy is not null)
            metadata[Elsa.Http.Core.HttpEndpointRouting.PolicyMetadataKey] = policy;
        if (requestTimeout is { } timeout)
            metadata[Elsa.Http.Core.HttpEndpointRouting.RequestTimeoutMetadataKey] =
                timeout.ToString("c", System.Globalization.CultureInfo.InvariantCulture);
        if (requestSizeLimit is { } sizeLimit)
            metadata[Elsa.Http.Core.HttpEndpointRouting.RequestSizeLimitMetadataKey] =
                sizeLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (responseMode != ResponseMode.Async)
            metadata[Elsa.Http.Core.HttpEndpointRouting.ResponseModeMetadataKey] = responseMode.ToString();

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
            Metadata: metadata,
            CreatedAt: DateTimeOffset.UnixEpoch);
    }

    /// <summary>A RequestServices provider carrying a single <typeparamref name="TService"/> registration.</summary>
    private static IServiceProvider ServicesWith<TService>(TService instance) where TService : class =>
        new ServiceCollection().AddSingleton(instance).BuildServiceProvider();

    /// <summary>
    /// A RequestServices provider carrying a live <see cref="SyncHttpResponseSink"/> (as the middleware's request
    /// scope would) plus any extra registrations. Returns the sink instance so a test can assert the middleware
    /// populated it and observe what the router callback marked.
    /// </summary>
    private static (IServiceProvider Services, SyncHttpResponseSink Sink) SyncServices(params object[] extras)
    {
        var sink = new SyncHttpResponseSink();
        var collection = new ServiceCollection().AddSingleton(sink);
        foreach (var extra in extras)
            collection.AddSingleton(extra.GetType(), extra);
        return (collection.BuildServiceProvider(), sink);
    }

    private static IOptions<HttpEndpointOptions> Options(string? basePath = null, long? maxBodyBytes = null) =>
        Microsoft.Extensions.Options.Options.Create(new HttpEndpointOptions
        {
            BasePath = basePath ?? new HttpEndpointOptions().BasePath,
            MaxRequestBodyBytes = maxBodyBytes ?? new HttpEndpointOptions().MaxRequestBodyBytes
        });

    private static DefaultHttpContext NewContext(
        string path,
        string method,
        string? body = null,
        string? contentType = null,
        IServiceProvider? services = null,
        Stream? bodyStream = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        if (bodyStream is not null)
            context.Request.Body = bodyStream;
        else if (body is not null)
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        if (contentType is not null)
            context.Request.ContentType = contentType;
        if (services is not null)
            context.RequestServices = services;
        context.Response.Body = new MemoryStream();
        return context;
    }

    /// <summary>
    /// A response feature whose <see cref="HasStarted"/> flips to true on <see cref="MarkStarted"/> — lets a sync
    /// test simulate the workflow starting the live response (writing headers) WITHOUT marking the sink, so the
    /// middleware's <c>Response.HasStarted</c> discriminator (not just the sink) is exercised.
    /// </summary>
    private sealed class StartedResponseFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public Microsoft.AspNetCore.Http.IHeaderDictionary Headers { get; set; } = new Microsoft.AspNetCore.Http.HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; private set; }

        public void MarkStarted() => HasStarted = true;

        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }

    /// <summary>A read-only stream that records whether it was ever read from (proves the body stays untouched on 401).</summary>
    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public bool WasRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            WasRead = true;
            return base.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WasRead = true;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private static async Task<string> ReadResponse(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
