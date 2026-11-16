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
