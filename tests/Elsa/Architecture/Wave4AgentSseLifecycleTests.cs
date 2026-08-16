using System.Net;
using System.Runtime.CompilerServices;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Xunit;

namespace Elsa.Architecture.Tests;

[Collection(Wave4AgentFastEndpointsCollection.Name)]
public sealed class Wave4AgentSseLifecycleTests
{
    [Fact]
    public async Task Stream_preserves_sse_headers_framing_and_awaited_backpressure()
    {
        await using var host = await Wave4AgentMinimalApiHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_elsa/agent/sessions/session-1/stream");
        request.Headers.Add(Wave4AgentHost.IdentityHeader, "use");

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        Assert.Equal("no", response.Headers.GetValues("X-Accel-Buffering").Single());
        Assert.Equal(
            "data: {\"Id\":\"event-1\",\"Kind\":1,\"Content\":\"hello\",\"ProposalId\":null,\"Error\":null,\"CreatedAt\":\"2026-08-16T12:00:00+00:00\",\"ResultKind\":null,\"Payload\":null}\n\n" +
            "data: {\"Id\":\"event-2\",\"Kind\":4,\"Content\":null,\"ProposalId\":null,\"Error\":null,\"CreatedAt\":\"2026-08-16T12:00:00+00:00\",\"ResultKind\":null,\"Payload\":null}\n\n",
            body);
        Assert.Equal(2, body.Split("data: ", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.DoesNotContain("heartbeat", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Client_cancellation_disposes_the_streaming_enumerator()
    {
        var streaming = new Wave4TrackingStreamingService();
        await using var host = await Wave4AgentMinimalApiHost.StartAsync(streaming);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_elsa/agent/sessions/session-1/stream");
        request.Headers.Add(Wave4AgentHost.IdentityHeader, "use");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
        await streaming.Started.Task.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        response.Dispose();

        await streaming.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(streaming.Disposed.Task.IsCompletedSuccessfully);
    }

    private sealed class Wave4TrackingStreamingService : IAgentStreamingService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(string sessionId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                Started.TrySetResult();
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return new AgentStreamEvent("heartbeat-test", AgentStreamEventKind.Progress, "pending", null, null, Wave4AgentFixtures.Now);
                    await Task.Delay(50, cancellationToken);
                }
            }
            finally
            {
                Disposed.TrySetResult();
            }
        }
    }
}
