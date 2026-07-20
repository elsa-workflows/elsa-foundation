using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
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
    public async Task GroupCommit_ConcurrentCommits_Fold_AndEveryRunPersistsExactlyOneMarker()
    {
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
                // At least one real multi-member fold formed (near-certain at 64 concurrent commits through one gate).
                Assert.True(coordinator.BatchFlushCount > 0, "expected at least one multi-member group commit under high concurrency");
                Assert.True(coordinator.BatchedMemberCount > coordinator.BatchFlushCount, "a batch must fold more than one member per flush");
                // Deterministic correctness: every run persisted exactly one durable marker, none lost or duplicated.
                for (var index = 0; index < concurrency; index++)
                    Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, $"commit-{index}"));
                Assert.Equal(concurrency, await CountCheckpointMarkersAsync(store));
            }
        }
        finally
        {
            DeleteSqliteFiles(dbPath);
        }
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

    private static async Task<int> CountCheckpointMarkersAsync(IDocumentStore store)
    {
#pragma warning disable GW0004
        var result = await store.QueryAsync(new PortableDocumentQuery(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind));
#pragma warning restore GW0004
        return (int)result.TotalCount;
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
