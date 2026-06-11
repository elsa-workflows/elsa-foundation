using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Scheduler payload for recording that one activity execution reached completed state and needs completion-drain work.
/// </summary>
public sealed class RuntimeCompleteActivityCommandPayload
{
    public const string ActivityInvocationCompletedReason = "ActivityInvocationCompleted";

    [JsonConstructor]
    public RuntimeCompleteActivityCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string executableNodeId,
        string activityExecutionId,
        string? parentActivityExecutionId,
        string? branchId,
        IReadOnlyCollection<string>? outcomeNames,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (parentActivityExecutionId is not null && string.IsNullOrWhiteSpace(parentActivityExecutionId))
            throw new ArgumentException("Parent activity execution ID cannot be blank when provided.", nameof(parentActivityExecutionId));

        if (branchId is not null && string.IsNullOrWhiteSpace(branchId))
            throw new ArgumentException("Branch ID cannot be blank when provided.", nameof(branchId));

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
    }

    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public string ExecutableNodeId { get; }
    public string ActivityExecutionId { get; }
    public string? ParentActivityExecutionId { get; }
    public string? BranchId { get; }
    public IReadOnlyCollection<string> OutcomeNames { get; }
    public string Reason { get; }
}
