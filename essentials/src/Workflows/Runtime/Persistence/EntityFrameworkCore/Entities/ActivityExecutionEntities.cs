namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>Relational envelope and projections for one scoped activity execution state row.</summary>
public sealed class ActivityExecutionStateEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string ActivityExecutionId { get; set; } = null!;
    public string ActivityExecutionIdHash { get; set; } = null!;
    public string ActivityExecutionIdOrderKey { get; set; } = null!;
    public string? ParentActivityExecutionId { get; set; }
    public string? ParentActivityExecutionIdHash { get; set; }
    public string? ExecutionScopeId { get; set; }
    public string? ExecutionScopeIdHash { get; set; }
    public string Status { get; set; } = null!;
    public long ExecutionSequence { get; set; }
    public long ScheduledAtUtcTicks { get; set; }
    public int ScheduledAtOffsetMinutes { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}

/// <summary>Relational envelope and summary projections for committed activity inspection evidence.</summary>
public sealed class ActivityExecutionInspectionEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string ActivityExecutionId { get; set; } = null!;
    public string ActivityExecutionIdHash { get; set; } = null!;
    public string ActivityExecutionIdOrderKey { get; set; } = null!;
    public string? ExecutionScopeId { get; set; }
    public string? ExecutionScopeIdHash { get; set; }
    public string Status { get; set; } = null!;
    public long SummaryExecutionSequence { get; set; }
    public long SummaryScheduledAtUtcTicks { get; set; }
    public int SummaryScheduledAtOffsetMinutes { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}

/// <summary>Relational envelope and hierarchy projections derived from committed inspection evidence.</summary>
public sealed class ActivityExecutionHierarchyEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string ActivityExecutionId { get; set; } = null!;
    public string ActivityExecutionIdHash { get; set; } = null!;
    public string ActivityExecutionIdOrderKey { get; set; } = null!;
    public string ExecutionScopeId { get; set; } = null!;
    public string ExecutionScopeIdHash { get; set; } = null!;
    public string? ParentActivityExecutionId { get; set; }
    public string? ParentActivityExecutionIdHash { get; set; }
    public bool IsScopeRoot { get; set; }
    public long ExecutionSequence { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}
