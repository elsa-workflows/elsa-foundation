namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

internal sealed class RuntimeKernelScheduleControlEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string TenantKeyHash { get; set; } = null!;
    public string? OwnerSlotHash { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = RuntimeKernelScheduleControlEfModule.InitialSchemaVersion;
    public long Revision { get; set; }
    public long InsertionOrdinal { get; set; }
}
