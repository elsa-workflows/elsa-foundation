namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Complete immutable identity and expected mutable binding for one exact-set adoption member.</summary>
public sealed record RuntimeCheckpointPreparedAdoptionMember(
    string CommitId,
    string LedgerToken,
    long WorkflowCheckpointOrder,
    string CanonicalInputFingerprint,
    string CanonicalInputReference,
    RuntimeExecutionFence? OriginalFence,
    long OriginalOrderRevision,
    long OriginalContextRevision,
    RuntimeCheckpointRecoveryAuthority? RecoveryAuthority,
    RuntimeExecutionFence? ExpectedCurrentAuthorityFence,
    long ExpectedAuthorityRevision);
