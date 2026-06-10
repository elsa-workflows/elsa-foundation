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

        var postCommitIntents = commit.PostCommitIntents;
        var dispatchedIntentIds = new List<string>();

        for (var index = 0; index < postCommitIntents.Count; index++)
        {
            var intent = postCommitIntents[index];

            try
            {
                await postCommitIntentDispatcher.DispatchAsync(intent, cancellationToken);
                dispatchedIntentIds.Add(intent.IntentId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var undispatchedIntentIds = postCommitIntents
                    .Skip(index + 1)
                    .Select(undispatchedIntent => undispatchedIntent.IntentId)
                    .ToArray();

                throw new RuntimePostCommitIntentDispatchException(
                    commit.CommitId,
                    intent.IntentId,
                    dispatchedIntentIds.ToArray(),
                    undispatchedIntentIds,
                    exception);
            }
        }

        return decision;
    }
}
