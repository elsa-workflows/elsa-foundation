namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Runtime scheduler state for an in-workflow generator activity.
/// </summary>
public sealed class GeneratorRegistration
{
    public GeneratorRegistration(
        string generatorId,
        string workflowExecutionId,
        string generatorActivityExecutionId,
        string? owningScopeActivityExecutionId,
        string? branchId,
        DateTimeOffset registeredAt,
        GeneratorStatus status = GeneratorStatus.Active,
        GeneratorStopPolicy stopPolicy = GeneratorStopPolicy.ScopeEnd,
        GeneratorBackpressurePolicy backpressurePolicy = GeneratorBackpressurePolicy.Pause,
        DateTimeOffset? expiresAt = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(generatorActivityExecutionId);

        if (owningScopeActivityExecutionId is not null && string.IsNullOrWhiteSpace(owningScopeActivityExecutionId))
            throw new ArgumentException("Owning scope activity execution ID cannot be blank when provided.", nameof(owningScopeActivityExecutionId));

        if (branchId is not null && string.IsNullOrWhiteSpace(branchId))
            throw new ArgumentException("Branch ID cannot be blank when provided.", nameof(branchId));

        if (expiresAt is not null && expiresAt <= registeredAt)
            throw new ArgumentException("Generator expiration must be after registration time.", nameof(expiresAt));

        if (stopPolicy == GeneratorStopPolicy.TimeWindow && expiresAt is null)
            throw new ArgumentException("Time-window generator stop policy requires an expiration time.", nameof(expiresAt));

        if (stopPolicy != GeneratorStopPolicy.TimeWindow && expiresAt is not null)
            throw new ArgumentException("Generator expiration is only valid with the time-window stop policy.", nameof(expiresAt));

        GeneratorId = generatorId;
        WorkflowExecutionId = workflowExecutionId;
        GeneratorActivityExecutionId = generatorActivityExecutionId;
        OwningScopeActivityExecutionId = owningScopeActivityExecutionId;
        BranchId = branchId;
        RegisteredAt = registeredAt;
        Status = status;
        StopPolicy = stopPolicy;
        BackpressurePolicy = backpressurePolicy;
        ExpiresAt = expiresAt;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string GeneratorId { get; }
    public string WorkflowExecutionId { get; }
    public string GeneratorActivityExecutionId { get; }
    public string? OwningScopeActivityExecutionId { get; }
    public string? BranchId { get; }
    public DateTimeOffset RegisteredAt { get; }
    public GeneratorStatus Status { get; }
    public GeneratorStopPolicy StopPolicy { get; }
    public GeneratorBackpressurePolicy BackpressurePolicy { get; }
    public DateTimeOffset? ExpiresAt { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

/// <summary>
/// One emission from an in-workflow generator activity.
/// </summary>
public sealed class GeneratedEvent
{
    public GeneratedEvent(
        string generatedEventId,
        string workflowExecutionId,
        string generatorActivityExecutionId,
        string? branchId,
        string name,
        long sequence,
        DateTimeOffset occurredAt,
        GeneratedEventDurability durability,
        RuntimeDurableValueReference? payloadValue = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generatedEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(generatorActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (branchId is not null && string.IsNullOrWhiteSpace(branchId))
            throw new ArgumentException("Branch ID cannot be blank when provided.", nameof(branchId));

        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Generated event sequence cannot be negative.");

        GeneratedEventId = generatedEventId;
        WorkflowExecutionId = workflowExecutionId;
        GeneratorActivityExecutionId = generatorActivityExecutionId;
        BranchId = branchId;
        Name = name;
        Sequence = sequence;
        OccurredAt = occurredAt;
        Durability = durability;
        PayloadValue = payloadValue;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string GeneratedEventId { get; }
    public string WorkflowExecutionId { get; }
    public string GeneratorActivityExecutionId { get; }
    public string? BranchId { get; }
    public string Name { get; }
    public long Sequence { get; }
    public DateTimeOffset OccurredAt { get; }
    public GeneratedEventDurability Durability { get; }
    public RuntimeDurableValueReference? PayloadValue { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

/// <summary>
/// Deterministic scheduler work created from a generator emission.
/// </summary>
public sealed class SchedulerGeneratedEventWorkItem
{
    public SchedulerGeneratedEventWorkItem(
        string workItemId,
        GeneratedEvent generatedEvent,
        DateTimeOffset enqueuedAt,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        ArgumentNullException.ThrowIfNull(generatedEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        WorkItemId = workItemId;
        GeneratedEvent = generatedEvent;
        EnqueuedAt = enqueuedAt;
        Reason = reason;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string WorkItemId { get; }
    public GeneratedEvent GeneratedEvent { get; }
    public string WorkflowExecutionId => GeneratedEvent.WorkflowExecutionId;
    public string GeneratorActivityExecutionId => GeneratedEvent.GeneratorActivityExecutionId;
    public string? BranchId => GeneratedEvent.BranchId;
    public DateTimeOffset EnqueuedAt { get; }
    public string Reason { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public enum GeneratorStatus
{
    Active,
    Paused,
    Completed,
    Cancelled,
    Suspended,
    Faulted
}

public enum GeneratorStopPolicy
{
    ScopeEnd,
    RepeatCount,
    TimeWindow,
    Condition,
    ExternalSignal,
    NoSubscribers
}

public enum GeneratorBackpressurePolicy
{
    Stop,
    Throttle,
    Pause,
    Fault
}

public enum GeneratedEventDurability
{
    Volatile,
    Durable,
    PolicyControlled
}
