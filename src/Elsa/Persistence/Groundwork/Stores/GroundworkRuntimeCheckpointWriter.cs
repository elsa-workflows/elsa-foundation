using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using System.Globalization;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IRuntimeCheckpointCommitStore"/> for the Groundwork bridge.
/// </summary>
/// <remarks>
/// <para>
/// Runtime checkpoints are applied through one Groundwork document unit-of-work so lifecycle state,
/// inspection projections, side-effect state, and the commit marker succeed or roll back together:
/// </para>
/// <list type="bullet">
/// <item>On entry, an existing marker document keyed by <c>CommitId</c> short-circuits redelivery.</item>
/// <item>The marker is committed in the same document unit-of-work as the runtime state changes.</item>
/// </list>
/// </remarks>
public sealed class GroundworkRuntimeCheckpointWriter : IRuntimeCheckpointCommitStore
{
    private static readonly TimeSpan CommitAcknowledgementReconciliationTimeout = TimeSpan.FromSeconds(10);
    private readonly IDocumentStore _commitLedger;
    private readonly IGroundworkRuntimeDocumentSerializer _serializer;
    private readonly IPersistenceAccessContextAccessor _accessContextAccessor;
    private readonly IWorkflowExecutableRootWriteLeaseManager _rootWriteLeaseManager;
    private readonly TimeProvider _timeProvider;

    public GroundworkRuntimeCheckpointWriter(
        IDocumentStore commitLedger,
        IGroundworkRuntimeDocumentSerializer serializer,
        IPersistenceAccessContextAccessor accessContextAccessor,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        ISchedulerStateStore schedulerStateStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IBookmarkStateStore bookmarkStateStore,
        IDurableValueStateStore durableValueStateStore,
        IIncidentStateStore incidentStateStore,
        IExecutionLivenessStateStore operationalStateStore,
        IWorkflowExecutableRootWriteLeaseManager rootWriteLeaseManager,
        TimeProvider? timeProvider = null)
        : this(
            commitLedger,
            serializer,
            accessContextAccessor,
            workflowExecutionStateStore,
            schedulerStateStore,
            activityExecutionStateStore,
            new GroundworkActivityExecutionInspectionStore(commitLedger, serializer),
            bookmarkStateStore,
            durableValueStateStore,
            incidentStateStore,
            operationalStateStore,
            rootWriteLeaseManager,
            timeProvider)
    {
    }

    public GroundworkRuntimeCheckpointWriter(
        IDocumentStore commitLedger,
        IGroundworkRuntimeDocumentSerializer serializer,
        IPersistenceAccessContextAccessor accessContextAccessor,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        ISchedulerStateStore schedulerStateStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IActivityExecutionInspectionWriter activityExecutionInspectionWriter,
        IBookmarkStateStore bookmarkStateStore,
        IDurableValueStateStore durableValueStateStore,
        IIncidentStateStore incidentStateStore,
        IExecutionLivenessStateStore operationalStateStore,
        IWorkflowExecutableRootWriteLeaseManager rootWriteLeaseManager,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(commitLedger);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(schedulerStateStore);
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(activityExecutionInspectionWriter);
        ArgumentNullException.ThrowIfNull(bookmarkStateStore);
        ArgumentNullException.ThrowIfNull(durableValueStateStore);
        ArgumentNullException.ThrowIfNull(incidentStateStore);
        ArgumentNullException.ThrowIfNull(operationalStateStore);
        ArgumentNullException.ThrowIfNull(rootWriteLeaseManager);
        _commitLedger = commitLedger;
        _serializer = serializer;
        _accessContextAccessor = accessContextAccessor;
        _rootWriteLeaseManager = rootWriteLeaseManager;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.CommitId);
        cancellationToken.ThrowIfCancellationRequested();
        if (commit.StateChanges.WorkflowExecution is { } workflowExecutionChange)
            _accessContextAccessor.Current.EnsureTenantScope(workflowExecutionChange.State.TenantId);

        var fingerprint = RuntimeCheckpointCommitFingerprint.Compute(commit);
        if (await LoadMarkerAsync(commit, cancellationToken) is { } existing)
            return ResolveReplay(commit, fingerprint, existing);

        ValidateWorkflowExecutionStateChange(commit.StateChanges.WorkflowExecution);
        ValidateSchedulerStateChange(commit);
        ValidateActivityExecutionStateChanges(commit);
        ValidateActivityExecutionInspectionChanges(commit);
        ValidateBookmarkStateChanges(commit);
        ValidateDurableValueStateChanges(commit);
        ValidateIncidentStateChanges(commit);
        ValidateOperationalStateChanges(commit);
        ValidateActivityScopeCleanups(commit);

        return await ExecuteWithWorkflowExecutionRootWriteLeaseAsync(
            commit,
            (candidate, token) => ApplyAtomicallyAsync(candidate, fingerprint, token),
            cancellationToken);
    }

    private async ValueTask<RuntimeCheckpointCommitStoreResult> ExecuteWithWorkflowExecutionRootWriteLeaseAsync(
        RuntimeCheckpointCommit commit,
        Func<RuntimeCheckpointCommit, CancellationToken, ValueTask<RuntimeCheckpointCommitStoreResult>> write,
        CancellationToken cancellationToken)
    {
        if (commit.StateChanges.WorkflowExecution is not { } workflowExecutionChange)
            return await write(commit, cancellationToken);

        RuntimeCheckpointCommitStoreResult? result = null;
        await _rootWriteLeaseManager.ExecuteAsync(
            workflowExecutionChange.State.PinnedExecutable.ArtifactId,
            $"checkpoint:{commit.CommitId}",
            async ct => result = await write(commit, ct),
            cancellationToken);
        return result ?? throw new InvalidOperationException("The checkpoint root-write lease completed without a store result.");
    }

    private static IReadOnlyCollection<string> OutboxIds(RuntimeCheckpointCommit commit) =>
        commit.StateChanges.PostCommitOutbox
            .Select(change => change.State.OutboxItemId)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private async ValueTask<CheckpointCommitMarker?> LoadMarkerAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await _commitLedger.LoadAsync(
                ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
                GroundworkPhysicalDocumentId.FromLogicalId(commit.CommitId),
                cancellationToken);
            if (envelope is null)
                return null;

            var marker = _serializer.Deserialize<CheckpointCommitMarker>(envelope);
            if (!StringComparer.Ordinal.Equals(marker.CommitId, commit.CommitId))
                throw new InvalidOperationException($"Groundwork physical document identity collision detected for runtime checkpoint commit '{commit.CommitId}'.");
            return marker;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new GroundworkRuntimeCheckpointWriterException($"Failed to load the runtime checkpoint commit marker for commit '{commit.CommitId}' and workflow execution '{commit.WorkflowExecutionId}'.", e);
        }
    }

    private async ValueTask<RuntimeCheckpointCommitStoreResult> ApplyAtomicallyAsync(
        RuntimeCheckpointCommit commit,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (_commitLedger.TransactionBoundary != TransactionBoundary.CrossUnitAtomic)
            throw new GroundworkRuntimeCheckpointWriterException($"The Groundwork document store cannot atomically commit runtime checkpoint '{commit.CommitId}' for workflow execution '{commit.WorkflowExecutionId}' because it does not support cross-unit atomic transactions.", new NotSupportedException($"Unsupported transaction boundary '{_commitLedger.TransactionBoundary}'."));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var unitOfWork = await _commitLedger.BeginAsync(RuntimeCheckpointCommitScope(), cancellationToken);
                var transactionalStore = new DocumentUnitOfWorkStore(_commitLedger.TransactionBoundary, _commitLedger.Access, unitOfWork);
                await ValidateAndTouchExpectedFenceAsync(transactionalStore, commit, cancellationToken);
                var stores = GroundworkApplyStores.Create(transactionalStore, _serializer, _accessContextAccessor);
                await ApplyWorkflowExecutionStateChangeAsync(stores.WorkflowExecutionStateStore, commit.StateChanges.WorkflowExecution, cancellationToken);
                await ApplySchedulerStateChangeAsync(stores.SchedulerStateStore, commit.StateChanges.Scheduler, cancellationToken);
                await ApplyActivityExecutionStateChangesAsync(stores.ActivityExecutionStateStore, commit.StateChanges.ActivityExecutions, cancellationToken);
                await ApplyActivityExecutionInspectionChangesAsync(stores.ActivityExecutionInspectionWriter, commit.StateChanges.ActivityExecutionInspections, cancellationToken);
                await ApplyActivityExecutionHierarchyChangesAsync(stores.ActivityExecutionHierarchyWriter, commit.StateChanges.ActivityExecutionInspections, cancellationToken);
                await ApplyBookmarkStateChangesAsync(stores.BookmarkStateStore, commit.StateChanges.Bookmarks, cancellationToken);
                await ApplyDurableValueStateChangesAsync(stores.DurableValueStateStore, commit.StateChanges.DurableValues, cancellationToken);
                await ApplyIncidentStateChangesAsync(stores.IncidentStateStore, commit.StateChanges.Incidents, cancellationToken);
                await ApplyOperationalStateChangesAsync(stores.ExecutionLivenessStateStore, commit.StateChanges.Operational, cancellationToken);
                await ApplyActivityScopeCleanupsAsync(stores, commit.StateChanges.ActivityScopeCleanups, cancellationToken);
                await ApplyPostCommitOutboxAsync(stores.PostCommitOutboxStore, commit.StateChanges.PostCommitOutbox, cancellationToken);
                await MarkCommittedAsync(transactionalStore, commit, fingerprint, cancellationToken);
                await unitOfWork.CommitAsync(cancellationToken);
                return new RuntimeCheckpointCommitStoreResult(OutboxIds(commit));
            }
            catch (FenceConcurrencyException)
            {
                if (await LoadMarkerAsync(commit, cancellationToken) is { } marker)
                    return ResolveReplay(commit, fingerprint, marker);
                if (await ExpectedFenceRemainsCurrentAsync(commit, cancellationToken))
                    continue;
                throw await NewStaleFenceExceptionAsync(commit, cancellationToken);
            }
            catch (CheckpointMarkerConcurrencyException)
            {
                var marker = await LoadMarkerAsync(commit, cancellationToken)
                    ?? throw new GroundworkRuntimeCheckpointWriterException(
                        $"Runtime checkpoint marker '{commit.CommitId}' conflicted but no committed marker could be reloaded.",
                        new InvalidOperationException("The create-only marker conflict could not be reconciled."));
                return ResolveReplay(commit, fingerprint, marker);
            }
            catch (DocumentCommitAcknowledgementUncertainException e)
            {
                using var reconciliation = new CancellationTokenSource(
                    CommitAcknowledgementReconciliationTimeout,
                    _timeProvider);
                try
                {
                    if (await LoadMarkerAsync(commit, reconciliation.Token) is { } marker)
                        return ResolveReplay(commit, fingerprint, marker);
                }
                catch (OperationCanceledException) when (reconciliation.IsCancellationRequested)
                {
                    // Preserve the uncertain-acknowledgement contract when the independently bounded
                    // reconciliation read itself times out. The provider may still have committed.
                }
                throw new GroundworkRuntimeCheckpointWriterException(
                    $"Runtime checkpoint '{commit.CommitId}' may have committed, but its durable acknowledgement could not be reconciled.",
                    e);
            }
            catch (Exception e) when (e is not OperationCanceledException and
                                      not RuntimeStaleFencingTokenException and
                                      not RuntimeCheckpointReplayConflictException and
                                      not GroundworkRuntimeCheckpointWriterException)
            {
                throw new GroundworkRuntimeCheckpointWriterException($"Failed to atomically commit runtime checkpoint '{commit.CommitId}' for workflow execution '{commit.WorkflowExecutionId}'.", e);
            }
        }
    }

    private static DocumentCommitScope RuntimeCheckpointCommitScope() =>
        DocumentCommitScope.Of(
            ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
            ElsaRuntimeStorageManifest.SchedulerStateDocumentKind,
            ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind,
            ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind,
            ElsaRuntimeStorageManifest.BookmarkStateDocumentKind,
            ElsaRuntimeStorageManifest.DurableValueStateDocumentKind,
            ElsaRuntimeStorageManifest.IncidentStateDocumentKind,
            ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
            ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind);

    private async ValueTask ValidateAndTouchExpectedFenceAsync(
        IDocumentStore store,
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        if (commit.ExpectedFence is not { } expectedFence)
            return;

        var operationalStateId = GroundworkExecutionLivenessStateStore.OwnershipStateId(commit.WorkflowExecutionId);
        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
            GroundworkExecutionLivenessStateStore.DocumentIdentity(commit.WorkflowExecutionId, operationalStateId),
            cancellationToken);
        if (envelope is null)
            throw NewStaleFenceException(commit, currentToken: 0, RuntimeFencingRejectionReason.NoActiveLease);

        var document = _serializer.Deserialize<GroundworkExecutionLivenessStateStore.ExecutionLivenessStateDocument>(envelope);
        var state = document.State;
        var currentToken = ReadHighestIssuedToken(state);
        var current = state.ExecutionLease;
        if (current is null)
            throw NewStaleFenceException(commit, currentToken, RuntimeFencingRejectionReason.NoActiveLease);
        if (current.IsExpired(_timeProvider.GetUtcNow()))
            throw NewStaleFenceException(commit, currentToken, RuntimeFencingRejectionReason.ExpiredLease);
        if (!Matches(current, expectedFence))
            throw NewStaleFenceException(commit, currentToken, RuntimeFencingRejectionReason.StaleToken);

        var result = await store.SaveAsync(
            new SaveDocumentRequest(
                envelope.DocumentKind,
                envelope.Id,
                envelope.SchemaVersion,
                envelope.ContentJson,
                envelope.Version),
            cancellationToken);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return;
        if (result.Status is DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound)
            throw new FenceConcurrencyException();
        throw new InvalidOperationException($"Groundwork rejected execution-fence touch with status '{result.Status}'.");
    }

    private async ValueTask<bool> ExpectedFenceRemainsCurrentAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        if (commit.ExpectedFence is not { } expectedFence)
            return false;

        var store = new GroundworkExecutionLivenessStateStore(_commitLedger, _serializer);
        var loaded = await store.FindVersionedAsync(
            commit.WorkflowExecutionId,
            GroundworkExecutionLivenessStateStore.OwnershipStateId(commit.WorkflowExecutionId),
            cancellationToken);
        return loaded?.State.ExecutionLease is { } current &&
               !current.IsExpired(_timeProvider.GetUtcNow()) &&
               Matches(current, expectedFence);
    }

    private async ValueTask<RuntimeStaleFencingTokenException> NewStaleFenceExceptionAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken)
    {
        var store = new GroundworkExecutionLivenessStateStore(_commitLedger, _serializer);
        var state = await store.FindAsync(
            commit.WorkflowExecutionId,
            GroundworkExecutionLivenessStateStore.OwnershipStateId(commit.WorkflowExecutionId),
            cancellationToken);
        var currentToken = ReadHighestIssuedToken(state);
        var reason = state?.ExecutionLease is null
            ? RuntimeFencingRejectionReason.NoActiveLease
            : state.ExecutionLease.IsExpired(_timeProvider.GetUtcNow())
                ? RuntimeFencingRejectionReason.ExpiredLease
                : RuntimeFencingRejectionReason.StaleToken;
        return NewStaleFenceException(commit, currentToken, reason);
    }

    private static RuntimeStaleFencingTokenException NewStaleFenceException(
        RuntimeCheckpointCommit commit,
        long currentToken,
        RuntimeFencingRejectionReason reason) =>
        new(
            commit.WorkflowExecutionId,
            commit.ExpectedFence?.FencingToken ?? 0,
            currentToken,
            reason);

    private static bool Matches(RuntimeExecutionLease lease, RuntimeExecutionFence fence) =>
        StringComparer.Ordinal.Equals(lease.LeaseId, fence.LeaseId) &&
        StringComparer.Ordinal.Equals(lease.OwnerId, fence.OwnerId) &&
        lease.FencingToken == fence.FencingToken;

    private static long ReadHighestIssuedToken(ExecutionLivenessState? state)
    {
        if (state is null)
            return 0;
        if (state.Metadata.TryGetValue(RuntimeMetadataKeys.OwnershipFencingToken, out var raw) &&
            long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var token))
        {
            return token;
        }

        return state.ExecutionLease?.FencingToken ?? 0;
    }

    private static RuntimeCheckpointCommitStoreResult ResolveReplay(
        RuntimeCheckpointCommit commit,
        string fingerprint,
        CheckpointCommitMarker marker)
    {
        if (!StringComparer.Ordinal.Equals(marker.Fingerprint, fingerprint))
            throw new RuntimeCheckpointReplayConflictException(commit.CommitId);
        return new RuntimeCheckpointCommitStoreResult(marker.PendingPostCommitWorkIds);
    }

    private async ValueTask MarkCommittedAsync(
        IDocumentStore store,
        RuntimeCheckpointCommit commit,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var marker = new CheckpointCommitMarker(
            commit.CommitId,
            commit.WorkflowExecutionId,
            commit.Checkpoint.OccurredAt,
            ElsaRuntimeStorageManifest.CheckpointCommitCollection,
            fingerprint,
            OutboxIds(commit));
        var (schemaVersion, content) = _serializer.Serialize(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, marker);
        var result = await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
                GroundworkPhysicalDocumentId.FromLogicalId(commit.CommitId),
                schemaVersion,
                content,
                ExpectedVersion: 0),
            cancellationToken);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return;
        if (result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
            throw new CheckpointMarkerConcurrencyException();
        throw new InvalidOperationException($"Groundwork rejected runtime checkpoint commit marker '{commit.CommitId}' with status '{result.Status}'.");
    }

    private static async ValueTask ApplyWorkflowExecutionStateChangeAsync(
        IWorkflowExecutionStateStore store,
        RuntimeStateChange<WorkflowExecutionState>? stateChange,
        CancellationToken cancellationToken)
    {
        if (stateChange is null)
            return;
        await store.SaveAsync(stateChange.State, cancellationToken);
    }

    private static async ValueTask ApplyPostCommitOutboxAsync(
        GroundworkRuntimePostCommitOutboxStore store,
        IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> stateChanges,
        CancellationToken cancellationToken)
    {
        // Persisted in the same unit-of-work as the rest of the checkpoint: the continuation work is durable
        // atomically with the state that produced it, so it can never be silently lost on this path.
        foreach (var stateChange in stateChanges)
            await store.SavePendingAsync(stateChange.State, cancellationToken);
    }

    private static async ValueTask ApplySchedulerStateChangeAsync(
        ISchedulerStateStore store,
        RuntimeStateChange<SchedulerState>? stateChange,
        CancellationToken cancellationToken)
    {
        if (stateChange is null)
            return;
        await store.SaveAsync(stateChange.State, cancellationToken);
    }

    private static async ValueTask ApplyActivityExecutionStateChangesAsync(
        IActivityExecutionStateStore store,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
            await store.SaveAsync(stateChange.State, cancellationToken);
    }

    private static async ValueTask ApplyActivityExecutionInspectionChangesAsync(
        IActivityExecutionInspectionWriter writer,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
            await writer.SaveAsync(stateChange.State, cancellationToken);
    }

    private static async ValueTask ApplyActivityExecutionHierarchyChangesAsync(
        IActivityExecutionHierarchyWriter writer,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
        {
            var projection = stateChange.State;
            if (!string.IsNullOrWhiteSpace(projection.ExecutionScopeId ?? projection.Provenance.ExecutionScopeId))
                await writer.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(projection), cancellationToken);
        }
    }

    private static async ValueTask ApplyBookmarkStateChangesAsync(
        IBookmarkStateStore store,
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Delete)
            {
                await store.DeleteAsync(stateChange.State.WorkflowExecutionId, stateChange.State.BookmarkId, cancellationToken);
                continue;
            }
            await store.SaveAsync(stateChange.State, cancellationToken);
        }
    }

    private static async ValueTask ApplyDurableValueStateChangesAsync(
        IDurableValueStateStore store,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Delete)
            {
                await store.DeleteAsync(stateChange.State.WorkflowExecutionId, stateChange.State.DurableValueId, cancellationToken);
                continue;
            }
            await store.SaveAsync(stateChange.State, cancellationToken);
        }
    }

    private static async ValueTask ApplyIncidentStateChangesAsync(
        IIncidentStateStore store,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
        {
            // Append and Upsert both resolve to a durable write. Unlike the in-memory writer, a redelivered
            // Append is not an error: a crash may have applied the incident without writing the commit
            // marker, so re-applying the same commit must be idempotent. TryAdd-then-Save guarantees the
            // incident exists without throwing on a second pass.
            if (stateChange.Operation == RuntimeStateChangeOperation.Append)
            {
                var added = await store.TryAddAsync(stateChange.State, cancellationToken);
                if (!added)
                    await store.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }
            await store.SaveAsync(stateChange.State, cancellationToken);
        }
    }

    private static async ValueTask ApplyOperationalStateChangesAsync(
        IExecutionLivenessStateStore store,
        IReadOnlyCollection<RuntimeStateChange<ExecutionLivenessState>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
            await store.SaveAsync(stateChange.State, cancellationToken);
    }

    private static async ValueTask ApplyActivityScopeCleanupsAsync(
        GroundworkApplyStores stores,
        IReadOnlyCollection<ActivityScopeCleanupRequest> cleanups,
        CancellationToken cancellationToken)
    {
        foreach (var cleanup in cleanups)
        {
            foreach (var bookmarkId in cleanup.BookmarkIds)
                await stores.BookmarkStateStore.DeleteAsync(cleanup.WorkflowExecutionId, bookmarkId, cancellationToken);
            foreach (var timerId in cleanup.TimerIds)
                await stores.DurableTimerStore.DeleteAsync(cleanup.WorkflowExecutionId, timerId, cancellationToken);
            foreach (var workItemId in cleanup.SchedulerWorkItemIds)
                await stores.SchedulerWorkQueue.DeleteAsync(cleanup.WorkflowExecutionId, workItemId, cancellationToken);
        }
    }

    private static void ValidateWorkflowExecutionStateChange(RuntimeStateChange<WorkflowExecutionState>? stateChange)
    {
        if (stateChange is null)
            return;
        if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
            throw new InvalidOperationException($"The Groundwork checkpoint writer can only project workflow execution state '{RuntimeStateChangeOperation.Upsert}' changes.");
        if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Workflow execution state change StateId must match WorkflowExecutionState.WorkflowExecutionId.");
    }

    private static void ValidateSchedulerStateChange(RuntimeCheckpointCommit commit)
    {
        if (commit.StateChanges.Scheduler is null)
            return;
        var stateChange = commit.StateChanges.Scheduler;
        if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
            throw new InvalidOperationException($"The Groundwork checkpoint writer can only project scheduler state '{RuntimeStateChangeOperation.Upsert}' changes.");
        if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Scheduler state change StateId must match SchedulerState.WorkflowExecutionId.");
        if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Scheduler state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
    }

    private static void ValidateActivityExecutionStateChanges(RuntimeCheckpointCommit commit)
    {
        foreach (var stateChange in commit.StateChanges.ActivityExecutions)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project activity execution state '{RuntimeStateChangeOperation.Upsert}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.Execution.ActivityExecutionId))
                throw new InvalidOperationException("Activity execution state change StateId must match ActivityExecution.ActivityExecutionId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.Execution.WorkflowExecutionId))
                throw new InvalidOperationException("Activity execution state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
            if (stateChange.State.ExecutionScopeId is not null &&
                stateChange.State.Provenance.ExecutionScopeId is not null &&
                !StringComparer.Ordinal.Equals(stateChange.State.ExecutionScopeId, stateChange.State.Provenance.ExecutionScopeId))
                throw new InvalidOperationException("Activity execution state ExecutionScopeId must match ActivitySchedulingProvenance.ExecutionScopeId when both are present.");
            if (stateChange.State.Attempt is not null &&
                stateChange.State.Provenance.Attempt is not null &&
                stateChange.State.Attempt != stateChange.State.Provenance.Attempt)
                throw new InvalidOperationException("Activity execution state Attempt must match ActivitySchedulingProvenance.Attempt when both are present.");
        }
    }

    private static void ValidateActivityExecutionInspectionChanges(RuntimeCheckpointCommit commit)
    {
        foreach (var stateChange in commit.StateChanges.ActivityExecutionInspections)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project activity execution inspection '{RuntimeStateChangeOperation.Upsert}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.ActivityExecutionId))
                throw new InvalidOperationException("Activity execution inspection state change StateId must match ActivityExecutionInspectionProjection.ActivityExecutionId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Activity execution inspection WorkflowExecutionId must match the checkpoint workflow execution ID.");
            if (stateChange.State.ExecutionScopeId is not null &&
                stateChange.State.Provenance.ExecutionScopeId is not null &&
                !StringComparer.Ordinal.Equals(stateChange.State.ExecutionScopeId, stateChange.State.Provenance.ExecutionScopeId))
                throw new InvalidOperationException("Activity execution inspection ExecutionScopeId must match ActivitySchedulingProvenance.ExecutionScopeId when both are present.");
            if (stateChange.State.Attempt is not null &&
                stateChange.State.Provenance.Attempt is not null &&
                stateChange.State.Attempt != stateChange.State.Provenance.Attempt)
                throw new InvalidOperationException("Activity execution inspection Attempt must match ActivitySchedulingProvenance.Attempt when both are present.");
        }
    }

    private static void ValidateBookmarkStateChanges(RuntimeCheckpointCommit commit)
    {
        foreach (var stateChange in commit.StateChanges.Bookmarks)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Upsert and not RuntimeStateChangeOperation.Delete)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project bookmark state '{RuntimeStateChangeOperation.Upsert}' or '{RuntimeStateChangeOperation.Delete}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.BookmarkId))
                throw new InvalidOperationException("Bookmark state change StateId must match BookmarkState.BookmarkId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Bookmark state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private static void ValidateDurableValueStateChanges(RuntimeCheckpointCommit commit)
    {
        foreach (var stateChange in commit.StateChanges.DurableValues)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Upsert and not RuntimeStateChangeOperation.Delete)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project durable value state '{RuntimeStateChangeOperation.Upsert}' or '{RuntimeStateChangeOperation.Delete}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.DurableValueId))
                throw new InvalidOperationException("Durable value state change StateId must match DurableValueState.DurableValueId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Durable value state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private static void ValidateIncidentStateChanges(RuntimeCheckpointCommit commit)
    {
        foreach (var stateChange in commit.StateChanges.Incidents)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Append and not RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project incident state '{RuntimeStateChangeOperation.Append}' or '{RuntimeStateChangeOperation.Upsert}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.IncidentId))
                throw new InvalidOperationException("Incident state change StateId must match IncidentState.IncidentId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Incident state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private static void ValidateOperationalStateChanges(RuntimeCheckpointCommit commit)
    {
        var ownershipStateId = GroundworkExecutionLivenessStateStore.OwnershipStateId(commit.WorkflowExecutionId);
        foreach (var stateChange in commit.StateChanges.Operational)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project operational state '{RuntimeStateChangeOperation.Upsert}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.OperationalStateId))
                throw new InvalidOperationException("Operational state change StateId must match ExecutionLivenessState.OperationalStateId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Operational state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
            if (StringComparer.Ordinal.Equals(stateChange.State.OperationalStateId, ownershipStateId))
                throw new InvalidOperationException("Checkpoint operational changes cannot overwrite the reserved execution-ownership state.");
        }
    }

    private static void ValidateActivityScopeCleanups(RuntimeCheckpointCommit commit)
    {
        foreach (var cleanup in commit.StateChanges.ActivityScopeCleanups)
        {
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, cleanup.WorkflowExecutionId))
                throw new InvalidOperationException("Activity scope cleanup WorkflowExecutionId must match the checkpoint workflow execution ID.");
            if (!cleanup.ActivityExecutionIds.Contains(cleanup.ExecutionScopeId, StringComparer.Ordinal))
                throw new InvalidOperationException("Activity scope cleanup must include its outer execution scope ID.");
        }
    }

    private sealed record CheckpointCommitMarker(
        string CommitId,
        string WorkflowExecutionId,
        DateTimeOffset OccurredAt,
        string Collection,
        string Fingerprint,
        IReadOnlyCollection<string> PendingPostCommitWorkIds);

    private sealed class FenceConcurrencyException : Exception
    {
    }

    private sealed class CheckpointMarkerConcurrencyException : Exception
    {
    }

    private sealed record GroundworkApplyStores(
        IWorkflowExecutionStateStore WorkflowExecutionStateStore,
        ISchedulerStateStore SchedulerStateStore,
        IActivityExecutionStateStore ActivityExecutionStateStore,
        IActivityExecutionInspectionWriter ActivityExecutionInspectionWriter,
        IActivityExecutionHierarchyWriter ActivityExecutionHierarchyWriter,
        IBookmarkStateStore BookmarkStateStore,
        IDurableValueStateStore DurableValueStateStore,
        IIncidentStateStore IncidentStateStore,
        IExecutionLivenessStateStore ExecutionLivenessStateStore,
        GroundworkRuntimePostCommitOutboxStore PostCommitOutboxStore,
        IDurableTimerStore DurableTimerStore,
        IWorkflowSchedulerWorkQueue SchedulerWorkQueue)
    {
        public static GroundworkApplyStores Create(
            IDocumentStore store,
            IGroundworkRuntimeDocumentSerializer serializer,
            IPersistenceAccessContextAccessor accessContextAccessor) =>
            new(
                new GroundworkWorkflowExecutionStateStore(store, serializer, accessContextAccessor),
                new GroundworkSchedulerStateStore(store, serializer),
                new GroundworkActivityExecutionStateStore(store, serializer),
                new GroundworkActivityExecutionInspectionStore(store, serializer),
                new GroundworkActivityExecutionHierarchyStore(store, serializer),
                new GroundworkBookmarkStateStore(store, serializer),
                new GroundworkDurableValueStateStore(store, serializer),
                new GroundworkIncidentStateStore(store, serializer),
                new GroundworkExecutionLivenessStateStore(store, serializer),
                new GroundworkRuntimePostCommitOutboxStore(store, serializer),
                new GroundworkDurableTimerStore(store, serializer),
                new GroundworkWorkflowSchedulerWorkQueue(store, serializer));
    }

    private sealed class DocumentUnitOfWorkStore(
        TransactionBoundary transactionBoundary,
        DocumentStoreAccess access,
        IDocumentUnitOfWork unitOfWork) : IDocumentStore
    {
        public TransactionBoundary TransactionBoundary => transactionBoundary;
        public DocumentStoreAccess Access => access;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            unitOfWork.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            unitOfWork.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            unitOfWork.DeleteAsync(request, cancellationToken);

#pragma warning disable GW0004 // Required IDocumentStore compatibility member; this unit-of-work adapter rejects reads.
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Runtime checkpoint commit unit-of-work does not query documents.");
#pragma warning restore GW0004

#pragma warning disable GW0004 // Required IDocumentStore compatibility member; this unit-of-work adapter rejects reads.
        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Runtime checkpoint commit unit-of-work does not query documents.");
#pragma warning restore GW0004

#pragma warning disable GW0004 // Required IDocumentStore compatibility member; this unit-of-work adapter rejects reads.
        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Runtime checkpoint commit unit-of-work does not query documents.");
#pragma warning restore GW0004

#pragma warning disable GW0004 // Required IDocumentStore compatibility member; this unit-of-work adapter rejects reads.
        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Runtime checkpoint commit unit-of-work does not query documents.");
#pragma warning restore GW0004

        public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Nested document unit-of-work scopes are not supported.");
    }
}
