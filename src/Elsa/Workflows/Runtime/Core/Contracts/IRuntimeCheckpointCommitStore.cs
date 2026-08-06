using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeCheckpointCommitStore
{
    /// <summary>
    /// Durably reserves replay-stable generic provenance before enrichment. The default is a compatibility adapter for
    /// providers whose durable implementation is introduced in a later work unit.
    /// </summary>
    ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(
        RuntimeCheckpointPrepareRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var commit = request.Commit;
        var fingerprint = new RuntimeCheckpointPreparationPayloadCodec().Encode(request).PayloadSha256;
        var token = new RuntimeCheckpointPreparationToken(
            commit.CommitId,
            $"compat:{commit.CommitId}",
            new RuntimeCheckpointProvenance(request.RequestedExecutionContext, 1),
            0,
            0,
            commit.ExpectedFence,
            fingerprint,
            commit.CommitId,
            request.InitialPersistenceMode,
            request.RecoveryAuthority).Validate();
        return ValueTask.FromResult(RuntimeCheckpointPreparationResult.Prepared(token));
    }

    /// <summary>Commits a previously prepared enriched checkpoint using the provider's final atomic boundary.</summary>
    ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedAsync(
        RuntimeCheckpointPreparationToken token,
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        token.Validate();
        if (decision.Mode == RuntimeCheckpointPersistenceMode.Skip)
            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]) { Status = RuntimeCheckpointCommitStoreStatus.Skipped });
        return CommitAsync(commit, decision, cancellationToken);
    }

    /// <summary>
    /// Commits one provider-facing checkpoint. An existing equivalent commit marker takes replay precedence. For a
    /// new commit, <see cref="RuntimeCheckpointCommit.ExpectedFence"/> must be validated atomically with every state
    /// change, outbox item, and create-only marker; stale fences expose no partial checkpoint outcome.
    /// </summary>
    ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default);
}
