using System.Diagnostics;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimeCheckpointCommitter
{
    private readonly IRuntimeCheckpointCommitStore _checkpointCommitStore;
    private readonly IRuntimeCheckpointPreparationReplayer _preparationReplayer;
    private readonly RuntimeCheckpointPreparationPayloadCodec _preparationPayloadCodec = new();
    private readonly IRuntimeExecutionOwnershipContextAccessor? _ownershipContextAccessor;
    private readonly IWorkflowEngineTracer _tracer;
    private readonly IRuntimeConsumedSchedulerWorkClaimAccessor? _consumedWorkClaimAccessor;
    private readonly IRuntimeCoalescingSessionAccessor? _coalescingSessionAccessor;
    private readonly IRuntimeLiveDrainDeliveryAccessor? _liveDrainDeliveryAccessor;
    private readonly RuntimeInProcessHopFastPathOptions _inProcessHopFastPathOptions;
    private readonly IRuntimeCheckpointRecoveryAuthorityAccessor? _recoveryAuthorityAccessor;

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
        : this(persistencePolicy, checkpointCommitStore, ownershipContextAccessor, tracer, enrichers, [])
    {
    }

    public RuntimeCheckpointCommitter(
        IRuntimeCheckpointPersistencePolicy persistencePolicy,
        IRuntimeCheckpointCommitStore checkpointCommitStore,
        IRuntimeExecutionOwnershipContextAccessor? ownershipContextAccessor,
        IWorkflowEngineTracer? tracer,
        IEnumerable<IRuntimeCheckpointCommitEnricher> enrichers,
        IEnumerable<RuntimePostCommitIntentHandlerContribution> intentHandlerContributions,
        IRuntimeConsumedSchedulerWorkClaimAccessor? consumedWorkClaimAccessor = null,
        IRuntimeCoalescingSessionAccessor? coalescingSessionAccessor = null,
        IRuntimeLiveDrainDeliveryAccessor? liveDrainDeliveryAccessor = null,
        RuntimeInProcessHopFastPathOptions? inProcessHopFastPathOptions = null,
        IRuntimeCheckpointPreparationReplayer? preparationReplayer = null,
        IRuntimeCheckpointRecoveryAuthorityAccessor? recoveryAuthorityAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(persistencePolicy);
        ArgumentNullException.ThrowIfNull(checkpointCommitStore);
        ArgumentNullException.ThrowIfNull(enrichers);
        ArgumentNullException.ThrowIfNull(intentHandlerContributions);

        _checkpointCommitStore = checkpointCommitStore;
        _ownershipContextAccessor = ownershipContextAccessor;
        _tracer = tracer ?? NullWorkflowEngineTracer.Instance;
        var orderedEnrichers = enrichers
            .Select((enricher, index) => new { Enricher = enricher, Index = index })
            .OrderBy(item => item.Enricher.Order)
            .ThenBy(item => item.Index)
            .Select(item => item.Enricher)
            .ToArray();
        var contributions = intentHandlerContributions.ToArray();
        _preparationReplayer = preparationReplayer ??
            new RuntimeCheckpointPreparationReplayer(persistencePolicy, orderedEnrichers, contributions);
        _consumedWorkClaimAccessor = consumedWorkClaimAccessor;
        _coalescingSessionAccessor = coalescingSessionAccessor;
        _liveDrainDeliveryAccessor = liveDrainDeliveryAccessor;
        _inProcessHopFastPathOptions = inProcessHopFastPathOptions ?? new RuntimeInProcessHopFastPathOptions();
        _recoveryAuthorityAccessor = recoveryAuthorityAccessor;
    }

    public async ValueTask<RuntimeCheckpointCommitResult> CommitAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);

        // Ownership and the raw logical proposal are captured before enrichment. The store must durably reserve
        // replay-stable generic provenance before any enricher observes the checkpoint.
        commit = AttachExpectedFence(commit);
        var inProcessHopCandidates = CaptureInProcessHopCandidates(commit);
        var prepareRequest = RuntimeCheckpointPrepareRequest.From(commit) with
        {
            RecoveryAuthority = _recoveryAuthorityAccessor?.Current
        };
        var preparation = await _checkpointCommitStore.PrepareAsync(prepareRequest, cancellationToken);
        if (preparation.Status == RuntimeCheckpointPreparationStatus.Conflict)
            throw new RuntimeCheckpointReplayConflictException(commit.CommitId);
        if (preparation.Status == RuntimeCheckpointPreparationStatus.OwnershipLost)
            throw CreateOwnershipLostException(commit, "preparation");
        var preparationToken = preparation.Token
            ?? throw new InvalidOperationException($"Runtime checkpoint preparation for '{commit.CommitId}' returned no token.");
        var canonicalEnvelope = _preparationPayloadCodec.Encode(
            prepareRequest with { InitialPersistenceMode = preparationToken.InitialPersistenceMode });
        var preparedCommit = await _preparationReplayer.RehydrateAsync(
            new RuntimeCheckpointPreparedReservation(
                preparationToken,
                canonicalEnvelope,
                RuntimeLogicalCheckpointLedgerStatus.Prepared),
            cancellationToken);
        commit = preparedCommit.Commit;

        // MS-9: the checkpoint-commit span wraps the fenced commit path. StartCheckpointCommit returns null when tracing
        // is inactive, so no allocation and no semantic change; when active it only introduces Activity.Current (trace
        // context, not service location). No new awaits are inserted between the fenced awaits below — attribute writes
        // are synchronous and happen after their source values are already computed.
        using var activity = _tracer.StartCheckpointCommit(commit);

        // The shared preparation replayer has already folded deterministic post-commit intent work.
        var postCommitOutbox = commit.StateChanges.PostCommitOutbox;

        // WU-1 / spec 105: fold the claimed scheduler work item's fence-checked delete into this same commit so the
        // drainer can skip its separate acknowledgement. Suppressed while a coalescing session owns the execution — the
        // session's overlay queue + AdvanceInnerQueueAsync stay authoritative on durable queue advance in that mode.
        var consumedWorkItems = ResolveConsumedWorkItems(commit);

        var stateChanges = commit.StateChanges;
        if (consumedWorkItems.Count > 0)
            stateChanges = stateChanges.WithConsumedSchedulerWorkItems(consumedWorkItems);
        var commitToPersist = ReferenceEquals(stateChanges, commit.StateChanges)
            ? commit
            : commit with { StateChanges = stateChanges };

        // Policy was selected after deterministic enrichment/folding by the shared live/recovery replayer.
        var decision = preparedCommit.Decision;

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

            var skipped = await _checkpointCommitStore.CommitPreparedAsync(preparationToken, commitToPersist, decision, cancellationToken);
            if (skipped.Status is RuntimeCheckpointCommitStoreStatus.Conflict or RuntimeCheckpointCommitStoreStatus.OwnershipLost)
                throw new InvalidOperationException($"Runtime checkpoint '{commit.CommitId}' could not finalize its skipped preparation.");
            if (commitToPersist.StateChanges.PostCommitOutbox.Count > 0)
                return RuntimeCheckpointCommitResult.Failure(
                    commit,
                    decision,
                    RuntimeCheckpointCommitFailureCodes.SkipHasPostCommitWork,
                    "Checkpoint persistence policy skipped a commit that contains pending post-commit work.");

            return RuntimeCheckpointCommitResult.Success(commit, decision, []);
        }

        var storeResult = await _checkpointCommitStore.CommitPreparedAsync(preparationToken, commitToPersist, decision, cancellationToken);
        if (storeResult.Status == RuntimeCheckpointCommitStoreStatus.Conflict)
            throw new RuntimeCheckpointReplayConflictException(commit.CommitId);
        if (storeResult.Status == RuntimeCheckpointCommitStoreStatus.OwnershipLost)
            throw CreateOwnershipLostException(commit, "commit");

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

        // WU-3 / spec 109 (ADR 0031 follow-up (a)): a conduit exists only on the raw pre-prepare proposal. The
        // rehydrated final commit deliberately retains the durable payload and strips that in-memory object. Publish a
        // candidate only after a new durable commit and only when it exactly matches the final durable intent.
        if (storeResult.Status == RuntimeCheckpointCommitStoreStatus.Committed)
            PublishInProcessHopWorkItems(commit, inProcessHopCandidates);

        return RuntimeCheckpointCommitResult.Success(commit, decision, storeResult.PendingPostCommitWorkIds);
    }

    // Captures only the non-durable conduits from the raw proposal. They remain local to this call: the rehydrated
    // durable intent and its payload stay authoritative and are never modified to restore a conduit.
    private IReadOnlyCollection<InProcessHopCandidate> CaptureInProcessHopCandidates(RuntimeCheckpointCommit commit)
    {
        if (!_inProcessHopFastPathOptions.Enabled || commit.PostCommitIntents.Count == 0)
            return [];
        if (_liveDrainDeliveryAccessor?.Current is not { } scope || !scope.AppliesTo(commit.WorkflowExecutionId))
            return [];
        if (_coalescingSessionAccessor?.Current is { } session && session.AppliesTo(commit.WorkflowExecutionId))
            return [];

        List<InProcessHopCandidate>? candidates = null;
        foreach (var intent in commit.PostCommitIntents)
        {
            if (!StringComparer.Ordinal.Equals(intent.Kind, RuntimePostCommitIntentKinds.EnqueueSchedulerWork) ||
                intent.MaterializedSchedulerWorkItem is not { } workItem)
            {
                continue;
            }

            (candidates ??= []).Add(new InProcessHopCandidate(intent, workItem));
        }

        return candidates ?? [];
    }

    // Publishes each new committed EnqueueSchedulerWork candidate onto the owning live drain's in-process-hop carrier.
    // Guards (all must hold): the fast path is enabled; a live-drain delivery scope owns THIS execution (so the
    // dispatcher will look here rather than deserialize); no coalescing session owns the execution; and exactly one
    // final durable intent has the candidate's identity/association and structural payload. When any guard fails the
    // carrier stays empty and delivery deserializes the authoritative durable payload.
    private void PublishInProcessHopWorkItems(
        RuntimeCheckpointCommit commit,
        IReadOnlyCollection<InProcessHopCandidate> candidates)
    {
        if (!_inProcessHopFastPathOptions.Enabled)
            return;
        if (candidates.Count == 0)
            return;
        if (_liveDrainDeliveryAccessor?.Current is not { } scope || !scope.AppliesTo(commit.WorkflowExecutionId))
            return;
        if (_coalescingSessionAccessor?.Current is { } session && session.AppliesTo(commit.WorkflowExecutionId))
            return;

        foreach (var candidate in candidates)
        {
            var durableIntents = commit.PostCommitIntents
                .Where(intent => HasSameDurableIdentityAndAssociation(candidate.Intent, intent))
                .ToArray();
            if (durableIntents.Length != 1)
                continue;

            var durableIntent = durableIntents[0];
            if (candidate.Intent.Payload is not { } candidatePayload ||
                durableIntent.Payload is not { } durablePayload ||
                !JsonElement.DeepEquals(candidatePayload, durablePayload) ||
                !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(candidate.WorkItem), durablePayload))
            {
                continue;
            }

            scope.PublishHopWorkItem(durableIntent.IntentId, candidate.WorkItem);
        }
    }

    private static bool HasSameDurableIdentityAndAssociation(
        RuntimePostCommitIntent candidate,
        RuntimePostCommitIntent durable) =>
        StringComparer.Ordinal.Equals(candidate.IntentId, durable.IntentId) &&
        StringComparer.Ordinal.Equals(candidate.WorkflowExecutionId, durable.WorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(candidate.Kind, durable.Kind) &&
        candidate.RecordedAt == durable.RecordedAt &&
        StringComparer.Ordinal.Equals(candidate.ActivityExecutionId, durable.ActivityExecutionId) &&
        StringComparer.Ordinal.Equals(candidate.IdempotencyKey, durable.IdempotencyKey) &&
        StringComparer.Ordinal.Equals(candidate.DependsOnWaitRegistrationId, durable.DependsOnWaitRegistrationId) &&
        candidate.WaitFailurePolicy == durable.WaitFailurePolicy &&
        candidate.Metadata.Count == durable.Metadata.Count &&
        candidate.Metadata.All(entry =>
            durable.Metadata.TryGetValue(entry.Key, out var value) &&
            StringComparer.Ordinal.Equals(entry.Value, value));

    private sealed record InProcessHopCandidate(
        RuntimePostCommitIntent Intent,
        RuntimeSchedulerWorkItem WorkItem);

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
        if (_ownershipContextAccessor?.Current is not { } lease)
            return commit;

        if (!StringComparer.Ordinal.Equals(lease.WorkflowExecutionId, commit.WorkflowExecutionId))
            return commit;

        return commit with { ExpectedFence = lease.ToFence() };
    }

    private static Exception CreateOwnershipLostException(RuntimeCheckpointCommit commit, string phase) =>
        commit.ExpectedFence is { } fence
            ? new RuntimeStaleFencingTokenException(commit.WorkflowExecutionId, fence.FencingToken, 0)
            : new InvalidOperationException($"Runtime checkpoint {phase} lost ownership of workflow execution '{commit.WorkflowExecutionId}'.");
}
