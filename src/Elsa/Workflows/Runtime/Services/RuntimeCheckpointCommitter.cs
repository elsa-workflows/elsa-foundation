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
    private readonly IRuntimeExecutionOwnershipContextAccessor _ownershipContextAccessor;
    private readonly IWorkflowEngineTracer _tracer;
    private readonly IReadOnlyCollection<IRuntimeCheckpointCommitEnricher> _enrichers;
    private readonly IReadOnlyCollection<RuntimePostCommitIntentHandlerContribution> _intentHandlerContributions;
    private readonly IRuntimeConsumedSchedulerWorkClaimAccessor? _consumedWorkClaimAccessor;
    private readonly IRuntimeCoalescingSessionAccessor? _coalescingSessionAccessor;
    private readonly IRuntimeLiveDrainDeliveryAccessor? _liveDrainDeliveryAccessor;
    private readonly RuntimeInProcessHopFastPathOptions _inProcessHopFastPathOptions;

    /// <summary>
    /// Creates the committer. C1 (#1227): the four telescoping constructors collapsed into this single primary
    /// constructor — five required collaborators followed by optional collaborators that default to their no-op
    /// implementations. The ownership context accessor is <b>required by construction</b> so the W5 fence
    /// (<see cref="AttachExpectedFence"/>, which stamps the ambient lease onto the provider-facing envelope) can never
    /// be silently disabled by picking a narrower constructor: without it a commit made inside a fenced drain would
    /// carry no expected fence and a superseded writer would be admitted. The enricher and intent-handler contribution
    /// sets are required for the same reason — they carry commit enrichment and post-commit retry policy, so an empty
    /// set must be handed in deliberately rather than inferred from the constructor that happened to be selected.
    /// </summary>
    public RuntimeCheckpointCommitter(
        IRuntimeCheckpointPersistencePolicy persistencePolicy,
        IRuntimeCheckpointCommitStore checkpointCommitStore,
        IRuntimeExecutionOwnershipContextAccessor ownershipContextAccessor,
        IEnumerable<IRuntimeCheckpointCommitEnricher> enrichers,
        IEnumerable<RuntimePostCommitIntentHandlerContribution> intentHandlerContributions,
        IWorkflowEngineTracer? tracer = null,
        IRuntimeConsumedSchedulerWorkClaimAccessor? consumedWorkClaimAccessor = null,
        IRuntimeCoalescingSessionAccessor? coalescingSessionAccessor = null,
        IRuntimeLiveDrainDeliveryAccessor? liveDrainDeliveryAccessor = null,
        RuntimeInProcessHopFastPathOptions? inProcessHopFastPathOptions = null)
    {
        ArgumentNullException.ThrowIfNull(persistencePolicy);
        ArgumentNullException.ThrowIfNull(checkpointCommitStore);
        ArgumentNullException.ThrowIfNull(ownershipContextAccessor);
        ArgumentNullException.ThrowIfNull(enrichers);
        ArgumentNullException.ThrowIfNull(intentHandlerContributions);

        _persistencePolicy = persistencePolicy;
        _checkpointCommitStore = checkpointCommitStore;
        _ownershipContextAccessor = ownershipContextAccessor;
        _tracer = tracer ?? NullWorkflowEngineTracer.Instance;
        _enrichers = enrichers
            .Select((enricher, index) => new { Enricher = enricher, Index = index })
            .OrderBy(item => item.Enricher.Order)
            .ThenBy(item => item.Index)
            .Select(item => item.Enricher)
            .ToArray();
        _intentHandlerContributions = intentHandlerContributions.ToArray();
        _consumedWorkClaimAccessor = consumedWorkClaimAccessor;
        _coalescingSessionAccessor = coalescingSessionAccessor;
        _liveDrainDeliveryAccessor = liveDrainDeliveryAccessor;
        _inProcessHopFastPathOptions = inProcessHopFastPathOptions ?? new RuntimeInProcessHopFastPathOptions();
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
        var postCommitOutbox = RuntimePostCommitOutboxItems.CreatePendingChanges(commit, _intentHandlerContributions);

        // WU-1 / spec 105: fold the claimed scheduler work item's fence-checked delete into this same commit so the
        // drainer can skip its separate acknowledgement. Suppressed while a coalescing session owns the execution — the
        // session's overlay queue + AdvanceInnerQueueAsync stay authoritative on durable queue advance in that mode.
        var consumedWorkItems = ResolveConsumedWorkItems(commit);

        var stateChanges = commit.StateChanges;
        if (postCommitOutbox.Count > 0)
            stateChanges = stateChanges.WithPostCommitOutbox(postCommitOutbox);
        if (consumedWorkItems.Count > 0)
            stateChanges = stateChanges.WithConsumedSchedulerWorkItems(consumedWorkItems);
        var commitToPersist = ReferenceEquals(stateChanges, commit.StateChanges)
            ? commit
            : commit with { StateChanges = stateChanges };

        var storeResult = await _checkpointCommitStore.CommitAsync(commitToPersist, decision, cancellationToken);

        if (storeResult.PendingPostCommitWorkIds.Count != postCommitOutbox.Count)
            throw new InvalidOperationException(
                $"Checkpoint commit store persisted {storeResult.PendingPostCommitWorkIds.Count} post-commit outbox item(s) " +
                $"for commit '{commit.CommitId}' (workflow execution '{commit.WorkflowExecutionId}') but the checkpoint carried " +
                $"{postCommitOutbox.Count}. The continuation work would be silently dropped; the store must durably record every " +
                "post-commit outbox item it is handed.");

        if (storeResult.ConsumedSchedulerWorkItemIds.Count != consumedWorkItems.Count)
            throw new InvalidOperationException(
                $"Checkpoint commit store consumed {storeResult.ConsumedSchedulerWorkItemIds.Count} scheduler work item(s) " +
                $"for commit '{commit.CommitId}' (workflow execution '{commit.WorkflowExecutionId}') but the checkpoint carried " +
                $"{consumedWorkItems.Count}. The claimed work item's acknowledgement would be silently dropped; the store must " +
                "delete every consumed scheduler work item it is handed inside the same unit-of-work.");

        // The store durably deleted the claimed item(s), so the drainer must not issue a second acknowledgement.
        foreach (var workItemId in storeResult.ConsumedSchedulerWorkItemIds)
            _consumedWorkClaimAccessor?.MarkConsumedDurably(workItemId);

        // WU-3 / spec 109 (ADR 0031 follow-up (a)): the durable outbox item is now committed and authoritative. If a
        // live drain owns this execution's delivery, hand the still-materialized continuation work items to its
        // drain-scoped carrier so the scheduler intent dispatcher can enqueue them without re-deserializing the payload
        // we just persisted. Runs only after the commit succeeds, so a rolled-back commit publishes nothing.
        PublishInProcessHopWorkItems(commit);

        return RuntimeCheckpointCommitResult.Success(commit, decision, storeResult.PendingPostCommitWorkIds);
    }

    // Publishes each committed EnqueueSchedulerWork continuation's materialized work item onto the owning live drain's
    // in-process-hop carrier. Guards (all must hold): the fast path is enabled; a live-drain delivery scope owns THIS
    // execution (so the dispatcher will look here rather than deserialize); and no coalescing session owns the execution
    // (the coalescing overlay is authoritative on continuation delivery, mirroring spec 106 FR-003). When any guard
    // fails the carrier stays empty and delivery deserializes the durable payload — byte-identical result either way.
    private void PublishInProcessHopWorkItems(RuntimeCheckpointCommit commit)
    {
        if (!_inProcessHopFastPathOptions.Enabled)
            return;
        if (commit.PostCommitIntents.Count == 0)
            return;
        if (_liveDrainDeliveryAccessor?.Current is not { } scope || !scope.AppliesTo(commit.WorkflowExecutionId))
            return;
        if (_coalescingSessionAccessor?.Current is { } session && session.AppliesTo(commit.WorkflowExecutionId))
            return;

        foreach (var intent in commit.PostCommitIntents)
        {
            if (intent.MaterializedSchedulerWorkItem is { } workItem &&
                StringComparer.Ordinal.Equals(intent.Kind, RuntimePostCommitIntentKinds.EnqueueSchedulerWork))
                scope.PublishHopWorkItem(intent.IntentId, workItem);
        }
    }

    private static bool IsMandatoryCheckpoint(RuntimeCheckpoint checkpoint) =>
        checkpoint.Metadata.TryGetValue(RuntimeMetadataKeys.CheckpointRequirement, out var requirement) &&
        StringComparer.Ordinal.Equals(requirement, RuntimeMetadataKeys.CheckpointRequirementMandatory);

    private IReadOnlyCollection<ConsumedSchedulerWorkItem> ResolveConsumedWorkItems(RuntimeCheckpointCommit commit)
    {
        if (_consumedWorkClaimAccessor is not { PendingConsume: { } pending, WasConsumedDurably: false })
            return [];

        // Consume-once per dispatch: WasConsumedDurably guards against a multi-commit handler (e.g. InvokeActivity)
        // re-attaching an already-deleted item, which would fail the fence as claim-lost.
        if (!StringComparer.Ordinal.Equals(pending.WorkflowExecutionId, commit.WorkflowExecutionId))
            return [];

        // Coalesced mode owns durable queue advance through the overlay; do not fold a durable delete that would
        // double-delete against RuntimeCoalescingSession.AdvanceInnerQueueAsync.
        if (_coalescingSessionAccessor?.Current is { } session && session.AppliesTo(commit.WorkflowExecutionId))
            return [];

        return [pending];
    }

    private RuntimeCheckpointCommit AttachExpectedFence(RuntimeCheckpointCommit commit)
    {
        if (_ownershipContextAccessor.Current is not { } lease)
            return commit;

        if (!StringComparer.Ordinal.Equals(lease.WorkflowExecutionId, commit.WorkflowExecutionId))
            return commit;

        return commit with { ExpectedFence = lease.ToFence() };
    }
}
