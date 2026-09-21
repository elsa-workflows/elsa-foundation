namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>Relational envelope and query projections for one workflow run-health state.</summary>
public sealed class WorkflowRunHealthStateEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string WorkflowExecutionIdOrderKey { get; set; } = null!;
    public string DefinitionId { get; set; } = null!;
    public string DefinitionIdHash { get; set; } = null!;
    public string DefinitionIdOrderKey { get; set; } = null!;
    public int RunKind { get; set; }
    public long? StartedAtUtcTicks { get; set; }
    public int? StartedAtOffsetMinutes { get; set; }
    public int Status { get; set; }
    public long IncidentCount { get; set; }
    public long IncidentBearingCount { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}
