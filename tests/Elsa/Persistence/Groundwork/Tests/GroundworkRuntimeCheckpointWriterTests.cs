using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using System.Text.Json;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

// Behavioral assertions for the Groundwork checkpoint writer. The writer orchestrates the bridged seam
// stores and a durable per-CommitId marker. These tests prove the three properties that make it a faithful
// (and restart-safe) replacement for the in-memory writer: full multi-seam persistence, durability across a
// simulated process restart, and idempotency under at-least-once redelivery.
public sealed class GroundworkRuntimeCheckpointWriterTests
{
    private static readonly RuntimeCheckpointPersistenceDecision Decision =
        new(RuntimeCheckpointPersistenceMode.Immediate);

    [Fact]
    public async Task Commit_Persists_All_Seam_State_And_Survives_Restart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-checkpoint-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            // First "process": apply the commit, then dispose the store to flush and close the connection.
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var writer = CreateWriter(fixture.DocumentStore);
                await writer.CommitAsync(BuildCommit("commit-1"), Decision);
            }

            // Second "process": a brand-new store over the same database file. Nothing is in memory, so anything
            // we can read back was genuinely persisted by the first process.
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var store = fixture.DocumentStore;
                Assert.Equal(WorkflowExecutionStatus.Running, (await new GroundworkWorkflowExecutionStateStore(
                    store,
                    GroundworkTestSerialization.Serializer,
                    GroundworkTestAccess.DefaultAccessContextAccessor).FindAsync("wf-1"))!.Status);
                Assert.Equal(7L, (await new GroundworkSchedulerStateStore(store, GroundworkTestSerialization.Serializer).FindAsync("wf-1"))!.Version);
                Assert.NotNull(await new GroundworkActivityExecutionStateStore(store, GroundworkTestSerialization.Serializer).FindAsync("wf-1", "ae-1"));
                Assert.NotNull(await new GroundworkBookmarkStateStore(store, GroundworkTestSerialization.Serializer).FindAsync("wf-1", "bm-1"));
                Assert.NotNull(await new GroundworkDurableValueStateStore(store, GroundworkTestSerialization.Serializer).FindAsync("wf-1", "dv-1"));
                Assert.NotNull(await new GroundworkExecutionLivenessStateStore(store, GroundworkTestSerialization.Serializer).FindAsync("wf-1", "op-1"));
                Assert.Equal(IncidentStatus.Open, (await new GroundworkIncidentStateStore(store, GroundworkTestSerialization.Serializer).FindAsync("wf-1", "inc-1"))!.Status);

                // The durable commit marker proves the commit is recorded as applied.
                Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, "commit-1"));
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Redelivered_Commit_With_Same_CommitId_Is_Skipped()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var writer = CreateWriter(store);
        var commit = BuildCommit("commit-1", bookmarkNode: "node-v1");

        await writer.CommitAsync(commit, Decision);
        await writer.CommitAsync(commit, Decision);

        var bookmark = await new GroundworkBookmarkStateStore(store, GroundworkTestSerialization.Serializer).FindAsync("wf-1", "bm-1");
        Assert.Equal("node-v1", bookmark!.ExecutableNodeId);
    }

    [Fact]
    public async Task Redelivered_Commit_With_Same_CommitId_Rejects_Conflicting_Payload()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var writer = CreateWriter(store);

        await writer.CommitAsync(BuildCommit("commit-1", bookmarkNode: "node-v1"), Decision);
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(async () =>
            await writer.CommitAsync(BuildCommit("commit-1", bookmarkNode: "node-v2"), Decision));

        var bookmark = await new GroundworkBookmarkStateStore(store, GroundworkTestSerialization.Serializer).FindAsync("wf-1", "bm-1");
        Assert.Equal("node-v1", bookmark!.ExecutableNodeId);
    }

    [Fact]
    public async Task Commit_Rejects_Explicit_Tenant_Outside_The_Current_Scope_Before_Provider_IO()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var writer = CreateWriter(store, GroundworkTestAccess.AccessContext("tenant-a"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await writer.CommitAsync(BuildCommit("commit-wrong-tenant", tenantId: "tenant-b"), Decision));

        Assert.Equal(0, store.LoadCount);
        Assert.Equal(0, store.BeginCount);
        Assert.DoesNotContain("tenant-a", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-b", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitId_Beyond_Portable_Document_Limit_Remains_Idempotent()
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create("sqlite");
        var store = fixture.DocumentStore;
        var writer = CreateWriter(store);
        var commitId = $"commit:{new string('x', 450)}";
        Assert.True(commitId.Length > 450, $"Expected the regression identity to exceed 450 code units, but observed {commitId.Length}.");

        await writer.CommitAsync(BuildCommit(commitId, bookmarkNode: "node-v1"), Decision);
        await writer.CommitAsync(BuildCommit(commitId, bookmarkNode: "node-v1"), Decision);

        var bookmark = await new GroundworkBookmarkStateStore(store, GroundworkTestSerialization.Serializer).FindAsync("wf-1", "bm-1");
        Assert.Equal("node-v1", bookmark!.ExecutableNodeId);
    }

    [Fact]
    public async Task CommitMarker_PhysicalAliasCollision_FailsClosed()
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create("sqlite");
        var writer = CreateWriter(fixture.DocumentStore);
        var longCommitId = new string('x', 451);
        var collidingShortCommitId = GroundworkPhysicalDocumentIdTestData.PhysicalAliasFor(longCommitId);
        await writer.CommitAsync(BuildCommit(longCommitId), Decision);

        var exception = await Assert.ThrowsAsync<GroundworkRuntimeCheckpointWriterException>(
            async () => await writer.CommitAsync(BuildCommit(collidingShortCommitId), Decision));

        Assert.Contains("physical document identity collision", exception.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replayed_Commit_After_Marker_Loss_Reapplies_Without_Throwing()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var writer = CreateWriter(store);

        await writer.CommitAsync(BuildCommit("commit-1"), Decision);

        // Simulate a crash that applied the state but lost the marker: drop the marker and replay the same commit.
        // The incident Append in particular must not fail on the second pass.
        await store.DeleteAsync(new DeleteDocumentRequest(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, "commit-1"));
        await writer.CommitAsync(BuildCommit("commit-1"), Decision);

        Assert.Equal(IncidentStatus.Open, (await new GroundworkIncidentStateStore(store, GroundworkTestSerialization.Serializer).FindAsync("wf-1", "inc-1"))!.Status);
        Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, "commit-1"));
    }

    [Fact]
    public async Task UncertainCommit_ReconcilesMarker_AfterCallerTokenIsCanceled()
    {
        using var callerCancellation = new CancellationTokenSource();
        var inner = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var store = new UncertainAfterCommitDocumentStore(inner, callerCancellation);
        var writer = CreateWriter(store);

        var result = await writer.CommitAsync(BuildCommit("commit-uncertain"), Decision, callerCancellation.Token);

        Assert.True(callerCancellation.IsCancellationRequested);
        Assert.Empty(result.PendingPostCommitWorkIds);
        Assert.NotNull(await inner.LoadAsync(
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
            "commit-uncertain"));
    }

    [Fact]
    public async Task UncertainCommit_ReconciliationTimeout_PreservesMayHaveCommittedFailure()
    {
        var inner = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var store = new UncertainWithoutCommitDocumentStore(inner);
        var timeProvider = new ManualTimerTimeProvider();
        var writer = CreateWriter(store, timeProvider: timeProvider);

        var commit = writer.CommitAsync(BuildCommit("commit-timeout"), Decision).AsTask();
        await store.ReconciliationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire();

        var exception = await Assert.ThrowsAsync<GroundworkRuntimeCheckpointWriterException>(() => commit);
        Assert.Contains("may have committed", exception.Message, StringComparison.Ordinal);
        Assert.IsType<DocumentCommitAcknowledgementUncertainException>(exception.InnerException);
        Assert.Null(await inner.LoadAsync(
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
            "commit-timeout"));
    }

    private static GroundworkRuntimeCheckpointWriter CreateWriter(
        IDocumentStore store,
        IPersistenceAccessContextAccessor? accessContextAccessor = null,
        TimeProvider? timeProvider = null)
    {
        accessContextAccessor ??= GroundworkTestAccess.DefaultAccessContextAccessor;
        return new(
            store,
            GroundworkTestSerialization.Serializer,
            accessContextAccessor,
            new GroundworkWorkflowExecutionStateStore(store, GroundworkTestSerialization.Serializer, accessContextAccessor),
            new GroundworkSchedulerStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkActivityExecutionStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkBookmarkStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkDurableValueStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkIncidentStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkExecutionLivenessStateStore(store, GroundworkTestSerialization.Serializer),
            PassThroughRootWriteLeaseManager.Instance,
            timeProvider);
    }

    private static RuntimeCheckpointCommit BuildCommit(
        string commitId,
        string bookmarkNode = "node-bm-1",
        string? tenantId = null)
    {
        const string wf = "wf-1";
        var stateChanges = new RuntimeCheckpointStateChangeSet(
            workflowExecution: Change(wf, RuntimeStateChangeOperation.Upsert, WorkflowState(wf, tenantId)),
            scheduler: Change(wf, RuntimeStateChangeOperation.Upsert, Scheduler(wf, 7)),
            activityExecutions: [Change("ae-1", RuntimeStateChangeOperation.Upsert, ActivityState(wf, "ae-1"))],
            bookmarks: [Change("bm-1", RuntimeStateChangeOperation.Upsert, Bookmark(wf, "bm-1", bookmarkNode))],
            durableValues: [Change("dv-1", RuntimeStateChangeOperation.Upsert, DurableValue(wf, "dv-1"))],
            incidents: [Change("inc-1", RuntimeStateChangeOperation.Append, Incident(wf, "inc-1"))],
            operational: [Change("op-1", RuntimeStateChangeOperation.Upsert, Operational(wf, "op-1"))]);

        var checkpoint = new RuntimeCheckpoint(
            CheckpointId: $"cp-{commitId}",
            Name: "checkpoint",
            WorkflowExecutionId: wf,
            OccurredAt: DateTimeOffset.UnixEpoch,
            ActivityExecutionIds: ["ae-1"],
            Metadata: new Dictionary<string, string>());

        return new RuntimeCheckpointCommit(
            commitId,
            checkpoint,
            stateChanges,
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());
    }

    private static RuntimeStateChange<T> Change<T>(string stateId, RuntimeStateChangeOperation operation, T state) =>
        new(stateId, operation, state, new Dictionary<string, string>());

    private static WorkflowExecutionState WorkflowState(string workflowExecutionId, string? tenantId) => new(
        workflowExecutionId,
        new WorkflowExecutableIdentity($"artifact-{workflowExecutionId}", "definition-1", "version-1", "1", $"hash-{workflowExecutionId}"),
        WorkflowExecutionStatus.Running,
        SubStatus: null,
        CreatedAt: DateTimeOffset.UnixEpoch,
        StartedAt: null,
        UpdatedAt: null,
        CompletedAt: null,
        CorrelationId: null,
        ParentWorkflowExecutionId: null,
        TenantId: tenantId,
        SystemMetadata: new Dictionary<string, string>());

    private static SchedulerState Scheduler(string workflowExecutionId, long version) => new(
        workflowExecutionId,
        version,
        pendingWork:
        [
            new ScheduledActivityWorkItem(
                WorkItemId: $"work-{workflowExecutionId}",
                WorkflowExecutionId: workflowExecutionId,
                ExecutableNodeId: $"node-{workflowExecutionId}",
                ActivityExecutionId: null,
                SchedulingActivityExecutionId: null,
                BranchId: null,
                IterationId: null,
                EnqueuedAt: DateTimeOffset.UnixEpoch,
                Reason: "scheduled")
        ]);

    private static ActivityExecutionState ActivityState(string workflowExecutionId, string activityExecutionId) => new(
        new ActivityExecution(activityExecutionId, workflowExecutionId, $"node-{activityExecutionId}", "authored", "Elsa.Log", "1.0.0"),
        ActivityExecutionStatus.Running,
        SubStatus: null,
        ScheduledAt: DateTimeOffset.UnixEpoch,
        StartedAt: DateTimeOffset.UnixEpoch,
        CompletedAt: null,
        SchedulingActivityExecutionId: null,
        ParentActivityExecutionId: null,
        BranchId: null,
        IterationId: null,
        CallStackDepth: 0,
        BookmarkIds: [],
        IncidentIds: [],
        FaultCount: 0,
        AggregateFaultCount: 0,
        Metadata: new Dictionary<string, string>());

    private static BookmarkState Bookmark(string workflowExecutionId, string bookmarkId, string node) => new(
        BookmarkId: bookmarkId,
        WorkflowExecutionId: workflowExecutionId,
        ActivityExecutionId: "ae-1",
        ExecutableNodeId: node,
        ResumeTargetId: "resume-1",
        StimulusType: "delivery-status",
        StimulusHash: "sha256:stimulus",
        Payload: null,
        Metadata: new Dictionary<string, string>(),
        CreatedAt: DateTimeOffset.UnixEpoch,
        ExpiresAt: null);

    private static DurableValueState DurableValue(string workflowExecutionId, string durableValueId) => new(
        durableValueId,
        workflowExecutionId,
        $"value-{durableValueId}",
        new RuntimeValueTypeDescriptor("int", null, null),
        DurableValueLifecycle.Instance,
        DurableValueStorage.Inline,
        Json("42"),
        externalReference: null,
        sourceActivityExecutionId: null,
        capturedAt: DateTimeOffset.UnixEpoch,
        metadata: new Dictionary<string, string>());

    private static ExecutionLivenessState Operational(string workflowExecutionId, string operationalStateId) => new(
        operationalStateId,
        workflowExecutionId,
        executionLease: null,
        heartbeat: null,
        drain: null,
        interruptedExecution: null);

    private static IncidentState Incident(string workflowExecutionId, string incidentId) => new(
        incidentId,
        workflowExecutionId,
        activityExecutionId: null,
        executableNodeId: null,
        IncidentSeverity.Error,
        IncidentStatus.Open,
        IncidentResolutionAction.None,
        failureType: "System.Exception",
        message: "boom",
        createdAt: DateTimeOffset.UnixEpoch,
        resolvedAt: null);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class UncertainAfterCommitDocumentStore(
        IDocumentStore inner,
        CancellationTokenSource callerCancellation) : IDocumentStore
    {
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;
        public DocumentStoreAccess Access => inner.Access;

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) => inner.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) => inner.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) => inner.DeleteAsync(request, cancellationToken);

#pragma warning disable GW0004 // IDocumentStore compatibility surface delegated by the fault-injection wrapper.
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
            DocumentStoreQuery query,
            CancellationToken cancellationToken = default) => inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.AnyAsync(query, cancellationToken);
#pragma warning restore GW0004

        public async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new UncertainAfterCommitUnitOfWork(
                await inner.BeginAsync(scope, cancellationToken),
                callerCancellation);
    }

    private sealed class UncertainAfterCommitUnitOfWork(
        IDocumentUnitOfWork inner,
        CancellationTokenSource callerCancellation) : IDocumentUnitOfWork
    {
        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) => inner.SaveAsync(request, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) => inner.DeleteAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) => inner.LoadAsync(documentKind, id, cancellationToken);

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await inner.CommitAsync(cancellationToken);
            await callerCancellation.CancelAsync();
            throw new DocumentCommitAcknowledgementUncertainException(
                [ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind]);
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            inner.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class UncertainWithoutCommitDocumentStore(IDocumentStore inner) : IDocumentStore
    {
        private int _markerLoads;

        public TaskCompletionSource ReconciliationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;
        public DocumentStoreAccess Access => inner.Access;

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) => inner.SaveAsync(request, cancellationToken);

        public async Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(documentKind, ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, StringComparison.Ordinal) &&
                Interlocked.Increment(ref _markerLoads) == 2)
            {
                ReconciliationStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return await inner.LoadAsync(documentKind, id, cancellationToken);
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) => inner.DeleteAsync(request, cancellationToken);

#pragma warning disable GW0004 // IDocumentStore compatibility surface delegated by the fault-injection wrapper.
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
            DocumentStoreQuery query,
            CancellationToken cancellationToken = default) => inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.AnyAsync(query, cancellationToken);
#pragma warning restore GW0004

        public async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new UncertainWithoutCommitUnitOfWork(await inner.BeginAsync(scope, cancellationToken));
    }

    private sealed class UncertainWithoutCommitUnitOfWork(IDocumentUnitOfWork inner) : IDocumentUnitOfWork
    {
        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) => inner.SaveAsync(request, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) => inner.DeleteAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) => inner.LoadAsync(documentKind, id, cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            throw new DocumentCommitAcknowledgementUncertainException(
                [ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind]);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            inner.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class ManualTimerTimeProvider : TimeProvider
    {
        private ManualTimer? _timer;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            Assert.Null(_timer);
            return _timer = new ManualTimer(callback, state);
        }

        public void Fire() => (_timer ?? throw new InvalidOperationException("No reconciliation timer was created.")).Fire();

        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;

            public void Fire()
            {
                if (!_disposed)
                    callback(state);
            }

            public void Dispose() => _disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
