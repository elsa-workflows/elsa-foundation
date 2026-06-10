using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimeCheckpointCommitter(
    IRuntimeCheckpointPersistencePolicy persistencePolicy,
    IRuntimeCheckpointWriter checkpointWriter,
    IRuntimePostCommitIntentDispatcher postCommitIntentDispatcher)
{
    public async ValueTask<RuntimeCheckpointPersistenceDecision> CommitAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken = default)
    {
        var decision = await persistencePolicy.DecideAsync(commit.Checkpoint, cancellationToken);

        if (decision.Mode == RuntimeCheckpointPersistenceMode.Skip)
            return decision;

        await checkpointWriter.WriteAsync(commit, decision, cancellationToken);

        foreach (var intent in commit.PostCommitIntents)
            await postCommitIntentDispatcher.DispatchAsync(intent, cancellationToken);

        return decision;
    }
}
