using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeCheckpointCommitStore
{
    /// <summary>
    /// Commits one provider-facing checkpoint. An existing equivalent commit marker takes replay precedence. For a
    /// new commit, <see cref="RuntimeCheckpointCommit.ExpectedFence"/> must be validated atomically with every state
    /// change, outbox item, and create-only marker; stale fences expose no partial checkpoint outcome.
    /// </summary>
    ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default);
}
