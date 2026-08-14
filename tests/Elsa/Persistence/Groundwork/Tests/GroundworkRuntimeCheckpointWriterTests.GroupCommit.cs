using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

// Behavioral assertions for spec-115 cross-drain group commit. The coordinator folds concurrent checkpoint commits that
// are contending for the single durable writer into one shared unit-of-work / one fsync, while preserving every
// per-commit guarantee: 1 durable marker per run, byte-identical state, failure isolation, and no solo regression.
public sealed partial class GroundworkRuntimeCheckpointWriterTests
{
    [Fact]
    public async Task GroupCommit_SoloCommit_IsNotBatched_ButStillPersists()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var coordinator = new RuntimeGroupCommitCoordinator(store, new RuntimeGroupCommitOptions());
        var writer = CreateWriter(store, coordinator);

        await writer.CommitAsync(BuildDistinctCommit(1), Decision);

        Assert.Equal(1, coordinator.SoloFlushCount);
        Assert.Equal(0, coordinator.BatchFlushCount);
        Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, "commit-1"));
    }

    [Fact]
    public async Task GroupCommit_ConcurrentCommits_EveryRunPersistsExactlyOneMarker()
    {
        // Correctness under real concurrency: whatever mix of solo flushes and folds the scheduler happens to produce,
        // every run must land exactly one durable marker and no batch may degrade. Deliberately asserts nothing about
        // the SHAPE of the batching -- how many members actually overlap at the gate is a property of the machine, not
        // of the coordinator, and asserting it here made the test fail deterministically on a 2-core runner (#1266).
        // Fold formation is pinned separately, and deterministically, by
        // GroupCommit_MembersQueuedBehindAnInFlightFlush_FoldIntoOneSharedTransaction.
        const int concurrency = 64;
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-groupcommit-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var store = fixture.DocumentStore;
                var coordinator = new RuntimeGroupCommitCoordinator(store, new RuntimeGroupCommitOptions());

                // One shared coordinator across many concurrent writers over one store mirrors the production host, where
                // every scoped drain resolves the same singleton coordinator.
                await RunConcurrentCommitsAsync(store, coordinator, Enumerable.Range(0, concurrency).ToArray());

                Assert.Equal(0, coordinator.DegradedBatchCount);
                // Deterministic correctness: every run persisted exactly one durable marker, none lost or duplicated.
                for (var index = 0; index < concurrency; index++)
                    Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, $"commit-{index}"));
                Assert.Equal(concurrency, await CountCheckpointMarkersAsync(store));
                // Every commit was accounted for by exactly one flush, solo or folded.
                Assert.Equal(
                    concurrency,
                    coordinator.SoloFlushCount + coordinator.BatchedMemberCount);
            }
        }
        finally
        {
            DeleteSqliteFiles(dbPath);
        }
    }

    [Fact]
    public async Task GroupCommit_MembersQueuedBehindAnInFlightFlush_FoldIntoOneSharedTransaction()
    {
        // Fold formation, constructed rather than raced for. The coordinator folds whoever is ALREADY queued when a
        // leader takes the gate, so the precondition is "members enqueued while a flush is in flight" -- not "N tasks
        // running at once", which is what the machine gets to decide.
        //
        // Construction: the first member becomes the leader, finds itself alone, and blocks inside its solo flush while
        // still holding the gate. Every later SubmitAsync then enqueues SYNCHRONOUSLY -- the enqueue precedes the first
        // await, so the member is in the queue by the time the call returns -- and parks on the gate. Releasing the
        // leader lets the next leader drain all of them into one shared unit-of-work. Nothing here depends on the
        // thread pool running anything in parallel, so it holds at DOTNET_PROCESSOR_COUNT=1.
        const int followers = 8;
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var coordinator = new RuntimeGroupCommitCoordinator(store, new RuntimeGroupCommitOptions());
        var leaderIsHoldingTheGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLeader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var leader = SubmitMarkerAsync(coordinator, store, 0, async token =>
        {
            leaderIsHoldingTheGate.SetResult();
            await releaseLeader.Task.WaitAsync(token);
        }).AsTask();
        await leaderIsHoldingTheGate.Task;

        var queued = new List<Task<RuntimeCheckpointCommitStoreResult>>();
        for (var index = 1; index <= followers; index++)
            queued.Add(SubmitMarkerAsync(coordinator, store, index).AsTask());

        releaseLeader.SetResult();
        await leader;
        await Task.WhenAll(queued);

        // The leader flushed alone; everyone queued behind it folded into shared transactions, and at least one of
        // those carried more than one member (that difference is the fsyncs group commit actually saved).
        Assert.Equal(1, coordinator.SoloFlushCount);
        Assert.Equal(0, coordinator.DegradedBatchCount);
        Assert.True(coordinator.BatchFlushCount >= 1, "the queued members must have folded into at least one shared transaction");
        Assert.Equal(followers, coordinator.BatchedMemberCount);
        Assert.True(
            coordinator.BatchedMemberCount > coordinator.BatchFlushCount,
            "a batch must fold more than one member per flush");

        // The folded writes are durable, not merely counted.
        for (var index = 0; index <= followers; index++)
            Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, $"fold-{index}"));
    }

    // Submits one marker write to the coordinator. The staged and solo paths write the same document, so a member's
    // marker lands exactly once whether it was folded into a shared unit-of-work or flushed on its own.
    private static ValueTask<RuntimeCheckpointCommitStoreResult> SubmitMarkerAsync(
        RuntimeGroupCommitCoordinator coordinator,
        IDocumentStore store,
        int index,
        Func<CancellationToken, Task>? beforeSoloFlush = null) =>
        coordinator.SubmitAsync(
            batchKey: string.Empty,
            DocumentCommitScope.Of(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind),
            async (transactionalStore, token) => await WriteFoldMarkerAsync(transactionalStore, index, token),
            async token =>
            {
                if (beforeSoloFlush is not null)
                    await beforeSoloFlush(token);
                return await WriteFoldMarkerAsync(store, index, token);
            },
            CancellationToken.None);

    private static async ValueTask<RuntimeCheckpointCommitStoreResult> WriteFoldMarkerAsync(
        IDocumentStore store,
        int index,
        CancellationToken cancellationToken)
    {
        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
                $"fold-{index}",
                "1.0.0",
                /*lang=json,strict*/ """{"collection":"checkpointCommit"}""",
                ExpectedVersion: 0),
            cancellationToken);
        return new RuntimeCheckpointCommitStoreResult([]);
    }

    [Fact]
    public async Task GroupCommit_PoisonedMember_DegradesBatch_AndEveryValidRunStillPersistsExactlyOnce()
    {
        // A stale-fence commit throws inside the shared unit-of-work, poisoning the whole batch. Failure isolation
        // (FR-4) requires that every OTHER member still commits exactly once and only the poisoned member fails.
        const int validCount = 24;
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-groupcommit-degrade-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var store = fixture.DocumentStore;
                var coordinator = new RuntimeGroupCommitCoordinator(store, new RuntimeGroupCommitOptions());
                var writer = CreateWriter(store, coordinator);

                var validIndexes = Enumerable.Range(0, validCount).ToArray();
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var valid = validIndexes.Select(index => Task.Run(async () =>
                {
                    var runWriter = CreateWriter(store, coordinator);
                    await start.Task;
                    await runWriter.CommitAsync(BuildDistinctCommit(index), Decision);
                })).ToArray();
                var poisoned = Task.Run(async () =>
                {
                    var runWriter = CreateWriter(store, coordinator);
                    await start.Task;
                    await runWriter.CommitAsync(BuildStaleFenceCommit(9000), Decision);
                });
                start.SetResult();

                await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => poisoned);
                await Task.WhenAll(valid);

                // Every valid run committed exactly once despite sharing (or degrading out of) a batch with the poison.
                foreach (var index in validIndexes)
                    Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, $"commit-{index}"));
                Assert.Null(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, "commit-9000"));
                Assert.Equal(validCount, await CountCheckpointMarkersAsync(store));
            }
        }
        finally
        {
            DeleteSqliteFiles(dbPath);
        }
    }

    private static async Task RunConcurrentCommitsAsync(IDocumentStore store, RuntimeGroupCommitCoordinator coordinator, IReadOnlyList<int> indexes)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = indexes.Select(index => Task.Run(async () =>
        {
            var writer = CreateWriter(store, coordinator);
            await start.Task;
            await writer.CommitAsync(BuildDistinctCommit(index), Decision);
        })).ToArray();
        start.SetResult();
        await Task.WhenAll(tasks);
    }

    // Counted through the ledger's declared list-all route rather than an unbounded scan; the page bound is
    // far above the marker count any of these commits produce.
    private const int MarkerPageBound = 1024;

    private static async Task<int> CountCheckpointMarkersAsync(IDocumentStore store)
    {
        var result = await ((IBoundedDocumentStore)store).QueryAsync(new DocumentQuery(
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
            ElsaRuntimeStorageManifest.ListCheckpointCommitsQuery,
            [
                DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                    ElsaRuntimeStorageManifest.CollectionField,
                    ElsaRuntimeStorageManifest.CheckpointCommitCollection))
            ],
            take: MarkerPageBound));
        return result.Documents.Count;
    }

    private static void DeleteSqliteFiles(string dbPath)
    {
        foreach (var path in new[] { dbPath, $"{dbPath}-wal", $"{dbPath}-shm" })
            if (File.Exists(path))
                File.Delete(path);
    }

    private static GroundworkRuntimeCheckpointWriter CreateWriter(
        IDocumentStore store,
        RuntimeGroupCommitCoordinator groupCommitCoordinator,
        IPersistenceAccessContextAccessor? accessContextAccessor = null)
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
            timeProvider: null,
            groupCommitCoordinator);
    }

    private static RuntimeCheckpointCommit BuildDistinctCommit(int index, string? tenantId = null) =>
        BuildDistinctCommit(index.ToString(), tenantId, index);

    private static RuntimeCheckpointCommit BuildStaleFenceCommit(int index) =>
        BuildDistinctCommit(index) with { ExpectedFence = new RuntimeExecutionFence("lease-stale", "owner-stale", 1) };

    private static RuntimeCheckpointCommit BuildDistinctCommit(string tag, string? tenantId, int seed)
    {
        var wf = $"wf-{tag}";
        var ae = $"ae-{tag}";
        var stateChanges = new RuntimeCheckpointStateChangeSet(
            workflowExecution: Change(wf, RuntimeStateChangeOperation.Upsert, WorkflowState(wf, tenantId)),
            scheduler: Change(wf, RuntimeStateChangeOperation.Upsert, Scheduler(wf, 7)),
            activityExecutions: [Change(ae, RuntimeStateChangeOperation.Upsert, ActivityState(wf, ae))],
            bookmarks: [Change($"bm-{tag}", RuntimeStateChangeOperation.Upsert, Bookmark(wf, $"bm-{tag}", $"node-{tag}"))],
            durableValues: [Change($"dv-{tag}", RuntimeStateChangeOperation.Upsert, DurableValue(wf, $"dv-{tag}"))],
            incidents: [Change($"inc-{tag}", RuntimeStateChangeOperation.Append, Incident(wf, $"inc-{tag}"))],
            operational: [Change($"op-{tag}", RuntimeStateChangeOperation.Upsert, Operational(wf, $"op-{tag}"))]);

        var checkpoint = new RuntimeCheckpoint(
            CheckpointId: $"cp-{tag}",
            Name: "checkpoint",
            WorkflowExecutionId: wf,
            OccurredAt: DateTimeOffset.UnixEpoch,
            ActivityExecutionIds: [ae],
            Metadata: new Dictionary<string, string>());

        return new RuntimeCheckpointCommit(
            $"commit-{tag}",
            checkpoint,
            stateChanges,
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());
    }
}
