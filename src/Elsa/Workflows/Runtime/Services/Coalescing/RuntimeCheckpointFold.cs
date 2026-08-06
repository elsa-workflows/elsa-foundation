using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>Compatibility facade over the Runtime Core prepared-fold builder.</summary>
public static class RuntimeCheckpointFold
{
    /// <summary>Folds the durable effects of committed prepared checkpoints; Skip decisions contribute nothing.</summary>
    public static RuntimeCheckpointStateChangeSet FoldPrepared(
        IReadOnlyList<RuntimeCheckpointPreparedCommit> preparedCommits)
    {
        ArgumentNullException.ThrowIfNull(preparedCommits);
        if (preparedCommits.Count == 0)
            return RuntimeCheckpointPreparedFoldBuilder.Fold([]);
        var members = preparedCommits.Select(prepared => new RuntimeCheckpointPreparedFoldMember(
            prepared.Token,
            prepared.Decision.Mode == RuntimeCheckpointPersistenceMode.Skip
                ? RuntimeCheckpointPreparedDisposition.Skipped
                : RuntimeCheckpointPreparedDisposition.Committed,
            prepared.CurrentAuthorityFence,
            prepared.AuthorityRevision,
            prepared)).ToArray();
        var request = new RuntimeCheckpointPreparedFoldRequest(
            preparedCommits[0].Commit.WorkflowExecutionId,
            members,
            members[^1].WorkflowCheckpointOrder,
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            members[0].ExpectedCurrentAuthorityFence);
        return RuntimeCheckpointPreparedFoldBuilder.Build(request);
    }

    /// <summary>Folds ordered state changes without carrying post-commit outbox.</summary>
    public static RuntimeCheckpointStateChangeSet Fold(IReadOnlyList<RuntimeCheckpointStateChangeSet> changeSets) =>
        RuntimeCheckpointPreparedFoldBuilder.Fold(changeSets);
}
