namespace Elsa.Etl.Persistence.EntityFrameworkCore.Entities;

internal sealed class EtlConnectorRunRecord
{
    public string ScopeKey { get; set; } = null!;
    public string RunKey { get; set; } = null!;
    public string BindingsJson { get; set; } = null!;
    public string? PreparedJson { get; set; }
    /// <summary>When a continuous run's completion was requested; null for finite runs and before any request.</summary>
    public long? CompletionRequestedAtUtcTicks { get; set; }
    public long RetainedInputBytes { get; set; }
    public long SourceInputReservedBytes { get; set; }
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
    public long? NotBeforeUtcTicks { get; set; }
    public long? ExpiresAtUtcTicks { get; set; }
    public long PageCount { get; set; }
    public long PageBytes { get; set; }
    public long SourceInputReservedBytes { get; set; }
    public long MetadataBytes { get; set; }
    public long AcceptedBatchCount { get; set; }
    public long AcceptedRecords { get; set; }
    public long RejectedRecords { get; set; }
    public long FilteredRecords { get; set; }
    public string? OwedBatchId { get; set; }
    public long BatchMetadataBytes { get; set; }
    public long RetainedOutputBytes { get; set; }
    public long? CurrentRequestSequence { get; set; }
    public string? CurrentRequestHash { get; set; }
    /// <summary>Caught-up polls a continuous shard has recorded; part of its page-cache key above 0 (spec 1196).</summary>
    public long PollGeneration { get; set; }
    /// <summary>The base page sequence of a compacted continuous shard: pages below it were deleted (spec 1196).</summary>
    public long CompactedPageCount { get; set; }
    public long CompactedBatchCount { get; set; }
    public long CompactedAcceptedRecords { get; set; }
    public long CompactedRejectedRecords { get; set; }
    public long CompactedFilteredRecords { get; set; }
    public long CompactedMainEffects { get; set; }
    public long CompactedErrorEffects { get; set; }
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
    public string? HttpDeliveryJson { get; set; }
    public string? HttpDeliveryDigest { get; set; }
    public int? MainEffectKind { get; set; }
    public int? ErrorEffectKind { get; set; }
    public long BatchOrdinal { get; set; }
    public long? HttpPageSequence { get; set; }
    public long AcceptanceMetadataBytes { get; set; }
    public bool MainFilePublished { get; set; }
    public bool ErrorFilePublished { get; set; }
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
    public string? FetchJson { get; set; }
    public string? FetchDigest { get; set; }
    public long FetchMetadataBytes { get; set; }
    public long SourceMetadataReserveBytes { get; set; }
    public long InputReservationBytes { get; set; }
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
    public int AcceptedItemCount { get; set; }
    public string SchemaVersion { get; set; } = null!;
}

/// <summary>The high watermark of the last completed run for one SQL source checkpoint (spec 1199).</summary>
internal sealed class EtlConnectorSourceWatermarkRecord
{
    public string ScopeKey { get; set; } = null!;
    /// <summary>The hash of the definition, source alias, pinned source handle, table and watermark column.</summary>
    public string CheckpointKey { get; set; } = null!;
    public long Watermark { get; set; }
    /// <summary>The run whose completion last raised the checkpoint.</summary>
    public string RunKey { get; set; } = null!;
    public long UpdatedAtUtcTicks { get; set; }
    public string SchemaVersion { get; set; } = null!;
}
