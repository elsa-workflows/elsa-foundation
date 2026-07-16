namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Runtime-owned read model for committed inspection evidence for one concrete activity execution.
/// </summary>
public sealed record ActivityExecutionInspectionProjection(
    string ActivityExecutionId,
    string WorkflowExecutionId,
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion,
    ActivityExecutionStatus Status,
    string? SubStatus,
    long ExecutionSequence,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FirstCheckpointId,
    string? LastCheckpointId,
    DateTimeOffset? LastCommittedAt,
    ActivitySchedulingProvenance Provenance,
    IReadOnlyCollection<string> OutcomeNames,
    IReadOnlyCollection<ActivityExecutionBookmarkSummary> Bookmarks,
    IReadOnlyCollection<ActivityExecutionIncidentSummary> Incidents,
    IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> ValueSnapshots,
    IReadOnlyDictionary<string, string> Metadata,
    string? ExecutionScopeId = null,
    ActivityExecutionAttemptLineage? Attempt = null)
{
    public static ActivityExecutionInspectionProjection FromState(
        ActivityExecutionState state,
        string checkpointId,
        DateTimeOffset committedAt,
        IReadOnlyCollection<string>? outcomeNames = null,
        IReadOnlyCollection<ActivityExecutionBookmarkSummary>? bookmarks = null,
        IReadOnlyCollection<ActivityExecutionIncidentSummary>? incidents = null,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);

        var mergedMetadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var item in metadata ?? new Dictionary<string, string>())
            mergedMetadata[item.Key] = item.Value;

        return new ActivityExecutionInspectionProjection(
            ActivityExecutionId: state.Execution.ActivityExecutionId,
            WorkflowExecutionId: state.Execution.WorkflowExecutionId,
            ExecutableNodeId: state.Execution.ExecutableNodeId,
            AuthoredActivityId: state.Execution.AuthoredActivityId,
            ActivityType: state.Execution.ActivityType,
            ActivityTypeVersion: state.Execution.ActivityTypeVersion,
            Status: state.Status,
            SubStatus: state.SubStatus,
            ExecutionSequence: state.ExecutionSequence,
            ScheduledAt: state.ScheduledAt,
            StartedAt: state.StartedAt,
            CompletedAt: state.CompletedAt,
            FirstCheckpointId: checkpointId,
            LastCheckpointId: checkpointId,
            LastCommittedAt: committedAt,
            Provenance: state.Provenance,
            ExecutionScopeId: state.ExecutionScopeId ?? state.Provenance.ExecutionScopeId,
            Attempt: state.Attempt ?? state.Provenance.Attempt,
            OutcomeNames: outcomeNames ?? [],
            Bookmarks: bookmarks ?? [],
            Incidents: incidents ?? [],
            ValueSnapshots: MergeValueSnapshots([], valueSnapshots),
            Metadata: RuntimeModelMetadata.Snapshot(mergedMetadata));
    }

    public ActivityExecutionInspectionProjection Merge(
        ActivityExecutionState state,
        string checkpointId,
        DateTimeOffset committedAt,
        IReadOnlyCollection<string>? outcomeNames = null,
        IReadOnlyCollection<ActivityExecutionBookmarkSummary>? bookmarks = null,
        IReadOnlyCollection<ActivityExecutionIncidentSummary>? incidents = null,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var mergedMetadata = Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var item in state.Metadata)
            mergedMetadata[item.Key] = item.Value;
        foreach (var item in metadata ?? new Dictionary<string, string>())
            mergedMetadata[item.Key] = item.Value;

        return this with
        {
            Status = state.Status,
            SubStatus = state.SubStatus,
            ExecutionSequence = state.ExecutionSequence,
            ScheduledAt = state.ScheduledAt,
            StartedAt = state.StartedAt,
            CompletedAt = state.CompletedAt,
            LastCheckpointId = checkpointId,
            LastCommittedAt = committedAt,
            Provenance = state.Provenance,
            ExecutionScopeId = state.ExecutionScopeId ?? state.Provenance.ExecutionScopeId,
            Attempt = state.Attempt ?? state.Provenance.Attempt,
            OutcomeNames = outcomeNames ?? OutcomeNames,
            Bookmarks = MergeBy(Bookmarks, bookmarks, item => item.BookmarkId),
            Incidents = MergeBy(Incidents, incidents, item => item.IncidentId),
            ValueSnapshots = MergeValueSnapshots(ValueSnapshots, valueSnapshots),
            Metadata = RuntimeModelMetadata.Snapshot(mergedMetadata)
        };
    }

    private static IReadOnlyCollection<T> MergeBy<T>(
        IReadOnlyCollection<T> existing,
        IReadOnlyCollection<T>? additions,
        Func<T, string> keySelector)
    {
        if (additions is null || additions.Count == 0)
            return existing;

        var result = existing.ToDictionary(keySelector, StringComparer.Ordinal);
        foreach (var item in additions)
            result[keySelector(item)] = item;
        return result.Values.ToArray();
    }

    private static IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> MergeValueSnapshots(
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> existing,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? additions)
    {
        if (additions is null || additions.Count == 0)
            return existing;

        var result = existing.ToList();
        var evaluationIds = existing
            .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.EvaluationId))
            .Select(snapshot => snapshot.EvaluationId!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var addition in additions)
        {
            if (string.IsNullOrWhiteSpace(addition.EvaluationId))
            {
                result.Add(addition);
                continue;
            }

            if (!evaluationIds.Add(addition.EvaluationId))
                continue;

            if (addition.Subject != ActivityExecutionInspectionValueSubject.ActivityInput || string.IsNullOrWhiteSpace(addition.InputKey))
            {
                result.Add(addition);
                continue;
            }

            var nextSequence = result
                .Where(snapshot =>
                    snapshot.Subject == ActivityExecutionInspectionValueSubject.ActivityInput &&
                    StringComparer.Ordinal.Equals(snapshot.InputKey, addition.InputKey))
                .Select(snapshot => snapshot.Sequence ?? 0)
                .DefaultIfEmpty(0)
                .Max() + 1;
            result.Add(addition with { Sequence = nextSequence });
        }

        return result;
    }
}

/// <summary>
/// Lightweight runtime-owned summary for workflow-instance activity execution inspection lists.
/// </summary>
public sealed record ActivityExecutionInspectionSummaryProjection(
    string ActivityExecutionId,
    string WorkflowExecutionId,
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion,
    ActivityExecutionStatus Status,
    string? SubStatus,
    long ExecutionSequence,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FirstCheckpointId,
    string? LastCheckpointId,
    DateTimeOffset? LastCommittedAt,
    ActivitySchedulingProvenance Provenance,
    IReadOnlyCollection<string> OutcomeNames,
    int BookmarkCount,
    int IncidentCount,
    int ValueSnapshotCount,
    IReadOnlyDictionary<string, string> Metadata,
    string? ExecutionScopeId = null,
    ActivityExecutionAttemptLineage? Attempt = null)
{
    public static ActivityExecutionInspectionSummaryProjection FromProjection(ActivityExecutionInspectionProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        return new ActivityExecutionInspectionSummaryProjection(
            projection.ActivityExecutionId,
            projection.WorkflowExecutionId,
            projection.ExecutableNodeId,
            projection.AuthoredActivityId,
            projection.ActivityType,
            projection.ActivityTypeVersion,
            projection.Status,
            projection.SubStatus,
            projection.ExecutionSequence,
            projection.ScheduledAt,
            projection.StartedAt,
            projection.CompletedAt,
            projection.FirstCheckpointId,
            projection.LastCheckpointId,
            projection.LastCommittedAt,
            projection.Provenance,
            projection.OutcomeNames,
            projection.Bookmarks.Count,
            projection.Incidents.Count,
            projection.ValueSnapshots.Count,
            projection.Metadata,
            projection.ExecutionScopeId,
            projection.Attempt);
    }
}
