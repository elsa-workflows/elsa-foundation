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

/// <summary>Relational envelope and claim projections for one durable timer.</summary>
public sealed class DurableTimerEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string WorkflowExecutionIdOrderKey { get; set; } = null!;
    public string TimerId { get; set; } = null!;
    public string TimerIdHash { get; set; } = null!;
    public string TimerIdOrderKey { get; set; } = null!;
    public string StimulusType { get; set; } = null!;
    public string StimulusHash { get; set; } = null!;
    public long DueTimeUtcTicks { get; set; }
    public int DueTimeOffsetMinutes { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public int CreatedAtOffsetMinutes { get; set; }
    public string ClaimOrderKey { get; set; } = null!;
    public string? ClaimOwnerId { get; set; }
    public long ClaimToken { get; set; }
    public long? ClaimedAtUtcTicks { get; set; }
    public int? ClaimedAtOffsetMinutes { get; set; }
    public long? VisibleAfterUtcTicks { get; set; }
    public int? VisibleAfterOffsetMinutes { get; set; }
    public int FailureCount { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}

/// <summary>Relational envelope and fenced-claim projections for one durable scheduler work item.</summary>
public sealed class SchedulerWorkItemEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string WorkflowExecutionIdOrderKey { get; set; } = null!;
    public string WorkItemId { get; set; } = null!;
    public string WorkItemIdHash { get; set; } = null!;
    public string WorkOrderKey { get; set; } = null!;
    public long EnqueuedAtUtcTicks { get; set; }
    public int EnqueuedAtOffsetMinutes { get; set; }
    public long RecordedAtUtcTicks { get; set; }
    public int RecordedAtOffsetMinutes { get; set; }
    public string? ClaimOwnerId { get; set; }
    public long ClaimToken { get; set; }
    public long? ClaimedAtUtcTicks { get; set; }
    public int? ClaimedAtOffsetMinutes { get; set; }
    public long? VisibleAfterUtcTicks { get; set; }
    public int? VisibleAfterOffsetMinutes { get; set; }
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

/// <summary>Immutable create-only replay marker for one runtime checkpoint commit.</summary>
public sealed class RuntimeCheckpointCommitEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string CommitId { get; set; } = null!;
    public string CommitIdHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string WorkflowExecutionIdOrderKey { get; set; } = null!;
    public long OccurredAtUtcTicks { get; set; }
    public string Fingerprint { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string PendingPostCommitWorkIdsJson { get; set; } = null!;
    public string ConsumedSchedulerWorkItemIdsJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}

/// <summary>Relational envelope and due/activation projections for one recurring start schedule.</summary>
public sealed class RecurringTriggerScheduleEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string ScheduleId { get; set; } = null!;
    public string ScheduleIdHash { get; set; } = null!;
    public string ScheduleIdOrderKey { get; set; } = null!;
    public string ArtifactId { get; set; } = null!;
    public string ArtifactIdHash { get; set; } = null!;
    public string ArtifactIdOrderKey { get; set; } = null!;
    public string ExecutableNodeId { get; set; } = null!;
    public string StimulusType { get; set; } = null!;
    public string StimulusHash { get; set; } = null!;
    public int Kind { get; set; }
    public string Expression { get; set; } = null!;
    public long NextOccurrenceUtcTicks { get; set; }
    public int NextOccurrenceOffsetMinutes { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public int CreatedAtOffsetMinutes { get; set; }
    public string? ActivationId { get; set; }
    public string? ActivationIdHash { get; set; }
    public string? ActivationIdOrderKey { get; set; }
    public string? SlotId { get; set; }
    public bool IsActive { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}

/// <summary>Atomic activation projection marker for recurring schedule preparation and activation.</summary>
public sealed class RecurringTriggerScheduleProjectionStateEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string ActivationId { get; set; } = null!;
    public string ActivationIdHash { get; set; } = null!;
    public string ActivationIdOrderKey { get; set; } = null!;
    public string? ArtifactId { get; set; }
    public string? ArtifactIdHash { get; set; }
    public string? ArtifactIdOrderKey { get; set; }
    public bool IsActive { get; set; }
    public int ScheduleCount { get; set; }
    public string ProjectionFingerprint { get; set; } = null!;
    public string ScheduleIdsJson { get; set; } = null!;
    public string ScheduleFingerprintsJson { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}
