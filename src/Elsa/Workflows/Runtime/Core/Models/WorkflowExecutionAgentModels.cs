namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class WorkflowExecutionCommandEnvelope
{
    public WorkflowExecutionCommandEnvelope(
        string envelopeId,
        string workflowExecutionId,
        WorkflowExecutionCommand command,
        string idempotencyKey,
        WorkflowExecutionCommandDeliveryMode deliveryMode,
        DateTimeOffset enqueuedAt,
        long? sequence = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envelopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Command delivery sequence cannot be negative.");

        if (!string.Equals(workflowExecutionId, command.WorkflowExecutionId, StringComparison.Ordinal))
            throw new ArgumentException("Envelope workflow execution ID must match the command workflow execution ID.", nameof(workflowExecutionId));

        EnvelopeId = envelopeId;
        Command = command;
        WorkflowExecutionId = workflowExecutionId;
        IdempotencyKey = idempotencyKey;
        DeliveryMode = deliveryMode;
        Sequence = sequence;
        EnqueuedAt = enqueuedAt;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string EnvelopeId { get; }
    public WorkflowExecutionCommand Command { get; }
    public string WorkflowExecutionId { get; }
    public string IdempotencyKey { get; }
    public WorkflowExecutionCommandDeliveryMode DeliveryMode { get; }
    public long? Sequence { get; }
    public DateTimeOffset EnqueuedAt { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class WorkflowExecutionCommandDispatchResult
{
    public WorkflowExecutionCommandDispatchResult(
        string envelopeId,
        string workflowExecutionId,
        WorkflowExecutionCommandDispatchStatus status,
        DateTimeOffset recordedAt,
        string? reason = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envelopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        if (status is WorkflowExecutionCommandDispatchStatus.Rejected or WorkflowExecutionCommandDispatchStatus.Deferred)
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (status is not (WorkflowExecutionCommandDispatchStatus.Rejected or WorkflowExecutionCommandDispatchStatus.Deferred) && reason is not null)
            throw new ArgumentException("Only rejected or deferred command dispatch results can carry a reason.", nameof(reason));

        EnvelopeId = envelopeId;
        WorkflowExecutionId = workflowExecutionId;
        Status = status;
        RecordedAt = recordedAt;
        Reason = reason;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string EnvelopeId { get; }
    public string WorkflowExecutionId { get; }
    public WorkflowExecutionCommandDispatchStatus Status { get; }
    public DateTimeOffset RecordedAt { get; }
    public string? Reason { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class WorkflowExecutionAgentActivationRequest
{
    public WorkflowExecutionAgentActivationRequest(
        string workflowExecutionId,
        WorkflowExecutionAgentActivationReason reason,
        DateTimeOffset requestedAt,
        string requestedBy,
        WorkflowExecutionAgentCapabilities requiredCapabilities,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);

        WorkflowExecutionId = workflowExecutionId;
        Reason = reason;
        RequestedAt = requestedAt;
        RequestedBy = requestedBy;
        RequiredCapabilities = requiredCapabilities;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string WorkflowExecutionId { get; }
    public WorkflowExecutionAgentActivationReason Reason { get; }
    public DateTimeOffset RequestedAt { get; }
    public string RequestedBy { get; }
    public WorkflowExecutionAgentCapabilities RequiredCapabilities { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class WorkflowExecutionAgentDescriptor
{
    public WorkflowExecutionAgentDescriptor(
        string workflowExecutionId,
        string agentId,
        string providerName,
        WorkflowExecutionAgentStatus status,
        WorkflowExecutionAgentCapabilities capabilities,
        DateTimeOffset activatedAt,
        string? lastCheckpointId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        if (lastCheckpointId is not null && string.IsNullOrWhiteSpace(lastCheckpointId))
            throw new ArgumentException("Last checkpoint ID cannot be blank when provided.", nameof(lastCheckpointId));

        WorkflowExecutionId = workflowExecutionId;
        AgentId = agentId;
        ProviderName = providerName;
        Status = status;
        Capabilities = capabilities;
        ActivatedAt = activatedAt;
        LastCheckpointId = lastCheckpointId;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string WorkflowExecutionId { get; }
    public string AgentId { get; }
    public string ProviderName { get; }
    public WorkflowExecutionAgentStatus Status { get; }
    public WorkflowExecutionAgentCapabilities Capabilities { get; }
    public DateTimeOffset ActivatedAt { get; }
    public string? LastCheckpointId { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class WorkflowExecutionAgentPassivationRequest
{
    public WorkflowExecutionAgentPassivationRequest(
        string workflowExecutionId,
        WorkflowExecutionAgentPassivationBoundary boundary,
        DateTimeOffset requestedAt,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        WorkflowExecutionId = workflowExecutionId;
        Boundary = boundary;
        RequestedAt = requestedAt;
        Reason = reason;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string WorkflowExecutionId { get; }
    public WorkflowExecutionAgentPassivationBoundary Boundary { get; }
    public DateTimeOffset RequestedAt { get; }
    public string Reason { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

[Flags]
public enum WorkflowExecutionAgentCapabilities
{
    None = 0,
    InProcessMailbox = 1 << 0,
    DistributedPlacement = 1 << 1,
    LeaseFencing = 1 << 2,
    DurableTimers = 1 << 3,
    Passivation = 1 << 4
}

public enum WorkflowExecutionCommandDeliveryMode
{
    AtMostOnce,
    AtLeastOnce
}

public enum WorkflowExecutionCommandDispatchStatus
{
    Accepted,
    Duplicate,
    Rejected,
    Deferred
}

public enum WorkflowExecutionAgentActivationReason
{
    Start,
    ResumeBookmark,
    ContinueVolatileWait,
    SchedulerWork,
    ControlPlaneCommand,
    Recovery,
    GeneratedEvent
}

public enum WorkflowExecutionAgentStatus
{
    Active,
    Passivating,
    Passivated,
    Unavailable
}

public enum WorkflowExecutionAgentPassivationBoundary
{
    AfterCheckpointCommit,
    EnteringDurableSuspension,
    RegisteringVolatileWait,
    HostDrain,
    ProviderSafeBoundary
}
