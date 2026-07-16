using System.Diagnostics;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimeCheckpointCommitter
{
    private readonly IRuntimeCheckpointPersistencePolicy _persistencePolicy;
    private readonly IRuntimeCheckpointCommitStore _checkpointCommitStore;
    private readonly IRuntimeExecutionOwnershipContextAccessor? _ownershipContextAccessor;
    private readonly IWorkflowEngineTracer _tracer;
    private readonly IReadOnlyCollection<IRuntimeCheckpointCommitEnricher> _enrichers;

    public RuntimeCheckpointCommitter(
        IRuntimeCheckpointPersistencePolicy persistencePolicy,
        IRuntimeCheckpointCommitStore checkpointCommitStore)
        : this(persistencePolicy, checkpointCommitStore, ownershipContextAccessor: null)
    {
    }

    public RuntimeCheckpointCommitter(
        IRuntimeCheckpointPersistencePolicy persistencePolicy,
        IRuntimeCheckpointCommitStore checkpointCommitStore,
        IRuntimeExecutionOwnershipContextAccessor? ownershipContextAccessor,
        IWorkflowEngineTracer? tracer = null)
        : this(persistencePolicy, checkpointCommitStore, ownershipContextAccessor, tracer, [])
    {
    }

    public RuntimeCheckpointCommitter(
        IRuntimeCheckpointPersistencePolicy persistencePolicy,
        IRuntimeCheckpointCommitStore checkpointCommitStore,
        IRuntimeExecutionOwnershipContextAccessor? ownershipContextAccessor,
        IWorkflowEngineTracer? tracer,
        IEnumerable<IRuntimeCheckpointCommitEnricher> enrichers)
    {
        ArgumentNullException.ThrowIfNull(persistencePolicy);
        ArgumentNullException.ThrowIfNull(checkpointCommitStore);
        ArgumentNullException.ThrowIfNull(enrichers);

        _persistencePolicy = persistencePolicy;
        _checkpointCommitStore = checkpointCommitStore;
        _ownershipContextAccessor = ownershipContextAccessor;
        _tracer = tracer ?? NullWorkflowEngineTracer.Instance;
        _enrichers = enrichers.ToArray();
    }

    public async ValueTask<RuntimeCheckpointCommitResult> CommitAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);

        foreach (var enricher in _enrichers)
            commit = await enricher.EnrichAsync(commit, cancellationToken);

        // MS-9: the checkpoint-commit span wraps the fenced commit path. StartCheckpointCommit returns null when tracing
        // is inactive, so no allocation and no semantic change; when active it only introduces Activity.Current (trace
        // context, not service location). No new awaits are inserted between the fenced awaits below — attribute writes
        // are synchronous and happen after their source values are already computed.
        using var activity = _tracer.StartCheckpointCommit(commit);

        // Carry the ambient ownership identity into the provider-facing envelope. Durable stores decide replay first,
        // then validate this fence inside the same atomic decision as state, outbox, and the commit marker.
        commit = AttachExpectedFence(commit);

        var decision = await _persistencePolicy.DecideAsync(commit.Checkpoint, cancellationToken);

        if (activity is not null)
        {
            activity.SetTag(WorkflowEngineTelemetry.CheckpointPersistenceModeTag, decision.Mode.ToString());
            activity.SetTag(WorkflowEngineTelemetry.CheckpointMandatoryTag, IsMandatoryCheckpoint(commit.Checkpoint));
            activity.SetTag(WorkflowEngineTelemetry.CheckpointPostCommitIntentsTag, commit.PostCommitIntents.Count);
        }

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

    private RuntimeCheckpointCommit AttachExpectedFence(RuntimeCheckpointCommit commit)
    {
        if (_ownershipContextAccessor?.Current is not { } lease)
            return commit;

        if (!StringComparer.Ordinal.Equals(lease.WorkflowExecutionId, commit.WorkflowExecutionId))
            return commit;

        return commit with { ExpectedFence = lease.ToFence() };
    }
}
