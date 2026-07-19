using System.Text.Json;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed partial class GroundworkRuntimeCheckpointWriterTests
{
    [Theory]
    [InlineData(false, WorkflowDispatchStatus.Cancelled)]
    [InlineData(true, WorkflowDispatchStatus.Started)]
    public async Task Parent_cancellation_checkpoint_atomically_persists_parent_dispatch_marker_and_cancel_outbox(
        bool admitBeforeCancellation,
        WorkflowDispatchStatus expectedDispatchStatus)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create("sqlite");
        var executionStore = NewExecutionStore(fixture.DocumentStore);
        var dispatchStore = NewDispatchStore(fixture.DocumentStore, fixture.BoundedDocumentStore);
        var pending = CancellationDispatch("wf-1");
        await executionStore.SaveAsync(WorkflowState("wf-1", null));
        await dispatchStore.SaveAsync(pending);
        if (admitBeforeCancellation)
            await dispatchStore.TryAdmitAsync(pending.DispatchId, DateTimeOffset.UnixEpoch.AddSeconds(1));
        var commit = BuildCancellationCommit(
            admitBeforeCancellation ? "commit-cancel-admitted" : "commit-cancel-pending",
            pending);

        var result = await CreateWriter(fixture.DocumentStore).CommitAsync(commit, Decision);

        var parent = Assert.IsType<WorkflowExecutionState>(await executionStore.FindAsync("wf-1"));
        var dispatch = Assert.IsType<WorkflowDispatchRecord>(await dispatchStore.FindAsync(pending.DispatchId));
        var cancelOutbox = Assert.Single(result.PendingPostCommitWorkIds);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, parent.Status);
        Assert.Equal(expectedDispatchStatus, dispatch.Status);
        Assert.Equal(!admitBeforeCancellation, WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(dispatch));
        Assert.Equal(admitBeforeCancellation, WorkflowDispatchLifecycle.IsCancellationRequested(dispatch));
        Assert.NotNull(await fixture.DocumentStore.LoadAsync(
            ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
            cancelOutbox));
        Assert.NotNull(await fixture.DocumentStore.LoadAsync(
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
            commit.CommitId));
    }

    [Fact]
    public async Task Parent_cancellation_checkpoint_replay_is_deterministic_and_conflicting_request_fails_closed()
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create("sqlite");
        var executionStore = NewExecutionStore(fixture.DocumentStore);
        var dispatchStore = NewDispatchStore(fixture.DocumentStore, fixture.BoundedDocumentStore);
        var pending = CancellationDispatch("wf-1");
        await executionStore.SaveAsync(WorkflowState("wf-1", null));
        await dispatchStore.SaveAsync(pending);
        await dispatchStore.TryAdmitAsync(pending.DispatchId, DateTimeOffset.UnixEpoch.AddSeconds(1));
        var commit = BuildCancellationCommit("commit-cancel-replay", pending);
        var writer = CreateWriter(fixture.DocumentStore);

        var first = await writer.CommitAsync(commit, Decision);
        var replay = await writer.CommitAsync(commit, Decision);
        var persisted = Assert.IsType<WorkflowDispatchRecord>(await dispatchStore.FindAsync(pending.DispatchId));

        Assert.Equal(first.PendingPostCommitWorkIds, replay.PendingPostCommitWorkIds);
        Assert.True(WorkflowDispatchLifecycle.IsCancellationRequested(persisted));
        var conflict = commit with
        {
            StateChanges = commit.StateChanges.WithWorkflowDispatchCancellations(
                [CancellationRequest(pending, DateTimeOffset.UnixEpoch.AddSeconds(3))])
        };
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(
            () => writer.CommitAsync(conflict, Decision).AsTask());
        Assert.True(WorkflowDispatchLifecycle.RecordsEqual(
            persisted,
            Assert.IsType<WorkflowDispatchRecord>(await dispatchStore.FindAsync(pending.DispatchId))));
    }

    [Fact]
    public async Task Cancellation_checkpoint_reloads_when_admission_commits_after_its_pending_read()
    {
        var inner = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var store = new CancellationReadBarrierDocumentStore(inner);
        var executionStore = NewExecutionStore(store);
        var dispatchStore = NewDispatchStore(store);
        var pending = CancellationDispatch("wf-1");
        await executionStore.SaveAsync(WorkflowState("wf-1", null));
        await dispatchStore.SaveAsync(pending);
        var commit = BuildCancellationCommit("commit-cancel-stale-read", pending);

        var cancellation = CreateWriter(store).CommitAsync(commit, Decision).AsTask();
        await store.CancellationReadPending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var admission = await dispatchStore.TryAdmitAsync(
            pending.DispatchId,
            DateTimeOffset.UnixEpoch.AddSeconds(1));
        store.ReleaseCancellationRead();
        var commitResult = await cancellation;

        var persisted = Assert.IsType<WorkflowDispatchRecord>(await dispatchStore.FindAsync(pending.DispatchId));
        Assert.Equal(WorkflowDispatchAdmissionDisposition.Admitted, admission.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Started, persisted.Status);
        Assert.True(WorkflowDispatchLifecycle.IsCancellationRequested(persisted));
        Assert.Equal(1, store.CancellationWriteConflicts);
        Assert.Single(commitResult.PendingPostCommitWorkIds);
        Assert.Equal(
            WorkflowExecutionStatus.Cancelled,
            Assert.IsType<WorkflowExecutionState>(await executionStore.FindAsync("wf-1")).Status);
        Assert.NotNull(await inner.LoadAsync(
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
            commit.CommitId));
    }

    private static GroundworkWorkflowExecutionStateStore NewExecutionStore(IDocumentStore store) =>
        new(store, GroundworkTestSerialization.Serializer, GroundworkTestAccess.DefaultAccessContextAccessor);

    private static WorkflowDispatchRecord CancellationDispatch(string workflowExecutionId) =>
        GroundworkWorkflowDispatchStoreTests.Pending(
            workflowExecutionId,
            "ae-1",
            mode: WorkflowDispatchMode.WaitForCompletion,
            cancellationPropagation: true);

    private static GroundworkWorkflowDispatchStore NewDispatchStore(
        IDocumentStore store,
        IBoundedDocumentStore? boundedStore = null) =>
        new(store, GroundworkTestSerialization.Serializer, GroundworkTestAccess.DefaultAccessContextAccessor, boundedStore);

    private static RuntimeCheckpointCommit BuildCancellationCommit(
        string commitId,
        WorkflowDispatchRecord dispatch)
    {
        var recordedAt = DateTimeOffset.UnixEpoch.AddSeconds(2);
        var request = CancellationRequest(dispatch, recordedAt);
        var identity = DispatchIdentity(dispatch.ParentWorkflowExecutionId);
        var intent = new RuntimePostCommitIntent(
            identity.ChildCancelIntentId,
            dispatch.ParentWorkflowExecutionId,
            "Elsa.Activities.DispatchWorkflow.CancelChild",
            recordedAt,
            dispatch.ParentActivityExecutionId,
            identity.ChildCancelIdempotencyKey,
            JsonSerializer.SerializeToElement(new
            {
                dispatchId = dispatch.DispatchId,
                parentWorkflowExecutionId = dispatch.ParentWorkflowExecutionId,
                parentActivityExecutionId = dispatch.ParentActivityExecutionId,
                childWorkflowExecutionId = dispatch.ChildWorkflowExecutionId
            }));
        var outbox = new RuntimePostCommitOutboxItem(
            RuntimePostCommitOutboxItems.OutboxItemId(commitId, intent),
            intent,
            RuntimePostCommitOutboxStatus.Pending,
            recordedAt,
            recordedAt,
            retryPolicy: RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5)));
        var parent = WorkflowState(dispatch.ParentWorkflowExecutionId, dispatch.TenantId) with
        {
            Status = WorkflowExecutionStatus.Cancelled,
            UpdatedAt = recordedAt,
            CompletedAt = recordedAt
        };
        var metadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory
        };
        return new RuntimeCheckpointCommit(
            commitId,
            new RuntimeCheckpoint(
                $"checkpoint:{commitId}",
                RuntimeCheckpointNames.WorkflowCancelled,
                dispatch.ParentWorkflowExecutionId,
                recordedAt,
                [dispatch.ParentActivityExecutionId],
                metadata),
            new RuntimeCheckpointStateChangeSet(
                Change(parent.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, parent),
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                workflowDispatches: [],
                activityExecutionInspections: null,
                postCommitOutbox: [Change(outbox.OutboxItemId, RuntimeStateChangeOperation.Upsert, outbox)],
                activityScopeCleanups: null,
                workflowDispatchCancellations: [request]),
            [intent],
            metadata);
    }

    private static WorkflowDispatchCancellationRequest CancellationRequest(
        WorkflowDispatchRecord dispatch,
        DateTimeOffset requestedAt) =>
        new(
            dispatch.DispatchId,
            dispatch.ParentWorkflowExecutionId,
            dispatch.ParentActivityExecutionId,
            dispatch.ChildWorkflowExecutionId,
            requestedAt);

    private sealed class CancellationReadBarrierDocumentStore(IDocumentStore inner) : IDocumentStore
    {
        private int _pauseCancellationRead = 1;
        private readonly TaskCompletionSource _releaseCancellationRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationReadPending { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CancellationWriteConflicts { get; private set; }
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;
        public DocumentStoreAccess Access => inner.Access;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

#pragma warning disable GW0004 // IDocumentStore compatibility surface delegated by the barrier wrapper.
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);
#pragma warning restore GW0004

        public async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new CancellationBarrierUnitOfWork(await inner.BeginAsync(scope, cancellationToken), this);

        public void ReleaseCancellationRead() => _releaseCancellationRead.TrySetResult();

        private sealed class CancellationBarrierUnitOfWork(
            IDocumentUnitOfWork innerUnitOfWork,
            CancellationReadBarrierDocumentStore owner) : IDocumentUnitOfWork
        {
            public async Task<DocumentStoreWriteResult> SaveAsync(
                SaveDocumentRequest request,
                CancellationToken cancellationToken = default)
            {
                var result = await innerUnitOfWork.SaveAsync(request, cancellationToken);
                if (request.DocumentKind == ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind &&
                    result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                {
                    owner.CancellationWriteConflicts++;
                }
                return result;
            }

            public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
                innerUnitOfWork.DeleteAsync(request, cancellationToken);

            public async Task<DocumentEnvelope?> LoadAsync(
                string documentKind,
                string id,
                CancellationToken cancellationToken = default)
            {
                var envelope = await innerUnitOfWork.LoadAsync(documentKind, id, cancellationToken);
                if (documentKind == ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind &&
                    Interlocked.Exchange(ref owner._pauseCancellationRead, 0) == 1)
                {
                    owner.CancellationReadPending.TrySetResult();
                    await owner._releaseCancellationRead.Task.WaitAsync(cancellationToken);
                }
                return envelope;
            }

            public Task CommitAsync(CancellationToken cancellationToken = default) => innerUnitOfWork.CommitAsync(cancellationToken);
            public Task RollbackAsync(CancellationToken cancellationToken = default) => innerUnitOfWork.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => innerUnitOfWork.DisposeAsync();
        }
    }
}
