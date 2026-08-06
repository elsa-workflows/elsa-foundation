namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Digest-verifiable canonical input and provider authority for one durable Prepared ledger entry.</summary>
public sealed record RuntimeCheckpointPreparedReservation(
    RuntimeCheckpointPreparationToken Token,
    RuntimeCheckpointPreparationPayloadEnvelope CanonicalEnvelope,
    RuntimeLogicalCheckpointLedgerStatus Status,
    RuntimeExecutionFence? CurrentAuthorityFence = null,
    long AuthorityRevision = 1)
{
    public string CommitId => Token.CommitId;
    public RuntimeCheckpointProvenance Provenance => Token.Provenance;
    public long ExpectedOrderRevision => Token.ExpectedOrderRevision;
    public long ExpectedContextRevision => Token.ExpectedContextRevision;
    public RuntimeExecutionFence? ExpectedFence => Token.ExpectedFence;
    public RuntimeCheckpointPersistenceMode CandidatePersistenceMode => Token.InitialPersistenceMode;
    public string InputFingerprint => Token.CanonicalInputFingerprint;
    public RuntimeCheckpointRecoveryAuthority? RecoveryAuthority => Token.RecoveryAuthority;
    public RuntimeCheckpointAuthorityBinding AuthorityBinding =>
        new RuntimeCheckpointAuthorityBinding(CurrentAuthorityFence, AuthorityRevision).Validate();
}
