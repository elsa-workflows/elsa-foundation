namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Durable runtime wait correlation record used by wait-dependent post-commit intents.
/// </summary>
public sealed class RuntimeWaitRegistration
{
    public RuntimeWaitRegistration(
        string waitRegistrationId,
        string workflowExecutionId,
        string activityExecutionId,
        string? bookmarkId,
        string correlationId,
        string stimulusType,
        IReadOnlyDictionary<string, string> matchCriteria,
        RuntimeWaitRegistrationStatus status,
        DateTimeOffset registeredAt,
        DateTimeOffset? expiresAt,
        RuntimeWaitDependentIntentFailurePolicy failurePolicy,
        RuntimeEarlySignalPolicy earlySignalPolicy,
        string? dependsOnPostCommitIntentId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(waitRegistrationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);

        if (bookmarkId is not null && string.IsNullOrWhiteSpace(bookmarkId))
            throw new ArgumentException("Bookmark ID cannot be blank when provided.", nameof(bookmarkId));

        if (dependsOnPostCommitIntentId is not null && string.IsNullOrWhiteSpace(dependsOnPostCommitIntentId))
            throw new ArgumentException("Dependent post-commit intent ID cannot be blank when provided.", nameof(dependsOnPostCommitIntentId));

        if (failurePolicy == RuntimeWaitDependentIntentFailurePolicy.Compensate && dependsOnPostCommitIntentId is null)
            throw new ArgumentException("Compensation failure policy requires a dependent post-commit intent.", nameof(dependsOnPostCommitIntentId));

        if (expiresAt is not null && expiresAt <= registeredAt)
            throw new ArgumentException("Wait registration expiration must be after registration time.", nameof(expiresAt));

        WaitRegistrationId = waitRegistrationId;
        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        BookmarkId = bookmarkId;
        CorrelationId = correlationId;
        StimulusType = stimulusType;
        MatchCriteria = SnapshotMatchCriteria(matchCriteria, nameof(matchCriteria));
        Status = status;
        RegisteredAt = registeredAt;
        ExpiresAt = expiresAt;
        FailurePolicy = failurePolicy;
        EarlySignalPolicy = earlySignalPolicy;
        DependsOnPostCommitIntentId = dependsOnPostCommitIntentId;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string WaitRegistrationId { get; }
    public string WorkflowExecutionId { get; }
    public string ActivityExecutionId { get; }
    public string? BookmarkId { get; }
    public string CorrelationId { get; }
    public string StimulusType { get; }
    public IReadOnlyDictionary<string, string> MatchCriteria { get; }
    public RuntimeWaitRegistrationStatus Status { get; }
    public DateTimeOffset RegisteredAt { get; }
    public DateTimeOffset? ExpiresAt { get; }
    public RuntimeWaitDependentIntentFailurePolicy FailurePolicy { get; }
    public RuntimeEarlySignalPolicy EarlySignalPolicy { get; }
    public string? DependsOnPostCommitIntentId { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public bool IsMatchable => Status is RuntimeWaitRegistrationStatus.Reserved or RuntimeWaitRegistrationStatus.Active;

    public bool IsTerminal => Status is RuntimeWaitRegistrationStatus.Satisfied
        or RuntimeWaitRegistrationStatus.Cancelled
        or RuntimeWaitRegistrationStatus.Expired
        or RuntimeWaitRegistrationStatus.Faulted;

    private static IReadOnlyDictionary<string, string> SnapshotMatchCriteria(IReadOnlyDictionary<string, string> matchCriteria, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(matchCriteria);

        if (matchCriteria.Count == 0)
            throw new ArgumentException("Wait registration match criteria cannot be empty.", parameterName);

        if (matchCriteria.Any(pair => string.IsNullOrWhiteSpace(pair.Key)))
            throw new ArgumentException("Wait registration match criteria keys cannot be blank.", parameterName);

        if (matchCriteria.Any(pair => string.IsNullOrWhiteSpace(pair.Value)))
            throw new ArgumentException("Wait registration match criteria values cannot be blank.", parameterName);

        return RuntimeModelMetadata.Snapshot(matchCriteria);
    }
}

public enum RuntimeWaitRegistrationStatus
{
    Reserved,
    Active,
    Satisfied,
    Cancelled,
    Expired,
    Faulted
}

public enum RuntimeEarlySignalPolicy
{
    SatisfyReservedWait,
    BufferUntilIntentDelivered,
    RejectUnexpectedSignal
}
