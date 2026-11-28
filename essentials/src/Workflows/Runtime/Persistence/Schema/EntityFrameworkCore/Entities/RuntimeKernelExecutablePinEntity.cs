namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

internal sealed class RuntimeKernelExecutablePinEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string Fingerprint { get; set; } = null!;
    public string? ArtifactId { get; set; }
    public string? ContentJson { get; set; }
    public string SchemaVersion { get; set; } = RuntimeKernelExecutablePinEfModule.SchemaVersion;
    public long Revision { get; set; }
    public long InsertionOrdinal { get; set; }
    public long? ReleasedAtUtcTicks { get; set; }
}

internal sealed class RuntimeKernelExecutablePinScanEntity
{
    public string ScopeKeyHash { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string SchemaVersion { get; set; } = RuntimeKernelExecutablePinEfModule.SchemaVersion;
    public long LastOrdinal { get; set; }
    public Guid Pass { get; set; }
    public long? Through { get; set; }
    public long AfterOrdinal { get; set; }
    public string? AfterId { get; set; }
}
