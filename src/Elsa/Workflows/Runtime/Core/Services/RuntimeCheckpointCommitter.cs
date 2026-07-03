using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimeCheckpointCommitter
{
    private readonly IRuntimeCheckpointPersistencePolicy _persistencePolicy;
    private readonly IRuntimeCheckpointCommitStore _checkpointCommitStore;
    private readonly IRuntimeExecutionOwnershipContextAccessor? _ownershipContextAccessor;
    private readonly IRuntimeExecutionOwnershipService? _ownershipService;

    public RuntimeCheckpointCommitter(
        IRuntimeCheckpointPersistencePolicy persistencePolicy,
        IRuntimeCheckpointCommitStore checkpointCommitStore)
        : this(persistencePolicy, checkpointCommitStore, ownershipContextAccessor: null, ownershipService: null)
    {
    }

    public RuntimeCheckpointCommitter(
        IRuntimeCheckpointPersistencePolicy persistencePolicy,
        IRuntimeCheckpointCommitStore checkpointCommitStore,
        IRuntimeExecutionOwnershipContextAccessor? ownershipContextAccessor,
        IRuntimeExecutionOwnershipService? ownershipService)
    {
        ArgumentNullException.ThrowIfNull(persistencePolicy);
        ArgumentNullException.ThrowIfNull(checkpointCommitStore);

        _persistencePolicy = persistencePolicy;
        _checkpointCommitStore = checkpointCommitStore;
        _ownershipContextAccessor = ownershipContextAccessor;
        _ownershipService = ownershipService;
    }

    public async ValueTask<RuntimeCheckpointCommitResult> CommitAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);

        // Single-writer fencing (RT-2): if an ownership scope is active for this workflow execution, reject a commit
        // whose fencing token is not the current owner's before any state is persisted. Unwired (both null) or no
        // active scope leaves the commit path byte-for-byte unchanged.
        await EnsureOwnershipAsync(commit, cancellationToken);

        var decision = await _persistencePolicy.DecideAsync(commit.Checkpoint, cancellationToken);

        if (decision.Mode == RuntimeCheckpointPersistenceMode.Skip)
        {
            if (IsMandatoryCheckpoint(commit.Checkpoint))
                throw new InvalidOperationException(
                    $"Mandatory runtime checkpoint '{commit.Checkpoint.CheckpointId}' cannot be skipped by the persistence policy.");

            if (commit.PostCommitIntents.Count > 0)
                return RuntimeCheckpointCommitResult.Failure(
                    commit,
                    decision,
                    RuntimeCheckpointCommitFailureCodes.SkipHasPostCommitWork,
                    "Checkpoint persistence policy skipped a commit that contains pending post-commit work.");

            return RuntimeCheckpointCommitResult.Success(commit, decision, []);
        }

        // Fold post-commit intents into the applied change set so the provider persists them atomically with
        // the rest of the checkpoint through its uniform apply path, then verify the provider acknowledged them.
        var postCommitOutbox = RuntimePostCommitOutboxItems.CreatePendingChanges(commit);
        var commitToPersist = postCommitOutbox.Count == 0
            ? commit
            : commit with { StateChanges = commit.StateChanges.WithPostCommitOutbox(postCommitOutbox) };

        var storeResult = await _checkpointCommitStore.CommitAsync(commitToPersist, decision, cancellationToken);

        if (storeResult.PendingPostCommitWorkIds.Count != postCommitOutbox.Count)
            throw new InvalidOperationException(
                $"Checkpoint commit store persisted {storeResult.PendingPostCommitWorkIds.Count} post-commit outbox item(s) " +
                $"for commit '{commit.CommitId}' (workflow execution '{commit.WorkflowExecutionId}') but the checkpoint carried " +
                $"{postCommitOutbox.Count}. The continuation work would be silently dropped; the store must durably record every " +
                "post-commit outbox item it is handed.");

        return RuntimeCheckpointCommitResult.Success(commit, decision, storeResult.PendingPostCommitWorkIds);
    }

    private static bool IsMandatoryCheckpoint(RuntimeCheckpoint checkpoint) =>
        checkpoint.Metadata.TryGetValue(RuntimeMetadataKeys.CheckpointRequirement, out var requirement) &&
        StringComparer.Ordinal.Equals(requirement, RuntimeMetadataKeys.CheckpointRequirementMandatory);

    private async ValueTask EnsureOwnershipAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        if (_ownershipContextAccessor?.Current is not { } lease || _ownershipService is null)
            return;

        if (!StringComparer.Ordinal.Equals(lease.WorkflowExecutionId, commit.WorkflowExecutionId))
            return;

        await _ownershipService.EnsureCurrentAsync(commit.WorkflowExecutionId, lease.FencingToken, cancellationToken);
    }
}
