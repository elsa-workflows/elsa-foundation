namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A verified, deterministically replayed logical checkpoint ready for provider finalization.</summary>
public sealed record RuntimeCheckpointPreparedCommit(
    RuntimeCheckpointPreparationToken Token,
    RuntimeCheckpointCommit Commit,
    RuntimeCheckpointPersistenceDecision Decision)
{
    /// <summary>
    /// Fingerprint captured by the deterministic replay boundary. Record <c>with</c> mutations retain this authority,
    /// allowing providers to reject a changed commit payload without learning anything about its enrichers.
    /// </summary>
    public string VerifiedCommitFingerprint { get; init; } = RuntimeCheckpointCommitFingerprint.Compute(Commit);
}
