using System.Net;
using Xunit;

namespace Elsa.Workbench.Tests;

public sealed class NativeHostingScenarioTests
{
    [Fact]
    public async Task Shared_json_sender_reports_a_non_json_error_with_request_and_host_diagnostics_once()
    {
        const string body = "upstream rejected the workflow lookup";
        var handler = new FixedResponseHandler(HttpStatusCode.BadRequest, body);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var outputCalls = 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NativeHostingScenario.SendJsonAsync(
                client,
                new HttpRequestMessage(HttpMethod.Get, "/runtime/workflows/instances/execution-1"),
                () => { outputCalls++; return "owned host output"; }));

        Assert.Contains("GET /runtime/workflows/instances/execution-1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("400", exception.Message, StringComparison.Ordinal);
        Assert.Contains(body, exception.Message, StringComparison.Ordinal);
        Assert.Contains("owned host output", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, outputCalls);
    }

    [Fact]
    public async Task Shared_json_sender_parses_a_successful_response()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, "{\"status\":\"ready\"}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var response = await NativeHostingScenario.SendJsonAsync(
            client,
            new HttpRequestMessage(HttpMethod.Get, "/runtime/workflows/instances/execution-1"),
            () => "owned host output");

        Assert.Equal("ready", response?["status"]?.GetValue<string>());
        Assert.Equal(1, handler.CallCount);
    }

    private sealed class FixedResponseHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        private int callCount;
        public int CallCount => Volatile.Read(ref callCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
        }
    }
}
