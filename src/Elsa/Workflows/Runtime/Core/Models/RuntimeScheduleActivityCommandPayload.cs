using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Scheduler payload for starting work on one executable activity node.
/// </summary>
public sealed class RuntimeScheduleActivityCommandPayload
{
    public const string WorkflowStartReason = "WorkflowStart";

    [JsonConstructor]
    public RuntimeScheduleActivityCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string executableNodeId,
        string reason,
        string? activityExecutionId = null,
        string? schedulingActivityExecutionId = null)
    {
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (activityExecutionId is not null && string.IsNullOrWhiteSpace(activityExecutionId))
            throw new ArgumentException("Activity execution ID cannot be blank when provided.", nameof(activityExecutionId));

        if (schedulingActivityExecutionId is not null && string.IsNullOrWhiteSpace(schedulingActivityExecutionId))
            throw new ArgumentException("Scheduling activity execution ID cannot be blank when provided.", nameof(schedulingActivityExecutionId));

        PinnedExecutable = pinnedExecutable;
        ExecutableNodeId = executableNodeId;
        Reason = reason;
        ActivityExecutionId = activityExecutionId;
        SchedulingActivityExecutionId = schedulingActivityExecutionId;
    }

    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public string ExecutableNodeId { get; }
    public string Reason { get; }
    public string? ActivityExecutionId { get; }
    public string? SchedulingActivityExecutionId { get; }
}
