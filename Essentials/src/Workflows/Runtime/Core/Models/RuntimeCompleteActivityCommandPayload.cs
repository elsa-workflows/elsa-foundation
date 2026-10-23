using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Scheduler payload for deriving activity completion continuations and durable parent evaluations.
/// </summary>
public sealed class RuntimeCompleteActivityCommandPayload : IActivityCommandPayload
{
    public const string ActivityInvocationCompletedReason = "ActivityInvocationCompleted";
    public const string ParentCompletionEvaluationReason = "ParentCompletionEvaluation";
    public const string ParentOutcomeEvaluationReason = "ParentOutcomeEvaluation";
    public const string ContinuationSchedulingReason = "ContinuationScheduling";

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
        : this(
            pinnedExecutable,
            executableNodeId,
            activityExecutionId,
            parentActivityExecutionId,
            branchId,
            outcomeNames,
            reason,
            completionKind,
            completedChildActivityExecutionId,
            parentOutcome: null)
    {
    }

    [JsonConstructor]
    public RuntimeCompleteActivityCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string executableNodeId,
        string activityExecutionId,
        string? parentActivityExecutionId,
        string? branchId,
        IReadOnlyCollection<string>? outcomeNames,
        string reason,
        SchedulerCompletionKind completionKind,
        string? completedChildActivityExecutionId,
        RuntimeParentOutcome? parentOutcome = null)
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

        var isParentEvaluation = completionKind is SchedulerCompletionKind.ParentCompletionEvaluation or SchedulerCompletionKind.ParentOutcomeEvaluation;

        if (isParentEvaluation && completedChildActivityExecutionId is null)
            throw new ArgumentException("Parent evaluation work requires a completed child activity execution ID.", nameof(completedChildActivityExecutionId));

        if (!isParentEvaluation && completedChildActivityExecutionId is not null)
            throw new ArgumentException("Completed child activity execution ID is only valid for parent evaluation work.", nameof(completedChildActivityExecutionId));

        if (isParentEvaluation && completedChildActivityExecutionId == activityExecutionId)
            throw new ArgumentException("Parent evaluation cannot use the same activity execution as parent and completed child.", nameof(completedChildActivityExecutionId));

        if (completionKind == SchedulerCompletionKind.ParentOutcomeEvaluation && parentOutcome is null)
            throw new ArgumentNullException(nameof(parentOutcome), "Parent outcome evaluation work requires a versioned parent outcome.");

        if (completionKind != SchedulerCompletionKind.ParentOutcomeEvaluation && parentOutcome is not null)
            throw new ArgumentException("A parent outcome is only valid for parent outcome evaluation work.", nameof(parentOutcome));

        if (parentOutcome is not null && parentOutcome.Notifications.Any(notification =>
                !StringComparer.Ordinal.Equals(notification.NotifyingChildActivityExecutionId, completedChildActivityExecutionId)))
            throw new ArgumentException("Parent outcome notifications must come from the completed child activity execution.", nameof(parentOutcome));

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
        ParentOutcome = parentOutcome;
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeParentOutcome? ParentOutcome { get; }
}
