using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryWorkflowExecutionStateStore() : InMemoryKeyedStateStore<string, WorkflowExecutionState>(StringComparer.Ordinal), IWorkflowExecutionStateStore
{
    public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Save(state.WorkflowExecutionId, state));
    }

    public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Find(workflowExecutionId));
    }

    public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new(SnapshotAll());
    }

    public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(
        WorkflowExecutionStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        return new(ReadValues(states => QueryPage(states, query)));
    }

    public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyCollection<string> artifactIds = SnapshotAll()
            .Select(x => x.PinnedExecutable.ArtifactId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new(artifactIds);
    }

    public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Remove(workflowExecutionId));
    }

    private static WorkflowExecutionStatePage QueryPage(
        IEnumerable<WorkflowExecutionState> states,
        WorkflowExecutionStatePageQuery query)
    {
        var comparer = Comparer<WorkflowExecutionState>.Create(WorkflowExecutionStateHistory.Compare);
        var candidates = new SortedSet<WorkflowExecutionState>(comparer);
        var pageSize = WorkflowExecutionStateHistory.EffectivePageSize(query);
        var limit = checked(pageSize + 1);
        long total = 0;

        foreach (var state in states)
        {
            if (!WorkflowExecutionStateHistory.Matches(state, query))
                continue;

            total++;
            if (query.Cursor is { } cursor)
            {
                var comparison = WorkflowExecutionStateHistory.Compare(state, cursor);
                if (cursor.Direction == WorkflowExecutionStatePageDirection.Next ? comparison <= 0 : comparison >= 0)
                    continue;
            }

            candidates.Add(state);
            if (candidates.Count <= limit)
                continue;

            if (query.Cursor?.Direction == WorkflowExecutionStatePageDirection.Previous)
                candidates.Remove(candidates.Min!);
            else
                candidates.Remove(candidates.Max!);
        }

        var hasExtra = candidates.Count > pageSize;
        if (hasExtra)
        {
            if (query.Cursor?.Direction == WorkflowExecutionStatePageDirection.Previous)
                candidates.Remove(candidates.Min!);
            else
                candidates.Remove(candidates.Max!);
        }

        var items = candidates.ToArray();
        var hasPrevious = query.Cursor?.Direction switch
        {
            WorkflowExecutionStatePageDirection.Next => true,
            WorkflowExecutionStatePageDirection.Previous => hasExtra,
            _ => false
        };
        var hasNext = query.Cursor?.Direction switch
        {
            WorkflowExecutionStatePageDirection.Previous => true,
            _ => hasExtra
        };

        return new(
            items,
            hasPrevious && items.Length > 0
                ? WorkflowExecutionStateHistory.Cursor(items[0], WorkflowExecutionStatePageDirection.Previous, query)
                : null,
            hasNext && items.Length > 0
                ? WorkflowExecutionStateHistory.Cursor(items[^1], WorkflowExecutionStatePageDirection.Next, query)
                : null,
            hasPrevious,
            hasNext,
            total);
    }
}
