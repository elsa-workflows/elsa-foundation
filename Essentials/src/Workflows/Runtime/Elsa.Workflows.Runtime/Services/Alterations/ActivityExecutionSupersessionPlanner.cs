using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Produces the narrow cleanup needed when one activity execution is replaced. It retains the source record for
/// supersession history, while reclaiming only source-subtree stimuli and cancelling still-live descendants.
/// </summary>
public sealed class ActivityExecutionSupersessionPlanner(IActivityScopeCleanupStore cleanupStore)
{
    private readonly IActivityScopeCleanupStore _cleanupStore = cleanupStore ?? throw new ArgumentNullException(nameof(cleanupStore));

    public async ValueTask<ActivityExecutionSupersessionPlan> PlanAsync(
        string workflowExecutionId,
        ActivityExecutionState source,
        IReadOnlyCollection<ActivityExecutionState> allActivities,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(allActivities);

        var subtree = Traverse(source, allActivities);
        // Cleanup resources can be keyed either by the activity execution identity or a derived execution scope.
        // Capture both identities for every member of the superseded subtree before its live descendants are cancelled.
        var cleanupIds = subtree
            .SelectMany(state => new[] { state.Execution.ActivityExecutionId, state.ExecutionScopeId })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);
        var cleanup = await _cleanupStore.CaptureAsync(
            workflowExecutionId,
            source.Execution.ActivityExecutionId,
            cleanupIds,
            cancellationToken);
        var descendants = subtree
            .Where(state => !StringComparer.Ordinal.Equals(state.Execution.ActivityExecutionId, source.Execution.ActivityExecutionId))
            .Where(state => state.Status is ActivityExecutionStatus.Scheduled or ActivityExecutionStatus.Running or ActivityExecutionStatus.Waiting or ActivityExecutionStatus.Suspended)
            .Select(state => state with
            {
                Status = ActivityExecutionStatus.Cancelled,
                SubStatus = "SupersededAncestor",
                CompletedAt = occurredAt
            })
            .OrderBy(state => state.ExecutionSequence)
            .ThenBy(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal)
            .ToArray();
        return new ActivityExecutionSupersessionPlan(descendants, cleanup);
    }

    private static IReadOnlyCollection<ActivityExecutionState> Traverse(ActivityExecutionState source, IReadOnlyCollection<ActivityExecutionState> allActivities)
    {
        var children = allActivities
            .Where(state => !string.IsNullOrWhiteSpace(state.ParentActivityExecutionId))
            .GroupBy(state => state.ParentActivityExecutionId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var result = new List<ActivityExecutionState>();
        var queue = new Queue<ActivityExecutionState>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        queue.Enqueue(source);
        while (queue.TryDequeue(out var state))
        {
            if (!visited.Add(state.Execution.ActivityExecutionId))
                continue;
            result.Add(state);
            if (children.TryGetValue(state.Execution.ActivityExecutionId, out var directChildren))
                foreach (var child in directChildren)
                    queue.Enqueue(child);
        }
        return result;
    }
}

public sealed record ActivityExecutionSupersessionPlan(
    IReadOnlyCollection<ActivityExecutionState> CancelledDescendants,
    ActivityScopeCleanupRequest Cleanup);
