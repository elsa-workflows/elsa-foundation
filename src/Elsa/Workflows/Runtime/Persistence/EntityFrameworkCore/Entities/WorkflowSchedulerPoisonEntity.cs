namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>Relational envelope and ordered projections for one scheduler-poison record.</summary>
public sealed class WorkflowSchedulerPoisonEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string WorkflowExecutionIdOrderKey { get; set; } = null!;
    public string WorkItemId { get; set; } = null!;
    public string WorkItemIdHash { get; set; } = null!;
    public string WorkItemIdOrderKey { get; set; } = null!;
    public long FirstFailedAtUtcTicks { get; set; }
    public long LastFailedAtUtcTicks { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}
