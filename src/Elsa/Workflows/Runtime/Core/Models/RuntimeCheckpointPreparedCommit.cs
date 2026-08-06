namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A verified, deterministically replayed logical checkpoint ready for provider finalization.</summary>
public sealed record RuntimeCheckpointPreparedCommit(
    RuntimeCheckpointPreparationToken Token,
    RuntimeCheckpointCommit Commit,
    RuntimeCheckpointPersistenceDecision Decision)
{
    /// <summary>The provider-owned mutable authority binding observed for this exact preparation.</summary>
    public RuntimeExecutionFence? CurrentAuthorityFence { get; init; } = Token.ExpectedFence;

    /// <summary>The provider CAS revision paired with <see cref="CurrentAuthorityFence"/>.</summary>
    public long AuthorityRevision { get; init; } = 1;

    /// <summary>
    /// Fingerprint captured by the deterministic replay boundary. Record <c>with</c> mutations retain this authority,
    /// allowing providers to reject a changed commit payload without learning anything about its enrichers.
    /// </summary>
    public string VerifiedCommitFingerprint { get; init; } = RuntimeCheckpointCommitFingerprint.Compute(Commit);
}
