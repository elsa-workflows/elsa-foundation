namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

internal sealed class RuntimeKernelEventPublicationEntity
{
    public string SlotHash { get; set; } = null!;
    public long Revision { get; set; }
    public string ScopeHash { get; set; } = null!;
    public string TenantHash { get; set; } = null!;
    public string? ArtifactId { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = RuntimeKernelEventPublicationEfModule.SchemaVersion;
    public bool Current { get; set; }
    public bool Applied { get; set; }
}
