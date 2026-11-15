namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Activity-owned request to schedule one child executable node, at once or, with <see cref="NotBefore"/>, not before a
/// due time (R-7, spec 261).
/// </summary>
public sealed class RuntimeChildActivityScheduleRequest
{
    public RuntimeChildActivityScheduleRequest(
        string executableNodeId,
        string? schedulingActivityExecutionId,
        IReadOnlyDictionary<string, string>? metadata = null,
        ActivitySchedulingProvenance? schedulingProvenance = null,
        LoopIterationScopeRequest? iterationFrame = null,
        DateTimeOffset? notBefore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);

        if (schedulingActivityExecutionId is not null && string.IsNullOrWhiteSpace(schedulingActivityExecutionId))
            throw new ArgumentException("Scheduling activity execution ID cannot be blank when provided.", nameof(schedulingActivityExecutionId));

        ExecutableNodeId = executableNodeId;
        SchedulingActivityExecutionId = schedulingActivityExecutionId;
        Metadata = RuntimeModelMetadata.Snapshot(metadata ?? new Dictionary<string, string>());
        SchedulingProvenance = schedulingProvenance ?? ActivitySchedulingProvenance.Empty;
        IterationFrame = iterationFrame;
        NotBefore = notBefore?.ToUniversalTime();
    }

    public string ExecutableNodeId { get; }
    public string? SchedulingActivityExecutionId { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public ActivitySchedulingProvenance SchedulingProvenance { get; }
    public LoopIterationScopeRequest? IterationFrame { get; }

    /// <summary>
    /// The time before which the child must not be scheduled, in UTC; <see langword="null"/> schedules it as soon as the
    /// evaluation commits, as does a time at or before the commit.
    /// </summary>
    public DateTimeOffset? NotBefore { get; }
}
