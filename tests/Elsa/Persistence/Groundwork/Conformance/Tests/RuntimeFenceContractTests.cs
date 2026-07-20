using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Exceptions;
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
using System.Text.Json;
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

        foreach (var (window, decisionIsDurable) in CheckpointFailureWindows())
            await AssertCheckpointBundleFailureAndReopenAsync(driver, window, decisionIsDurable);
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

    /// <summary>
    /// Exercises the same multi-document checkpoint commit used by the production writer around every named
    /// provider decision boundary. This is intentionally a real provider test: the fault decorator only controls
    /// when the provider acknowledgement is interrupted; the underlying transaction, reopen, and replay all run
    /// against the selected physical provider.
    /// </summary>
    private static async Task AssertCheckpointBundleFailureAndReopenAsync(
        GroundworkProviderDriver driver,
        string windowId,
        bool decisionIsDurable)
    {
        await driver.ResetPhysicalAsync();
        var commit = FullBundleCommit($"checkpoint-failure-{windowId}");
        RuntimeExecutionFence expectedFence;
        long initialOwnershipVersion;

        await using (var client = await driver.OpenPhysicalClientAsync())
        {
            var ownership = await Ownership(client.DocumentStore, "owner-checkpoint-failure")
                .AcquireAsync(commit.WorkflowExecutionId);
            expectedFence = ownership.ToFence();
            initialOwnershipVersion = (await client.DocumentStore.LoadAsync(
                ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
                DocumentId.Compose(commit.WorkflowExecutionId, OwnershipStateId(commit.WorkflowExecutionId))))!.Version;
            commit = commit with { ExpectedFence = expectedFence };

            var failures = new GroundworkFailureController();
            failures.FailAt(new GroundworkFailureWindow(windowId));
            var decorated = new GroundworkFailureInjectingDocumentStore(
                client.DocumentStore,
                client.BoundedDocumentStore!,
                client.DocumentStore.Access,
                failures,
                GroundworkDocumentStoreFailureWindows.OperationalRuntime);

            var exception = await Assert.ThrowsAsync<GroundworkRuntimeCheckpointWriterException>(async () =>
                await Writer(decorated).CommitAsync(commit, Decision));
            Assert.IsType<InjectedGroundworkFailureException>(exception.InnerException);
        }

        await using (var reopenedClient = await driver.OpenPhysicalClientAsync())
        {
            await AssertFullBundleAsync(
                reopenedClient.DocumentStore,
                commit,
                decisionIsDurable,
                expectedFence,
                initialOwnershipVersion + (decisionIsDurable ? 1 : 0));

            // Before a durable decision, this is the first successful commit. After it, this is a replay. Both
            // routes must converge to one complete bundle after the physical client is reopened.
            var result = await Writer(reopenedClient.DocumentStore).CommitAsync(commit, Decision);
            Assert.Equal(new[] { OutboxId(commit) }, result.PendingPostCommitWorkIds);
            await AssertFullBundleAsync(
                reopenedClient.DocumentStore,
                commit,
                present: true,
                expectedFence,
                initialOwnershipVersion + 1);
        }
    }

    private static IReadOnlyList<(string WindowId, bool DecisionIsDurable)> CheckpointFailureWindows()
    {
        var windows = GroundworkDocumentStoreFailureWindows.OperationalRuntime;
        return
        [
            (windows.BeforeProviderDecision.Value, false),
            (windows.DuringProviderDecision!.Value.Value, true),
            (windows.AfterDurableDecision.Value, true)
        ];
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

    private static RuntimeCheckpointCommit FullBundleCommit(string commitId)
    {
        const string workflowExecutionId = "wf-full-checkpoint-bundle";
        var activity = new ActivityExecutionState(
            new ActivityExecution("activity-full", workflowExecutionId, "node-full", "authored-full", "Elsa.Log", "1.0.0"),
            ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: Now,
            StartedAt: Now,
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
            Metadata: new Dictionary<string, string>())
        {
            ExecutionScopeId = "activity-full"
        };
        var inspection = ActivityExecutionInspectionProjection.FromState(activity, $"checkpoint:{commitId}", Now);
        var intent = new RuntimePostCommitIntent(
            "intent-full",
            workflowExecutionId,
            "runtime-fence-contract",
            Now,
            activity.Execution.ActivityExecutionId,
            "intent-full",
            payload: null);
        var stateChanges = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>(
                workflowExecutionId,
                RuntimeStateChangeOperation.Upsert,
                new WorkflowExecutionState(
                    workflowExecutionId,
                    new WorkflowExecutableIdentity("artifact-full", "definition-full", "version-full", "1", "hash-full"),
                    WorkflowExecutionStatus.Running,
                    SubStatus: null,
                    CreatedAt: Now,
                    StartedAt: Now,
                    UpdatedAt: Now,
                    CompletedAt: null,
                    CorrelationId: null,
                    ParentWorkflowExecutionId: null,
                    TenantId: null,
                    SystemMetadata: new Dictionary<string, string>()),
                new Dictionary<string, string>()),
            new RuntimeStateChange<SchedulerState>(
                workflowExecutionId,
                RuntimeStateChangeOperation.Upsert,
                new SchedulerState(workflowExecutionId, version: 1, pendingWork: []),
                new Dictionary<string, string>()),
            [new RuntimeStateChange<ActivityExecutionState>(activity.Execution.ActivityExecutionId, RuntimeStateChangeOperation.Upsert, activity, new Dictionary<string, string>())],
            [new RuntimeStateChange<BookmarkState>(
                "bookmark-full",
                RuntimeStateChangeOperation.Upsert,
                new BookmarkState("bookmark-full", workflowExecutionId, activity.Execution.ActivityExecutionId, "node-full", "resume-full", "contract", "sha256:full", null, new Dictionary<string, string>(), Now, null),
                new Dictionary<string, string>())],
            [new RuntimeStateChange<DurableValueState>(
                "value-full",
                RuntimeStateChangeOperation.Upsert,
                new DurableValueState(
                    "value-full",
                    workflowExecutionId,
                    "value-full",
                    new RuntimeValueTypeDescriptor("int", null, null),
                    DurableValueLifecycle.Instance,
                    DurableValueStorage.Inline,
                    Json("42"),
                    externalReference: null,
                    sourceActivityExecutionId: activity.Execution.ActivityExecutionId,
                    capturedAt: Now,
                    metadata: new Dictionary<string, string>()),
                new Dictionary<string, string>())],
            [new RuntimeStateChange<IncidentState>(
                "incident-full",
                RuntimeStateChangeOperation.Append,
                new IncidentState("incident-full", workflowExecutionId, activity.Execution.ActivityExecutionId, "node-full", IncidentSeverity.Error, IncidentStatus.Open, IncidentResolutionAction.None, "System.Exception", "full bundle", Now, null),
                new Dictionary<string, string>())],
            [new RuntimeStateChange<ExecutionLivenessState>(
                "operational-full",
                RuntimeStateChangeOperation.Upsert,
                new ExecutionLivenessState("operational-full", workflowExecutionId, executionLease: null, heartbeat: null, drain: null, interruptedExecution: null),
                new Dictionary<string, string>())],
            workflowDispatches: [],
            activityExecutionInspections:
            [new RuntimeStateChange<ActivityExecutionInspectionProjection>(inspection.ActivityExecutionId, RuntimeStateChangeOperation.Upsert, inspection, new Dictionary<string, string>())]);
        var commit = new RuntimeCheckpointCommit(
            commitId,
            new RuntimeCheckpoint($"checkpoint:{commitId}", "runtime-fence-contract", workflowExecutionId, Now, [activity.Execution.ActivityExecutionId], new Dictionary<string, string>()),
            stateChanges,
            [intent],
            new Dictionary<string, string>());

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

    private static async Task AssertFullBundleAsync(
        IDocumentStore store,
        RuntimeCheckpointCommit commit,
        bool present,
        RuntimeExecutionFence expectedFence,
        long expectedOwnershipVersion)
    {
        const string workflowExecutionId = "wf-full-checkpoint-bundle";
        var serializer = GroundworkProviderTestSerialization.Serializer;
        var workflow = await new GroundworkWorkflowExecutionStateStore(store, serializer, GroundworkTestAccess.DefaultAccessContextAccessor)
            .FindAsync(workflowExecutionId);
        var activity = await new GroundworkActivityExecutionStateStore(store, serializer)
            .FindAsync(workflowExecutionId, "activity-full");
        var inspection = await new GroundworkActivityExecutionInspectionStore(store, serializer)
            .FindAsync(workflowExecutionId, "activity-full");
        var hierarchy = await store.LoadAsync(
            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind,
            DocumentId.Compose(workflowExecutionId, "activity-full"));
        var scheduler = await new GroundworkSchedulerStateStore(store, serializer)
            .FindAsync(workflowExecutionId);
        var bookmark = await new GroundworkBookmarkStateStore(store, serializer)
            .FindAsync(workflowExecutionId, "bookmark-full");
        var value = await new GroundworkDurableValueStateStore(store, serializer)
            .FindAsync(workflowExecutionId, "value-full");
        var incident = await new GroundworkIncidentStateStore(store, serializer)
            .FindAsync(workflowExecutionId, "incident-full");
        var liveness = await Liveness(store).FindAsync(workflowExecutionId, "operational-full");
        var ownership = await Liveness(store).FindAsync(workflowExecutionId, OwnershipStateId(workflowExecutionId));
        var ownershipEnvelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
            DocumentId.Compose(workflowExecutionId, OwnershipStateId(workflowExecutionId)));
        var marker = await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, commit.CommitId);
        var outbox = await store.LoadAsync(ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind, OutboxId(commit));

        Assert.NotNull(ownership);
        Assert.Equal(expectedFence.LeaseId, ownership!.ExecutionLease!.LeaseId);
        Assert.Equal(expectedFence.OwnerId, ownership.ExecutionLease.OwnerId);
        Assert.Equal(expectedFence.FencingToken, ownership.ExecutionLease.FencingToken);
        Assert.NotNull(ownershipEnvelope);
        Assert.Equal(expectedOwnershipVersion, ownershipEnvelope!.Version);

        if (!present)
        {
            Assert.Null(workflow);
            Assert.Null(activity);
            Assert.Null(inspection);
            Assert.Null(hierarchy);
            Assert.Null(scheduler);
            Assert.Null(bookmark);
            Assert.Null(value);
            Assert.Null(incident);
            Assert.Null(liveness);
            Assert.Null(marker);
            Assert.Null(outbox);
            return;
        }

        Assert.Equal(workflowExecutionId, workflow!.WorkflowExecutionId);
        Assert.Equal("activity-full", activity!.Execution.ActivityExecutionId);
        Assert.Equal("activity-full", inspection!.ActivityExecutionId);
        Assert.NotNull(hierarchy);
        Assert.Equal(workflowExecutionId, scheduler!.WorkflowExecutionId);
        Assert.Equal("bookmark-full", bookmark!.BookmarkId);
        Assert.Equal("value-full", value!.DurableValueId);
        Assert.Equal("incident-full", incident!.IncidentId);
        Assert.Equal("operational-full", liveness!.OperationalStateId);
        Assert.NotNull(marker);
        Assert.NotNull(outbox);
    }

    private static string OwnershipStateId(string workflowExecutionId) => $"ownership:{workflowExecutionId}";

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
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
