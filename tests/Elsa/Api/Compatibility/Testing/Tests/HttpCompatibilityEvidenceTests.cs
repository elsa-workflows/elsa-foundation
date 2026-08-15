using System.Net;
using System.Net.Http.Json;
using System.Text;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.Serialization;
using Xunit;

namespace Elsa.Api.Compatibility.Testing.Tests;

public sealed class HttpCompatibilityEvidenceTests
{
    [Fact]
    public async Task Captures_binding_json_status_and_problem_details_canonically()
    {
        using var client = new HttpClient(new FixedHandler(HttpStatusCode.BadRequest,
            "application/problem+json", "{\"detail\":\"bad\",\"title\":\"Invalid\"}"));
        var testCase = new HttpCompatibilityCase(new EndpointIdentity("/api/orders/{id}", "post"), "invalid",
            () => new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/orders/7?filter=open")
            {
                Content = JsonContent.Create(new { id = 7 })
            })
        { Binding = "route=id;query=filter;body=json", PagingFiltering = "query=?filter=open" };

        var evidence = await HttpEvidenceCapture.CaptureAsync(client, testCase);

        Assert.Equal("route=id;query=filter;body=json", evidence.Binding);
        Assert.Equal(CompatibilityJson.Canonicalize("{\"detail\":\"bad\",\"title\":\"Invalid\"}"), evidence.Json);
        Assert.Equal(400, evidence.StatusCode);
        Assert.Equal(evidence.Json, evidence.ProblemDetails);
        Assert.Equal("query=?filter=open", evidence.PagingFiltering);
    }

    [Fact]
    public async Task Bounds_streaming_to_frames_and_bytes_and_preserves_terminal_state()
    {
        using var client = new HttpClient(new FixedHandler(HttpStatusCode.OK, "text/event-stream", "one\ntwo\nthree\nfour"));
        var testCase = new HttpCompatibilityCase(new EndpointIdentity("/events", "get"), "stream",
            () => new HttpRequestMessage(HttpMethod.Get, "http://localhost/events"))
        { BoundedStreaming = true, MaxStreamFrames = 2, MaxStreamBytes = 100 };

        var evidence = await HttpEvidenceCapture.CaptureAsync(client, testCase);

        Assert.Equal("one\ntwo", evidence.Streaming);
        Assert.Equal("Completed", evidence.TerminalState);
    }

    [Fact]
    public async Task Automatically_bounds_event_streams_even_when_the_case_omits_the_streaming_flag()
    {
        using var client = new HttpClient(new FixedHandler(HttpStatusCode.OK, "text/event-stream", new string('x', 70_000)));
        var testCase = new HttpCompatibilityCase(new EndpointIdentity("/events", "get"), "auto-bounded-stream",
            () => new HttpRequestMessage(HttpMethod.Get, "http://localhost/events"));

        var evidence = await HttpEvidenceCapture.CaptureAsync(client, testCase);

        Assert.Equal(64 * 1024, evidence.Body.Length);
        Assert.Equal("Bounded", evidence.TerminalState);
    }

    [Fact]
    public async Task Reads_an_ordinary_json_response_past_the_stream_bound()
    {
        var beforeBody = $"{{\"prefix\":\"{new string('x', 70_000)}\",\"tail\":\"before\"}}";
        var afterBody = $"{{\"prefix\":\"{new string('x', 70_000)}\",\"tail\":\"after\"}}";
        using var beforeClient = new HttpClient(new FixedHandler(HttpStatusCode.OK, "application/json", beforeBody));
        using var afterClient = new HttpClient(new FixedHandler(HttpStatusCode.OK, "application/json", afterBody));
        var testCase = new HttpCompatibilityCase(new EndpointIdentity("/large", "get"), "default",
            () => new HttpRequestMessage(HttpMethod.Get, "http://localhost/large"));

        var before = await HttpEvidenceCapture.CaptureAsync(beforeClient, testCase);
        var after = await HttpEvidenceCapture.CaptureAsync(afterClient, testCase);

        Assert.Contains("before", before.Json, StringComparison.Ordinal);
        Assert.Contains("after", after.Json, StringComparison.Ordinal);
        Assert.NotEqual(before.Json, after.Json);
        Assert.Equal("Completed", after.TerminalState);
    }

    private sealed class FixedHandler(HttpStatusCode status, string mediaType, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType)
            });
    }
}
