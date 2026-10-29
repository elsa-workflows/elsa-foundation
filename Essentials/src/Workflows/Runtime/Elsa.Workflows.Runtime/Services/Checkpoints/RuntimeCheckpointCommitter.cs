using System.Diagnostics;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Diagnostics;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

public sealed class RuntimeCheckpointCommitter
{
    private readonly IRuntimeCheckpointPersistencePolicy _persistencePolicy;
    private readonly IPersistenceAccessContextAccessor? _persistenceAccessContextAccessor;
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
    /// constructor: five required collaborators followed by optional collaborators that default to their no-op
    /// implementations. The ownership context accessor is <b>required by construction</b> so the fence
    /// (<see cref="AttachExpectedFence"/>, which stamps the ambient lease onto the provider-facing envelope) can never
    /// be silently disabled by picking a narrower constructor: without it a commit made inside a fenced drain would
    /// carry no expected fence and a superseded writer would be admitted. The enricher and intent-handler contribution
    /// sets are required for the same reason. They carry commit enrichment and post-commit retry policy, so an empty
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

    public RuntimeCheckpointCommitter(IRuntimeCheckpointPersistencePolicy persistencePolicy,
        IRuntimeCheckpointCommitStore checkpointCommitStore, IRuntimeExecutionOwnershipContextAccessor ownershipContextAccessor,
        IEnumerable<IRuntimeCheckpointCommitEnricher> enrichers, IEnumerable<RuntimePostCommitIntentHandlerContribution> intentHandlerContributions,
        IWorkflowEngineTracer? tracer, IRuntimeConsumedSchedulerWorkClaimAccessor? consumedWorkClaimAccessor,
        IRuntimeCoalescingSessionAccessor? coalescingSessionAccessor, IRuntimeLiveDrainDeliveryAccessor? liveDrainDeliveryAccessor,
        RuntimeInProcessHopFastPathOptions? inProcessHopFastPathOptions, IPersistenceAccessContextAccessor? persistenceAccessContextAccessor)
        : this(persistencePolicy, checkpointCommitStore, ownershipContextAccessor, enrichers, intentHandlerContributions, tracer,
            consumedWorkClaimAccessor, coalescingSessionAccessor, liveDrainDeliveryAccessor, inProcessHopFastPathOptions)
    {
        _persistenceAccessContextAccessor = persistenceAccessContextAccessor;
    }

    public async ValueTask<RuntimeCheckpointCommitResult> CommitAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);

        foreach (var enricher in _enrichers)
            commit = await enricher.EnrichAsync(commit, cancellationToken);

        foreach (var enricher in _enrichers)
            await enricher.ValidateFinalAsync(commit, cancellationToken);

        // The checkpoint-commit span wraps the fenced commit path. StartCheckpointCommit returns null when tracing
        // is inactive, so no allocation and no semantic change; when active it only introduces Activity.Current (trace
        // context, not service location). No new awaits are inserted between the fenced awaits below — attribute writes
        // are synchronous and happen after their source values are already computed.
        using var activity = _tracer.StartCheckpointCommit(commit);

        // Carry the ambient ownership identity into the provider-facing envelope. Durable stores decide replay first,
        // then validate this fence inside the same atomic decision as state, outbox, and the commit marker.
        commit = AttachExpectedFence(commit);

        if (commit.ModelState is not null && !_checkpointCommitStore.SupportsModelCheckpoint)
            throw new NotSupportedException("The selected checkpoint store cannot atomically persist model state and accepted inputs.");
        if (commit.ExpectedModelExecution is not null && !_checkpointCommitStore.SupportsModelAdmission)
            throw new NotSupportedException("The selected checkpoint store cannot atomically validate model-owned native worker admission.");
        if (commit.ExpectedBookmarkResume is not null && !_checkpointCommitStore.SupportsExactBookmarkResume)
            throw new NotSupportedException("The selected checkpoint store cannot enforce exact bookmark resume admission.");
        var decision = await _persistencePolicy.DecideAsync(commit.Checkpoint, cancellationToken);
        if (commit.ModelState is not null || commit.ExpectedModelExecution is not null)
            decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate,
                "Model decisions and native worker admission require an atomic physical checkpoint.");
        else if (commit.ExpectedBookmarkResume is not null)
            decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate,
                "Exact bookmark admission requires an atomic physical checkpoint.");
        else if (!commit.StateChanges.BackgroundActivityWork.IsEmpty)
            decision = new RuntimeCheckpointPersistenceDecision(
                RuntimeCheckpointPersistenceMode.Immediate,
                "Background activity work participants require an atomic checkpoint flush.");
        else if (HasDelayedIntent(commit))
            // R-7 (spec 261): a delayed child schedule is written durably at once in every persistence mode. Folded into
            // a coalesced segment it would live only in the drain's memory until the end-of-drain flush.
            decision = new RuntimeCheckpointPersistenceDecision(
                RuntimeCheckpointPersistenceMode.Immediate,
                "A delayed post-commit intent requires a durable checkpoint.");

        if (activity is not null)
        {
            activity.SetTag(WorkflowEngineTelemetry.CheckpointPersistenceDecisionTag, decision.Mode.ToString());
            activity.SetTag(WorkflowEngineTelemetry.CheckpointMandatoryTag, IsMandatoryCheckpoint(commit));
            activity.SetTag(WorkflowEngineTelemetry.CheckpointPostCommitIntentsTag, commit.PostCommitIntents.Count);
        }

        if (decision.Mode == RuntimeCheckpointPersistenceMode.Skip)
        {
            if (IsMandatoryCheckpoint(commit))
                throw new InvalidOperationException(
                    $"Mandatory runtime checkpoint '{commit.Checkpoint.CheckpointId}' cannot be skipped by the persistence policy.");

            if (commit.PostCommitIntents.Count > 0 || commit.StateChanges.PostCommitOutbox.Count > 0)
                return RuntimeCheckpointCommitResult.Failure(
                    commit,
                    decision,
                    RuntimeCheckpointCommitFailureCodes.SkipHasPostCommitWork,
                    "Checkpoint persistence policy skipped a commit that contains pending post-commit work.");

            return RuntimeCheckpointCommitResult.Success(commit, decision, []);
        }

        // Fold post-commit intents into the applied change set so the provider persists them atomically with
        // the rest of the checkpoint through its uniform apply path, then verify the provider acknowledged them.
        var postCommitOutbox = RuntimePostCommitOutboxItems.CreatePendingChanges(commit, _intentHandlerContributions, RequiredSourceScope(commit));

        // Fold the claimed scheduler work item's fence-checked delete into this same commit so the
        // drainer can skip its separate acknowledgement. Suppressed while a coalescing session owns the execution — the
        // session's overlay queue + AdvanceInnerQueueAsync stay authoritative on durable queue advance in that mode.
        var consumedWorkItems = ResolveConsumedWorkItems(commit);

        var stateChanges = commit.StateChanges;
        if (postCommitOutbox.Count > 0)
            stateChanges = stateChanges.WithPostCommitOutbox(stateChanges.PostCommitOutbox.Concat(postCommitOutbox).ToArray());
        if (consumedWorkItems.Count > 0)
            stateChanges = stateChanges.WithConsumedSchedulerWorkItems(consumedWorkItems);
        var commitToPersist = ReferenceEquals(stateChanges, commit.StateChanges)
            ? commit
            : commit with { StateChanges = stateChanges };

        // The structural checkpoint rules are applied here, to the exact commit the store receives, rather than inside
        // any store: every store is handed an already-validated commit, so the rules cannot differ between providers.
        RuntimeCheckpointCommitValidator.Validate(commitToPersist);

        // Validate materialized and declared responsibilities together before canonical folding. Extra or conflicting
        // materialized work must refuse rather than disappear; equivalent representations retain the ordinary one-row form.
        if (postCommitOutbox.Count > 0 && commit.StateChanges.PostCommitOutbox.Count > 0)
            commitToPersist = commitToPersist with { StateChanges = commitToPersist.StateChanges.WithPostCommitOutbox(postCommitOutbox) };

        // #1254: the store deletes the claimed item inside this call, but MarkConsumedDurably can only run once it
        // returns. A claim renewal firing in between finds nothing and would read that as a successor having stolen the
        // item. Publishing the attempt lets the drainer's renewal loop recognize its own consume as the explanation
        // instead. The attempt has to stay open until the durable mark lands, not just until the store call returns:
        // the row is already gone the moment it returns, so closing the attempt any earlier reopens this same window
        // around the validations below.
        RuntimeCheckpointCommitStoreResult storeResult;
        using (BeginConsumeAttempt(consumedWorkItems))
        {
            storeResult = await _checkpointCommitStore.CommitAsync(commitToPersist, decision, cancellationToken);

            if (storeResult.PendingPostCommitWorkIds.Count != commitToPersist.StateChanges.PostCommitOutbox.Count)
                throw new InvalidOperationException(
                    $"Checkpoint commit store persisted {storeResult.PendingPostCommitWorkIds.Count} post-commit outbox item(s) " +
                    $"for commit '{commit.CommitId}' (workflow execution '{commit.WorkflowExecutionId}') but the checkpoint carried " +
                    $"{commitToPersist.StateChanges.PostCommitOutbox.Count}. The continuation work would be silently dropped; the store must durably record every " +
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
        }

        // The durable outbox item is now committed and authoritative. If a
        // live drain owns this execution's delivery, hand the still-materialized continuation work items to its
        // drain-scoped carrier so the scheduler intent dispatcher can enqueue them without re-deserializing the payload
        // we just persisted. Runs only after the commit succeeds, so a rolled-back commit publishes nothing.
        PublishInProcessHopWorkItems(commit);

        return RuntimeCheckpointCommitResult.Success(commit, decision, storeResult.PendingPostCommitWorkIds);
    }

    // Publishes each committed EnqueueSchedulerWork continuation's materialized work item onto the owning live drain's
    // in-process-hop carrier. Guards (all must hold): the fast path is enabled; a live-drain delivery scope owns THIS
    // execution (so the dispatcher will look here rather than deserialize); and the drain's session for the execution
    // has stopped folding (while it folds, its overlay is authoritative on continuation delivery). When any guard
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
            // A delayed intent (R-7) is delivered by the outbox sweep once it is due, never by this drain.
            if (intent.MaterializedSchedulerWorkItem is { } workItem &&
                StringComparer.Ordinal.Equals(intent.Kind, RuntimePostCommitIntentKinds.EnqueueSchedulerWork) &&
                !intent.IsDelayedPast(commit.Checkpoint.OccurredAt))
                scope.PublishHopWorkItem(intent.IntentId, workItem);
        }
    }

    // ResolveConsumedWorkItems yields at most the one pending claim, so a single attempt handle covers the commit.
    private IDisposable? BeginConsumeAttempt(IReadOnlyCollection<ConsumedSchedulerWorkItem> consumedWorkItems) =>
        _consumedWorkClaimAccessor is { } accessor && consumedWorkItems.FirstOrDefault() is { } consumed
            ? accessor.BeginConsumeAttempt(consumed.WorkItemId)
            : null;

    private PersistenceScope? RequiredSourceScope(RuntimeCheckpointCommit commit)
    {
        var required = commit.PostCommitIntents.FirstOrDefault(intent => _intentHandlerContributions.Any(contribution =>
            contribution.RequiresDurableSource && contribution.IntentKind == intent.Kind));
        if (required is null) return null;
        try
        {
            return (_persistenceAccessContextAccessor?.Current ??
                throw new InvalidOperationException("A required-source checkpoint needs explicit persistence access.")).RequireScope();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw new RuntimePostCommitSourceBoundaryException(null, required.Kind, exception, false);
        }
    }

    private static bool HasDelayedIntent(RuntimeCheckpointCommit commit) =>
        commit.PostCommitIntents.Any(intent => intent.IsDelayedPast(commit.Checkpoint.OccurredAt));

    private static bool IsMandatoryCheckpoint(RuntimeCheckpointCommit commit) =>
        commit.ExpectedModelExecution is not null ||
        !commit.StateChanges.BackgroundActivityWork.IsEmpty ||
        HasDelayedIntent(commit) ||
        commit.Checkpoint.Metadata.TryGetValue(RuntimeMetadataKeys.CheckpointRequirement, out var requirement) &&
        StringComparer.Ordinal.Equals(requirement, RuntimeMetadataKeys.CheckpointRequirementMandatory);

    private IReadOnlyCollection<ConsumedSchedulerWorkItem> ResolveConsumedWorkItems(RuntimeCheckpointCommit commit)
    {
        if (commit.RetainSchedulerWork)
            return [];

        if (_consumedWorkClaimAccessor is not { PendingConsume: { } pending, WasConsumedDurably: false })
            return [];

        // Consume-once per dispatch: WasConsumedDurably guards against a multi-commit handler (e.g. InvokeActivity)
        // re-attaching an already-deleted item, which would fail the fence as claim-lost.
        if (!StringComparer.Ordinal.Equals(pending.WorkflowExecutionId, commit.WorkflowExecutionId))
            return [];

        // A folding session owns durable queue advance through the overlay; do not fold a durable delete that would
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
