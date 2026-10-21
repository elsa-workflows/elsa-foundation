namespace Elsa.Etl.Persistence.EntityFrameworkCore.Entities;

internal sealed class EtlConnectorRunRecord
{
    public string ScopeKey { get; set; } = null!;
    public string RunKey { get; set; } = null!;
    public string BindingsJson { get; set; } = null!;
    public string? PreparedJson { get; set; }
    public long RetainedInputBytes { get; set; }
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
    public long Revision { get; set; }
    public string SchemaVersion { get; set; } = null!;
}
