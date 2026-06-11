using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Scheduler payload for recording that one activity execution reached completed state and needs completion-drain work.
/// </summary>
public sealed class RuntimeCompleteActivityCommandPayload
{
    public const string ActivityInvocationCompletedReason = "ActivityInvocationCompleted";
    public const string ParentCompletionEvaluationReason = "ParentCompletionEvaluation";
    public const string ContinuationSchedulingReason = "ContinuationScheduling";

    [JsonConstructor]
    public RuntimeCompleteActivityCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string executableNodeId,
        string activityExecutionId,
        string? parentActivityExecutionId,
        string? branchId,
        IReadOnlyCollection<string>? outcomeNames,
        string reason,
        SchedulerCompletionKind completionKind = SchedulerCompletionKind.ActivityCompleted,
        string? completedChildActivityExecutionId = null)
    {
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (parentActivityExecutionId is not null && string.IsNullOrWhiteSpace(parentActivityExecutionId))
            throw new ArgumentException("Parent activity execution ID cannot be blank when provided.", nameof(parentActivityExecutionId));

        if (branchId is not null && string.IsNullOrWhiteSpace(branchId))
            throw new ArgumentException("Branch ID cannot be blank when provided.", nameof(branchId));

        if (!Enum.IsDefined(completionKind))
            throw new ArgumentOutOfRangeException(nameof(completionKind), completionKind, "Completion kind is not defined.");

        if (completedChildActivityExecutionId is not null && string.IsNullOrWhiteSpace(completedChildActivityExecutionId))
            throw new ArgumentException("Completed child activity execution ID cannot be blank when provided.", nameof(completedChildActivityExecutionId));

        if (completionKind == SchedulerCompletionKind.ParentCompletionEvaluation && completedChildActivityExecutionId is null)
            throw new ArgumentException("Parent completion evaluation work requires a completed child activity execution ID.", nameof(completedChildActivityExecutionId));

        if (completionKind != SchedulerCompletionKind.ParentCompletionEvaluation && completedChildActivityExecutionId is not null)
            throw new ArgumentException("Completed child activity execution ID is only valid for parent completion evaluation work.", nameof(completedChildActivityExecutionId));

        if (completionKind == SchedulerCompletionKind.ParentCompletionEvaluation && completedChildActivityExecutionId == activityExecutionId)
            throw new ArgumentException("Parent completion evaluation cannot use the same activity execution as parent and completed child.", nameof(completedChildActivityExecutionId));

        var outcomeSnapshot = (outcomeNames ?? []).ToArray();
        if (outcomeSnapshot.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Outcome names cannot contain blank values.", nameof(outcomeNames));

        if (outcomeSnapshot.Distinct(StringComparer.Ordinal).Count() != outcomeSnapshot.Length)
            throw new ArgumentException("Outcome names cannot contain duplicates.", nameof(outcomeNames));

        PinnedExecutable = pinnedExecutable;
        ExecutableNodeId = executableNodeId;
        ActivityExecutionId = activityExecutionId;
        ParentActivityExecutionId = parentActivityExecutionId;
        BranchId = branchId;
        OutcomeNames = outcomeSnapshot;
        Reason = reason;
        CompletionKind = completionKind;
        CompletedChildActivityExecutionId = completedChildActivityExecutionId;
    }

    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public string ExecutableNodeId { get; }
    public string ActivityExecutionId { get; }
    public string? ParentActivityExecutionId { get; }
    public string? BranchId { get; }
    public IReadOnlyCollection<string> OutcomeNames { get; }
    public string Reason { get; }
    public SchedulerCompletionKind CompletionKind { get; }
    public string? CompletedChildActivityExecutionId { get; }
}
