using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Observability event emitted by runtime execution. History events are not used to resume execution.
/// </summary>
public sealed class RuntimeHistoryEvent
{
    public RuntimeHistoryEvent(
        string historyEventId,
        string workflowExecutionId,
        long sequence,
        RuntimeHistoryEventCategory category,
        string name,
        DateTimeOffset occurredAt,
        string? activityExecutionId = null,
        string? executableNodeId = null,
        string? checkpointId = null,
        string? incidentId = null,
        JsonElement? payload = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "History event sequence cannot be negative.");

        HistoryEventId = historyEventId;
        WorkflowExecutionId = workflowExecutionId;
        Sequence = sequence;
        Category = category;
        Name = name;
        OccurredAt = occurredAt;
        ActivityExecutionId = activityExecutionId;
        ExecutableNodeId = executableNodeId;
        CheckpointId = checkpointId;
        IncidentId = incidentId;
        Payload = payload?.Clone();
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string HistoryEventId { get; }
    public string WorkflowExecutionId { get; }
    public long Sequence { get; }
    public RuntimeHistoryEventCategory Category { get; }
    public string Name { get; }
    public DateTimeOffset OccurredAt { get; }
    public string? ActivityExecutionId { get; }
    public string? ExecutableNodeId { get; }
    public string? CheckpointId { get; }
    public string? IncidentId { get; }
    public JsonElement? Payload { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class WorkflowLifecycleHistoryEvent
{
    public WorkflowLifecycleHistoryEvent(
        string historyEventId,
        string workflowExecutionId,
        WorkflowExecutionStatus status,
        string name,
        DateTimeOffset occurredAt,
        WorkflowExecutableIdentity? pinnedExecutable = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        HistoryEventId = historyEventId;
        WorkflowExecutionId = workflowExecutionId;
        Status = status;
        Name = name;
        OccurredAt = occurredAt;
        PinnedExecutable = pinnedExecutable;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string HistoryEventId { get; }
    public string WorkflowExecutionId { get; }
    public WorkflowExecutionStatus Status { get; }
    public string Name { get; }
    public DateTimeOffset OccurredAt { get; }
    public WorkflowExecutableIdentity? PinnedExecutable { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class ActivityLifecycleHistoryEvent
{
    public ActivityLifecycleHistoryEvent(
        string historyEventId,
        ActivityExecution execution,
        ActivityExecutionStatus status,
        string name,
        DateTimeOffset occurredAt,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyEventId);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        HistoryEventId = historyEventId;
        Execution = execution;
        Status = status;
        Name = name;
        OccurredAt = occurredAt;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string HistoryEventId { get; }
    public ActivityExecution Execution { get; }
    public ActivityExecutionStatus Status { get; }
    public string Name { get; }
    public DateTimeOffset OccurredAt { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class RuntimeDiagnostic
{
    public RuntimeDiagnostic(
        string diagnosticId,
        RuntimeDiagnosticSeverity severity,
        string source,
        string message,
        DateTimeOffset occurredAt,
        string? workflowExecutionId = null,
        string? activityExecutionId = null,
        string? code = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        DiagnosticId = diagnosticId;
        Severity = severity;
        Source = source;
        Message = message;
        OccurredAt = occurredAt;
        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        Code = code;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string DiagnosticId { get; }
    public RuntimeDiagnosticSeverity Severity { get; }
    public string Source { get; }
    public string Message { get; }
    public DateTimeOffset OccurredAt { get; }
    public string? WorkflowExecutionId { get; }
    public string? ActivityExecutionId { get; }
    public string? Code { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public enum RuntimeHistoryEventCategory
{
    WorkflowLifecycle,
    ActivityLifecycle,
    BookmarkLifecycle,
    ValueLifecycle,
    IncidentLifecycle,
    SchedulerLifecycle,
    OperationalLifecycle
}

public enum RuntimeDiagnosticSeverity
{
    Trace,
    Information,
    Warning,
    Error,
    Critical
}
