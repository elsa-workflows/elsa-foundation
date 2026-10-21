namespace Elsa.Etl.Persistence.EntityFrameworkCore.Entities;

internal sealed class EtlConnectorRunRecord
{
    public string ScopeKey { get; set; } = null!;
    public string RunKey { get; set; } = null!;
    public string BindingsJson { get; set; } = null!;
    public string? PreparedJson { get; set; }
    public long RetainedInputBytes { get; set; }
    public long RetainedMetadataBytes { get; set; }
    public long RetainedOutputBytes { get; set; }
    public long Revision { get; set; }
    public string SchemaVersion { get; set; } = null!;
}

internal sealed class EtlConnectorPartitionRecord
{
    public string ScopeKey { get; set; } = null!;
    public string RunKey { get; set; } = null!;
    public int PartitionOrdinal { get; set; }
    public string PositionJson { get; set; } = null!;
    public bool Completed { get; set; }
    public string? Owner { get; set; }
    public long Fence { get; set; }
    public long? ExpiresAtUtcTicks { get; set; }
    public long PageCount { get; set; }
    public long PageBytes { get; set; }
    public long MetadataBytes { get; set; }
    public string? OwedBatchId { get; set; }
    public long BatchMetadataBytes { get; set; }
    public long RetainedOutputBytes { get; set; }
    public long? CurrentRequestSequence { get; set; }
    public string? CurrentRequestHash { get; set; }
    public long Revision { get; set; }
    public string SchemaVersion { get; set; } = null!;
}

internal sealed class EtlConnectorBatchRecord
{
    public string ScopeKey { get; set; } = null!;
    public string RunKey { get; set; } = null!;
    public int PartitionOrdinal { get; set; }
    public string BatchId { get; set; } = null!;
    public string StartKey { get; set; } = null!;
    public string DescriptorJson { get; set; } = null!;
    public string DescriptorDigest { get; set; } = null!;
    public string? OutputJson { get; set; }
    public string? OutputDigest { get; set; }
    public long OutputBytes { get; set; }
    public long RecordCount { get; set; }
    public long InputBytes { get; set; }
    public long MetadataBytes { get; set; }
    public bool Settled { get; set; }
    public string SchemaVersion { get; set; } = null!;
}

internal sealed class EtlConnectorHttpRequestRecord
{
    public string ScopeKey { get; set; } = null!;
    public string RunKey { get; set; } = null!;
    public int PartitionOrdinal { get; set; }
    public long PageSequence { get; set; }
    public string RequestHash { get; set; } = null!;
    public string RequestJson { get; set; } = null!;
    public long MetadataBytes { get; set; }
    public long? PreviousPageSequence { get; set; }
    public bool? PreviousHasContinuation { get; set; }
    public string SchemaVersion { get; set; } = null!;
}

internal sealed class EtlConnectorHttpPageRecord
{
    public string ScopeKey { get; set; } = null!;
    public string RunKey { get; set; } = null!;
    public int PartitionOrdinal { get; set; }
    public long PageSequence { get; set; }
    public string RequestHash { get; set; } = null!;
    public string NextRequestHash { get; set; } = null!;
    public bool HasContinuation { get; set; }
    public string PageJson { get; set; } = null!;
    public long ByteLength { get; set; }
    public string PageDigest { get; set; } = null!;
    public long MetadataBytes { get; set; }
    public string SchemaVersion { get; set; } = null!;
}
