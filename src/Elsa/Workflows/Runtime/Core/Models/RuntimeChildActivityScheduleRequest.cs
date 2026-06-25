namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Activity-owned request to schedule one child executable node.
/// </summary>
public sealed class RuntimeChildActivityScheduleRequest
{
    public RuntimeChildActivityScheduleRequest(
        string executableNodeId,
        string? schedulingActivityExecutionId,
        IReadOnlyDictionary<string, string>? metadata = null,
        ActivitySchedulingProvenance? schedulingProvenance = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);

        if (schedulingActivityExecutionId is not null && string.IsNullOrWhiteSpace(schedulingActivityExecutionId))
            throw new ArgumentException("Scheduling activity execution ID cannot be blank when provided.", nameof(schedulingActivityExecutionId));

        ExecutableNodeId = executableNodeId;
        SchedulingActivityExecutionId = schedulingActivityExecutionId;
        Metadata = RuntimeModelMetadata.Snapshot(metadata ?? new Dictionary<string, string>());
        SchedulingProvenance = schedulingProvenance ?? ActivitySchedulingProvenance.Empty;
    }

    public string ExecutableNodeId { get; }
    public string? SchedulingActivityExecutionId { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public ActivitySchedulingProvenance SchedulingProvenance { get; }
}
