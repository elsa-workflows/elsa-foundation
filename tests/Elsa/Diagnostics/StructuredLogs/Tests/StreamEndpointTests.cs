using System.Text.RegularExpressions;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Endpoints;
using Elsa.Diagnostics.StructuredLogs.Live;
using Elsa.Diagnostics.StructuredLogs.Storage;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;
using Elsa.Api.FastEndpoints.Sse;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

/// <summary>
/// Covers the durable multi-process SSE tail. Provider read-after pages are the only payload/order source;
/// each host's in-memory feed is only a wake hint, and bounded polling discovers remote-only commits.
/// The endpoint is internal, so it is built through FastEndpoints' <see cref="Factory"/> via reflection.
/// </summary>
public sealed class StreamEndpointTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Two_hosts_tail_shared_durable_order_and_reconnect_exactly_once()
    {
        var options = TailOptions(pageSize: 1);
        var durable = new SharedDurableSequence();
        var feedA = new InMemoryStructuredLogLiveFeed(options);
        var feedB = new InMemoryStructuredLogLiveFeed(options);
        var storeA = new SharedStore(durable);
        var storeB = new SharedStore(durable);
        var sinkA = new StructuredLogSink(storeA, feedA);
        var sinkB = new StructuredLogSink(storeB, feedB);
        var anchor = await storeA.AppendAsync(TestEntries.Create(sequence: 1, message: "anchor"));
        var (endpoint, body) = CreateEndpoint(feedA, storeA, options, anchor.ReplayCursor!.Value.Value);
        using var cts = new CancellationTokenSource(TestTimeout);
        var handleTask = HandleAsync(endpoint, cts.Token);

        sinkB.Emit(TestEntries.Create(message: "B:C1"));
        await durable.WaitForCountAsync(2, cts.Token);
        sinkA.Emit(TestEntries.Create(message: "A:C2"));
        await durable.WaitForCountAsync(3, cts.Token);
        var committed = durable.Snapshot();
        var c1 = committed[1].ReplayCursor!.Value;
        var c2 = committed[2].ReplayCursor!.Value;
        feedA.Publish(committed[2]);
        feedA.Publish(committed[2]);

        await body.WaitForTextAsync(c2.Value, cts.Token);
        await cts.CancelAsync();
        await handleTask;

        Assert.Equal(1, CountFrames(body.Text, c1));
        Assert.Equal(1, CountFrames(body.Text, c2));
        Assert.True(body.Text.IndexOf(c1.Value, StringComparison.Ordinal) < body.Text.IndexOf(c2.Value, StringComparison.Ordinal));

        var (reconnected, reconnectBody) = CreateEndpoint(feedA, storeA, options, c1.Value);
        using var reconnectCts = new CancellationTokenSource(TestTimeout);
        var reconnectTask = HandleAsync(reconnected, reconnectCts.Token);
        await reconnectBody.WaitForTextAsync(c2.Value, reconnectCts.Token);
        await reconnectCts.CancelAsync();
        await reconnectTask;
        Assert.Equal(1, CountFrames(reconnectBody.Text, c2));
    }

    [Fact]
    public async Task Commit_during_blocked_replay_is_found_without_a_poll_gap()
    {
        var options = TailOptions(pageSize: 1);
        var durable = new SharedDurableSequence();
        var feedA = new InMemoryStructuredLogLiveFeed(options);
        var feedB = new InMemoryStructuredLogLiveFeed(options);
        var storeA = new SharedStore(durable);
        var storeB = new SharedStore(durable);
        var sinkB = new StructuredLogSink(storeB, feedB);
        var anchor = await storeA.AppendAsync(TestEntries.Create(sequence: 1, message: "anchor"));
        durable.BlockNextRead();
        var (endpoint, body) = CreateEndpoint(feedA, storeA, options, anchor.ReplayCursor!.Value.Value);
        using var cts = new CancellationTokenSource(TestTimeout);
        var handleTask = HandleAsync(endpoint, cts.Token);
        await durable.ReadBlocked.WaitAsync(cts.Token);

        sinkB.Emit(TestEntries.Create(message: "committed-during-replay"));
        await durable.WaitForCountAsync(2, cts.Token);
        durable.ReleaseRead();
        var cursor = durable.Snapshot()[1].ReplayCursor!.Value;
        await body.WaitForTextAsync(cursor.Value, cts.Token);
        await cts.CancelAsync();
        await handleTask;
        Assert.Equal(1, CountFrames(body.Text, cursor));
    }

    [Fact]
    public async Task Filtered_tail_advances_over_nonmatching_remote_commits()
    {
        var options = TailOptions(pageSize: 1);
        var durable = new SharedDurableSequence();
        var feedA = new InMemoryStructuredLogLiveFeed(options);
        var feedB = new InMemoryStructuredLogLiveFeed(options);
        var storeA = new SharedStore(durable);
        var storeB = new SharedStore(durable);
        var anchor = await storeA.AppendAsync(TestEntries.Create(sequence: 1, sourceId: "match", message: "anchor"));
        var (endpoint, body) = CreateEndpoint(feedA, storeA, options, anchor.ReplayCursor!.Value.Value, source: "match");
        using var cts = new CancellationTokenSource(TestTimeout);
        var handleTask = HandleAsync(endpoint, cts.Token);

        var skipped = await storeB.AppendAsync(TestEntries.Create(sequence: 2, sourceId: "other", message: "skip"));
        var matched = await storeB.AppendAsync(TestEntries.Create(sequence: 3, sourceId: "match", message: "deliver"));
        await body.WaitForTextAsync(matched.ReplayCursor!.Value.Value, cts.Token);
        await cts.CancelAsync();
        await handleTask;
        Assert.DoesNotContain(skipped.ReplayCursor!.Value.Value, body.Text, StringComparison.Ordinal);
        Assert.Equal(1, CountFrames(body.Text, matched.ReplayCursor.Value));
    }

    [Fact]
    public async Task Failed_local_wake_feed_does_not_stop_remote_durable_polling()
    {
        var options = TailOptions(pageSize: 1);
        var durable = new SharedDurableSequence();
        var storeA = new SharedStore(durable);
        var storeB = new SharedStore(durable);
        var anchor = await storeA.AppendAsync(TestEntries.Create(sequence: 1, message: "anchor"));
        var (endpoint, body) = CreateEndpoint(
            new ThrowingLiveFeed(),
            storeA,
            options,
            anchor.ReplayCursor!.Value.Value);
        using var cts = new CancellationTokenSource(TestTimeout);
        var handleTask = HandleAsync(endpoint, cts.Token);

        var remote = await storeB.AppendAsync(TestEntries.Create(sequence: 2, message: "remote"));
        await body.WaitForTextAsync(remote.ReplayCursor!.Value.Value, cts.Token);
        await cts.CancelAsync();
        await handleTask;

        Assert.Equal(1, CountFrames(body.Text, remote.ReplayCursor.Value));
    }

    [Fact]
    public async Task Cancellation_is_bounded_when_local_wake_feed_ignores_it()
    {
        var options = TailOptions(pageSize: 1);
        var durable = new SharedDurableSequence();
        var store = new SharedStore(durable);
        var anchor = await store.AppendAsync(TestEntries.Create(sequence: 1, message: "anchor"));
        var (endpoint, body) = CreateEndpoint(
            new HangingLiveFeed(),
            store,
            options,
            anchor.ReplayCursor!.Value.Value);
        using var cts = new CancellationTokenSource(TestTimeout);
        var handleTask = HandleAsync(endpoint, cts.Token);
        await body.WaitForTextAsync(": keep-alive", cts.Token);

        await cts.CancelAsync();

        await handleTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Wrong_scope_cursor_returns_one_non_disclosing_conflict_response()
    {
        var options = TestOptions.Create();
        var source = new InMemoryStructuredLogStore(options, new("tenant-a", "shell-a", "structured-logs"));
        var target = new InMemoryStructuredLogStore(options, new("tenant-a", "shell-b", "structured-logs"));
        var committed = await source.AppendAsync(TestEntries.Create(sequence: 1));
        var feed = new InMemoryStructuredLogLiveFeed(options);
        var (endpoint, body) = CreateEndpoint(feed, target, options, committed.ReplayCursor!.Value.Value);

        await HandleAsync(endpoint, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, endpoint.HttpContext.Response.StatusCode);
        Assert.Equal("The structured log replay cursor is unavailable.", body.Text);
        Assert.DoesNotContain("tenant", body.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scope", body.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nul_cursor_is_rejected_at_the_sse_request_boundary()
    {
        var options = TestOptions.Create();
        var store = new InMemoryStructuredLogStore(options);
        var feed = new InMemoryStructuredLogLiveFeed(options);
        var (endpoint, body) = CreateEndpoint(feed, store, options, "slrc1.test.bad\0cursor");

        await HandleAsync(endpoint, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, endpoint.HttpContext.Response.StatusCode);
        Assert.Equal("The structured log replay cursor is unavailable.", body.Text);
    }

    private static int CountFrames(string sse, StructuredLogReplayCursor cursor) => Regex.Matches(sse, $"id: {Regex.Escape(cursor.Value)}\n").Count;

    private static StructuredLogReplayCursor Cursor(long sequence) => new($"slrc1.test.{sequence}");

    private static StructuredLogEntry Committed(long sequence, string message) =>
        TestEntries.Create(sequence: sequence, message: message) with { ReplayCursor = Cursor(sequence) };

    private static IOptions<StructuredLogsOptions> TailOptions(int pageSize)
    {
        var options = TestOptions.Create().Value;
        options.MaxRecentQuerySize = pageSize;
        options.TailPollInterval = TimeSpan.FromMilliseconds(10);
        return Options.Create(options);
    }

    private static (BaseEndpoint Endpoint, CapturingResponseBody Body) CreateEndpoint(
        IStructuredLogLiveFeed feed,
        IStructuredLogStore store,
        IOptions<StructuredLogsOptions> options,
        string? lastEventId,
        string? source = null)
    {
        var body = new CapturingResponseBody();
        var formatter = new StructuredLogSseFormatter(new StructuredLogEntrySerializer());
        var streamWriter = new SseStreamWriter<StructuredLogStreamItem>(formatter, TimeSpan.FromMilliseconds(50));
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
            if (lastEventId is not null)
                context.Request.Headers["Last-Event-ID"] = lastEventId;
            if (source is not null)
                context.Request.QueryString = new QueryString($"?source={Uri.EscapeDataString(source)}");
            context.Response.Body = body;
        };

        var endpoint = (BaseEndpoint)create.Invoke(
            null,
            [configureContext, new object[] { feed, store, binder, streamWriter, options }])!;

        return (endpoint, body);
    }

    private static Task HandleAsync(BaseEndpoint endpoint, CancellationToken ct) =>
        (Task)endpoint.GetType()
            .GetMethod("HandleAsync", [typeof(CancellationToken)])!
            .Invoke(endpoint, [ct])!;

    private sealed class SharedDurableSequence
    {
        private readonly object _gate = new();
        private readonly List<StructuredLogEntry> _entries = [];
        private TaskCompletionSource? _readBlocked;
        private TaskCompletionSource? _releaseRead;

        public Task ReadBlocked => _readBlocked?.Task ?? Task.CompletedTask;

        public StructuredLogEntry Append(StructuredLogEntry entry)
        {
            lock (_gate)
            {
                var position = _entries.Count + 1;
                var committed = entry with { ReplayCursor = Cursor(position) };
                _entries.Add(committed);
                return committed;
            }
        }

        public StructuredLogEntry[] Snapshot()
        {
            lock (_gate)
                return _entries.ToArray();
        }

        public void BlockNextRead()
        {
            _readBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _releaseRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task AfterSnapshotAsync(CancellationToken cancellationToken)
        {
            var blocked = Interlocked.Exchange(ref _readBlocked, null);
            var release = _releaseRead;
            if (blocked is null || release is null)
                return;
            blocked.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            Interlocked.CompareExchange(ref _releaseRead, null, release);
        }

        public void ReleaseRead() => _releaseRead?.TrySetResult();

        public async Task WaitForCountAsync(int count, CancellationToken cancellationToken)
        {
            while (Snapshot().Length < count)
                await Task.Delay(5, cancellationToken);
        }
    }

    private sealed class SharedStore(SharedDurableSequence durable) : IStructuredLogStore
    {
        public ValueTask<StructuredLogEntry> AppendAsync(StructuredLogEntry entry, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(durable.Append(entry));
        }

        public Task<long> GetHighWaterMarkAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(durable.Snapshot().Select(x => x.Sequence).DefaultIfEmpty().Max());

        public Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(StructuredLogFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StructuredLogEntry>>(durable.Snapshot().Where(filter.Matches).ToArray());

        public Task<StructuredLogReplayCursor?> GetTailCursorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(durable.Snapshot().LastOrDefault()?.ReplayCursor);

        public async Task<StructuredLogReadPage> ReadAfterAsync(StructuredLogReplayCursor? afterCursor, StructuredLogFilter filter, int maxCount, CancellationToken cancellationToken = default)
        {
            var snapshot = durable.Snapshot();
            await durable.AfterSnapshotAsync(cancellationToken);
            var start = 0;
            if (afterCursor is { } cursor)
            {
                start = Array.FindIndex(snapshot, x => x.ReplayCursor == cursor) + 1;
                if (start == 0)
                    throw new Elsa.Diagnostics.StructuredLogs.Core.Exceptions.StructuredLogReplayCursorUnavailableException();
            }

            var scanned = snapshot.Skip(start).Take(maxCount).ToArray();
            return new(
                scanned.Where(filter.Matches).ToArray(),
                scanned.LastOrDefault()?.ReplayCursor ?? afterCursor,
                start + scanned.Length < snapshot.Length);
        }

        public Task TrimAsync(int keepNewest, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingLiveFeed : IStructuredLogLiveFeed
    {
        public IAsyncEnumerable<StructuredLogStreamItem> Subscribe(
            StructuredLogFilter filter,
            CancellationToken cancellationToken) => new ThrowingEnumerable();

        private sealed class ThrowingEnumerable : IAsyncEnumerable<StructuredLogStreamItem>, IAsyncEnumerator<StructuredLogStreamItem>
        {
            public StructuredLogStreamItem Current => throw new InvalidOperationException();

            public IAsyncEnumerator<StructuredLogStreamItem> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

            public ValueTask<bool> MoveNextAsync() => ValueTask.FromException<bool>(new InvalidOperationException("wake feed failed"));

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class HangingLiveFeed : IStructuredLogLiveFeed
    {
        public IAsyncEnumerable<StructuredLogStreamItem> Subscribe(
            StructuredLogFilter filter,
            CancellationToken cancellationToken) => new HangingEnumerable();

        private sealed class HangingEnumerable : IAsyncEnumerable<StructuredLogStreamItem>, IAsyncEnumerator<StructuredLogStreamItem>
        {
            private readonly TaskCompletionSource<bool> _never = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public StructuredLogStreamItem Current => throw new InvalidOperationException();

            public IAsyncEnumerator<StructuredLogStreamItem> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

            public ValueTask<bool> MoveNextAsync() => new(_never.Task);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
