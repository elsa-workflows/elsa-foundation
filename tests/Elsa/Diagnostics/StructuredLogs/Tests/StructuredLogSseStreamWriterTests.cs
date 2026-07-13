using System.Text;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogSseStreamWriterTests
{
    private readonly StructuredLogSseStreamWriter _writer = new(
        new StructuredLogSseFormatter(new StructuredLogEntrySerializer()),
        TimeSpan.FromMilliseconds(10));

    [Fact]
    public void Constructor_WhenFormatterIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new StructuredLogSseStreamWriter(null!));
    }

    [Fact]
    public void Constructor_WhenHeartbeatIntervalIsNotPositive_Throws()
    {
        var formatter = new StructuredLogSseFormatter(new StructuredLogEntrySerializer());

        Assert.Throws<ArgumentOutOfRangeException>(() => new StructuredLogSseStreamWriter(formatter, TimeSpan.Zero));
    }

    [Fact]
    public void Constructor_WhenPendingMoveNextCleanupTimeoutIsNotPositive_Throws()
    {
        var formatter = new StructuredLogSseFormatter(new StructuredLogEntrySerializer());

        Assert.Throws<ArgumentOutOfRangeException>(() => new StructuredLogSseStreamWriter(formatter, TimeSpan.FromMilliseconds(10), TimeSpan.Zero));
    }

    [Fact]
    public async Task StreamAsync_WhenResponseIsNull_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _writer.StreamAsync(null!, SingleEntryStream(), CancellationToken.None));
    }

    [Fact]
    public async Task StreamAsync_WhenStreamIsNull_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _writer.StreamAsync(CreateResponse(new MemoryStream()), null!, CancellationToken.None));
    }

    [Fact]
    public async Task StreamAsync_WritesEntriesUntilStreamCompletes()
    {
        var responseBody = new CapturingResponseBody();
        var response = CreateResponse(responseBody);

        await _writer.StreamAsync(response, SingleEntryStream(), CancellationToken.None);

        var output = responseBody.Text;
        Assert.Contains("event: entry\n", output);
        Assert.Contains("id: slrc1.", output);
        Assert.Contains("\"message\":\"streamed\"", output);
    }

    [Fact]
    public async Task StreamAsync_WritesHeartbeatWhileStreamIsIdle()
    {
        var responseBody = new CapturingResponseBody();
        var response = CreateResponse(responseBody);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stream = new BlockingStream();

        var streamTask = _writer.StreamAsync(response, stream, cts.Token);

        await WaitForResponseAsync(responseBody, ": keep-alive\n\n", cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => streamTask);
        Assert.Contains(": keep-alive\n\n", responseBody.Text);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task StreamAsync_WhenCancelledDuringPendingMoveNext_DisposesAfterMoveNextCompletes()
    {
        var responseBody = new CapturingResponseBody();
        var response = CreateResponse(responseBody);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stream = new BlockingStream();

        var streamTask = _writer.StreamAsync(response, stream, cts.Token);
        await stream.MoveNextStarted.Task.WaitAsync(cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => streamTask);
        Assert.True(stream.MoveNextCompleted);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task StreamAsync_WhenPendingMoveNextIgnoresCancellation_BoundsCleanupWait()
    {
        var writer = new StructuredLogSseStreamWriter(
            new StructuredLogSseFormatter(new StructuredLogEntrySerializer()),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10));
        var responseBody = new CapturingResponseBody();
        var response = CreateResponse(responseBody);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stream = new BlockingStream(observeCancellation: false);

        var streamTask = writer.StreamAsync(response, stream, cts.Token);
        await stream.MoveNextStarted.Task.WaitAsync(cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => streamTask.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.False(stream.MoveNextCompleted);
        Assert.True(stream.DisposeAttempted);
        Assert.False(stream.Disposed);
    }

    private static async IAsyncEnumerable<StructuredLogStreamItem> SingleEntryStream()
    {
        var entry = TestEntries.Create(sequence: 42, level: LogLevel.Information, message: "streamed");
        yield return StructuredLogStreamItem.ForEntry(entry with
        {
            ReplayCursor = new StructuredLogReplayCursor("slrc1.test.42")
        });
        await Task.CompletedTask;
    }

    private static HttpResponse CreateResponse(Stream body)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        return context.Response;
    }

    private static async Task WaitForResponseAsync(CapturingResponseBody stream, string expected, CancellationToken cancellationToken)
    {
        await stream.WaitForTextAsync(expected, cancellationToken);
    }

    private sealed class BlockingStream(bool observeCancellation = true) : IAsyncEnumerable<StructuredLogStreamItem>, IAsyncEnumerator<StructuredLogStreamItem>
    {
        private CancellationToken _cancellationToken;
        private int _moveNextPending;

        public TaskCompletionSource MoveNextStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool MoveNextCompleted { get; private set; }
        public bool DisposeAttempted { get; private set; }
        public bool Disposed { get; private set; }
        public StructuredLogStreamItem Current => throw new InvalidOperationException("The blocking stream never yields an item.");

        public IAsyncEnumerator<StructuredLogStreamItem> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            _cancellationToken = cancellationToken;
            return this;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            if (Interlocked.Exchange(ref _moveNextPending, 1) == 1)
                throw new InvalidOperationException("Concurrent MoveNextAsync calls are not expected.");

            MoveNextStarted.TrySetResult();
            return new ValueTask<bool>(WaitUntilCancelledAsync());
        }

        public ValueTask DisposeAsync()
        {
            DisposeAttempted = true;
            if (Volatile.Read(ref _moveNextPending) == 1)
                throw new NotSupportedException("DisposeAsync was called while MoveNextAsync was still pending.");

            Disposed = true;
            return ValueTask.CompletedTask;
        }

        private async Task<bool> WaitUntilCancelledAsync()
        {
            try
            {
                var cancellationToken = observeCancellation ? _cancellationToken : CancellationToken.None;
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return false;
            }
            finally
            {
                MoveNextCompleted = true;
                Volatile.Write(ref _moveNextPending, 0);
            }
        }
    }
}
