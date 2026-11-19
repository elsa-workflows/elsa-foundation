namespace Elsa.Etl.Runtime.Output;

/// <summary>Provides the stateless ETL-owned file publishers for JSONL and CSV output, CSV and JSONL input snapshot, and index chunks.</summary>
internal sealed class FileChunkPublisherFactory : IFileChunkPublisherFactory
{
    private readonly IFileChunkPublisher _jsonl = new FileChunkStore();
    private readonly IFileChunkPublisher _csv = new FileChunkStore(".csv");
    private readonly IFileChunkPublisher _index = new FileChunkStore(".index");
    private readonly IFileChunkPublisher _jsonlSnapshot = new FileChunkStore(".jsonl");
    private readonly IFileChunkPublisher _csvOutput = new FileChunkStore(".csv");

    public IFileChunkPublisher Create(EtlFileChunkFormat format) => format switch
    {
        EtlFileChunkFormat.Jsonl => _jsonl,
        EtlFileChunkFormat.Csv => _csv,
        EtlFileChunkFormat.Index => _index,
        EtlFileChunkFormat.JsonlSnapshot => _jsonlSnapshot,
        EtlFileChunkFormat.CsvOutput => _csvOutput,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported ETL chunk format.")
    };
}
