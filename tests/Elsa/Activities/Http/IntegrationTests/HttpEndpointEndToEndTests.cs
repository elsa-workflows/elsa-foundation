using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Models;

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
        await _fixture.PublishHttpEndpointWorkflowAsync("artifact-orders", Path, ResultOutputName);

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

    private static async Task<IReadOnlyList<string>> ReadStartedIdsAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        var payload = await JsonDocument.ParseAsync(stream);
        return payload.RootElement.GetProperty("started").EnumerateArray().Select(e => e.GetString()!).ToArray();
    }
}
