namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

public sealed class DurableValueStateEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string WorkflowExecutionIdOrderKey { get; set; } = null!;
    public string DurableValueId { get; set; } = null!;
    public string DurableValueIdHash { get; set; } = null!;
    public string DurableValueIdOrderKey { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}

public sealed class SchedulerStateEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string WorkflowExecutionIdOrderKey { get; set; } = null!;
    public string Collection { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}

/// <summary>Relational envelope and recovery projections for one execution-liveness state.</summary>
public sealed class ExecutionLivenessStateEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string WorkflowExecutionIdOrderKey { get; set; } = null!;
    public string OperationalStateId { get; set; } = null!;
    public string OperationalStateIdHash { get; set; } = null!;
    public string OperationalStateIdOrderKey { get; set; } = null!;
    public int? InterruptedStatus { get; set; }
    public long? InterruptedAtUtcTicks { get; set; }
    public string? LeaseOwnerId { get; set; }
    public long? LeaseAcquiredAtUtcTicks { get; set; }
    public long? LeaseExpiresAtUtcTicks { get; set; }
    public string? HeartbeatOwnerId { get; set; }
    public long? HeartbeatRecordedAtUtcTicks { get; set; }
    public bool HasOperationalOwner { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}

/// <summary>Relational envelope and visible-scope projections for one workflow hold state.</summary>
public sealed class WorkflowHoldStateEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string ControlPlaneStateId { get; set; } = null!;
    public string ControlPlaneStateIdHash { get; set; } = null!;
    public string ControlPlaneStateIdOrderKey { get; set; } = null!;
    public string? WorkflowExecutionId { get; set; }
    public string? WorkflowExecutionIdHash { get; set; }
    public string? WorkflowExecutionIdOrderKey { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}

/// <summary>Relational envelope and query projections for one runtime incident state.</summary>
public sealed class IncidentStateEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string WorkflowExecutionIdOrderKey { get; set; } = null!;
    public string IncidentId { get; set; } = null!;
    public string IncidentIdHash { get; set; } = null!;
    public string IncidentIdOrderKey { get; set; } = null!;
    public int Status { get; set; }
    public int Severity { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public long? ResolvedAtUtcTicks { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}
