using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Services.History;

namespace Elsa.Workflows.Runtime.Services.ActivityExecutions;

public sealed class RuntimeActivityExecutionInspectionAccumulator : IRuntimeActivityExecutionInspectionAccumulator
{
    private readonly IActivityExecutionInspectionStore _store;
    private readonly IWorkflowExecutionStateStore? _executions;
    private readonly RuntimeHistoryOptions? _history;
    public RuntimeActivityExecutionInspectionAccumulator(IActivityExecutionInspectionStore store) => _store = store;
    public RuntimeActivityExecutionInspectionAccumulator(IActivityExecutionInspectionStore store, IWorkflowExecutionStateStore executions, RuntimeHistoryOptions? history = null)
        : this(store) { _executions = executions; _history = history; }

    public bool SupportsCheckpointValueCapture => _executions is not null && _history is not null;

    public ValueTask<ActivityExecutionInspectionProjection> BuildProjectionAsync(
        ActivityExecutionState state,
        string checkpointId,
        DateTimeOffset committedAt,
        IReadOnlyCollection<string>? outcomeNames = null,
        IReadOnlyCollection<ActivityExecutionBookmarkSummary>? bookmarks = null,
        IReadOnlyCollection<ActivityExecutionIncidentSummary>? incidents = null,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default) =>
        BuildAsync(state, checkpointId, committedAt, null, outcomeNames, bookmarks, incidents, valueSnapshots, metadata, cancellationToken);

    public ValueTask<ActivityExecutionInspectionProjection> BuildProjectionWithSourceAsync(
        ActivityExecutionState state, string checkpointId, DateTimeOffset committedAt, Func<ActivityValueCaptureSource> source,
        IReadOnlyCollection<string>? outcomeNames = null,
        IReadOnlyCollection<ActivityExecutionBookmarkSummary>? bookmarks = null,
        IReadOnlyCollection<ActivityExecutionIncidentSummary>? incidents = null,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default) =>
        BuildAsync(state, checkpointId, committedAt, source ?? throw new ArgumentNullException(nameof(source)), outcomeNames, bookmarks, incidents, valueSnapshots, metadata, cancellationToken);

    private async ValueTask<ActivityExecutionInspectionProjection> BuildAsync(
        ActivityExecutionState state, string checkpointId, DateTimeOffset committedAt, Func<ActivityValueCaptureSource>? sourceFactory,
        IReadOnlyCollection<string>? outcomeNames, IReadOnlyCollection<ActivityExecutionBookmarkSummary>? bookmarks,
        IReadOnlyCollection<ActivityExecutionIncidentSummary>? incidents, IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots,
        IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        cancellationToken.ThrowIfCancellationRequested();

        var policy = _history is null || _executions is null ? null : (await _executions.FindAsync(state.Execution.WorkflowExecutionId, cancellationToken))?.HistoryCapturePolicy;
        var capture = RuntimeHistorySourceFacts.IsFinalProfile(policy);
        var source = capture ? sourceFactory?.Invoke() : null;
        if (capture && valueSnapshots is { Count: > 0 } && source is null)
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The value owner did not supply immutable capture provenance.");
        if (source is not null && source.WorkflowExecutionId != state.Execution.WorkflowExecutionId)
            throw new InvalidOperationException("Value capture provenance belongs to a different workflow.");
        var additions = capture ? (valueSnapshots ?? []).Select((value, ordinal) => Stamp(value, ordinal)).ToArray() : valueSnapshots;
        var existing = await _store.FindAsync(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId, cancellationToken);
        var alreadyCaptured = capture && existing is not null ? existing.ValueSnapshots.Select(value => value.EvidenceId).ToHashSet(StringComparer.Ordinal) : null;
        var mergeAdditions = alreadyCaptured is null ? additions : (additions ?? []).Where(value => !alreadyCaptured.Contains(value.EvidenceId)).ToArray();
        var projection = existing is null
            ? ActivityExecutionInspectionProjection.FromState(state, checkpointId, committedAt, outcomeNames, bookmarks, incidents, mergeAdditions, metadata)
            : existing.Merge(state, checkpointId, committedAt, outcomeNames, bookmarks, incidents, mergeAdditions, metadata);
        return capture ? projection with { CheckpointValueCapture = new(checkpointId, source?.SchedulerWorkItemId, (additions ?? []).Select(value => value.EvidenceId!).ToArray()) } : projection;

        ActivityExecutionInspectionValueSnapshot Stamp(ActivityExecutionInspectionValueSnapshot value, int ordinal)
        {
            var phase = value.Phase ?? (source?.Phase is { } known && known != ActivityValueCapturePhase.Unknown ? known.ToString() : null);
            var evaluation = value.EvaluationId ?? (source?.SchedulerWorkItemId is null || source.Phase == ActivityValueCapturePhase.Unknown ? null :
                "evaluation:" + RuntimeHistoryIdentity.CreateBinding([state.Execution.WorkflowExecutionId, state.InvocationId, source.SchedulerWorkItemId,
                    state.Attempts?.LastOrDefault()?.AttemptId, value.Subject.ToString(), value.InputKey ?? value.Name]));
            var identity = value.EvidenceId ?? "value-" + RuntimeHistoryIdentity.CreateBinding([state.Execution.WorkflowExecutionId, state.InvocationId,
                checkpointId, source?.SchedulerWorkItemId, value.Subject.ToString(), value.InputKey ?? value.Name, evaluation,
                evaluation is null ? ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture) : null]);
            return value with { EvidenceId = identity, EvaluationId = evaluation, Phase = phase };
        }
    }
}
