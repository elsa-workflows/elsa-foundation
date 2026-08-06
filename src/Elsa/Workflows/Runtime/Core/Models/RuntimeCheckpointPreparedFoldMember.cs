namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>The explicit terminal outcome assigned to one durable prepared checkpoint by an atomic fold.</summary>
public enum RuntimeCheckpointPreparedDisposition
{
    Committed,
    Skipped,
    Failed
}

/// <summary>
/// One explicit ordered member of a provider-atomic fold. A failed member is a trusted generic terminal outcome;
/// transient, digest, authority, cancellation, and provider failures reject the batch instead of becoming Failed.
/// </summary>
public sealed record RuntimeCheckpointPreparedFoldMember(
    RuntimeCheckpointPreparationToken Token,
    RuntimeCheckpointPreparedDisposition Disposition,
    RuntimeExecutionFence? ExpectedCurrentAuthorityFence,
    long ExpectedAuthorityRevision,
    RuntimeCheckpointPreparedCommit? PreparedCommit = null,
    string? FailureCode = null,
    string? FailureMessage = null)
{
    public const int MaximumFailureCodeLength = 128;
    public const int MaximumFailureMessageLength = 1024;

    public string CommitId => Token.CommitId;
    public long WorkflowCheckpointOrder => Token.Provenance.WorkflowCheckpointOrder;

    /// <summary>Validates the explicit outcome shape without consulting provider state.</summary>
    /// <exception cref="ArgumentNullException">The durable token is null.</exception>
    /// <exception cref="ArgumentException">The explicit outcome shape or failure metadata is invalid.</exception>
    public RuntimeCheckpointPreparedFoldMember Validate()
    {
        ArgumentNullException.ThrowIfNull(Token);
        Token.Validate();
        if (!Enum.IsDefined(Disposition))
            throw new ArgumentOutOfRangeException(nameof(Disposition), Disposition, "The prepared-checkpoint disposition is invalid.");
        if (ExpectedAuthorityRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(ExpectedAuthorityRevision));

        if (Disposition == RuntimeCheckpointPreparedDisposition.Failed)
        {
            if (PreparedCommit is not null)
                throw new ArgumentException("A Failed prepared-checkpoint member cannot carry a rehydrated commit.", nameof(PreparedCommit));
            ArgumentException.ThrowIfNullOrWhiteSpace(FailureCode);
            if (FailureCode.Length > MaximumFailureCodeLength)
                throw new ArgumentOutOfRangeException(nameof(FailureCode), $"Failure code cannot exceed {MaximumFailureCodeLength} characters.");
            if (FailureMessage?.Length > MaximumFailureMessageLength)
                throw new ArgumentOutOfRangeException(nameof(FailureMessage), $"Failure message cannot exceed {MaximumFailureMessageLength} characters.");
            return this;
        }

        ArgumentNullException.ThrowIfNull(PreparedCommit);
        if (!Equals(Token, PreparedCommit.Token))
            throw new ArgumentException("The fold member token does not match its verified rehydrated commit.", nameof(PreparedCommit));
        if (FailureCode is not null || FailureMessage is not null)
            throw new ArgumentException("Only a Failed prepared-checkpoint member can carry failure metadata.", nameof(FailureCode));

        var isSkip = PreparedCommit.Decision.Mode == RuntimeCheckpointPersistenceMode.Skip;
        if (Disposition == RuntimeCheckpointPreparedDisposition.Skipped && !isSkip)
            throw new ArgumentException("A Skipped member requires a verified Skip persistence decision.", nameof(PreparedCommit));
        if (Disposition == RuntimeCheckpointPreparedDisposition.Committed && isSkip)
            throw new ArgumentException("A Committed member cannot carry a Skip persistence decision.", nameof(PreparedCommit));
        return this;
    }
}
