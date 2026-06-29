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

public static class WorkflowExecutionStatusExtensions
{
    /// <summary>
    /// Returns <see langword="true"/> when the workflow execution has reached a terminal status
    /// (<see cref="WorkflowExecutionStatus.Completed"/>, <see cref="WorkflowExecutionStatus.Faulted"/>,
    /// or <see cref="WorkflowExecutionStatus.Cancelled"/>) after which no further scheduler work may run.
    /// </summary>
    public static bool IsTerminal(this WorkflowExecutionStatus status) =>
        status is WorkflowExecutionStatus.Completed
            or WorkflowExecutionStatus.Faulted
            or WorkflowExecutionStatus.Cancelled;
}
