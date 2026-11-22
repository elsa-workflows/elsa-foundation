namespace Elsa.Etl.Persistence.EntityFrameworkCore.Entities;

internal sealed class EtlRuntimeRunRecord
{
    public string ScopeKey { get; set; } = null!;
    public string? TenantId { get; set; }
    public string PersistencePartition { get; set; } = null!;
    public string RunKey { get; set; } = null!;
    public string RunId { get; set; } = null!;
    public string InvocationFingerprint { get; set; } = null!;
    public string InvocationJson { get; set; } = null!;
    public string? BindingsJson { get; set; }
    public int Status { get; set; }
    public string? PreparationOwner { get; set; }
    public long PreparationFence { get; set; }
    public long? PreparationExpiresAtUtcTicks { get; set; }
    public long OutstandingRecords { get; set; }
    public long OutstandingBytes { get; set; }
    public long AcceptedRecords { get; set; }
    public long RejectedRecords { get; set; }
    public long FilteredRecords { get; set; }
    public long Revision { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public long UpdatedAtUtcTicks { get; set; }
    public string SchemaVersion { get; set; } = null!;
}

internal sealed class EtlRuntimePreparedRecord
{
    public string ScopeKey { get; set; } = null!;
    public string RunKey { get; set; } = null!;
    public int FormatVersion { get; set; }
    public string SourceKey { get; set; } = null!;
    public string SourceHash { get; set; } = null!;
    public long SourceLength { get; set; }
    public string LookupKey { get; set; } = null!;
    public string LookupHash { get; set; } = null!;
    public long LookupLength { get; set; }
    public string SourceIndexKey { get; set; } = null!;
    public string SourceIndexHash { get; set; } = null!;
    public long SourceIndexLength { get; set; }
    public long SourceRecordCount { get; set; }
    public string SchemaVersion { get; set; } = null!;
}

internal sealed class EtlRuntimePartitionRecord
{
    public string ScopeKey { get; set; } = null!;
    public string RunKey { get; set; } = null!;
    public int PartitionOrdinal { get; set; }
    public long StartOrdinal { get; set; }
    public long EndOrdinalExclusive { get; set; }
    public long NextOrdinal { get; set; }
    public string? Owner { get; set; }
    public long Fence { get; set; }
    public long? ExpiresAtUtcTicks { get; set; }
    public long Revision { get; set; }
    public string SchemaVersion { get; set; } = null!;
}

internal sealed class EtlRuntimeReservationRecord
{
    public string ScopeKey { get; set; } = null!;
    public string RunKey { get; set; } = null!;
    public int PartitionOrdinal { get; set; }
    public long StartOrdinal { get; set; }
    public long EndOrdinalExclusive { get; set; }
    public string BatchId { get; set; } = null!;
    public long InputBytes { get; set; }
    public string InputHash { get; set; } = null!;
    public bool Settled { get; set; }
    public string SchemaVersion { get; set; } = null!;
}

internal sealed class EtlRuntimeManifestRecord
{
    public string ScopeKey { get; set; } = null!;
    public string RunKey { get; set; } = null!;
    public int PartitionOrdinal { get; set; }
    public long BatchStartOrdinal { get; set; }
    public int Route { get; set; }
    public string BatchId { get; set; } = null!;
    public string ChunkKey { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public long ByteLength { get; set; }
    public long RecordCount { get; set; }
    public long AcceptedAtUtcTicks { get; set; }
    public string SchemaVersion { get; set; } = null!;
}

internal sealed class EtlRuntimeTerminalRecord
{
    public string ScopeKey { get; set; } = null!;
    public string RunKey { get; set; } = null!;
    public string ResultId { get; set; } = null!;
    public int Outcome { get; set; }
    public string ResultJson { get; set; } = null!;
    public string ResultHash { get; set; } = null!;
    public long SettledAtUtcTicks { get; set; }
    public string SchemaVersion { get; set; } = null!;
}

internal sealed class EtlRuntimeOutboxRecord
{
    public string ScopeKey { get; set; } = null!;
    public string RunKey { get; set; } = null!;
    public string DeliveryId { get; set; } = null!;
    public string PayloadHash { get; set; } = null!;
    public string? Owner { get; set; }
    public long Fence { get; set; }
    public long? ExpiresAtUtcTicks { get; set; }
    public int Attempts { get; set; }
    public int? Disposition { get; set; }
    public long? AcknowledgedAtUtcTicks { get; set; }
    public string SchemaVersion { get; set; } = null!;
}
