using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Runtime-owned correlation data explaining why and from where an activity execution was scheduled.
/// </summary>
public sealed record ActivitySchedulingProvenance(
    string? ParentActivityExecutionId,
    string? SchedulingActivityExecutionId,
    string? SchedulingWorkflowExecutionId,
    string? BranchId,
    string? IterationId,
    string? ExecutionPathId,
    string? ExecutionScopeId,
    string? SchedulingCause,
    IReadOnlyDictionary<string, string> Metadata,
    ActivityExecutionAttemptLineage? Attempt = null)
{
    /// <summary>
    /// The activity executions whose completion caused this scheduling (spec 1229 FR-007): the completed child of a
    /// parent callback or a loop's next iteration, or the arrivals that satisfied a join. Absent when the scheduling
    /// activity alone is the cause, and on older records. Inspection reads it to decide which writes happen before this
    /// invocation; nothing at run time depends on it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? CausedBy { get; init; }

    /// <summary>Returns this provenance with <paramref name="causedBy"/> as its causes, unless it already names some.</summary>
    public ActivitySchedulingProvenance WithCausedBy(IEnumerable<string?>? causedBy)
    {
        if (CausedBy is { Count: > 0 } || causedBy is null)
            return this;
        var causes = causedBy
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return causes.Length == 0 ? this : this with { CausedBy = causes };
    }

    public static ActivitySchedulingProvenance Empty { get; } = new(
        ParentActivityExecutionId: null,
        SchedulingActivityExecutionId: null,
        SchedulingWorkflowExecutionId: null,
        BranchId: null,
        IterationId: null,
        ExecutionPathId: null,
        ExecutionScopeId: null,
        SchedulingCause: null,
        Metadata: new Dictionary<string, string>(),
        Attempt: null);

    public static ActivitySchedulingProvenance From(
        string workflowExecutionId,
        string? parentActivityExecutionId,
        string? schedulingActivityExecutionId,
        string? branchId,
        string? iterationId,
        string? executionPathId,
        string? executionScopeId,
        string? schedulingCause,
        IReadOnlyDictionary<string, string>? metadata = null,
        ActivityExecutionAttemptLineage? attempt = null) =>
        new(
            ParentActivityExecutionId: parentActivityExecutionId,
            SchedulingActivityExecutionId: schedulingActivityExecutionId,
            SchedulingWorkflowExecutionId: workflowExecutionId,
            BranchId: branchId,
            IterationId: iterationId,
            ExecutionPathId: executionPathId,
            ExecutionScopeId: executionScopeId,
            SchedulingCause: schedulingCause,
            Metadata: RuntimeModelMetadata.Snapshot(metadata),
            Attempt: attempt);
}
