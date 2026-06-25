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

    public ValueTask<IReadOnlyCollection<ActivityExecutionInspectionSummaryProjection>> ListSummariesAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return new ValueTask<IReadOnlyCollection<ActivityExecutionInspectionSummaryProjection>>(
                ListByWorkflowExecutionId(workflowExecutionId)
                    .Select(ActivityExecutionInspectionSummaryProjection.FromProjection)
                    .ToArray());
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
}
