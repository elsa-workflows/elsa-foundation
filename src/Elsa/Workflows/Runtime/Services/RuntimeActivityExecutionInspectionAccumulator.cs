using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimeActivityExecutionInspectionAccumulator(IActivityExecutionInspectionStore store) : IRuntimeActivityExecutionInspectionAccumulator
{
    public async ValueTask<ActivityExecutionInspectionProjection> BuildProjectionAsync(
        ActivityExecutionState state,
        string checkpointId,
        DateTimeOffset committedAt,
        IReadOnlyCollection<string>? outcomeNames = null,
        IReadOnlyCollection<ActivityExecutionBookmarkSummary>? bookmarks = null,
        IReadOnlyCollection<ActivityExecutionIncidentSummary>? incidents = null,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await store.FindAsync(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId, cancellationToken);
        return existing is null
            ? ActivityExecutionInspectionProjection.FromState(state, checkpointId, committedAt, outcomeNames, bookmarks, incidents, valueSnapshots, metadata)
            : existing.Merge(state, checkpointId, committedAt, outcomeNames, bookmarks, incidents, valueSnapshots, metadata);
    }
}
