using System.Text;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Endpoints;
using Microsoft.AspNetCore.Http;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

public sealed class OpenTelemetrySseStreamWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 26, 10, 0, 0, TimeSpan.Zero);
    private readonly OpenTelemetrySseStreamWriter _writer = new(
        new OpenTelemetrySseFormatter(new OpenTelemetryStreamItemSerializer()),
        TimeSpan.FromMilliseconds(10));

    [Fact]
    public void Constructor_WhenFormatterIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OpenTelemetrySseStreamWriter(null!));
    }

    [Fact]
    public void Constructor_WhenHeartbeatIntervalIsNotPositive_Throws()
    {
        var formatter = new OpenTelemetrySseFormatter(new OpenTelemetryStreamItemSerializer());

        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenTelemetrySseStreamWriter(formatter, TimeSpan.Zero));
    }

    [Fact]
    public void Constructor_WhenPendingMoveNextCleanupTimeoutIsNotPositive_Throws()
    {
        var formatter = new OpenTelemetrySseFormatter(new OpenTelemetryStreamItemSerializer());

        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenTelemetrySseStreamWriter(formatter, TimeSpan.FromMilliseconds(10), TimeSpan.Zero));
    }

    [Fact]
    public async Task StreamAsync_WhenResponseIsNull_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _writer.StreamAsync(null!, SingleTraceStream(), CancellationToken.None));
    }

    [Fact]
    public async Task StreamAsync_WhenStreamIsNull_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _writer.StreamAsync(CreateResponse(new MemoryStream()), null!, CancellationToken.None));
    }

    [Fact]
    public async Task StreamAsync_WritesItemsUntilStreamCompletes()
    {
        var responseBody = new CapturingResponseBody();
        var response = CreateResponse(responseBody);

        await _writer.StreamAsync(response, SingleTraceStream(), CancellationToken.None);

        var output = responseBody.Text;
        Assert.Contains("event: trace\n", output);
        Assert.Contains("\"traceId\":\"trace-42\"", output);
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
        var writer = new OpenTelemetrySseStreamWriter(
            new OpenTelemetrySseFormatter(new OpenTelemetryStreamItemSerializer()),
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

    private static async IAsyncEnumerable<OpenTelemetryStreamItem> SingleTraceStream()
    {
        yield return new OpenTelemetryStreamItem
        {
            Trace = new TelemetryTrace("trace-42", "root", "name", Now, Now.AddMilliseconds(5), TimeSpan.FromMilliseconds(5), SpanStatus.Ok, ["resource-1"], [], 1)
        };
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

    private sealed class CapturingResponseBody : Stream
    {
        private readonly object _gate = new();
        private readonly MemoryStream _buffer = new();
        private TaskCompletionSource _writeSignal = NewSignal();

        public string Text
        {
            get
            {
                lock (_gate)
                    return Encoding.UTF8.GetString(_buffer.ToArray());
            }
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length
        {
            get
            {
                lock (_gate)
                    return _buffer.Length;
            }
        }

        public override long Position
        {
            get => Length;
            set => throw new NotSupportedException();
        }

        public async Task WaitForTextAsync(string expected, CancellationToken cancellationToken)
        {
            while (true)
            {
                Task writeTask;
                lock (_gate)
                {
                    if (TextUnsafe().Contains(expected))
                        return;

                    writeTask = _writeSignal.Task;
                }

                await writeTask.WaitAsync(cancellationToken);
            }
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_gate)
            {
                _buffer.Position = _buffer.Length;
                _buffer.Write(buffer, offset, count);
                SignalWrite();
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _buffer.Position = _buffer.Length;
                _buffer.Write(buffer.Span);
                SignalWrite();
            }

            return ValueTask.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        private void SignalWrite()
        {
            _writeSignal.TrySetResult();
            _writeSignal = NewSignal();
        }

        private string TextUnsafe() => Encoding.UTF8.GetString(_buffer.ToArray());

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BlockingStream(bool observeCancellation = true) : IAsyncEnumerable<OpenTelemetryStreamItem>, IAsyncEnumerator<OpenTelemetryStreamItem>
    {
        private CancellationToken _cancellationToken;
        private int _moveNextPending;

        public TaskCompletionSource MoveNextStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool MoveNextCompleted { get; private set; }
        public bool DisposeAttempted { get; private set; }
        public bool Disposed { get; private set; }
        public OpenTelemetryStreamItem Current => throw new InvalidOperationException("The blocking stream never yields an item.");

        public IAsyncEnumerator<OpenTelemetryStreamItem> GetAsyncEnumerator(CancellationToken cancellationToken = default)
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
