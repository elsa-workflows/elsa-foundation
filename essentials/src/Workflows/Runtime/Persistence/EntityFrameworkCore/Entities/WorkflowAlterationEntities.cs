namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>Relational projections plus the lossless provider-neutral plan document.</summary>
public sealed class WorkflowAlterationPlanEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string PlanId { get; set; } = null!;
    public string PlanIdHash { get; set; } = null!;
    public string PlanIdOrderKey { get; set; } = null!;
    public string TenantIdempotencyKey { get; set; } = null!;
    public string TenantIdempotencyKeyHash { get; set; } = null!;
    public int Status { get; set; }
    public string ActiveOrderKey { get; set; } = null!;
    public long CreatedAtUtcTicks { get; set; }
    public long Revision { get; set; }
    public int? CleanupTerminalStatus { get; set; }
    public string? CleanupSafeFailureJson { get; set; }
    public long? CleanupCompletedAtUtcTicks { get; set; }
    public long CleanupDeletedCount { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
}

/// <summary>Relational projections plus the lossless provider-neutral job document.</summary>
public sealed class WorkflowAlterationJobEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string JobId { get; set; } = null!;
    public string JobIdHash { get; set; } = null!;
    public string JobIdOrderKey { get; set; } = null!;
    public string PlanId { get; set; } = null!;
    public string PlanIdHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdOrderKey { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string TenantPartition { get; set; } = null!;
    public string TenantPartitionHash { get; set; } = null!;
    public long CaptureOrdinal { get; set; }
    public long? ClaimableAtUtcTicks { get; set; }
    public int Status { get; set; }
    public string? CheckpointCommitId { get; set; }
    public string? CheckpointCommitIdHash { get; set; }
    public long Revision { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
}
