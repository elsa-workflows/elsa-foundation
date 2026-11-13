using System.Data.Common;
using System.Text.Json;
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
        ActivityExecutionState state, string checkpointId, DateTimeOffset committedAt, Func<ActivityValueCaptureOrigin> source,
        IReadOnlyCollection<string>? outcomeNames = null,
        IReadOnlyCollection<ActivityExecutionBookmarkSummary>? bookmarks = null,
        IReadOnlyCollection<ActivityExecutionIncidentSummary>? incidents = null,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default) =>
        BuildAsync(state, checkpointId, committedAt, source ?? throw new ArgumentNullException(nameof(source)), outcomeNames, bookmarks, incidents, valueSnapshots, metadata, cancellationToken);

    private async ValueTask<ActivityExecutionInspectionProjection> BuildAsync(
        ActivityExecutionState state, string checkpointId, DateTimeOffset committedAt, Func<ActivityValueCaptureOrigin>? sourceFactory,
        IReadOnlyCollection<string>? outcomeNames, IReadOnlyCollection<ActivityExecutionBookmarkSummary>? bookmarks,
        IReadOnlyCollection<ActivityExecutionIncidentSummary>? incidents, IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots,
        IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        cancellationToken.ThrowIfCancellationRequested();

        RuntimeHistoryCapturePolicy? policy = null;
        if (_history is not null && _executions is not null)
        {
            try { policy = (await _executions.FindAsync(state.Execution.WorkflowExecutionId, cancellationToken))?.HistoryCapturePolicy; }
            catch (Exception exception) when (IsMalformed(exception))
            {
                throw new RuntimeHistoryIntegrityException(state.Execution.WorkflowExecutionId,
                    "the admitted value-capture policy could not be interpreted", exception);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsUnavailable(exception))
            {
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable",
                    $"The value owner could not read the admitted policy for execution '{state.Execution.WorkflowExecutionId}'.", exception);
            }
        }
        var capture = RuntimeHistorySourceFacts.IsFinalProfile(policy);
        var source = capture ? sourceFactory?.Invoke() : null;
        if (capture && valueSnapshots is { Count: > 0 } && source is null)
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The value owner did not supply immutable capture provenance.");
        if (source is not null && source.WorkflowExecutionId != state.Execution.WorkflowExecutionId)
            throw new InvalidOperationException("Value capture provenance belongs to a different workflow.");
        ActivityExecutionInspectionProjection? existing;
        try { existing = await _store.FindAsync(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId, cancellationToken); }
        catch (Exception exception) when (capture && IsMalformed(exception))
        {
            throw new RuntimeHistoryIntegrityException(state.Execution.WorkflowExecutionId,
                $"inspection evidence for activity '{state.Execution.ActivityExecutionId}' could not be interpreted", exception);
        }
        catch (Exception exception) when (capture && !cancellationToken.IsCancellationRequested && IsUnavailable(exception))
        {
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable",
                $"The value owner could not read inspection evidence for execution '{state.Execution.WorkflowExecutionId}' activity '{state.Execution.ActivityExecutionId}'.", exception);
        }
        var additions = capture ? (valueSnapshots ?? []).Select((value, ordinal) => Stamp(value, ordinal)).ToArray() : valueSnapshots;
        var mergeAdditions = capture ? ValidateAdditions(existing?.ValueSnapshots ?? [], additions ?? []) : additions;
        var projection = existing is null
            ? ActivityExecutionInspectionProjection.FromState(state, checkpointId, committedAt, outcomeNames, bookmarks, incidents, mergeAdditions, metadata)
            : existing.Merge(state, checkpointId, committedAt, outcomeNames, bookmarks, incidents, mergeAdditions, metadata);
        return capture ? projection with { CheckpointValueCapture = new(checkpointId, source?.SchedulerWorkItemId,
            (additions ?? []).Select(value => value.EvidenceId!).Distinct(StringComparer.Ordinal).ToArray()) } : projection;

        ActivityExecutionInspectionValueSnapshot Stamp(ActivityExecutionInspectionValueSnapshot value, int ordinal)
        {
            var phase = value.Phase ?? (source?.Phase is { } known && known != ActivityValueCapturePhase.Unknown ? known.ToString() : null);
            var evaluation = value.EvaluationId ?? (source?.SchedulerWorkItemId is null || source.Phase == ActivityValueCapturePhase.Unknown ? null :
                "evaluation:" + RuntimeHistoryIdentity.CreateBinding([state.Execution.WorkflowExecutionId, state.InvocationId, source.SchedulerWorkItemId,
                    state.Attempts?.LastOrDefault()?.AttemptId, value.Subject.ToString(), value.InputKey ?? value.Name]));
            var identity = value.EvidenceId ?? "value-" + RuntimeHistoryIdentity.CreateBinding([state.Execution.WorkflowExecutionId, state.InvocationId,
                evaluation is null ? checkpointId : null, source?.SchedulerWorkItemId, value.Subject.ToString(), value.InputKey ?? value.Name, evaluation,
                evaluation is null ? ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture) : null]);
            return value with { EvidenceId = identity, EvaluationId = evaluation, Phase = phase };
        }
    }

    private static IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> ValidateAdditions(
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> existing,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> additions)
    {
        var evidence = new Dictionary<string, ActivityExecutionInspectionValueSnapshot>(StringComparer.Ordinal);
        var evaluations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in existing) Remember(value);
        var result = new List<ActivityExecutionInspectionValueSnapshot>();
        foreach (var value in additions)
        {
            if (evidence.TryGetValue(value.EvidenceId!, out var retained))
            {
                // Sequence is assigned by the inspection projection when the owner leaves it unspecified.
                var normalized = value.Sequence is null ? value with { Sequence = retained.Sequence } : value;
                if (!Equivalent(retained, normalized))
                    throw new InvalidOperationException($"Conflicting value evidence '{value.EvidenceId}'.");
                continue;
            }
            Remember(value);
            result.Add(value);
        }
        return result;

        void Remember(ActivityExecutionInspectionValueSnapshot value)
        {
            if (string.IsNullOrWhiteSpace(value.EvidenceId) || !evidence.TryAdd(value.EvidenceId, value))
                throw new InvalidOperationException("Value evidence identities must be present and unique.");
            if (!string.IsNullOrWhiteSpace(value.EvaluationId) && !evaluations.TryAdd(value.EvaluationId, value.EvidenceId))
                throw new InvalidOperationException($"Evaluation '{value.EvaluationId}' has conflicting evidence identities.");
        }
    }

    private static bool Equivalent(ActivityExecutionInspectionValueSnapshot left, ActivityExecutionInspectionValueSnapshot right) =>
        left.Name == right.Name && left.Subject == right.Subject && left.CaptureMode == right.CaptureMode &&
        left.Type?.Kind == right.Type?.Kind && left.Type?.Id == right.Type?.Id &&
        (left.Type is null) == (right.Type is null) && JsonEqual(left.Type?.Schema, right.Type?.Schema) &&
        left.CapturedAt.EqualsExact(right.CapturedAt) && JsonEqual(left.Payload, right.Payload) &&
        left.CaptureReason == right.CaptureReason && left.IsSensitive == right.IsSensitive &&
        RuntimeModelMetadata.AreEqual(left.Metadata, right.Metadata) && left.InputKey == right.InputKey &&
        left.EvaluationId == right.EvaluationId && left.Phase == right.Phase && left.Sequence == right.Sequence &&
        left.Failure == right.Failure && left.EvidenceId == right.EvidenceId;

    private static bool JsonEqual(JsonElement? left, JsonElement? right) =>
        left.HasValue == right.HasValue && (!left.HasValue || JsonElement.DeepEquals(left.Value, right!.Value));

    private static bool IsMalformed(Exception exception) =>
        exception is InvalidDataException or JsonException or RuntimeHistoryIntegrityException ||
        exception.InnerException is { } cause && IsMalformed(cause);

    private static bool IsUnavailable(Exception exception)
    {
        if (IsMalformed(exception) || RuntimeCheckpointCommitValidationException.IsCauseOf(exception)) return false;
        var unavailable = false;
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
        {
            if (cause is ArgumentException) return false;
            unavailable |= cause is IOException or DbException or TimeoutException or NotSupportedException or
                OperationCanceledException or RuntimeHistoryUnavailableException or RuntimeSchedulerWorkUnavailableException;
        }
        return unavailable;
    }
}
