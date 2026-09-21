using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Scheduler command for cancelling one activity execution scope without cancelling its workflow.</summary>
public sealed class CancelActivityScopeCommand
{
    [JsonConstructor]
    public CancelActivityScopeCommand(
        string activityExecutionId,
        string executionScopeId,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionScopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!StringComparer.Ordinal.Equals(activityExecutionId, executionScopeId))
            throw new ArgumentException("An activity-boundary cancellation scope must equal the outer activity execution id.", nameof(executionScopeId));

        ActivityExecutionId = activityExecutionId;
        ExecutionScopeId = executionScopeId;
        Reason = reason;
    }

    public string ActivityExecutionId { get; }
    public string ExecutionScopeId { get; }
    public string Reason { get; }
}

/// <summary>Exact resources selected for cleanup by one scope-cancellation checkpoint.</summary>
public sealed record ActivityScopeCleanupRequest(
    string WorkflowExecutionId,
    string ExecutionScopeId,
    IReadOnlyCollection<string> ActivityExecutionIds,
    IReadOnlyCollection<string> BookmarkIds,
    IReadOnlyCollection<string> TimerIds,
    IReadOnlyCollection<string> SchedulerWorkItemIds);
