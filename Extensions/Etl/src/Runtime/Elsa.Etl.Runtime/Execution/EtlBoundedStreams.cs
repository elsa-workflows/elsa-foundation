using System.Security.Cryptography;

namespace Elsa.Etl.Runtime.Execution;

/// <summary>Limits bytes written by an incremental canonical JSON serializer.</summary>
internal class EtlBoundedCountingStream(long maxBytes) : Stream
{
    public long ByteLength { get; private set; }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => ByteLength;
    public override long Position { get => ByteLength; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length > maxBytes - ByteLength)
            throw new EtlPayloadLimitExceededException();
        ByteLength += buffer.Length;
    }
}

/// <summary>Hashes the exact bytes accepted by a bounded counting stream.</summary>
internal sealed class EtlBoundedHashingStream(IncrementalHash hash, long maxBytes)
    : EtlBoundedCountingStream(maxBytes)
{
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        base.Write(buffer);
        hash.AppendData(buffer);
    }
}

/// <summary>Provides one bounded sequential view over a range in a shared source stream.</summary>
/// <remarks>Disposing the range view never disposes the source stream owned by its batch builder.</remarks>
internal sealed class EtlBoundedRangeReadStream(Stream source, long remaining, bool flushSupported) : Stream
{
    public override bool CanRead => source.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush()
    {
        if (!flushSupported)
            throw new NotSupportedException();
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (remaining <= 0)
            return 0;
        var read = await source.ReadAsync(buffer[..(int)Math.Min(buffer.Length, remaining)], cancellationToken);
        remaining -= read;
        return read;
    }
}

/// <summary>An internal control signal that a canonical payload exceeds its configured byte limit.</summary>
internal sealed class EtlPayloadLimitExceededException : Exception
{
}
