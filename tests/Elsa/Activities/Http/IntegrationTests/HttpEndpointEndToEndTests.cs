using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Models;
using Elsa.Workflows.Runtime.Http.Exceptions;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// Acceptance for spec 089 sub-unit A User Story 1 (HTTP endpoint parity, async/202 baseline), proven at the
/// host level over a real ASP.NET Core pipeline (see <see cref="HttpEndpointHostFixture"/>). These tests exercise
/// the full inbound path — request → <c>HttpEndpointMiddleware</c> → real stimulus router → real start dispatcher
/// → in-process agent → durable state — with no fakes, and assert both the 202/404/pass-through response contract
/// and that the started workflow observed the <em>live</em> request rather than the authored-route fallback.
/// </summary>
public sealed class HttpEndpointEndToEndTests : IAsyncLifetime
{
    private const string Path = "orders/webhook";
    private const string ResultOutputName = "EndpointResult";

    private HttpEndpointHostFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await HttpEndpointHostFixture.StartAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task MatchingRequest_StartsWorkflow_RepliesAccepted_AndTheRunObservesTheLiveRequest()
    {
        // SupportedMethods is routing-significant now (spec 089 B): a POST endpoint must author ["POST"] or it
        // 404s under the GET default.
        await _fixture.PublishHttpEndpointWorkflowAsync("artifact-orders", Path, ResultOutputName, "POST");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/workflows/http/{Path}?tenant=acme")
        {
            Content = new StringContent("""{"orderId":7}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Elsa-Test", "header-value");

        var response = await _fixture.Client.SendAsync(request);

        // Response contract: 202 Accepted with the started execution id.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var started = await ReadStartedIdsAsync(response);
        var workflowExecutionId = Assert.Single(started);

        // The started run's durable state reflects the LIVE request, not the authored-route fallback: the
        // fallback would carry method "*" (no SupportedMethods authored) and no body/header/query.
        var captured = await _fixture.ReadCapturedOutputAsync(workflowExecutionId, ResultOutputName);
        var model = captured.Deserialize<HttpRequestModel>()!;

        Assert.Equal("POST", model.Method);
        Assert.Equal(Path, model.Path);
        Assert.Equal("""{"orderId":7}""", model.Body);
        Assert.Equal("header-value", Assert.Contains("X-Elsa-Test", model.Headers)[0]);
        Assert.Equal("acme", Assert.Contains("tenant", model.Query)[0]);
    }

    [Fact]
    public async Task RequestUnderBasePath_WithNoMatchingTrigger_RepliesNotFound()
    {
        // No workflow published for this path, so the router starts nothing.
        var response = await _fixture.Client.GetAsync("/workflows/http/nothing-here");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RequestOutsideBasePath_PassesThroughToDownstreamSentinel()
    {
        var response = await _fixture.Client.GetAsync("/api/orders");

        // The endpoint middleware ignored it and the request reached the terminal sentinel middleware.
        Assert.Equal((HttpStatusCode)HttpEndpointHostFixture.SentinelStatusCode, response.StatusCode);
    }

    [Fact]
    public async Task TemplatedEndpoint_MatchingMethodAndPath_Starts_AndTheRunObservesExtractedRouteData()
    {
        // Spec US2: publish orders/{id} for GET,DELETE. A GET on a concrete path matches, the run starts, and the
        // durable Result carries the extracted route value id=42.
        await _fixture.PublishHttpEndpointWorkflowAsync("artifact-templated", "orders/{id}", ResultOutputName, "GET", "DELETE");

        var response = await _fixture.Client.GetAsync("/workflows/http/orders/42");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var workflowExecutionId = Assert.Single(await ReadStartedIdsAsync(response));

        var captured = await _fixture.ReadCapturedOutputAsync(workflowExecutionId, ResultOutputName);
        var model = captured.Deserialize<HttpRequestModel>()!;
        Assert.Equal("GET", model.Method);
        Assert.Equal("42", Assert.Contains("id", model.RouteData!));
    }

    [Fact]
    public async Task TemplatedEndpoint_UnsupportedMethod_RepliesNotFound()
    {
        // orders/{id} accepts GET,DELETE only — POST has no (template, method) binding, so nothing matches.
        await _fixture.PublishHttpEndpointWorkflowAsync("artifact-templated-2", "orders/{id}", ResultOutputName, "GET", "DELETE");

        var response = await _fixture.Client.PostAsync("/workflows/http/orders/42", new StringContent(""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TwoWorkflowsOnTheSameTemplateAndMethod_FailsTheSecondPublish_PreWrite_AndFirstKeepsServing()
    {
        // Spec US2 + issue #592 item 2: a (template, GET) claimed by two distinct workflow definitions is an
        // authoring error caught at PUBLISH time, on the indexer's PRE-write validation seam — the second
        // publish fails with the durable index untouched, rather than the collision persisting and only
        // surfacing as a request-time 409.
        await _fixture.PublishHttpEndpointWorkflowAsync("artifact-a", "orders/{id}", ResultOutputName, "GET");

        await Assert.ThrowsAsync<EndpointRoutingConflictException>(() =>
            _fixture.PublishHttpEndpointWorkflowAsync("artifact-b", "orders/{id}", ResultOutputName, "GET"));

        // Pre-write proof: the conflicting artifact left NOTHING in the durable index.
        Assert.Empty(await _fixture.ListTriggerBindingsAsync("artifact-b"));

        // The store stayed clean, so the endpoint keeps serving normally — the request routes to artifact-a's
        // workflow (202, one started run), no 409.
        var response = await _fixture.Client.GetAsync("/workflows/http/orders/42");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(await ReadStartedIdsAsync(response));
    }

    // ---- Spec 089 sub-unit C (T010): authorization, ParsedContent, per-endpoint size limit end to end ----

    [Fact]
    public async Task AuthorizedEndpoint_AnonymousRequest_Replies401_AndStartsNoInstance()
    {
        await _fixture.PublishHttpEndpointWorkflowAsync(
            "artifact-secure", "secure/orders", ResultOutputName, ["POST"], authorize: true, policy: null, requestSizeLimit: null);

        var response = await _fixture.Client.PostAsync("/workflows/http/secure/orders",
            new StringContent("""{"id":1}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // Fail closed before dispatch: the runtime persisted no execution.
        Assert.Equal(0, await _fixture.CountWorkflowExecutionsAsync());
    }

    [Fact]
    public async Task AuthorizedEndpoint_WithTestAuthHeader_Replies202()
    {
        await _fixture.PublishHttpEndpointWorkflowAsync(
            "artifact-secure-ok", "secure/ok", ResultOutputName, ["POST"], authorize: true, policy: null, requestSizeLimit: null);

        var request = new HttpRequestMessage(HttpMethod.Post, "/workflows/http/secure/ok")
        {
            Content = new StringContent("""{"id":1}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", "Test alice");

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(await ReadStartedIdsAsync(response));
    }

    [Fact]
    public async Task JsonBody_SurfacesParsedContentOnTheRunsDurableResult()
    {
        await _fixture.PublishHttpEndpointWorkflowAsync("artifact-parse", "parse/orders", ResultOutputName, "POST");

        var response = await _fixture.Client.PostAsync("/workflows/http/parse/orders",
            new StringContent("""{"orderId":7,"customer":"acme"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var workflowExecutionId = Assert.Single(await ReadStartedIdsAsync(response));

        var captured = await _fixture.ReadCapturedOutputAsync(workflowExecutionId, ResultOutputName);
        var model = captured.Deserialize<HttpRequestModel>()!;

        // The real HttpRequestBodyParser (T005) turned the JSON body into the wire-safe ParsedContent element.
        Assert.NotNull(model.ParsedContent);
        var parsed = model.ParsedContent!.Value;
        Assert.Equal(JsonValueKind.Object, parsed.ValueKind);
        Assert.Equal(7, parsed.GetProperty("orderId").GetInt32());
        Assert.Equal("acme", parsed.GetProperty("customer").GetString());
    }

    [Fact]
    public async Task EndpointWithSmallRequestSizeLimit_OversizedBody_Replies413_AndStartsNoInstance()
    {
        await _fixture.PublishHttpEndpointWorkflowAsync(
            "artifact-sized", "sized/orders", ResultOutputName, ["POST"], authorize: null, policy: null, requestSizeLimit: 16);

        var response = await _fixture.Client.PostAsync("/workflows/http/sized/orders",
            new StringContent(new string('x', 64), Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, await _fixture.CountWorkflowExecutionsAsync());
    }

    private static async Task<IReadOnlyList<string>> ReadStartedIdsAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        var payload = await JsonDocument.ParseAsync(stream);
        return payload.RootElement.GetProperty("started").EnumerateArray().Select(e => e.GetString()!).ToArray();
    }
}
