namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Continuation state for one workflow execution pinned to an exact executable artifact snapshot.
/// </summary>
public sealed record WorkflowExecutionState(
    string WorkflowExecutionId,
    WorkflowExecutableIdentity PinnedExecutable,
    WorkflowExecutionStatus Status,
    string? SubStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? CompletedAt,
    string? CorrelationId,
    string? ParentWorkflowExecutionId,
    string? TenantId,
    IReadOnlyDictionary<string, string> SystemMetadata);

public enum WorkflowExecutionStatus
{
    Pending,
    Running,
    Suspended,
    Completed,
    Faulted,
    Cancelled
}
