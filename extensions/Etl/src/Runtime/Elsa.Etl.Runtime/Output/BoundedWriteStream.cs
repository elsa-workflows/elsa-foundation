namespace Elsa.Etl.Runtime.Output;

/// <summary>Single-writer byte admission; disposal leaves its caller-owned destination open.</summary>
internal sealed class BoundedWriteStream(Stream destination, long maxBytes) : Stream
{
    private long _written;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => destination.CanWrite;
    public override long Length => _written;
    public override long Position { get => _written; set => throw new NotSupportedException(); }
    public override void Flush() => destination.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => destination.FlushAsync(cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        CheckSize(buffer.Length);
        destination.Write(buffer);
        _written += buffer.Length;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckSize(buffer.Length);
        await destination.WriteAsync(buffer, cancellationToken);
        _written += buffer.Length;
    }

    private void CheckSize(int count)
    {
        if (count > maxBytes - _written)
            throw new EtlEffectException("etl.output-too-large", "ETL output exceeds the configured byte limit.");
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
