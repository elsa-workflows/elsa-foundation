using System.Text.Json.Serialization;

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
[method: JsonConstructor]
public sealed record ActivityExecutionState(
    ActivityExecution Execution,
    ActivityExecutionStatus Status,
    string? SubStatus,
    long ExecutionSequence,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? SchedulingActivityExecutionId,
    string? ParentActivityExecutionId,
    string? BranchId,
    string? IterationId,
    ActivitySchedulingProvenance Provenance,
    int? CallStackDepth,
    IReadOnlyCollection<string> BookmarkIds,
    IReadOnlyCollection<string> IncidentIds,
    int FaultCount,
    int AggregateFaultCount,
    IReadOnlyDictionary<string, string> Metadata,
    string? ExecutionScopeId = null,
    ActivityExecutionAttemptLineage? Attempt = null)
{
    public ActivityExecutionState(
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
        IReadOnlyDictionary<string, string> Metadata)
        : this(
            Execution,
            Status,
            SubStatus,
            ExecutionSequence: 0,
            ScheduledAt,
            StartedAt,
            CompletedAt,
            SchedulingActivityExecutionId,
            ParentActivityExecutionId,
            BranchId,
            IterationId,
            ActivitySchedulingProvenance.From(
                Execution.WorkflowExecutionId,
                ParentActivityExecutionId,
                SchedulingActivityExecutionId,
                BranchId,
                IterationId,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: null),
            CallStackDepth,
            BookmarkIds,
            IncidentIds,
            FaultCount,
            AggregateFaultCount,
            Metadata,
            ExecutionScopeId: null,
            Attempt: null)
    {
    }
}

public enum ActivityExecutionStatus
{
    Scheduled,
    Running,
    Waiting,
    Suspended,
    Completed,
    Faulted,
    Cancelled,
    Recovered
}
