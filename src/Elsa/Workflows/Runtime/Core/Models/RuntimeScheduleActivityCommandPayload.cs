using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Scheduler payload for starting work on one executable activity node.
/// </summary>
public sealed class RuntimeScheduleActivityCommandPayload
{
    public const string WorkflowStartReason = "WorkflowStart";
    public const string ActivityCompletionReason = "ActivityCompletion";

    [JsonConstructor]
    public RuntimeScheduleActivityCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string executableNodeId,
        string activityExecutionId,
        string reason,
        string? schedulingActivityExecutionId = null,
        string? parentActivityExecutionId = null,
        ActivitySchedulingProvenance? schedulingProvenance = null,
        LoopIterationScopeRequest? iterationFrame = null)
    {
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (schedulingActivityExecutionId is not null && string.IsNullOrWhiteSpace(schedulingActivityExecutionId))
            throw new ArgumentException("Scheduling activity execution ID cannot be blank when provided.", nameof(schedulingActivityExecutionId));

        if (parentActivityExecutionId is not null && string.IsNullOrWhiteSpace(parentActivityExecutionId))
            throw new ArgumentException("Parent activity execution ID cannot be blank when provided.", nameof(parentActivityExecutionId));

        PinnedExecutable = pinnedExecutable;
        ExecutableNodeId = executableNodeId;
        Reason = reason;
        ActivityExecutionId = activityExecutionId;
        SchedulingActivityExecutionId = schedulingActivityExecutionId;
        ParentActivityExecutionId = parentActivityExecutionId;
        SchedulingProvenance = schedulingProvenance ?? ActivitySchedulingProvenance.Empty;
        IterationFrame = iterationFrame;
    }

    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public string ExecutableNodeId { get; }
    public string Reason { get; }
    public string ActivityExecutionId { get; }
    public string? SchedulingActivityExecutionId { get; }
    public string? ParentActivityExecutionId { get; }
    public ActivitySchedulingProvenance SchedulingProvenance { get; }
    public LoopIterationScopeRequest? IterationFrame { get; }
}
