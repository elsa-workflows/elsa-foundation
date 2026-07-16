using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

// Behavioral assertions for the Groundwork checkpoint writer. The writer orchestrates the bridged seam
// stores and a durable per-CommitId marker. These tests prove the three properties that make it a faithful
// (and restart-safe) replacement for the in-memory writer: full multi-seam persistence, durability across a
// simulated process restart, and idempotency under at-least-once redelivery.
public sealed partial class GroundworkRuntimeCheckpointWriterTests
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
    public async Task Commit_Persists_WorkflowDispatch_And_Outbox_In_The_Checkpoint_Boundary()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var writer = CreateWriter(store);
        var commit = BuildCommit("commit-dispatch", includeDispatch: true);
        var outbox = PendingDispatchOutbox("commit-dispatch", "wf-1");
        commit = commit with
        {
            StateChanges = commit.StateChanges.WithPostCommitOutbox(
            [
                Change(outbox.OutboxItemId, RuntimeStateChangeOperation.Upsert, outbox)
            ])
        };

        var result = await writer.CommitAsync(commit, Decision);

        var dispatch = await new GroundworkWorkflowDispatchStore(
            store,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor).FindAsync(DispatchIdentity("wf-1").DispatchId);
        Assert.NotNull(dispatch);
        Assert.Equal([outbox.OutboxItemId], result.PendingPostCommitWorkIds);
        Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind, outbox.OutboxItemId));
        Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, "commit-dispatch"));
    }

    [Fact]
    public async Task Wait_Checkpoint_Persists_Suspension_Bookmark_Dispatch_And_Start_Outbox_Across_Restart_And_Replay()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-dispatch-wait-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        var commit = BuildWaitCommit("commit-dispatch-wait");
        var identity = DispatchIdentity("wf-1");
        try
        {
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
                await CreateWriter(fixture.DocumentStore).CommitAsync(commit, Decision);

            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var writer = CreateWriter(fixture.DocumentStore);
                var replay = await writer.CommitAsync(commit, Decision);
                Assert.Single(replay.PendingPostCommitWorkIds);

                var activity = await new GroundworkActivityExecutionStateStore(
                    fixture.DocumentStore,
                    GroundworkTestSerialization.Serializer).FindAsync("wf-1", "ae-1");
                Assert.NotNull(activity);
                Assert.Equal(ActivityExecutionStatus.Suspended, activity.Status);
                Assert.Equal([identity.WaitBookmarkId], activity.BookmarkIds);

                var bookmark = await new GroundworkBookmarkStateStore(
                    fixture.DocumentStore,
                    GroundworkTestSerialization.Serializer).FindAsync("wf-1", identity.WaitBookmarkId);
                Assert.NotNull(bookmark);
                Assert.Null(bookmark.ExpiresAt);
                Assert.Equal(identity.WaitStimulusHash, bookmark.StimulusHash);

                var dispatch = await new GroundworkWorkflowDispatchStore(
                    fixture.DocumentStore,
                    GroundworkTestSerialization.Serializer,
                    GroundworkTestAccess.DefaultAccessContextAccessor).FindAsync(identity.DispatchId);
                Assert.NotNull(dispatch);
                Assert.Equal(WorkflowDispatchMode.WaitForCompletion, dispatch.Mode);
                Assert.NotNull(await fixture.DocumentStore.LoadAsync(
                    ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
                    RuntimePostCommitOutboxItems.OutboxItemId(commit.CommitId, PendingDispatchOutbox(commit.CommitId, "wf-1").Intent)));

                var conflict = BuildWaitCommit("commit-dispatch-wait", bookmarkNode: "node-conflict");
                await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() => writer.CommitAsync(conflict, Decision).AsTask());
                Assert.Equal("node-ae-1", (await new GroundworkBookmarkStateStore(
                    fixture.DocumentStore,
                    GroundworkTestSerialization.Serializer).FindAsync("wf-1", identity.WaitBookmarkId))!.ExecutableNodeId);
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Committed_Dispatch_Is_Materialized_Once_After_Runtime_Recreation_Before_Delivery()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-dispatch-before-delivery-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
                await CommitDispatchAsync(fixture, "commit-before-delivery");

            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var services = DispatchServices.Create(fixture);
                await NewProcessor(services, DateTimeOffset.UnixEpoch.AddSeconds(1))
                    .ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10));

                await AssertConvergedAsync(services, "commit-before-delivery");
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Expired_Claim_Is_Resumed_Once_After_Runtime_Recreation()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-dispatch-expired-claim-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                await CommitDispatchAsync(fixture, "commit-expired-claim");
                var services = DispatchServices.Create(fixture);
                Assert.Single(await services.Outbox.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                    "crashed-owner",
                    DateTimeOffset.UnixEpoch.AddSeconds(1),
                    TimeSpan.FromMinutes(1),
                    1)));
            }

            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var services = DispatchServices.Create(fixture);
                await NewProcessor(services, DateTimeOffset.UnixEpoch.AddMinutes(2))
                    .ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10));

                await AssertConvergedAsync(services, "commit-expired-claim");
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Materialized_Child_Is_Repaired_Without_A_Second_Logical_Child_After_Runtime_Recreation()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-dispatch-after-materialization-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                await CommitDispatchAsync(fixture, "commit-after-materialization");
                var services = DispatchServices.Create(fixture, crashAfterMaterialization: true);

                await Assert.ThrowsAsync<WorkflowDispatchAdmissionProjectionException>(() =>
                    NewProcessor(services, DateTimeOffset.UnixEpoch.AddSeconds(1))
                        .ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10)).AsTask());

                var dispatch = (await services.Dispatches.FindAsync(DispatchIdentity("wf-1").DispatchId))!;
                Assert.NotNull(await services.Executions.FindAsync(dispatch.ChildWorkflowExecutionId));
                Assert.Equal(WorkflowDispatchStatus.Pending, dispatch.Status);
            }

            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var services = DispatchServices.Create(fixture);
                await NewProcessor(services, DateTimeOffset.UnixEpoch.AddMinutes(2))
                    .ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10));

                await AssertConvergedAsync(services, "commit-after-materialization");
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Dispatch_Retention_Remains_Until_Both_Linked_Executions_Are_Absent_In_Sqlite()
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create("sqlite");
        var services = DispatchServices.Create(fixture);
        var pending = Dispatch("wf-1");
        var terminal = pending
            .TransitionTo(WorkflowDispatchStatus.Started, DateTimeOffset.UnixEpoch.AddSeconds(1))
            .TransitionTo(WorkflowDispatchStatus.Completed, DateTimeOffset.UnixEpoch.AddSeconds(2));
        await services.Dispatches.SaveAsync(pending);
        await services.Dispatches.SaveAsync(terminal);
        await services.Executions.SaveAsync(WorkflowState(pending.ParentWorkflowExecutionId, null));
        await services.Executions.SaveAsync(ChildState(pending));
        var collector = new WorkflowDispatchRetentionCollector(
            services.Dispatches,
            services.Dispatches,
            services.Executions,
            NullLogger<WorkflowDispatchRetentionCollector>.Instance);

        Assert.Equal(1, (await collector.SweepAsync()).RetainedCount);
        Assert.True(await services.Executions.DeleteAsync(pending.ParentWorkflowExecutionId));
        Assert.Equal(1, (await collector.SweepAsync()).RetainedCount);
        Assert.True(await services.Executions.DeleteAsync(pending.ChildWorkflowExecutionId));
        Assert.Equal(1, (await collector.SweepAsync()).DeletedCount);
        Assert.Null(await services.Dispatches.FindAsync(pending.DispatchId));
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
    public async Task ScopeCancellation_CleansResourcesAndTerminalizesBoundaryInOneDurableCommit()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-scope-cancel-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var store = fixture.DocumentStore;
                await new GroundworkActivityExecutionStateStore(store, GroundworkTestSerialization.Serializer)
                    .SaveAsync(ActivityState("wf-1", "ae-1"));
                await new GroundworkBookmarkStateStore(store, GroundworkTestSerialization.Serializer)
                    .SaveAsync(Bookmark("wf-1", "bm-1", "node-bm-1"));
                await new GroundworkDurableTimerStore(store, GroundworkTestSerialization.Serializer)
                    .SaveAsync(new DurableTimer("timer-1", "wf-1", "delivery-status", "sha256:stimulus", DateTimeOffset.UnixEpoch.AddHours(1), DateTimeOffset.UnixEpoch));
                await new GroundworkWorkflowSchedulerWorkQueue(store, GroundworkTestSerialization.Serializer)
                    .EnqueueAsync(new RuntimeSchedulerWorkItem(
                        "work-1", "wf-1", "command-1", WorkflowExecutionCommandKind.ResumeBookmark,
                        "envelope-1", "key-1", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                        executionScopeId: "ae-1"));

                await CreateWriter(store).CommitAsync(BuildScopeCancellationCommit(), Decision);
            }

            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var store = fixture.DocumentStore;
                Assert.Equal(ActivityExecutionStatus.Cancelled,
                    (await new GroundworkActivityExecutionStateStore(store, GroundworkTestSerialization.Serializer).FindAsync("wf-1", "ae-1"))!.Status);
                Assert.Null(await new GroundworkBookmarkStateStore(store, GroundworkTestSerialization.Serializer).FindAsync("wf-1", "bm-1"));
                Assert.Null(await new GroundworkDurableTimerStore(store, GroundworkTestSerialization.Serializer).FindAsync("wf-1", "timer-1"));
                Assert.Empty(await new GroundworkWorkflowSchedulerWorkQueue(store, GroundworkTestSerialization.Serializer)
                    .ListAsync(new RuntimeSchedulerWorkQuery("wf-1")));
                Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, "commit-scope-cancel"));
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
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

    private static async Task CommitDispatchAsync(
        GroundworkDocumentStoreFixture fixture,
        string commitId)
    {
        var commit = BuildCommit(commitId, includeDispatch: true);
        var outbox = PendingDispatchOutbox(commitId, "wf-1");
        commit = commit with
        {
            StateChanges = commit.StateChanges.WithPostCommitOutbox(
            [
                Change(outbox.OutboxItemId, RuntimeStateChangeOperation.Upsert, outbox)
            ])
        };
        await CreateWriter(fixture.DocumentStore).CommitAsync(commit, Decision);
    }

    private static RuntimePostCommitOutboxProcessor NewProcessor(
        DispatchServices services,
        DateTimeOffset now) =>
        new(
            services.Outbox,
            services.Dispatcher,
            new FixedTimeProvider(now),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            services.Dispatches);

    private static async Task AssertConvergedAsync(DispatchServices services, string commitId)
    {
        var dispatch = (await services.Dispatches.FindAsync(DispatchIdentity("wf-1").DispatchId))!;
        Assert.Equal(WorkflowDispatchStatus.Started, dispatch.Status);
        Assert.Equal(
            new[] { dispatch.ParentWorkflowExecutionId, dispatch.ChildWorkflowExecutionId }.Order(StringComparer.Ordinal),
            (await services.Executions.ListAsync())
            .Select(execution => execution.WorkflowExecutionId)
            .Order(StringComparer.Ordinal));
        Assert.Empty(await services.Outbox.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "completion-audit",
            DateTimeOffset.UnixEpoch.AddYears(1),
            TimeSpan.FromMinutes(1),
            10)));
        Assert.NotNull(await services.OutboxDocumentAsync(RuntimePostCommitOutboxItems.OutboxItemId(
            commitId,
            PendingDispatchOutbox(commitId, "wf-1").Intent)));
    }

    private static RuntimeCheckpointCommit BuildCommit(
        string commitId,
        string bookmarkNode = "node-bm-1",
        string? tenantId = null,
        bool includeDispatch = false)
    {
        const string wf = "wf-1";
        var stateChanges = new RuntimeCheckpointStateChangeSet(
            workflowExecution: Change(wf, RuntimeStateChangeOperation.Upsert, WorkflowState(wf, tenantId)),
            scheduler: Change(wf, RuntimeStateChangeOperation.Upsert, Scheduler(wf, 7)),
            activityExecutions: [Change("ae-1", RuntimeStateChangeOperation.Upsert, ActivityState(wf, "ae-1"))],
            bookmarks: [Change("bm-1", RuntimeStateChangeOperation.Upsert, Bookmark(wf, "bm-1", bookmarkNode))],
            durableValues: [Change("dv-1", RuntimeStateChangeOperation.Upsert, DurableValue(wf, "dv-1"))],
            incidents: [Change("inc-1", RuntimeStateChangeOperation.Append, Incident(wf, "inc-1"))],
            operational: [Change("op-1", RuntimeStateChangeOperation.Upsert, Operational(wf, "op-1"))],
            workflowDispatches: includeDispatch
                ? [Change(DispatchIdentity(wf).DispatchId, RuntimeStateChangeOperation.Upsert, Dispatch(wf))]
                : []);

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

    private static RuntimeCheckpointCommit BuildScopeCancellationCommit()
    {
        var cancelled = ActivityState("wf-1", "ae-1") with
        {
            Status = ActivityExecutionStatus.Cancelled,
            SubStatus = "ScopeCancelled",
            CompletedAt = DateTimeOffset.UnixEpoch
        };
        return new RuntimeCheckpointCommit(
            "commit-scope-cancel",
            new RuntimeCheckpoint(
                "checkpoint-scope-cancel", "ActivityCancelled", "wf-1", DateTimeOffset.UnixEpoch,
                ["ae-1"], new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [Change("ae-1", RuntimeStateChangeOperation.Upsert, cancelled)],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityScopeCleanups:
                [
                    new ActivityScopeCleanupRequest(
                        "wf-1", "ae-1", ["ae-1"], ["bm-1"], ["timer-1"], ["work-1"])
                ]),
            [],
            new Dictionary<string, string>());
    }

    private static RuntimeCheckpointCommit BuildWaitCommit(string commitId, string bookmarkNode = "node-ae-1")
    {
        const string workflowExecutionId = "wf-1";
        var identity = DispatchIdentity(workflowExecutionId);
        var activity = ActivityState(workflowExecutionId, "ae-1") with
        {
            Status = ActivityExecutionStatus.Suspended,
            SubStatus = BookmarkSuspension.SuspendedSubStatus,
            BookmarkIds = [identity.WaitBookmarkId]
        };
        var bookmark = Bookmark(workflowExecutionId, identity.WaitBookmarkId, bookmarkNode) with
        {
            StimulusHash = identity.WaitStimulusHash,
            ExpiresAt = null
        };
        var dispatch = Dispatch(workflowExecutionId, WorkflowDispatchMode.WaitForCompletion);
        var outbox = PendingDispatchOutbox(commitId, workflowExecutionId);
        var metadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory
        };
        return new RuntimeCheckpointCommit(
            CommitId: commitId,
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint:{commitId}",
                Name: RuntimeCheckpointNames.BookmarkCreated,
                WorkflowExecutionId: workflowExecutionId,
                OccurredAt: DateTimeOffset.UnixEpoch,
                ActivityExecutionIds: ["ae-1"],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [Change("ae-1", RuntimeStateChangeOperation.Upsert, activity)],
                bookmarks: [Change(identity.WaitBookmarkId, RuntimeStateChangeOperation.Upsert, bookmark)],
                durableValues: [Change("dv-1", RuntimeStateChangeOperation.Upsert, DurableValue(workflowExecutionId, "dv-1"))],
                incidents: [],
                operational: [],
                workflowDispatches: [Change(identity.DispatchId, RuntimeStateChangeOperation.Upsert, dispatch)],
                postCommitOutbox: [Change(outbox.OutboxItemId, RuntimeStateChangeOperation.Upsert, outbox)]),
            PostCommitIntents: [],
            Metadata: metadata);
    }

    private static RuntimeStateChange<T> Change<T>(string stateId, RuntimeStateChangeOperation operation, T state) =>
        new(stateId, operation, state, new Dictionary<string, string>());

    private static WorkflowDispatchIdentity DispatchIdentity(string workflowExecutionId) => new(workflowExecutionId, "ae-1");

    private static RuntimePostCommitOutboxItem PendingDispatchOutbox(string commitId, string workflowExecutionId)
    {
        var identity = DispatchIdentity(workflowExecutionId);
        var intent = new RuntimePostCommitIntent(
            identity.StartIntentId,
            workflowExecutionId,
            "elsa.dispatch-workflow.start-child.v1",
            DateTimeOffset.UnixEpoch,
            "ae-1",
            identity.StartIdempotencyKey,
            JsonSerializer.SerializeToElement(new { dispatchId = identity.DispatchId }));
        return new RuntimePostCommitOutboxItem(
            RuntimePostCommitOutboxItems.OutboxItemId(commitId, intent),
            intent,
            RuntimePostCommitOutboxStatus.Pending,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
    }

    private static WorkflowDispatchRecord Dispatch(
        string workflowExecutionId,
        WorkflowDispatchMode mode = WorkflowDispatchMode.FireAndForget) => new(
        dispatchId: DispatchIdentity(workflowExecutionId).DispatchId,
        parentWorkflowExecutionId: workflowExecutionId,
        parentActivityExecutionId: "ae-1",
        childWorkflowExecutionId: DispatchIdentity(workflowExecutionId).ChildWorkflowExecutionId,
        childExecutable: new WorkflowExecutableIdentity("artifact-child", "definition-child", "version-child", "1", "hash-child"),
        childSource: new WorkflowExecutableSourceProvenance(
            "source-child", "WorkflowDefinitionVersion", "version-child", "1",
            "definition-child", "version-child", "1", "publication-child", "slot-child"),
        mode: mode,
        status: WorkflowDispatchStatus.Pending,
        correlationId: null,
        tenantId: null,
        partition: new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
        runKind: WorkflowRunKind.PublishedRun,
        authority: new WorkflowExecutionAuthoritySnapshot(workflowExecutionId, "initiator-1"),
        inputDescriptors: [],
        createdAt: DateTimeOffset.UnixEpoch,
        updatedAt: DateTimeOffset.UnixEpoch);

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

    private static WorkflowExecutionState ChildState(WorkflowDispatchRecord dispatch) => new(
        dispatch.ChildWorkflowExecutionId,
        dispatch.ChildExecutable,
        WorkflowExecutionStatus.Running,
        SubStatus: null,
        CreatedAt: dispatch.CreatedAt,
        StartedAt: dispatch.CreatedAt,
        UpdatedAt: dispatch.CreatedAt,
        CompletedAt: null,
        dispatch.CorrelationId,
        dispatch.ParentWorkflowExecutionId,
        dispatch.TenantId,
        SystemMetadata: new Dictionary<string, string>())
    {
        PinnedSource = dispatch.ChildSource,
        Partition = dispatch.Partition,
        Authority = dispatch.Authority,
        RunKind = dispatch.RunKind,
        DispatchNestingDepth = dispatch.DispatchNestingDepth
    };

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

    private sealed class DispatchServices
    {
        private DispatchServices(
            GroundworkDocumentStoreFixture fixture,
            bool crashAfterMaterialization)
        {
            Fixture = fixture;
            Dispatches = new GroundworkWorkflowDispatchStore(
                fixture.DocumentStore,
                GroundworkTestSerialization.Serializer,
                GroundworkTestAccess.DefaultAccessContextAccessor,
                fixture.BoundedDocumentStore);
            Executions = new GroundworkWorkflowExecutionStateStore(
                fixture.DocumentStore,
                GroundworkTestSerialization.Serializer,
                GroundworkTestAccess.DefaultAccessContextAccessor);
            Outbox = new GroundworkRuntimePostCommitOutboxStore(
                fixture.DocumentStore,
                GroundworkTestSerialization.Serializer,
                fixture.BoundedDocumentStore,
                GroundworkTestAccess.DefaultAccessContextAccessor);
            Dispatcher = new MaterializingDispatchIntentDispatcher(
                Dispatches,
                Executions,
                crashAfterMaterialization);
        }

        public GroundworkDocumentStoreFixture Fixture { get; }
        public GroundworkWorkflowDispatchStore Dispatches { get; }
        public GroundworkWorkflowExecutionStateStore Executions { get; }
        public GroundworkRuntimePostCommitOutboxStore Outbox { get; }
        public IRuntimePostCommitIntentDispatcher Dispatcher { get; }

        public static DispatchServices Create(
            GroundworkDocumentStoreFixture fixture,
            bool crashAfterMaterialization = false) =>
            new(fixture, crashAfterMaterialization);

        public Task<DocumentEnvelope?> OutboxDocumentAsync(string outboxItemId) =>
            Fixture.DocumentStore.LoadAsync(
                ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
                outboxItemId);
    }

    private sealed class MaterializingDispatchIntentDispatcher(
        GroundworkWorkflowDispatchStore dispatches,
        GroundworkWorkflowExecutionStateStore executions,
        bool crashAfterMaterialization) : IRuntimePostCommitIntentDispatcher
    {
        private int _crashPending = crashAfterMaterialization ? 1 : 0;

        public async ValueTask DispatchAsync(
            RuntimePostCommitIntent intent,
            CancellationToken cancellationToken = default)
        {
            var dispatchId = intent.Payload!.Value.GetProperty("dispatchId").GetString()!;
            var dispatch = await dispatches.FindAsync(dispatchId, cancellationToken)
                ?? throw new InvalidOperationException($"Workflow dispatch '{dispatchId}' was not found.");
            var existing = await executions.FindAsync(dispatch.ChildWorkflowExecutionId, cancellationToken);
            if (existing is null)
                await executions.SaveAsync(ChildState(dispatch), cancellationToken);

            if (Interlocked.Exchange(ref _crashPending, 0) == 1)
            {
                throw new WorkflowDispatchAdmissionProjectionException(
                    dispatch.DispatchId,
                    new InvalidOperationException("simulated process loss after child materialization"));
            }

            var latest = await dispatches.FindAsync(dispatch.DispatchId, cancellationToken)
                ?? throw new InvalidOperationException($"Workflow dispatch '{dispatch.DispatchId}' was not found.");
            if (latest.Status == WorkflowDispatchStatus.Pending)
            {
                await dispatches.SaveAsync(
                    latest.TransitionTo(WorkflowDispatchStatus.Started, DateTimeOffset.UnixEpoch.AddSeconds(2)),
                    cancellationToken);
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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
