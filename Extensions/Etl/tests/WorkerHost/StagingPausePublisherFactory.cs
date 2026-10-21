using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Tests.Support;

/// <summary>Decorates the real publisher's writer; the real store still owns staging and final publication.</summary>
internal sealed class StagingPausePublisherFactory(IFileChunkPublisherFactory inner, string root, string role,
    EtlWorkerWritePause pause) : IFileChunkPublisherFactory
{
    private int _paused;

    public IFileChunkPublisher Create(EtlFileChunkFormat format) => new Publisher(inner.Create(format), this, format);

    private int PrefixLength(EtlFileChunkFormat format, int count)
    {
        var selected = pause == EtlWorkerWritePause.SourceSnapshot && format == EtlFileChunkFormat.Csv ||
                       pause == EtlWorkerWritePause.OutputChunk && format == EtlFileChunkFormat.Jsonl;
        return selected && count > 1 && Interlocked.CompareExchange(ref _paused, 1, 0) == 0 ? count / 2 : 0;
    }

    private async ValueTask HoldAsync(string outputRoot, string identity, EtlFileChunkFormat format,
        int prefixBytes, int writeBytes, CancellationToken cancellationToken)
    {
        var extension = format switch
        {
            EtlFileChunkFormat.Csv => ".csv",
            EtlFileChunkFormat.Index => ".index",
            EtlFileChunkFormat.Jsonl => ".jsonl",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        await EtlWorkerProtocol.WriteStagingAsync(root, role,
            new(outputRoot, identity, extension, prefixBytes, writeBytes, Environment.ProcessId), cancellationToken);
        while (!File.Exists(EtlWorkerProtocol.ReleasePath(root, role)))
            await Task.Delay(25, cancellationToken);
    }

    private sealed class Publisher(IFileChunkPublisher inner, StagingPausePublisherFactory owner,
        EtlFileChunkFormat format) : IFileChunkPublisher
    {
        public ValueTask<EtlChunk> PublishAsync(string outputRoot, string effectIdentity, long maxBytes,
            Func<Stream, CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) =>
            inner.PublishAsync(outputRoot, effectIdentity, maxBytes, async (destination, token) =>
            {
                using var wrapper = new HeldWriteStream(destination, owner, format, outputRoot, effectIdentity);
                await write(wrapper, token);
            }, cancellationToken);
    }

    private sealed class HeldWriteStream(Stream destination, StagingPausePublisherFactory owner,
        EtlFileChunkFormat format, string outputRoot, string identity) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => destination.CanWrite;
        public override long Length => destination.Length;
        public override long Position { get => destination.Position; set => throw new NotSupportedException(); }
        public override void Flush() => destination.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => destination.FlushAsync(cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var prefix = owner.PrefixLength(format, buffer.Length);
            if (prefix > 0)
            {
                destination.Write(buffer[..prefix]);
                destination.Flush();
                owner.HoldAsync(outputRoot, identity, format, prefix, buffer.Length, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }
            destination.Write(buffer[prefix..]);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var prefix = owner.PrefixLength(format, buffer.Length);
            if (prefix > 0)
            {
                await destination.WriteAsync(buffer[..prefix], cancellationToken);
                await destination.FlushAsync(cancellationToken);
                await owner.HoldAsync(outputRoot, identity, format, prefix, buffer.Length, cancellationToken);
            }
            await destination.WriteAsync(buffer[prefix..], cancellationToken);
        }
        // The underlying destination belongs to FileChunkStore and is never disposed by this wrapper.
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
