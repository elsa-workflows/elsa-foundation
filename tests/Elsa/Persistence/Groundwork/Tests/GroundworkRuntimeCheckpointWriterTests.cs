using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
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
                await writer.WriteAsync(BuildCommit("commit-1"), Decision);
            }

            // Second "process": a brand-new store over the same database file. Nothing is in memory, so anything
            // we can read back was genuinely persisted by the first process.
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var store = fixture.DocumentStore;
                Assert.Equal(WorkflowExecutionStatus.Running, (await new GroundworkWorkflowExecutionStateStore(store).FindAsync("wf-1"))!.Status);
                Assert.Equal(7L, (await new GroundworkSchedulerStateStore(store).FindAsync("wf-1"))!.Version);
                Assert.NotNull(await new GroundworkActivityExecutionStateStore(store).FindAsync("wf-1", "ae-1"));
                Assert.NotNull(await new GroundworkBookmarkStateStore(store).FindAsync("wf-1", "bm-1"));
                Assert.NotNull(await new GroundworkDurableValueStateStore(store).FindAsync("wf-1", "dv-1"));
                Assert.NotNull(await new GroundworkOperationalStateStore(store).FindAsync("wf-1", "op-1"));
                Assert.Equal(IncidentStatus.Open, (await new GroundworkIncidentStateStore(store).FindAsync("wf-1", "inc-1"))!.Status);

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

        await writer.WriteAsync(BuildCommit("commit-1", bookmarkNode: "node-v1"), Decision);
        // Same CommitId, different content. The marker must short-circuit the second write so the original wins.
        await writer.WriteAsync(BuildCommit("commit-1", bookmarkNode: "node-v2"), Decision);

        var bookmark = await new GroundworkBookmarkStateStore(store).FindAsync("wf-1", "bm-1");
        Assert.Equal("node-v1", bookmark!.ExecutableNodeId);
    }

    [Fact]
    public async Task Replayed_Commit_After_Marker_Loss_Reapplies_Without_Throwing()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var writer = CreateWriter(store);

        await writer.WriteAsync(BuildCommit("commit-1"), Decision);

        // Simulate a crash that applied the state but lost the marker: drop the marker and replay the same commit.
        // The incident Append in particular must not fail on the second pass.
        await store.DeleteAsync(new DeleteDocumentRequest(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, "commit-1"));
        await writer.WriteAsync(BuildCommit("commit-1"), Decision);

        Assert.Equal(IncidentStatus.Open, (await new GroundworkIncidentStateStore(store).FindAsync("wf-1", "inc-1"))!.Status);
        Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, "commit-1"));
    }

    private static GroundworkRuntimeCheckpointWriter CreateWriter(IDocumentStore store) => new(
        store,
        new GroundworkWorkflowExecutionStateStore(store),
        new GroundworkSchedulerStateStore(store),
        new GroundworkActivityExecutionStateStore(store),
        new GroundworkBookmarkStateStore(store),
        new GroundworkDurableValueStateStore(store),
        new GroundworkIncidentStateStore(store),
        new GroundworkOperationalStateStore(store));

    private static RuntimeCheckpointCommit BuildCommit(string commitId, string bookmarkNode = "node-bm-1")
    {
        const string wf = "wf-1";
        var stateChanges = new RuntimeCheckpointStateChangeSet(
            workflowExecution: Change(wf, RuntimeStateChangeOperation.Upsert, WorkflowState(wf)),
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

    private static WorkflowExecutionState WorkflowState(string workflowExecutionId) => new(
        workflowExecutionId,
        new WorkflowExecutableIdentity($"artifact-{workflowExecutionId}", "definition-1", "version-1", "1", $"hash-{workflowExecutionId}", null),
        WorkflowExecutionStatus.Running,
        SubStatus: null,
        CreatedAt: DateTimeOffset.UnixEpoch,
        StartedAt: null,
        UpdatedAt: null,
        CompletedAt: null,
        CorrelationId: null,
        ParentWorkflowExecutionId: null,
        TenantId: null,
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

    private static OperationalState Operational(string workflowExecutionId, string operationalStateId) => new(
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
}
