namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>Relational projections and serialized lifecycle record for one workflow dispatch.</summary>
public sealed class WorkflowDispatchEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string DispatchId { get; set; } = null!;
    public string DispatchIdHash { get; set; } = null!;
    public string DispatchIdOrderKey { get; set; } = null!;
    public string ParentWorkflowExecutionId { get; set; } = null!;
    public string ParentWorkflowExecutionIdHash { get; set; } = null!;
    public string ParentWorkflowExecutionIdOrderKey { get; set; } = null!;
    public string ParentActivityExecutionId { get; set; } = null!;
    public string ParentActivityExecutionIdHash { get; set; } = null!;
    public string ParentActivityExecutionIdOrderKey { get; set; } = null!;
    public string ChildWorkflowExecutionId { get; set; } = null!;
    public string ChildWorkflowExecutionIdHash { get; set; } = null!;
    public string ChildWorkflowExecutionIdOrderKey { get; set; } = null!;
    public string ChildArtifactId { get; set; } = null!;
    public string ChildArtifactIdHash { get; set; } = null!;
    public string ChildArtifactIdOrderKey { get; set; } = null!;
    public string? TestScopeId { get; set; }
    public string? TestScopeIdHash { get; set; }
    public string? TestScopeIdOrderKey { get; set; }
    public string? TenantId { get; set; }
    public string? TenantIdHash { get; set; }
    public int Mode { get; set; }
    public int Status { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public long UpdatedAtUtcTicks { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}
