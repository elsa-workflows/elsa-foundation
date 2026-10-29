using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.ActivityExecutions;

public sealed class InMemoryActivityExecutionInspectionStore : IActivityExecutionInspectionStore, IActivityExecutionInspectionWriter
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<ActivityExecutionInspectionProjectionKey, ActivityExecutionInspectionProjection> _projections = new();

    public bool SupportsExecutionBrowsing => true;

    public ValueTask<ActivityExecutionBrowsePage> BrowseAsync(ActivityExecutionBrowseQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            var workflow = ListByWorkflowExecutionId(query.WorkflowExecutionId).ToArray();
            var parents = workflow.ToDictionary(x => x.ActivityExecutionId, x => x.Provenance.ParentActivityExecutionId, StringComparer.Ordinal);
            bool IsDescendant(ActivityExecutionInspectionProjection item)
            {
                if (query.AncestorActivityExecutionId is null) return true;
                if (!parents.ContainsKey(query.AncestorActivityExecutionId)) return false;
                var visited = new HashSet<string>(StringComparer.Ordinal) { item.ActivityExecutionId };
                var parent = item.Provenance.ParentActivityExecutionId;
                while (parent is not null && visited.Add(parent))
                {
                    if (StringComparer.Ordinal.Equals(parent, query.AncestorActivityExecutionId)) return true;
                    parent = parents.GetValueOrDefault(parent);
                }
                return false;
            }
            var matches = workflow.Where(x =>
                (query.ExecutableNodeId is null || StringComparer.Ordinal.Equals(x.ExecutableNodeId, query.ExecutableNodeId)) &&
                (query.ParentActivityExecutionId is null || StringComparer.Ordinal.Equals(x.Provenance.ParentActivityExecutionId, query.ParentActivityExecutionId)) &&
                IsDescendant(x)).OrderBy(x => x.ScheduledAt).ThenBy(x => x.ExecutionSequence)
                .ThenBy(x => x.ActivityExecutionId, StringComparer.Ordinal).ToArray();
            var anchor = query.AnchorActivityExecutionId is null ? -1 : Array.FindIndex(matches, x => x.ActivityExecutionId == query.AnchorActivityExecutionId);
            if (query.AnchorActivityExecutionId is not null && anchor < 0)
                return ValueTask.FromResult(new ActivityExecutionBrowsePage(matches.LongLength, 0, []));
            var offset = query.Offset ?? (anchor >= 0 ? anchor / query.Limit * query.Limit : Math.Max(0, matches.LongLength - query.Limit));
            var iterationPositions = new Dictionary<(string Node, string? Parent, string Iteration), long>();
            var iterationCounts = new Dictionary<(string Node, string? Parent), long>();
            foreach (var item in matches)
            {
                if (item.Provenance.IterationId is not { } iteration) continue;
                var group = (item.ExecutableNodeId, item.Provenance.ParentActivityExecutionId);
                var key = (item.ExecutableNodeId, item.Provenance.ParentActivityExecutionId, iteration);
                if (!iterationPositions.ContainsKey(key)) iterationPositions[key] = iterationCounts[group] = iterationCounts.GetValueOrDefault(group) + 1;
            }
            var items = matches.Skip((int)Math.Min(offset, int.MaxValue)).Take(query.Limit).Select((x, index) =>
            {
                var position = offset + index;
                return new ActivityExecutionBrowseEntry(ActivityExecutionInspectionSummaryProjection.FromProjection(x), position + 1,
                    position > 0 ? matches[(int)position - 1].ActivityExecutionId : null,
                    position + 1 < matches.LongLength ? matches[(int)position + 1].ActivityExecutionId : null,
                    x.Provenance.IterationId is { } iteration ? iterationPositions[(x.ExecutableNodeId, x.Provenance.ParentActivityExecutionId, iteration)] : null);
            }).ToArray();
            return ValueTask.FromResult(new ActivityExecutionBrowsePage(matches.LongLength, offset, items));
        }
    }

    internal void DeleteForWorkflowExecution(string workflowExecutionId)
    {
        lock (_syncRoot)
        {
            var keys = _projections.Keys
                .Where(key => StringComparer.Ordinal.Equals(key.WorkflowExecutionId, workflowExecutionId))
                .ToArray();
            foreach (var key in keys)
                _projections.Remove(key);
        }
    }

    public ValueTask<bool> TryAppendObservationAsync(string workflowExecutionId, string activityExecutionId,
        ActivityExecutionObservation observation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            var key = new ActivityExecutionInspectionProjectionKey(workflowExecutionId, activityExecutionId);
            if (!_projections.TryGetValue(key, out var projection)) return ValueTask.FromResult(false);
            _projections[key] = projection with { Observations = ActivityExecutionObservation.Merge(projection.Observations, [observation]) };
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask SaveAsync(ActivityExecutionInspectionProjection projection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = new ActivityExecutionInspectionProjectionKey(projection.WorkflowExecutionId, projection.ActivityExecutionId);
            _projections.TryGetValue(key, out var previous);
            _projections[key] = projection with { Observations = ActivityExecutionObservation.Merge(previous?.Observations ?? [], projection.Observations) };
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<ActivityExecutionInspectionProjection?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _projections.TryGetValue(new ActivityExecutionInspectionProjectionKey(workflowExecutionId, activityExecutionId), out var projection);
            return new ValueTask<ActivityExecutionInspectionProjection?>(projection);
        }
    }

    public ValueTask<ActivityExecutionInspectionSummaryPage> ListSummariesPageAsync(
        ActivityExecutionInspectionSummaryPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var all = ListByWorkflowExecutionId(query.WorkflowExecutionId)
                .Select(ActivityExecutionInspectionSummaryProjection.FromProjection)
                .ToArray();
            var queryBinding = $"activity-inspection-summary:workflow:{query.WorkflowExecutionId}";
            var lastKey = InMemoryRuntimeStoreContinuation.Decode(
                query.ContinuationToken,
                queryBinding,
                nameof(query));
            var remaining = all
                .Where(summary =>
                    lastKey is null ||
                    StringComparer.Ordinal.Compare(SortKey(summary), lastKey) > 0)
                .Take(query.Limit + 1)
                .ToArray();
            var items = remaining.Take(query.Limit).ToArray();
            var nextContinuationToken = remaining.Length > items.Length
                ? InMemoryRuntimeStoreContinuation.Encode(queryBinding, SortKey(items[^1]))
                : null;
            return ValueTask.FromResult(
                new ActivityExecutionInspectionSummaryPage(
                    query,
                    items,
                    all.Length,
                    nextContinuationToken));
        }
    }

    private IEnumerable<ActivityExecutionInspectionProjection> ListByWorkflowExecutionId(string workflowExecutionId) =>
        _projections
            .Where(item => StringComparer.Ordinal.Equals(item.Key.WorkflowExecutionId, workflowExecutionId))
            .Select(item => item.Value)
            .OrderBy(projection => projection.ExecutionSequence)
            .ThenBy(projection => projection.ScheduledAt)
            .ThenBy(projection => projection.ActivityExecutionId, StringComparer.Ordinal);

    private readonly record struct ActivityExecutionInspectionProjectionKey(string WorkflowExecutionId, string ActivityExecutionId);

    private static string SortKey(ActivityExecutionInspectionSummaryProjection summary) =>
        $"{summary.ExecutionSequence:D20}:{summary.ScheduledAt.UtcTicks:D19}:{summary.ActivityExecutionId}";
}
