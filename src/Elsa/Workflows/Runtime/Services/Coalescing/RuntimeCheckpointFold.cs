using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Folds the ordered checkpoint change-sets buffered during a coalesced drain segment into a single change-set.
/// Upsert/Delete changes collapse to the last writer per <see cref="RuntimeStateChange{T}.StateId"/> (preserving
/// first-seen ordering); Append changes are never collapsed. The folded set is what the coalescing commit store
/// persists in one atomic commit at a flush boundary, giving Elsa-3-style single-write-per-burst durability.
/// </summary>
public static class RuntimeCheckpointFold
{
    /// <summary>
    /// Merges the supplied change-sets in order into one folded change-set. Post-commit outbox items are deliberately
    /// not folded here: the coalescing flush recomputes the still-undelivered continuation from the session's overlay
    /// outbox so consumed in-segment continuation is not durably re-recorded.
    /// </summary>
    public static RuntimeCheckpointStateChangeSet Fold(IReadOnlyList<RuntimeCheckpointStateChangeSet> changeSets)
    {
        ArgumentNullException.ThrowIfNull(changeSets);

        RuntimeStateChange<WorkflowExecutionState>? workflowExecution = null;
        RuntimeStateChange<SchedulerState>? scheduler = null;
        var activityExecutions = new MergeBuffer<ActivityExecutionState>();
        var inspections = new MergeBuffer<ActivityExecutionInspectionProjection>();
        var bookmarks = new MergeBuffer<BookmarkState>();
        var durableValues = new MergeBuffer<DurableValueState>();
        var incidents = new MergeBuffer<IncidentState>();
        var operational = new MergeBuffer<ExecutionLivenessState>();
        var workflowDispatches = new MergeBuffer<WorkflowDispatchRecord>();
        var workflowDispatchCancellations = new List<WorkflowDispatchCancellationRequest>();
        var activityScopeCleanups = new List<ActivityScopeCleanupRequest>();
        var consumedSchedulerWorkItems = new Dictionary<string, ConsumedSchedulerWorkItem>(StringComparer.Ordinal);
        WorkflowAlterationJobTerminalChange? alterationJobTerminalChange = null;

        foreach (var changeSet in changeSets)
        {
            if (changeSet.WorkflowExecution is not null)
                workflowExecution = changeSet.WorkflowExecution;

            if (changeSet.Scheduler is not null)
                scheduler = changeSet.Scheduler;

            activityExecutions.AddRange(changeSet.ActivityExecutions);
            inspections.AddRange(changeSet.ActivityExecutionInspections);
            bookmarks.AddRange(changeSet.Bookmarks);
            durableValues.AddRange(changeSet.DurableValues);
            incidents.AddRange(changeSet.Incidents);
            operational.AddRange(changeSet.Operational);
            workflowDispatches.AddRange(changeSet.WorkflowDispatches);
            workflowDispatchCancellations.AddRange(changeSet.WorkflowDispatchCancellations);
            activityScopeCleanups.AddRange(changeSet.ActivityScopeCleanups);
            // Union consumed work items across the segment (last writer per work-item id) so no per-hop consumption is
            // lost or duplicated in the fold.
            foreach (var consumed in changeSet.ConsumedSchedulerWorkItems)
                consumedSchedulerWorkItems[consumed.WorkItemId] = consumed;
            if (changeSet.AlterationJobTerminalChange is { } terminal)
            {
                if (alterationJobTerminalChange is not null)
                    throw new InvalidOperationException("Alteration job terminal evidence is a mandatory checkpoint boundary and cannot be folded.");
                alterationJobTerminalChange = terminal;
            }
        }

        return new RuntimeCheckpointStateChangeSet(
            workflowExecution,
            scheduler,
            activityExecutions.ToArray(),
            bookmarks.ToArray(),
            durableValues.ToArray(),
            incidents.ToArray(),
            operational.ToArray(),
            workflowDispatches: workflowDispatches.ToArray(),
            activityExecutionInspections: inspections.ToArray(),
            postCommitOutbox: null,
            activityScopeCleanups: activityScopeCleanups,
            workflowDispatchCancellations: workflowDispatchCancellations,
            consumedSchedulerWorkItems: consumedSchedulerWorkItems.Values.ToArray(),
            alterationJobTerminalChange: alterationJobTerminalChange);
    }

    private sealed class MergeBuffer<TState>
    {
        private readonly List<RuntimeStateChange<TState>> _changes = [];
        private readonly Dictionary<string, int> _collapsibleIndexByStateId = new(StringComparer.Ordinal);

        public void AddRange(IReadOnlyCollection<RuntimeStateChange<TState>> changes)
        {
            foreach (var change in changes)
                Add(change);
        }

        private void Add(RuntimeStateChange<TState> change)
        {
            // Append changes accumulate and must never be collapsed; Upsert/Delete collapse to the last writer per StateId.
            if (change.Operation == RuntimeStateChangeOperation.Append)
            {
                _changes.Add(change);
                return;
            }

            if (_collapsibleIndexByStateId.TryGetValue(change.StateId, out var index))
            {
                _changes[index] = change;
                return;
            }

            _collapsibleIndexByStateId[change.StateId] = _changes.Count;
            _changes.Add(change);
        }

        public RuntimeStateChange<TState>[] ToArray() => _changes.ToArray();
    }
}
