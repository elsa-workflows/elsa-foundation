namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Availability of a recorded occurrence segment, independently of workflow status.</summary>
public enum RuntimeHistoryOccurrenceAvailability { Available, Partial, Unavailable, Invalidated, TargetUnavailable }

/// <summary>A bounded read. Position is zero based within the admitted retained segment, not elapsed time.</summary>
public sealed class RuntimeHistoryOccurrenceQuery
{
    public RuntimeHistoryOccurrenceQuery(string workflowExecutionId, int limit = 50, string? sequenceToken = null,
        long? position = null, string? anchorActivityExecutionId = null, string? activityExecutionId = null,
        bool includeDescendants = false, string? accessBinding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
        if (position is not null && anchorActivityExecutionId is not null)
            throw new ArgumentException("Specify an ordinal or an exact occurrence anchor, not both.");
        if (anchorActivityExecutionId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(anchorActivityExecutionId);
        if (activityExecutionId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        if (includeDescendants && activityExecutionId is null)
            throw new ArgumentException("Descendant scope requires an exact activity execution.", nameof(includeDescendants));
        WorkflowExecutionId = workflowExecutionId; Limit = limit; SequenceToken = sequenceToken;
        Position = position; AnchorActivityExecutionId = anchorActivityExecutionId;
        ActivityExecutionId = activityExecutionId; IncludeDescendants = includeDescendants; AccessBinding = accessBinding;
    }

    public string WorkflowExecutionId { get; }
    public int Limit { get; }
    public string? SequenceToken { get; }
    public long? Position { get; }
    public string? AnchorActivityExecutionId { get; }
    public string? ActivityExecutionId { get; }
    public bool IncludeDescendants { get; }
    public string? AccessBinding { get; }
}

/// <summary>
/// One exact occurrence at its first eligible committed observation. That observation can be a
/// completion or continuation; it does not fabricate a captured start or a causal playback edge.
/// </summary>
public sealed class RuntimeHistoryOccurrence(long position, RuntimeHistoryEvent observation,
    string? previousActivityExecutionId = null, string? nextActivityExecutionId = null)
{
    public long Position { get; } = position;
    public RuntimeHistoryEvent Observation { get; } = observation;
    public string? PreviousActivityExecutionId { get; } = previousActivityExecutionId;
    public string? NextActivityExecutionId { get; } = nextActivityExecutionId;

    /// <summary>Closed version-one occurrence vocabulary shared by history provider projections.</summary>
    public static bool IsEligible(RuntimeHistoryEvent observation) => observation is
        { SchemaVersion: 1, Authority: RuntimeHistoryAuthority.CommittedTransition,
          Category: RuntimeHistoryEventCategory.ActivityLifecycle, ActivityExecutionId.Length: > 0 } &&
        IsEligibleKind(observation.Name);

    /// <summary>Supported version-one committed occurrence observation names.</summary>
    public static bool IsEligibleKind(string name) => name is "ActivityScheduled" or "ActivityStarted" or "ActivityAttemptClaimed" or
            "ActivityResumeClaimed" or "ActivityResumeRetried" or "ActivityCompleted" or "ActivitySuspended" or
            "ActivityCancelled" or "ActivityRecovered" or "ActivityFaulted" or "ActivityContinuationStarted" or
            "ActivityContinuationSuspended" or "ActivityContinuationCompleted" or "ActivityContinuationFaulted" or
            "ActivityContinuationCancelled" or "ActivityContinuationReturned" or "ActivityRetryScheduled";
}

/// <summary>
/// One coherent provider read. Counts and positions describe only the frozen retained segment.
/// A committed upper bound excludes later materialization; it is not a claim of source closure.
/// Reason is a safe closed code, not provider exception text. Unavailable windows have no token/rows.
/// </summary>
public sealed class RuntimeHistoryOccurrencePage(RuntimeHistoryHead? head,
    RuntimeHistoryOccurrenceAvailability availability, string? reason, long upperSequence,
    long retainedAfterSequence, long? totalCount, long? selectedPosition,
    IReadOnlyList<RuntimeHistoryOccurrence> items, string? sequenceToken, bool hasNewerHistory)
{
    public RuntimeHistoryHead? Head { get; } = head;
    public RuntimeHistoryOccurrenceAvailability Availability { get; } = availability;
    public string? Reason { get; } = reason;
    public long UpperSequence { get; } = upperSequence;
    public long RetainedAfterSequence { get; } = retainedAfterSequence;
    public long? TotalCount { get; } = totalCount;
    public long? SelectedPosition { get; } = selectedPosition;
    public IReadOnlyList<RuntimeHistoryOccurrence> Items { get; } = items;
    public string? SequenceToken { get; } = sequenceToken;
    public bool HasNewerHistory { get; } = hasNewerHistory;
}
