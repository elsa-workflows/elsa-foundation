using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Store;
using Groundwork.MongoDb.Documents;
using Groundwork.PostgreSql.Documents;
using Groundwork.Sqlite.Documents;
using Groundwork.SqlServer.Documents;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class RuntimeFenceContractTests
{
    private const string WorkflowExecutionId = "wf-fence-contract";
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Providers => new()
    {
        "sqlite",
        "sqlserver",
        "postgresql",
        "mongodb"
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Runtime_fence_contract_is_enforced_by_every_persistent_provider(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions);

        await driver.ResetPhysicalAsync();
        await AssertPhysicalClientsAsync(driver, providerKey);
        await AssertConcurrentAllocationAsync(driver);

        await driver.ResetPhysicalAsync();
        await AssertConditionalHeartbeatReleaseAndReopenAsync(driver);

        await driver.ResetPhysicalAsync();
        await AssertCheckpointFenceAndReplayAtomicityAsync(driver);
    }

    private static async Task AssertPhysicalClientsAsync(GroundworkProviderDriver driver, string providerKey)
    {
        await using var first = await driver.OpenPhysicalClientAsync();
        await using var second = await driver.OpenPhysicalClientAsync();
        var expectedType = providerKey switch
        {
            "sqlite" => typeof(SqlitePhysicalDocumentStore),
            "sqlserver" => typeof(SqlServerPhysicalDocumentStore),
            "postgresql" => typeof(PostgreSqlPhysicalDocumentStore),
            "mongodb" => typeof(MongoDbPhysicalDocumentStore),
            _ => throw new ArgumentOutOfRangeException(nameof(providerKey), providerKey, "Unknown Groundwork provider.")
        };

        Assert.Equal(expectedType, first.DocumentStore.GetType());
        Assert.Equal(expectedType, second.DocumentStore.GetType());
        Assert.Equal(TransactionBoundary.CrossUnitAtomic, first.DocumentStore.TransactionBoundary);
        Assert.Equal(TransactionBoundary.CrossUnitAtomic, second.DocumentStore.TransactionBoundary);
        Assert.NotSame(first.DocumentStore, second.DocumentStore);
    }

    private static async Task AssertConcurrentAllocationAsync(GroundworkProviderDriver driver)
    {
        await using var firstClient = await driver.OpenPhysicalClientAsync();
        await using var secondClient = await driver.OpenPhysicalClientAsync();
        var first = Ownership(firstClient.DocumentStore, "owner-a");
        var second = Ownership(secondClient.DocumentStore, "owner-b");

        var leases = await Task.WhenAll(
            Enumerable.Range(0, 16)
                .Select(index => (index & 1) == 0
                    ? first.AcquireAsync(WorkflowExecutionId).AsTask()
                    : second.AcquireAsync(WorkflowExecutionId).AsTask()));

        Assert.Equal(Enumerable.Range(1, 16).Select(x => (long)x), leases.Select(x => x.FencingToken).Order());
        var state = await Liveness(firstClient.DocumentStore).FindAsync(
            WorkflowExecutionId,
            $"ownership:{WorkflowExecutionId}");
        Assert.Equal(16, state!.ExecutionLease!.FencingToken);
    }

    private static async Task AssertConditionalHeartbeatReleaseAndReopenAsync(GroundworkProviderDriver driver)
    {
        long releasedToken;
        await using (var firstClient = await driver.OpenPhysicalClientAsync())
        await using (var secondClient = await driver.OpenPhysicalClientAsync())
        {
            var first = Ownership(firstClient.DocumentStore, "owner-a");
            var secondClock = new MutableTimeProvider(Now);
            var second = Ownership(secondClient.DocumentStore, "owner-b", secondClock);
            var stale = await first.AcquireAsync(WorkflowExecutionId);
            var current = await second.AcquireAsync(WorkflowExecutionId);

            secondClock.Now = Now.AddMinutes(1);
            var renewed = await second.HeartbeatAsync(current);
            await using var heartbeatReopenedClient = await driver.OpenPhysicalClientAsync();
            var renewedState = await Liveness(heartbeatReopenedClient.DocumentStore).FindAsync(
                WorkflowExecutionId,
                $"ownership:{WorkflowExecutionId}");

            Assert.Equal(RuntimeExecutionOwnershipTransitionStatus.Applied, renewed.Status);
            Assert.Equal(current.LeaseId, renewedState!.ExecutionLease!.LeaseId);
            Assert.Equal(current.OwnerId, renewedState.ExecutionLease.OwnerId);
            Assert.Equal(current.FencingToken, renewedState.ExecutionLease.FencingToken);
            Assert.Equal(secondClock.Now, renewedState.Heartbeat!.RecordedAt);
            Assert.True(renewedState.ExecutionLease.ExpiresAt > current.ExpiresAt);

            var heartbeat = await first.HeartbeatAsync(stale);
            var release = await first.ReleaseAsync(stale);
            var state = await Liveness(firstClient.DocumentStore).FindAsync(
                WorkflowExecutionId,
                $"ownership:{WorkflowExecutionId}");

            Assert.Equal(RuntimeExecutionOwnershipTransitionStatus.Stale, heartbeat.Status);
            Assert.Equal(RuntimeExecutionOwnershipTransitionStatus.Stale, release.Status);
            Assert.Equal(current.LeaseId, state!.ExecutionLease!.LeaseId);
            Assert.Equal(RuntimeExecutionOwnershipTransitionStatus.Applied, (await second.ReleaseAsync(current)).Status);
            await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
                () => second.EnsureCurrentAsync(WorkflowExecutionId, current.FencingToken).AsTask());
            releasedToken = current.FencingToken;
        }

        await using var reopenedClient = await driver.OpenPhysicalClientAsync();
        var reopened = await Ownership(reopenedClient.DocumentStore, "owner-c").AcquireAsync(WorkflowExecutionId);
        Assert.True(reopened.FencingToken > releasedToken);
    }

    private static async Task AssertCheckpointFenceAndReplayAtomicityAsync(GroundworkProviderDriver driver)
    {
        await using var firstClient = await driver.OpenPhysicalClientAsync();
        await using var secondClient = await driver.OpenPhysicalClientAsync();
        var first = Ownership(firstClient.DocumentStore, "owner-a");
        var second = Ownership(secondClient.DocumentStore, "owner-b");
        var stale = await first.AcquireAsync(WorkflowExecutionId);
        var current = await second.AcquireAsync(WorkflowExecutionId);
        var staleCommit = Commit("commit-stale", "node-stale", stale.ToFence());
        var firstWriter = Writer(firstClient.DocumentStore);

        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(async () =>
            await firstWriter.CommitAsync(staleCommit, Decision));

        await AssertBundleAbsentAsync(firstClient.DocumentStore, staleCommit);

        var currentCommit = Commit("commit-current", "node-current", current.ToFence());
        var secondWriter = Writer(secondClient.DocumentStore);
        var results = await Task.WhenAll(
            firstWriter.CommitAsync(currentCommit, Decision).AsTask(),
            secondWriter.CommitAsync(currentCommit, Decision).AsTask());
        Assert.All(results, result => Assert.Equal(new[] { OutboxId(currentCommit) }, result.PendingPostCommitWorkIds));
        await AssertBundlePresentAsync(firstClient.DocumentStore, currentCommit);

        var replay = await secondWriter.CommitAsync(currentCommit, Decision);
        Assert.Equal(new[] { OutboxId(currentCommit) }, replay.PendingPostCommitWorkIds);

        var conflicting = Commit(currentCommit.CommitId, "node-conflict", current.ToFence());
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(async () =>
            await secondWriter.CommitAsync(conflicting, Decision));
        await AssertBundlePresentAsync(secondClient.DocumentStore, currentCommit);
    }

    private static RuntimeExecutionOwnershipService Ownership(
        IDocumentStore store,
        string ownerId,
        TimeProvider? timeProvider = null) =>
        new(
            Liveness(store),
            timeProvider ?? new FixedTimeProvider(Now),
            new RuntimeExecutionOwnershipOptions
            {
                OwnerId = ownerId,
                LeaseDuration = TimeSpan.FromMinutes(5)
            });

    private static GroundworkExecutionLivenessStateStore Liveness(IDocumentStore store) =>
        new(store, GroundworkProviderTestSerialization.Serializer);

    private static GroundworkRuntimeCheckpointWriter Writer(IDocumentStore store) =>
        new(
            store,
            GroundworkProviderTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor,
            new GroundworkWorkflowExecutionStateStore(
                store,
                GroundworkProviderTestSerialization.Serializer,
                GroundworkTestAccess.DefaultAccessContextAccessor),
            new GroundworkSchedulerStateStore(store, GroundworkProviderTestSerialization.Serializer),
            new GroundworkActivityExecutionStateStore(store, GroundworkProviderTestSerialization.Serializer),
            new GroundworkBookmarkStateStore(store, GroundworkProviderTestSerialization.Serializer),
            new GroundworkDurableValueStateStore(store, GroundworkProviderTestSerialization.Serializer),
            new GroundworkIncidentStateStore(store, GroundworkProviderTestSerialization.Serializer),
            Liveness(store),
            PassThroughRootWriteLeaseManager.Instance,
            new FixedTimeProvider(Now));

    private static RuntimeCheckpointCommit Commit(string commitId, string nodeId, RuntimeExecutionFence fence)
    {
        var intent = new RuntimePostCommitIntent(
            intentId: "intent-1",
            workflowExecutionId: WorkflowExecutionId,
            kind: "runtime-fence-contract",
            recordedAt: Now,
            activityExecutionId: null,
            idempotencyKey: "intent-1",
            payload: null);
        var changes = new RuntimeCheckpointStateChangeSet(
            workflowExecution: null,
            scheduler: null,
            activityExecutions: [],
            bookmarks:
            [
                new RuntimeStateChange<BookmarkState>(
                    "bookmark-1",
                    RuntimeStateChangeOperation.Upsert,
                    new BookmarkState(
                        BookmarkId: "bookmark-1",
                        WorkflowExecutionId: WorkflowExecutionId,
                        ActivityExecutionId: "activity-1",
                        ExecutableNodeId: nodeId,
                        ResumeTargetId: "resume-1",
                        StimulusType: "contract",
                        StimulusHash: "sha256:contract",
                        Payload: null,
                        Metadata: new Dictionary<string, string>(),
                        CreatedAt: Now,
                        ExpiresAt: null),
                    new Dictionary<string, string>())
            ],
            durableValues: [],
            incidents: [],
            operational: []);
        var commit = new RuntimeCheckpointCommit(
            commitId,
            new RuntimeCheckpoint(
                $"checkpoint:{commitId}",
                "runtime-fence-contract",
                WorkflowExecutionId,
                Now,
                [],
                new Dictionary<string, string>()),
            changes,
            [intent],
            new Dictionary<string, string>())
        {
            ExpectedFence = fence
        };

        return commit with
        {
            StateChanges = commit.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(commit))
        };
    }

    private static async Task AssertBundleAbsentAsync(IDocumentStore store, RuntimeCheckpointCommit commit)
    {
        Assert.Null(await new GroundworkBookmarkStateStore(store, GroundworkProviderTestSerialization.Serializer)
            .FindAsync(WorkflowExecutionId, "bookmark-1"));
        Assert.Null(await store.LoadAsync(
            ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
            OutboxId(commit)));
        Assert.Null(await store.LoadAsync(
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
            commit.CommitId));
    }

    private static async Task AssertBundlePresentAsync(IDocumentStore store, RuntimeCheckpointCommit commit)
    {
        var bookmark = await new GroundworkBookmarkStateStore(store, GroundworkProviderTestSerialization.Serializer)
            .FindAsync(WorkflowExecutionId, "bookmark-1");
        Assert.Equal(commit.StateChanges.Bookmarks.Single().State.ExecutableNodeId, bookmark!.ExecutableNodeId);
        Assert.NotNull(await store.LoadAsync(
            ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
            OutboxId(commit)));
        Assert.NotNull(await store.LoadAsync(
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
            commit.CommitId));
    }

    private static string OutboxId(RuntimeCheckpointCommit commit) =>
        RuntimePostCommitOutboxItems.OutboxItemId(commit.CommitId, commit.PostCommitIntents.Single());

    private static RuntimeCheckpointPersistenceDecision Decision { get; } =
        new(RuntimeCheckpointPersistenceMode.Immediate);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class PassThroughRootWriteLeaseManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public static PassThroughRootWriteLeaseManager Instance { get; } = new();

        public ValueTask ExecuteAsync(
            string artifactId,
            string leaseId,
            Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) =>
            write(cancellationToken);
    }
}
