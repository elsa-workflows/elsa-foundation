namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>Immutable scope identity projections and the complete lifecycle record.</summary>
public sealed class WorkflowTestScopeEntity
{
    public string Id { get; set; } = null!;
    public string AccessScopeKey { get; set; } = null!;
    public string AccessScopeKeyHash { get; set; } = null!;
    public string ScopeId { get; set; } = null!;
    public string ScopeIdHash { get; set; } = null!;
    public string ScopeIdOrderKey { get; set; } = null!;
    public string? TenantId { get; set; }
    public string? TenantIdHash { get; set; }
    public string Partition { get; set; } = null!;
    public string PartitionHash { get; set; } = null!;
    public string PartitionOrderKey { get; set; } = null!;
    public long ExpiresAtUtcTicks { get; set; }
    public int State { get; set; }
    public long Revision { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
}
