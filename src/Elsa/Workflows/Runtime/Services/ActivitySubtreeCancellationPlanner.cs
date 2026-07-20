using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Shared planning core for cancelling one activity-execution subtree (spec 112): traverses the
/// subtree rooted at one execution, terminalizes its cancellable members, captures the resource
/// cleanup (bookmarks, durable timers, queued scheduler work), and suppresses the subtree's
/// non-terminal incidents. Planning is side-effect free; callers own the atomic checkpoint commit
/// that applies the plan.
/// </summary>
public sealed class ActivitySubtreeCancellationPlanner(
    IActivityScopeCleanupStore cleanupStore,
    IIncidentStateStore incidentStateStore)
{
    public async ValueTask<ActivitySubtreeCancellationPlan> PlanAsync(
        string workflowExecutionId,
        ActivityExecutionState root,
        IReadOnlyCollection<ActivityExecutionState> allStates,
        string subStatus,
        IReadOnlyDictionary<string, string> metadata,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(allStates);
        ArgumentException.ThrowIfNullOrWhiteSpace(subStatus);
        ArgumentNullException.ThrowIfNull(metadata);

        var scopedStates = TraverseSubtree(root, allStates, cancellationToken);
        var scopeIds = scopedStates.Select(state => state.Execution.ActivityExecutionId).ToHashSet(StringComparer.Ordinal);
        var cleanup = await cleanupStore.CaptureAsync(workflowExecutionId, root.Execution.ActivityExecutionId, scopeIds, cancellationToken);
        var cancelled = scopedStates
            .Where(IsCancellable)
            .Select(state => state with
            {
                Status = ActivityExecutionStatus.Cancelled,
                SubStatus = subStatus,
                CompletedAt = occurredAt,
                Metadata = Merge(state.Metadata, metadata)
            })
            .OrderBy(state => state.ExecutionSequence)
            .ThenBy(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal)
            .ToArray();
        var suppressedIncidents = (await incidentStateStore.ListAsync(workflowExecutionId, cancellationToken))
            .Where(incident => incident.ActivityExecutionId is { } activityExecutionId &&
                               scopeIds.Contains(activityExecutionId) &&
                               incident.Status is IncidentStatus.Open or IncidentStatus.Blocking)
            .OrderBy(incident => incident.IncidentId, StringComparer.Ordinal)
            .Select(incident => Suppress(incident, occurredAt, metadata))
            .ToArray();

        return new ActivitySubtreeCancellationPlan(root.Execution.ActivityExecutionId, cancelled, cleanup, suppressedIncidents);
    }

    private static IReadOnlyList<ActivityExecutionState> TraverseSubtree(
        ActivityExecutionState root,
        IReadOnlyCollection<ActivityExecutionState> allStates,
        CancellationToken cancellationToken)
    {
        var children = allStates.Where(state => state.ParentActivityExecutionId is not null)
            .GroupBy(state => state.ParentActivityExecutionId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(state => state.ExecutionSequence)
                .ThenBy(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var result = new List<ActivityExecutionState>();
        var queue = new Queue<ActivityExecutionState>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        queue.Enqueue(root);
        while (queue.TryDequeue(out var state))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(state.Execution.ActivityExecutionId))
                continue;
            result.Add(state);
            if (!children.TryGetValue(state.Execution.ActivityExecutionId, out var directChildren))
                continue;
            foreach (var child in directChildren)
                queue.Enqueue(child);
        }

        return result;
    }

    private static bool IsCancellable(ActivityExecutionState state) =>
        state.Status is ActivityExecutionStatus.Scheduled or ActivityExecutionStatus.Running or ActivityExecutionStatus.Waiting or ActivityExecutionStatus.Suspended;

    private static IncidentState Suppress(
        IncidentState incident,
        DateTimeOffset resolvedAt,
        IReadOnlyDictionary<string, string> metadata) =>
        new(
            incident.IncidentId,
            incident.WorkflowExecutionId,
            incident.ActivityExecutionId,
            incident.ExecutableNodeId,
            incident.Severity,
            IncidentStatus.Suppressed,
            IncidentResolutionAction.None,
            incident.FailureType,
            incident.Message,
            incident.CreatedAt,
            resolvedAt,
            Merge(incident.Metadata, metadata));

    private static IReadOnlyDictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> existing,
        IReadOnlyDictionary<string, string> additions)
    {
        var result = existing.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var item in additions)
            result[item.Key] = item.Value;
        return result;
    }
}

/// <summary>One subtree-cancellation's exact state effects; applied by the caller in one atomic checkpoint commit.</summary>
public sealed record ActivitySubtreeCancellationPlan(
    string RootActivityExecutionId,
    IReadOnlyList<ActivityExecutionState> CancelledStates,
    ActivityScopeCleanupRequest Cleanup,
    IReadOnlyList<IncidentState> SuppressedIncidents);
