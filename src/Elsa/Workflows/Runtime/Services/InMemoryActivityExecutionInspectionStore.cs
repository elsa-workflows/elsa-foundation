using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryActivityExecutionInspectionStore : IActivityExecutionInspectionStore, IActivityExecutionInspectionWriter
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<ActivityExecutionInspectionProjectionKey, ActivityExecutionInspectionProjection> _projections = new();

    public ValueTask SaveAsync(ActivityExecutionInspectionProjection projection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = new ActivityExecutionInspectionProjectionKey(projection.WorkflowExecutionId, projection.ActivityExecutionId);
            _projections[key] = projection;
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
