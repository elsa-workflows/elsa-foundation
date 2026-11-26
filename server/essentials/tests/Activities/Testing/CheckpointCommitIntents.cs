using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Testing;

/// <summary>The post-commit intents checkpoint commits carry, as a test reads them off the commits it recorded.</summary>
public static class CheckpointCommitIntents
{
    /// <summary>
    /// Every intent the <paramref name="commits"/> carry, once each. A commit carries an intent its own checkpoint
    /// decided, or, when a persistence mode folded earlier checkpoints into it, as an outbox item among its state
    /// changes. A commit can carry the same intent both ways, and a later commit can carry the same outbox item again
    /// with its delivery recorded.
    /// </summary>
    public static IReadOnlyList<RuntimePostCommitIntent> IntentsOf(params IEnumerable<RuntimeCheckpointCommit> commits) =>
        commits
            .SelectMany(commit => commit.PostCommitIntents.Concat(commit.StateChanges.PostCommitOutbox.Select(change => change.State.Intent)))
            .DistinctBy(intent => intent.IntentId)
            .ToList();
}
