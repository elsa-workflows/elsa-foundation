using System.Text.RegularExpressions;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Endpoints;
using Elsa.Diagnostics.StructuredLogs.Live;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

/// <summary>
/// Covers issue #411 (SSE resume gap): the stream endpoint replayed history via the store and only then
/// subscribed to the live feed, so any entry emitted between the history snapshot and the subscription was
/// neither replayed nor delivered live — silently lost. The endpoint must subscribe before querying
/// history (the in-memory feed registers subscribers eagerly, buffering entries published during replay)
/// and de-duplicate by sequence when an entry appears in both history and the buffered live stream.
/// The endpoint is internal, so it is built through FastEndpoints' <see cref="Factory"/> via reflection.
/// </summary>
public sealed class StreamEndpointTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Resume_delivers_entries_emitted_during_the_replay_window()
    {
        var options = TestOptions.Create();
        var feed = new InMemoryStructuredLogLiveFeed(options);
        // Simulates an entry emitted while the history query is in flight: sequence 7 is published to the
        // live feed during GetAfterAsync and is NOT part of the returned history snapshot.
        var store = new GapStore(
            history: [TestEntries.Create(sequence: 6, message: "history")],
            duringQuery: () => feed.Publish(TestEntries.Create(sequence: 7, message: "gap")));
        var (endpoint, body) = CreateEndpoint(feed, store, options, lastEventId: "5");
        using var cts = new CancellationTokenSource(TestTimeout);

        var handleTask = HandleAsync(endpoint, cts.Token);

        await body.WaitForTextAsync("id: 6", cts.Token);
        await body.WaitForTextAsync("id: 7", cts.Token); // Lost before the fix: times out and fails here.
        await cts.CancelAsync();
        await handleTask;

        Assert.Equal(1, CountFrames(body.Text, 6));
        Assert.Equal(1, CountFrames(body.Text, 7));
    }

    [Fact]
    public async Task Resume_does_not_duplicate_entries_present_in_both_history_and_the_buffered_live_stream()
    {
        var options = TestOptions.Create();
        var feed = new InMemoryStructuredLogLiveFeed(options);
        // Sequence 7 lands in BOTH the history snapshot and the live buffer (published during the query):
        // the endpoint must deliver it exactly once.
        var store = new GapStore(
            history: [TestEntries.Create(sequence: 6, message: "history"), TestEntries.Create(sequence: 7, message: "both")],
            duringQuery: () => feed.Publish(TestEntries.Create(sequence: 7, message: "both")));
        var (endpoint, body) = CreateEndpoint(feed, store, options, lastEventId: "5");
        using var cts = new CancellationTokenSource(TestTimeout);

        var handleTask = HandleAsync(endpoint, cts.Token);

        await body.WaitForTextAsync("id: 7", cts.Token);
        feed.Publish(TestEntries.Create(sequence: 8, message: "live"));
        await body.WaitForTextAsync("id: 8", cts.Token);
        await cts.CancelAsync();
        await handleTask;

        Assert.Equal(1, CountFrames(body.Text, 7));
        Assert.Equal(1, CountFrames(body.Text, 8));
    }

    private static int CountFrames(string sse, long sequence) => Regex.Matches(sse, $"id: {sequence}\n").Count;

    private static (BaseEndpoint Endpoint, CapturingResponseBody Body) CreateEndpoint(
        InMemoryStructuredLogLiveFeed feed,
        IStructuredLogStore store,
        IOptions<StructuredLogsOptions> options,
        string lastEventId)
    {
        var body = new CapturingResponseBody();
        var formatter = new StructuredLogSseFormatter(new StructuredLogEntrySerializer());
        var streamWriter = new StructuredLogSseStreamWriter(formatter, TimeSpan.FromMilliseconds(50));
        var binder = new StructuredLogFilterBinder();

        var endpointType = typeof(StructuredLogSseFormatter).Assembly
            .GetType("Elsa.Diagnostics.StructuredLogs.Endpoints.StreamEndpoint", throwOnError: true)!;

        var create = typeof(Factory).GetMethods()
            .Single(m => m.Name == nameof(Factory.Create)
                         && m.IsGenericMethodDefinition
                         && m.GetParameters() is [var first, var rest]
                         && first.ParameterType == typeof(Action<DefaultHttpContext>)
                         && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);

        Action<DefaultHttpContext> configureContext = context =>
        {
            context.Request.Headers["Last-Event-ID"] = lastEventId;
            context.Response.Body = body;
        };

        var endpoint = (BaseEndpoint)create.Invoke(
            null,
            [configureContext, new object[] { feed, store, binder, formatter, streamWriter, options }])!;

        return (endpoint, body);
    }

    private static Task HandleAsync(BaseEndpoint endpoint, CancellationToken ct) =>
        (Task)endpoint.GetType()
            .GetMethod("HandleAsync", [typeof(CancellationToken)])!
            .Invoke(endpoint, [ct])!;

    private sealed class GapStore(IReadOnlyList<StructuredLogEntry> history, Action duringQuery) : IStructuredLogStore
    {
        public void Append(StructuredLogEntry entry)
        {
        }

        public Task<long> GetHighWaterMarkAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(history.Count == 0 ? 0 : history.Max(e => e.Sequence));

        public Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(StructuredLogFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(history);

        public Task<IReadOnlyList<StructuredLogEntry>> GetAfterAsync(long afterSequence, StructuredLogFilter filter, CancellationToken cancellationToken = default)
        {
            duringQuery();
            return Task.FromResult<IReadOnlyList<StructuredLogEntry>>(history.Where(e => e.Sequence > afterSequence).ToList());
        }
    }
}
