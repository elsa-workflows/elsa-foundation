namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Closed semantic facts supplied by the owner of an actual operation, before checkpoint coalescing.</summary>
public enum RuntimeHistorySourceFactKind
{
    ActivityContinuationStarted,
    ActivityContinuationSuspended,
    ActivityContinuationCompleted,
    ActivityContinuationFaulted,
    ActivityContinuationCancelled,
    ChildWorkflowDispatchRequested,
    ChildWorkflowSettled,
    StimulusPublicationCommitted,
    ChildControlAccepted,
    ChildControlApplied,
    IncidentResolutionApplied,
    ActivityRetryScheduled,
    RecoveryRequested,
    ControlApplied,
    AlterationApplied,
    CompensationRegistered,
    CompensationStarted,
    CompensationHandlerScheduled,
    CompensationHandlerCompleted,
    CompensationHandlerFaulted,
    CompensationConfirmed,
    CompensationResolved,
    CompensationCompleted,
    TimerCancellationRequested,
    ActivityContinuationReturned,
    IncidentCreated,
    IncidentResolved,
    IncidentStrategyEvaluated,
    IncidentProjectionReconciled,
    IncidentSuppressed,
    IncidentDecisionApplied,
    CompensationAbandoned,
    DurableStateRecorded
}

/// <summary>Safe source data, never a payload, exception, capability or inferred snapshot transition.</summary>
public sealed record RuntimeHistorySourceFact
{
    public RuntimeHistorySourceFact(RuntimeHistorySourceFactKind kind, string subjectId, DateTimeOffset occurredAt,
        string? activityExecutionId = null, string? attemptId = null, RuntimeHistoryCausation? causation = null,
        string? relatedWorkflowExecutionId = null, string? relatedActivityExecutionId = null, string? timerId = null,
        string? incidentId = null, string? compensationEntryId = null, string? reasonCode = null,
        IReadOnlyList<string>? outcomeNames = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        if (new[] { subjectId, activityExecutionId, attemptId, relatedWorkflowExecutionId, relatedActivityExecutionId, timerId, incidentId, compensationEntryId }
            .Any(value => value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > 1024)) ||
            reasonCode?.Length > 128)
            throw new ArgumentException("History source facts require bounded semantic identities.");
        // Native outcomes remain exact source data. The public history projection applies
        // its metadata budget without imposing another limit on a valid native completion.
        var outcomes = (outcomeNames ?? []).ToArray();
        foreach (var window in outcomes.Chunk(128))
            if (window.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("History source outcomes require nonblank names.", nameof(outcomeNames));
        Kind = kind; SubjectId = subjectId; OccurredAt = occurredAt; ActivityExecutionId = activityExecutionId;
        AttemptId = attemptId; Causation = causation; RelatedWorkflowExecutionId = relatedWorkflowExecutionId;
        RelatedActivityExecutionId = relatedActivityExecutionId; TimerId = timerId; IncidentId = incidentId;
        CompensationEntryId = compensationEntryId; ReasonCode = reasonCode;
        OutcomeNames = Array.AsReadOnly(outcomes);
    }
    public RuntimeHistorySourceFactKind Kind { get; }
    public string SubjectId { get; }
    public DateTimeOffset OccurredAt { get; }
    public string? ActivityExecutionId { get; }
    public string? AttemptId { get; }
    public RuntimeHistoryCausation? Causation { get; }
    public string? RelatedWorkflowExecutionId { get; }
    public string? RelatedActivityExecutionId { get; }
    public string? TimerId { get; }
    public string? IncidentId { get; }
    public string? CompensationEntryId { get; }
    public string? ReasonCode { get; }
    public IReadOnlyList<string> OutcomeNames { get; }
}
