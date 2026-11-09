namespace Elsa.Etl.Core;

/// <summary>Finite admission and expansion ceilings for one published ETL pipeline.</summary>
/// <remarks>
/// These are logical payload limits, not a promise about managed-heap size or process memory. Design and runtime
/// validate combinations and enforce the limits; Core supplies finite defaults only.
/// </remarks>
public sealed record EtlLimits
{
    /// <summary>The requested number of disjoint input partitions.</summary>
    public int PartitionCount { get; init; } = 2;

    /// <summary>Maximum UTF-8 byte length of one logical source record.</summary>
    public int MaxRecordBytes { get; init; } = 64 * 1024;

    /// <summary>Maximum number of fields accepted in either CSV schema.</summary>
    public int MaxFields { get; init; } = 256;

    /// <summary>Maximum records in one admitted batch.</summary>
    public int MaxBatchRecords { get; init; } = 1_000;

    /// <summary>Maximum canonical input bytes in one admitted batch.</summary>
    public long MaxBatchBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>Maximum records admitted but not yet durably settled for the run.</summary>
    public long MaxOutstandingRecords { get; init; } = 10_000;

    /// <summary>Maximum canonical input bytes admitted but not yet durably settled for the run.</summary>
    public long MaxOutstandingBytes { get; init; } = 32 * 1024 * 1024;

    /// <summary>Maximum logical records in the pinned lookup snapshot.</summary>
    public long MaxLookupRecords { get; init; } = 100_000;

    /// <summary>Maximum bytes in the pinned lookup snapshot.</summary>
    public long MaxLookupBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Maximum serialized JSONL bytes emitted by one batch across its routes.</summary>
    public long MaxOutputBatchBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Maximum bytes retained for each source or lookup snapshot.</summary>
    public long MaxSnapshotBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>Maximum serialized size of the runnable pipeline artifact.</summary>
    public int MaxArtifactBytes { get; init; } = EtlArtifact.MaximumByteLength;
}
