namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record RuntimeCheckpointCommitStoreResult(
    IReadOnlyCollection<string> PendingPostCommitWorkIds)
{
    public RuntimeCheckpointCommitStoreStatus Status { get; init; } = RuntimeCheckpointCommitStoreStatus.Committed;
    public string? CommitFingerprint { get; init; }
    /// <summary>Bounded generic terminal failure identity when <see cref="Status"/> is <see cref="RuntimeCheckpointCommitStoreStatus.Failed"/>.</summary>
    public string? FailureCode { get; init; }
    /// <summary>Optional bounded generic terminal failure detail.</summary>
    public string? FailureMessage { get; init; }
    /// <summary>
    /// The claimed scheduler work-item ids the store durably deleted inside this commit's unit-of-work (WU-1 / spec 105),
    /// or the ids recorded on the replay marker when the commit was a redelivery. The committer asserts this matches the
    /// consume-changes it folded and, when non-empty, marks the ambient claim consumed so the drainer skips the separate
    /// acknowledgement. Empty on the legacy/coalesced paths.
    /// </summary>
    public IReadOnlyCollection<string> ConsumedSchedulerWorkItemIds { get; init; } = [];

    public bool Equals(RuntimeCheckpointCommitStoreResult? other) =>
        other is not null &&
        Status == other.Status &&
        StringComparer.Ordinal.Equals(CommitFingerprint, other.CommitFingerprint) &&
        StringComparer.Ordinal.Equals(FailureCode, other.FailureCode) &&
        StringComparer.Ordinal.Equals(FailureMessage, other.FailureMessage) &&
        PendingPostCommitWorkIds.SequenceEqual(other.PendingPostCommitWorkIds, StringComparer.Ordinal) &&
        ConsumedSchedulerWorkItemIds.SequenceEqual(other.ConsumedSchedulerWorkItemIds, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Status);
        hash.Add(CommitFingerprint, StringComparer.Ordinal);
        hash.Add(FailureCode, StringComparer.Ordinal);
        hash.Add(FailureMessage, StringComparer.Ordinal);
        foreach (var id in PendingPostCommitWorkIds)
            hash.Add(id, StringComparer.Ordinal);
        foreach (var id in ConsumedSchedulerWorkItemIds)
            hash.Add(id, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

public sealed class RuntimeCheckpointCommitResult
{
    private RuntimeCheckpointCommitResult(
        bool succeeded,
        string commitId,
        string workflowExecutionId,
        RuntimeCheckpointPersistenceDecision persistenceDecision,
        IReadOnlyCollection<string> pendingPostCommitWorkIds,
        string? failureCode,
        string? failureMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(persistenceDecision);

        if (succeeded && failureCode is not null)
            throw new ArgumentException("A successful checkpoint commit result cannot carry a failure code.", nameof(failureCode));

        if (!succeeded)
            ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);

        Succeeded = succeeded;
        CommitId = commitId;
        WorkflowExecutionId = workflowExecutionId;
        PersistenceDecision = persistenceDecision;
        PendingPostCommitWorkIds = pendingPostCommitWorkIds.ToArray();
        FailureCode = failureCode;
        FailureMessage = failureMessage;
    }

    public bool Succeeded { get; }
    public string CommitId { get; }
    public string WorkflowExecutionId { get; }
    public RuntimeCheckpointPersistenceDecision PersistenceDecision { get; }
    public IReadOnlyCollection<string> PendingPostCommitWorkIds { get; }
    public string? FailureCode { get; }
    public string? FailureMessage { get; }

    public static RuntimeCheckpointCommitResult Success(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision persistenceDecision,
        IReadOnlyCollection<string> pendingPostCommitWorkIds) =>
        new(
            succeeded: true,
            commit.CommitId,
            commit.WorkflowExecutionId,
            persistenceDecision,
            pendingPostCommitWorkIds,
            failureCode: null,
            failureMessage: null);

    public static RuntimeCheckpointCommitResult Failure(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision persistenceDecision,
        string failureCode,
        string failureMessage) =>
        new(
            succeeded: false,
            commit.CommitId,
            commit.WorkflowExecutionId,
            persistenceDecision,
            [],
            failureCode,
            failureMessage);
}

public static class RuntimeCheckpointCommitFailureCodes
{
    public const string SkipHasPostCommitWork = "runtime.checkpoint.skip_has_post_commit_work";
}
