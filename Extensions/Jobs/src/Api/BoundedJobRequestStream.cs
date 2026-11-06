namespace Elsa.Jobs.Api;

internal sealed class BoundedJobRequestStream(Stream inner, long maximum) : Stream
{
    private long _read;
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    private int Count(int amount)
    {
        _read = checked(_read + amount);
        if (_read > maximum) throw new Microsoft.AspNetCore.Http.BadHttpRequestException("The Jobs request body exceeds its supported bound.", 413);
        return amount;
    }
    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Count(await inner.ReadAsync(buffer, cancellationToken));
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Count(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
