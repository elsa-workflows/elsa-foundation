namespace Elsa.Etl.Runtime.Output;

/// <summary>The finite set of chunk formats owned by the ETL runtime.</summary>
internal enum EtlFileChunkFormat
{
    Jsonl,
    Csv,
    Index
}

/// <summary>Publishes one bounded ETL file chunk to a trusted root.</summary>
/// <remarks>
/// The write callback is caller-provided, while its destination stream is owned and disposed by the publisher; the
/// callback must not retain or dispose that stream. A successful publication represents a complete file written to
/// private staging within <c>maxBytes</c>. After the callback returns, the publisher flushes the staged
/// bytes, computes their SHA-256 descriptor, and atomically publishes the complete file without replacing an existing
/// one. The physical result is not authoritative until the runtime accepts it in the durable manifest. The format is
/// selected by the ETL runtime from its fixed feature-owned formats, never from authored paths or suffixes.
/// </remarks>
internal interface IFileChunkPublisher
{
    ValueTask<EtlChunk> PublishAsync(
        string outputRoot,
        string effectIdentity,
        long maxBytes,
        Func<Stream, CancellationToken, ValueTask> write,
        CancellationToken cancellationToken = default);
}

/// <summary>Selects a file publisher for one fixed ETL-owned chunk format.</summary>
internal interface IFileChunkPublisherFactory
{
    IFileChunkPublisher Create(EtlFileChunkFormat format);
}
