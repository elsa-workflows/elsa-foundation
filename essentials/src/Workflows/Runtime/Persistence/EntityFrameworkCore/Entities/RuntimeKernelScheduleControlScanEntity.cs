namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

internal sealed class RuntimeKernelScheduleControlScanEntity
{
    public string ScopeKeyHash { get; set; } = null!;
    public string TenantKeyHash { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long LastOrdinal { get; set; }
    public long Pass { get; set; }
    public long? Through { get; set; }
    public long AfterOrdinal { get; set; }
    public string? AfterId { get; set; }
}
