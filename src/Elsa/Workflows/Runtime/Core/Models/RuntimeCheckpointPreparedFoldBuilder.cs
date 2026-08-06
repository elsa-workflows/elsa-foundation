namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Builds and validates the exact durable effects of a prepared-checkpoint fold. Only explicitly committed
/// members contribute effects; skipped and failed members consume logical order without contributing state.
/// </summary>
public static class RuntimeCheckpointPreparedFoldBuilder
{
    /// <summary>Builds the exact effects represented by the fold request.</summary>
    public static RuntimeCheckpointStateChangeSet Build(RuntimeCheckpointPreparedFoldRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var committed = request.Members
            .Where(member => member.Disposition == RuntimeCheckpointPreparedDisposition.Committed)
            .Select(member => member.PreparedCommit!.Commit.StateChanges)
            .ToArray();
        var excludedOutboxIds = new HashSet<string>(
            request.DeliveredPostCommitOutboxItemIds ?? [],
            StringComparer.Ordinal);
        var folded = Fold(committed);
        var outbox = committed
            .SelectMany(changeSet => changeSet.PostCommitOutbox)
            .Where(change => !excludedOutboxIds.Contains(change.State.OutboxItemId))
            .ToArray();
        return folded.WithPostCommitOutbox(outbox);
    }

    /// <summary>Returns whether the caller-supplied folded effects exactly match the committed members.</summary>
    public static bool Matches(RuntimeCheckpointPreparedFoldRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var expected = Build(request);
        return StringComparer.Ordinal.Equals(
            RuntimeCheckpointCommitFingerprint.ComputeStateChanges(request.WorkflowExecutionId, expected),
            RuntimeCheckpointCommitFingerprint.ComputeStateChanges(request.WorkflowExecutionId, request.FoldedStateChanges));
    }

    /// <summary>
    /// Validates that every explicitly delivered outbox exclusion is unique and belongs to a committed member.
    /// This makes live in-segment delivery an exact, reviewable subtraction from the committed aggregate.
    /// </summary>
    public static bool HasValidDeliveredOutboxExclusions(RuntimeCheckpointPreparedFoldRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var exclusions = request.DeliveredPostCommitOutboxItemIds;
        if (exclusions is null or { Count: 0 })
            return true;

        var committedOutboxIds = request.Members
            .Where(member => member.Disposition == RuntimeCheckpointPreparedDisposition.Committed)
            .SelectMany(member => member.PreparedCommit!.Commit.StateChanges.PostCommitOutbox)
            .Select(change => change.State.OutboxItemId)
            .ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return exclusions.All(id =>
            !string.IsNullOrWhiteSpace(id) &&
            seen.Add(id) &&
            committedOutboxIds.Contains(id));
    }

    /// <summary>
    /// Merges ordered state changes. Upsert/Delete changes collapse to the last writer per state identity while
    /// preserving first-seen position; Append changes accumulate. Post-commit outbox is assigned by <see cref="Build"/>.
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
