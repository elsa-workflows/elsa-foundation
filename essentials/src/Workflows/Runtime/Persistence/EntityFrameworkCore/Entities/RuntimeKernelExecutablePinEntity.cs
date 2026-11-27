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
    public long? ReleasedAtUtcTicks { get; set; }
}
