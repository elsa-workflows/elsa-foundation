namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

internal sealed class RuntimeKernelScheduleOccurrenceEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string TenantKey { get; set; } = null!;
    public string TenantKeyHash { get; set; } = null!;
    public string ArtifactId { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = RuntimeKernelScheduleOccurrenceEfModule.SchemaVersion;
    public long Revision { get; set; }
    public long AcceptedAtUtcTicks { get; set; }
    public string? ActorReference { get; set; }
    public long? RetiredAtUtcTicks { get; set; }
}
