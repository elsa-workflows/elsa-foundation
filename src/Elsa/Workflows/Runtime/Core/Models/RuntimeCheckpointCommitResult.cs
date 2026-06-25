namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record RuntimeCheckpointCommitStoreResult(
    IReadOnlyCollection<string> PendingPostCommitWorkIds);

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
