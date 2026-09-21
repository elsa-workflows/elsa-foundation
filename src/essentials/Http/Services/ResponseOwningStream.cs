namespace Elsa.Http.Services;

/// <summary>
/// Wraps a content stream and takes ownership of the <see cref="HttpResponseMessage"/> it was read from,
/// disposing the response (and thereby releasing the underlying connection) once the stream is disposed.
/// </summary>
/// <remarks>
/// Disposing the content stream alone does not dispose the owning <see cref="HttpResponseMessage"/>, so a
/// downloadable sourced from an HTTP response would otherwise leak the response/connection lease even after
/// its stream has been consumed and disposed.
/// </remarks>
internal sealed class ResponseOwningStream(Stream inner, HttpResponseMessage response) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => inner.Read(buffer);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.WriteAsync(buffer, offset, count, cancellationToken);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            response.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        response.Dispose();
        await base.DisposeAsync();
    }
}
