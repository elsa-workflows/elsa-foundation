namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Single-use logical-ledger reservation used by the provider's final checkpoint CAS.</summary>
public sealed record RuntimeCheckpointPreparationToken(
    string CommitId,
    string LedgerToken,
    RuntimeCheckpointProvenance Provenance,
    long ExpectedOrderRevision,
    long ExpectedContextRevision,
    RuntimeExecutionFence? ExpectedFence,
    string CanonicalInputFingerprint,
    string CanonicalInputReference,
    RuntimeCheckpointPersistenceMode InitialPersistenceMode,
    RuntimeCheckpointRecoveryAuthority? RecoveryAuthority = null)
{
    public RuntimeCheckpointPreparationToken Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CommitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(LedgerToken);
        ArgumentNullException.ThrowIfNull(Provenance);
        ArgumentNullException.ThrowIfNull(Provenance.ExecutionContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(CanonicalInputFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(CanonicalInputReference);
        if (ExpectedOrderRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(ExpectedOrderRevision));
        if (ExpectedContextRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(ExpectedContextRevision));
        return this;
    }
}

public sealed record RuntimeCheckpointPrepareRequest(
    RuntimeCheckpointCommit Commit,
    string SourceIdentity,
    string OperationIdentity,
    RuntimeExecutionContextSnapshot RequestedExecutionContext,
    RuntimeCheckpointPersistenceMode InitialPersistenceMode = RuntimeCheckpointPersistenceMode.Immediate,
    RuntimeCheckpointRecoveryAuthority? RecoveryAuthority = null)
{
    public static RuntimeCheckpointPrepareRequest From(RuntimeCheckpointCommit commit) =>
        new(commit, commit.Checkpoint.Name, commit.Checkpoint.CheckpointId, RuntimeExecutionContextSnapshot.Empty);
}
