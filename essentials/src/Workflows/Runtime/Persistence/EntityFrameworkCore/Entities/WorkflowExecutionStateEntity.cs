namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>Relational envelope for one durable workflow execution state and its query projections.</summary>
public sealed class WorkflowExecutionStateEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string WorkflowExecutionIdOrderKey { get; set; } = null!;
    public string? TenantId { get; set; }
    public string? TenantIdHash { get; set; }
    public string DefinitionId { get; set; } = null!;
    public string DefinitionIdHash { get; set; } = null!;
    public int Status { get; set; }
    public int RunKind { get; set; }
    public long SortTimestampUtcTicks { get; set; }
    public string? CorrelationId { get; set; }
    public string? CorrelationIdHash { get; set; }
    public string ArtifactId { get; set; } = null!;
    public string ArtifactIdHash { get; set; } = null!;
    public string ArtifactIdOrderKey { get; set; } = null!;
    public string? AuthorityPartitionKey { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}
