using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Minimal runtime continuation state for an execution-affecting incident.
/// </summary>
public sealed class IncidentState
{
    public IncidentState(
        string incidentId,
        string workflowExecutionId,
        string? activityExecutionId,
        string? executableNodeId,
        IncidentSeverity severity,
        IncidentStatus status,
        IncidentResolutionAction resolutionAction,
        string failureType,
        string message,
        DateTimeOffset createdAt,
        DateTimeOffset? resolvedAt,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureType);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (resolvedAt < createdAt)
            throw new ArgumentException("Incident resolution time cannot be earlier than creation time.", nameof(resolvedAt));

        IncidentId = incidentId;
        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        ExecutableNodeId = executableNodeId;
        Severity = severity;
        Status = status;
        ResolutionAction = resolutionAction;
        FailureType = failureType;
        Message = message;
        CreatedAt = createdAt;
        ResolvedAt = resolvedAt;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string IncidentId { get; }
    public string WorkflowExecutionId { get; }
    public string? ActivityExecutionId { get; }
    public string? ExecutableNodeId { get; }
    public IncidentSeverity Severity { get; }
    public IncidentStatus Status { get; }
    public IncidentResolutionAction ResolutionAction { get; }
    public string FailureType { get; }
    public string Message { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? ResolvedAt { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public bool IsBlocking => Status == IncidentStatus.Blocking;
}

/// <summary>
/// Observation projection for incident details. This is not runtime continuation state.
/// </summary>
public sealed class IncidentHistoryProjection
{
    public IncidentHistoryProjection(
        string historyEventId,
        string incidentId,
        string workflowExecutionId,
        string? activityExecutionId,
        string? executableNodeId,
        string? activityType,
        IncidentSeverity severity,
        IncidentStatus status,
        string failureType,
        string message,
        string? exceptionType,
        string? stackTrace,
        DateTimeOffset occurredAt,
        JsonElement? diagnosticPayload = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureType);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        HistoryEventId = historyEventId;
        IncidentId = incidentId;
        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        ExecutableNodeId = executableNodeId;
        ActivityType = activityType;
        Severity = severity;
        Status = status;
        FailureType = failureType;
        Message = message;
        ExceptionType = exceptionType;
        StackTrace = stackTrace;
        OccurredAt = occurredAt;
        DiagnosticPayload = diagnosticPayload?.Clone();
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string HistoryEventId { get; }
    public string IncidentId { get; }
    public string WorkflowExecutionId { get; }
    public string? ActivityExecutionId { get; }
    public string? ExecutableNodeId { get; }
    public string? ActivityType { get; }
    public IncidentSeverity Severity { get; }
    public IncidentStatus Status { get; }
    public string FailureType { get; }
    public string Message { get; }
    public string? ExceptionType { get; }
    public string? StackTrace { get; }
    public DateTimeOffset OccurredAt { get; }
    public JsonElement? DiagnosticPayload { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public enum IncidentSeverity
{
    Information,
    Warning,
    Error,
    Critical
}

public enum IncidentStatus
{
    Open,
    Blocking,
    Resolved,
    Suppressed
}

public enum IncidentResolutionAction
{
    None,
    Continue,
    Retry,
    SuspendWorkflow,
    FaultWorkflow,
    WaitForIntervention
}
