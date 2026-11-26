using System.Text;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

/// <summary>
/// A write-only response body that captures everything written to it and lets tests await the arrival
/// of specific text. Shared by the SSE stream-writer tests and the stream-endpoint tests.
/// </summary>
internal sealed class CapturingResponseBody : Stream
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
