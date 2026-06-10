namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Durable identity for one concrete execution of one executable activity node.
/// </summary>
public sealed record ActivityExecution(
    string ActivityExecutionId,
    string WorkflowExecutionId,
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion);

/// <summary>
/// Lifecycle and relationship state for an activity execution.
/// </summary>
public sealed record ActivityExecutionState(
    ActivityExecution Execution,
    ActivityExecutionStatus Status,
    string? SubStatus,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? SchedulingActivityExecutionId,
    string? ParentActivityExecutionId,
    string? BranchId,
    string? IterationId,
    int? CallStackDepth,
    IReadOnlyCollection<string> BookmarkIds,
    IReadOnlyCollection<string> IncidentIds,
    int FaultCount,
    int AggregateFaultCount,
    IReadOnlyDictionary<string, string> Metadata);

public enum ActivityExecutionStatus
{
    Scheduled,
    Running,
    Waiting,
    Suspended,
    Completed,
    Faulted,
    Cancelled
}
