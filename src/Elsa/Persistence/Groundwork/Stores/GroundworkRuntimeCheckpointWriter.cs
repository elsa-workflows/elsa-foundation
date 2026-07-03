using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

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
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly IDocumentStore _commitLedger;
    private readonly IGroundworkRuntimeDocumentSerializer _serializer;

    public GroundworkRuntimeCheckpointWriter(
        IDocumentStore commitLedger,
        IGroundworkRuntimeDocumentSerializer serializer,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        ISchedulerStateStore schedulerStateStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IBookmarkStateStore bookmarkStateStore,
        IDurableValueStateStore durableValueStateStore,
        IIncidentStateStore incidentStateStore,
        IExecutionLivenessStateStore operationalStateStore)
        : this(
            commitLedger,
            serializer,
            workflowExecutionStateStore,
            schedulerStateStore,
            activityExecutionStateStore,
            new GroundworkActivityExecutionInspectionStore(commitLedger, serializer),
            bookmarkStateStore,
            durableValueStateStore,
            incidentStateStore,
            operationalStateStore)
    {
    }

    public GroundworkRuntimeCheckpointWriter(
        IDocumentStore commitLedger,
        IGroundworkRuntimeDocumentSerializer serializer,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        ISchedulerStateStore schedulerStateStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IActivityExecutionInspectionWriter activityExecutionInspectionWriter,
        IBookmarkStateStore bookmarkStateStore,
        IDurableValueStateStore durableValueStateStore,
        IIncidentStateStore incidentStateStore,
        IExecutionLivenessStateStore operationalStateStore)
    {
        ArgumentNullException.ThrowIfNull(commitLedger);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(schedulerStateStore);
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(activityExecutionInspectionWriter);
        ArgumentNullException.ThrowIfNull(bookmarkStateStore);
        ArgumentNullException.ThrowIfNull(durableValueStateStore);
        ArgumentNullException.ThrowIfNull(incidentStateStore);
        ArgumentNullException.ThrowIfNull(operationalStateStore);
        _commitLedger = commitLedger;
        _serializer = serializer;
    }

    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.CommitId);
        cancellationToken.ThrowIfCancellationRequested();

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            if (await IsCommittedAsync(commit, cancellationToken))
                return new RuntimeCheckpointCommitStoreResult(OutboxIds(commit));

            ValidateWorkflowExecutionStateChange(commit.StateChanges.WorkflowExecution);
            ValidateSchedulerStateChange(commit);
            ValidateActivityExecutionStateChanges(commit);
            ValidateActivityExecutionInspectionChanges(commit);
            ValidateBookmarkStateChanges(commit);
            ValidateDurableValueStateChanges(commit);
            ValidateIncidentStateChanges(commit);
            ValidateOperationalStateChanges(commit);

            await ApplyAtomicallyAsync(commit, cancellationToken);

            return new RuntimeCheckpointCommitStoreResult(OutboxIds(commit));
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static IReadOnlyCollection<string> OutboxIds(RuntimeCheckpointCommit commit) =>
        commit.StateChanges.PostCommitOutbox.Select(change => change.State.OutboxItemId).ToArray();

    private async ValueTask<bool> IsCommittedAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await _commitLedger.LoadAsync(
                ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
                commit.CommitId,
                cancellationToken);
            return envelope is not null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new GroundworkRuntimeCheckpointWriterException($"Failed to load the runtime checkpoint commit marker for commit '{commit.CommitId}' and workflow execution '{commit.WorkflowExecutionId}'.", e);
        }
    }

    private async ValueTask ApplyAtomicallyAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        if (_commitLedger.TransactionBoundary != TransactionBoundary.CrossUnitAtomic)
            throw new GroundworkRuntimeCheckpointWriterException($"The Groundwork document store cannot atomically commit runtime checkpoint '{commit.CommitId}' for workflow execution '{commit.WorkflowExecutionId}' because it does not support cross-unit atomic transactions.", new NotSupportedException($"Unsupported transaction boundary '{_commitLedger.TransactionBoundary}'."));

        try
        {
            await using var unitOfWork = await _commitLedger.BeginAsync(RuntimeCheckpointCommitScope(), cancellationToken);
            var transactionalStore = new DocumentUnitOfWorkStore(_commitLedger.TransactionBoundary, unitOfWork);
            var stores = GroundworkApplyStores.Create(transactionalStore, _serializer);
            await ApplyWorkflowExecutionStateChangeAsync(stores.WorkflowExecutionStateStore, commit.StateChanges.WorkflowExecution, cancellationToken);
            await ApplySchedulerStateChangeAsync(stores.SchedulerStateStore, commit.StateChanges.Scheduler, cancellationToken);
            await ApplyActivityExecutionStateChangesAsync(stores.ActivityExecutionStateStore, commit.StateChanges.ActivityExecutions, cancellationToken);
            await ApplyActivityExecutionInspectionChangesAsync(stores.ActivityExecutionInspectionWriter, commit.StateChanges.ActivityExecutionInspections, cancellationToken);
            await ApplyBookmarkStateChangesAsync(stores.BookmarkStateStore, commit.StateChanges.Bookmarks, cancellationToken);
            await ApplyDurableValueStateChangesAsync(stores.DurableValueStateStore, commit.StateChanges.DurableValues, cancellationToken);
            await ApplyIncidentStateChangesAsync(stores.IncidentStateStore, commit.StateChanges.Incidents, cancellationToken);
            await ApplyOperationalStateChangesAsync(stores.ExecutionLivenessStateStore, commit.StateChanges.Operational, cancellationToken);
            await ApplyPostCommitOutboxAsync(stores.PostCommitOutboxStore, commit.StateChanges.PostCommitOutbox, cancellationToken);
            await MarkCommittedAsync(transactionalStore, commit, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException and not GroundworkRuntimeCheckpointWriterException)
        {
            throw new GroundworkRuntimeCheckpointWriterException($"Failed to atomically commit runtime checkpoint '{commit.CommitId}' for workflow execution '{commit.WorkflowExecutionId}'.", e);
        }
    }

    private static DocumentCommitScope RuntimeCheckpointCommitScope() =>
        DocumentCommitScope.Of(
            ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
            ElsaRuntimeStorageManifest.SchedulerStateDocumentKind,
            ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind,
            ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
            ElsaRuntimeStorageManifest.BookmarkStateDocumentKind,
            ElsaRuntimeStorageManifest.DurableValueStateDocumentKind,
            ElsaRuntimeStorageManifest.IncidentStateDocumentKind,
            ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
            ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind);

    private async ValueTask MarkCommittedAsync(IDocumentStore store, RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        try
        {
            var marker = new CheckpointCommitMarker(commit.CommitId, commit.WorkflowExecutionId, commit.Checkpoint.OccurredAt);
            var (schemaVersion, content) = _serializer.Serialize(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, marker);
            await store.SaveAsync(
                new SaveDocumentRequest(
                    ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
                    commit.CommitId,
                    schemaVersion,
                    content),
                cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new GroundworkRuntimeCheckpointWriterException($"Failed to save the runtime checkpoint commit marker for commit '{commit.CommitId}' and workflow execution '{commit.WorkflowExecutionId}'.", e);
        }
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
        foreach (var stateChange in commit.StateChanges.Operational)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project operational state '{RuntimeStateChangeOperation.Upsert}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.OperationalStateId))
                throw new InvalidOperationException("Operational state change StateId must match ExecutionLivenessState.OperationalStateId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Operational state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private sealed record CheckpointCommitMarker(string CommitId, string WorkflowExecutionId, DateTimeOffset OccurredAt);

    private sealed record GroundworkApplyStores(
        IWorkflowExecutionStateStore WorkflowExecutionStateStore,
        ISchedulerStateStore SchedulerStateStore,
        IActivityExecutionStateStore ActivityExecutionStateStore,
        IActivityExecutionInspectionWriter ActivityExecutionInspectionWriter,
        IBookmarkStateStore BookmarkStateStore,
        IDurableValueStateStore DurableValueStateStore,
        IIncidentStateStore IncidentStateStore,
        IExecutionLivenessStateStore ExecutionLivenessStateStore,
        GroundworkRuntimePostCommitOutboxStore PostCommitOutboxStore)
    {
        public static GroundworkApplyStores Create(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer) =>
            new(
                new GroundworkWorkflowExecutionStateStore(store, serializer),
                new GroundworkSchedulerStateStore(store, serializer),
                new GroundworkActivityExecutionStateStore(store, serializer),
                new GroundworkActivityExecutionInspectionStore(store, serializer),
                new GroundworkBookmarkStateStore(store, serializer),
                new GroundworkDurableValueStateStore(store, serializer),
                new GroundworkIncidentStateStore(store, serializer),
                new GroundworkExecutionLivenessStateStore(store, serializer),
                new GroundworkRuntimePostCommitOutboxStore(store, serializer));
    }

    private sealed class DocumentUnitOfWorkStore(
        TransactionBoundary transactionBoundary,
        IDocumentUnitOfWork unitOfWork) : IDocumentStore
    {
        public TransactionBoundary TransactionBoundary => transactionBoundary;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            unitOfWork.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            unitOfWork.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            unitOfWork.DeleteAsync(request, cancellationToken);

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Runtime checkpoint commit unit-of-work does not query documents.");

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Runtime checkpoint commit unit-of-work does not query documents.");

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Runtime checkpoint commit unit-of-work does not query documents.");

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Runtime checkpoint commit unit-of-work does not query documents.");

        public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Nested document unit-of-work scopes are not supported.");
    }
}
