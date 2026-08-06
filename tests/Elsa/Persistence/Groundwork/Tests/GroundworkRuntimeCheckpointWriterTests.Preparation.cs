using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using System.Text.Json;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed partial class GroundworkRuntimeCheckpointWriterTests
{
    [Fact]
    public async Task Prepare_Persists_Only_The_Recovery_Ledger_And_Coordination()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var commit = BuildCommit("commit-prepared-only");

        var prepared = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));

        Assert.Equal(RuntimeCheckpointPreparationStatus.Prepared, prepared.Status);
        Assert.NotNull(await store.LoadAsync(
            ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind,
            $"prepared:{commit.CommitId}"));
        Assert.NotNull(await store.LoadAsync(
            ElsaRuntimeStorageManifest.RuntimeCheckpointCoordinationDocumentKind,
            $"coordination:{commit.WorkflowExecutionId}"));
        Assert.Null(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, commit.CommitId));
        Assert.Null(await new GroundworkWorkflowExecutionStateStore(
            store,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor).FindAsync(commit.WorkflowExecutionId));
        Assert.Empty(store.Snapshot(ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind));
    }

    [Fact]
    public async Task Prepared_Checkpoint_Finalizes_After_Writer_Restart_With_Stable_Provenance()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var commit = BuildCommit("commit-prepared-restart");
        var requestedContext = new RuntimeExecutionContextSnapshot(1, new Dictionary<string, string>
        {
            ["sample.context"] = "opaque"
        });
        var request = new RuntimeCheckpointPrepareRequest(commit, "source-a", "operation-a", requestedContext);
        var prepared = await CreateWriter(store).PrepareAsync(request);
        var token = Assert.IsType<RuntimeCheckpointPreparationToken>(prepared.Token);
        var enriched = commit with { Checkpoint = commit.Checkpoint with { Provenance = token.Provenance } };

        var result = await CreateWriter(store).CommitPreparedAsync(token, enriched, Decision);
        var replay = await CreateWriter(store).PrepareAsync(request);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, result.Status);
        Assert.Equal(RuntimeCheckpointPreparationStatus.Replay, replay.Status);
        Assert.Equal(token.Provenance, replay.Token!.Provenance);
        Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, commit.CommitId));
        Assert.NotNull(await new GroundworkWorkflowExecutionStateStore(
            store,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor).FindAsync(commit.WorkflowExecutionId));
        var coordination = await LoadCoordinationAsync(store, commit.WorkflowExecutionId);
        Assert.Equal(1, coordination.ReservedOrder);
        Assert.Equal(1, coordination.CommittedOrder);
        Assert.Equal(requestedContext, coordination.ExecutionContext);
    }

    [Fact]
    public async Task Prepared_Checkpoint_Finalizes_After_Durable_Store_Restart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-prepared-checkpoint-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        var commit = BuildCommit("commit-prepared-durable-restart");
        PreparationIdentity originalIdentity;
        try
        {
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var prepared = await CreateWriter(fixture.DocumentStore)
                    .PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
                originalIdentity = PreparationIdentity.From(Assert.IsType<RuntimeCheckpointPreparationToken>(prepared.Token));
            }

            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var recovered = await CreateWriter(fixture.DocumentStore)
                    .PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
                Assert.Equal(RuntimeCheckpointPreparationStatus.Replay, recovered.Status);
                var reissuedToken = Assert.IsType<RuntimeCheckpointPreparationToken>(recovered.Token);
                Assert.Equal(originalIdentity, PreparationIdentity.From(reissuedToken));
                var coordination = await LoadCoordinationAsync(fixture.DocumentStore, commit.WorkflowExecutionId);
                Assert.Equal(coordination.OrderRevision, reissuedToken.ExpectedOrderRevision);
                Assert.Equal(coordination.ContextRevision, reissuedToken.ExpectedContextRevision);
                Assert.Null(reissuedToken.ExpectedFence);

                var enriched = commit with { Checkpoint = commit.Checkpoint with { Provenance = reissuedToken.Provenance } };
                var result = await CreateWriter(fixture.DocumentStore).CommitPreparedAsync(reissuedToken, enriched, Decision);
                Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, result.Status);
            }

            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                Assert.NotNull(await fixture.DocumentStore.LoadAsync(
                    ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
                    commit.CommitId));
                Assert.NotNull(await new GroundworkWorkflowExecutionStateStore(
                    fixture.DocumentStore,
                    GroundworkTestSerialization.Serializer,
                    GroundworkTestAccess.DefaultAccessContextAccessor).FindAsync(commit.WorkflowExecutionId));
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Prepare_Replay_Rejects_Changed_Canonical_Input()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var first = BuildCommit("commit-canonical-conflict", bookmarkNode: "node-v1");
        var changed = BuildCommit("commit-canonical-conflict", bookmarkNode: "node-v2");
        await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(first));

        var conflict = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(changed));

        Assert.Equal(RuntimeCheckpointPreparationStatus.Conflict, conflict.Status);
        Assert.Null(conflict.Token);
    }

    [Fact]
    public async Task Separate_Checkpoints_Reserve_Strictly_Monotonic_Workflow_Orders()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());

        var first = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(BuildCommit("commit-order-1")));
        var second = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(BuildCommit("commit-order-2")));

        Assert.Equal(1, first.Token!.Provenance.WorkflowCheckpointOrder);
        Assert.Equal(2, second.Token!.Provenance.WorkflowCheckpointOrder);
    }

    [Fact]
    public async Task Prepared_Token_Cannot_Be_Rebound_To_A_Different_Ownership_Fence()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var commit = BuildCommit("commit-stale-prepared-token");
        var prepared = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        var changedFence = new RuntimeExecutionFence("lease-other", "owner-other", 2);
        var enriched = commit with
        {
            ExpectedFence = changedFence,
            Checkpoint = commit.Checkpoint with { Provenance = prepared.Token!.Provenance }
        };

        var result = await CreateWriter(store).CommitPreparedAsync(prepared.Token, enriched, Decision);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict, result.Status);
        Assert.Null(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, commit.CommitId));
    }

    [Fact]
    public async Task Skip_Finalizes_The_Ledger_Without_Exposing_State_Or_A_Commit_Marker()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var commit = BuildCommit("commit-prepared-skip");
        var prepared = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        var enriched = commit with { Checkpoint = commit.Checkpoint with { Provenance = prepared.Token!.Provenance } };

        var result = await CreateWriter(store).CommitPreparedAsync(
            prepared.Token,
            enriched,
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Skip));

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Skipped, result.Status);
        Assert.Null(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, commit.CommitId));
        Assert.Null(await new GroundworkWorkflowExecutionStateStore(
            store,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor).FindAsync(commit.WorkflowExecutionId));
        var coordination = await LoadCoordinationAsync(store, commit.WorkflowExecutionId);
        Assert.Equal(1, coordination.ReservedOrder);
        Assert.Equal(0, coordination.CommittedOrder);
    }

    [Fact]
    public async Task Skipped_Ledger_Replays_Through_Prepare_And_CommitPrepared_Without_Exposing_Effects()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var commit = BuildCommit("commit-prepared-skip-replay");
        var request = RuntimeCheckpointPrepareRequest.From(commit);
        var prepared = await CreateWriter(store).PrepareAsync(request);
        var enriched = commit with
        {
            Checkpoint = commit.Checkpoint with { Provenance = prepared.Token!.Provenance }
        };
        var skipped = await CreateWriter(store).CommitPreparedAsync(
            prepared.Token,
            enriched,
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Skip));
        var afterSkip = await LoadCoordinationAsync(store, commit.WorkflowExecutionId);

        var preparationReplay = await CreateWriter(store).PrepareAsync(request);
        var replayToken = Assert.IsType<RuntimeCheckpointPreparationToken>(preparationReplay.Token);
        var finalReplay = await CreateWriter(store).CommitPreparedAsync(
            replayToken,
            commit with { Checkpoint = commit.Checkpoint with { Provenance = replayToken.Provenance } },
            Decision);
        var afterReplay = await LoadCoordinationAsync(store, commit.WorkflowExecutionId);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Skipped, skipped.Status);
        Assert.Equal(RuntimeCheckpointPreparationStatus.Replay, preparationReplay.Status);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Skipped, preparationReplay.Receipt!.Status);
        Assert.Equal(skipped.CommitFingerprint, preparationReplay.Receipt.CommitFingerprint);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Replay, finalReplay.Status);
        Assert.Equal(skipped.CommitFingerprint, finalReplay.CommitFingerprint);
        Assert.Equal(skipped.PendingPostCommitWorkIds, finalReplay.PendingPostCommitWorkIds);
        Assert.Equal(skipped.ConsumedSchedulerWorkItemIds, finalReplay.ConsumedSchedulerWorkItemIds);
        Assert.Equal(afterSkip, afterReplay);
        Assert.Equal(1, afterReplay.ReservedOrder);
        Assert.Equal(0, afterReplay.CommittedOrder);
        Assert.Equal(0, afterReplay.ContextRevision);
        Assert.True(afterReplay.ExecutionContext.IsEmpty);
        Assert.Null(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, commit.CommitId));
        Assert.Null(await new GroundworkWorkflowExecutionStateStore(
            store,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor).FindAsync(commit.WorkflowExecutionId));
        Assert.Empty(store.Snapshot(ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind));
    }

    [Fact]
    public async Task Finalization_Failure_Rolls_Back_State_Outbox_Marker_Context_And_Ledger_Together()
    {
        var inner = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var store = new InterceptingDocumentStore(inner);
        var commit = BuildCommit("commit-prepared-rollback", includeDispatch: true);
        var outbox = PendingDispatchOutbox(commit.CommitId, commit.WorkflowExecutionId);
        commit = commit with
        {
            StateChanges = commit.StateChanges.WithPostCommitOutbox(
            [
                Change(outbox.OutboxItemId, RuntimeStateChangeOperation.Upsert, outbox)
            ])
        };
        var requestedContext = new RuntimeExecutionContextSnapshot(1, new Dictionary<string, string>
        {
            ["sample.context"] = "rollback"
        });
        var prepared = await CreateWriter(store).PrepareAsync(new RuntimeCheckpointPrepareRequest(
            commit,
            "source-a",
            "operation-a",
            requestedContext));
        var token = prepared.Token!;
        var enriched = commit with { Checkpoint = commit.Checkpoint with { Provenance = token.Provenance } };

        store.OnBeforeSave = FailAtCommitMarker;
        await Assert.ThrowsAsync<GroundworkRuntimeCheckpointWriterException>(
            () => CreateWriter(store).CommitPreparedAsync(token, enriched, Decision).AsTask());

        Assert.Null(await inner.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, commit.CommitId));
        Assert.Null(await new GroundworkWorkflowExecutionStateStore(
            inner,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor).FindAsync(commit.WorkflowExecutionId));
        Assert.Null(await inner.LoadAsync(ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind, outbox.OutboxItemId));
        var rolledBack = await LoadCoordinationAsync(inner, commit.WorkflowExecutionId);
        Assert.Equal(0, rolledBack.CommittedOrder);
        Assert.True(rolledBack.ExecutionContext.IsEmpty);

        var retry = await CreateWriter(inner).CommitPreparedAsync(token, enriched, Decision);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, retry.Status);
        Assert.NotNull(await inner.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, commit.CommitId));
        Assert.NotNull(await inner.LoadAsync(ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind, outbox.OutboxItemId));
        var committed = await LoadCoordinationAsync(inner, commit.WorkflowExecutionId);
        Assert.Equal(1, committed.CommittedOrder);
        Assert.Equal(requestedContext, committed.ExecutionContext);
        return;

        Task FailAtCommitMarker(SaveDocumentRequest request)
        {
            if (StringComparer.Ordinal.Equals(request.DocumentKind, ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind))
                throw new InvalidOperationException("simulated finalization failure");
            store.OnBeforeSave = FailAtCommitMarker;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Legacy_Committed_Marker_Reconciles_As_Terminal_Without_Rewriting_It()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var commit = BuildCommit("commit-legacy-reconcile");
        await CreateWriter(store).CommitAsync(commit, Decision);
        var before = await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, commit.CommitId);

        var replay = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        var afterPreparation = await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, commit.CommitId);
        var token = Assert.IsType<RuntimeCheckpointPreparationToken>(replay.Token);
        var finalReplay = await CreateWriter(store).CommitPreparedAsync(
            token,
            commit with { Checkpoint = commit.Checkpoint with { Provenance = token.Provenance } },
            Decision);
        var afterFinalReplay = await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, commit.CommitId);

        Assert.Equal(RuntimeCheckpointPreparationStatus.Replay, replay.Status);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, replay.Receipt!.Status);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Replay, finalReplay.Status);
        Assert.Null(replay.Receipt.CommitFingerprint);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(commit), finalReplay.CommitFingerprint);
        Assert.Equal(replay.Receipt.PendingPostCommitWorkIds, finalReplay.PendingPostCommitWorkIds);
        Assert.Equal(replay.Receipt.ConsumedSchedulerWorkItemIds, finalReplay.ConsumedSchedulerWorkItemIds);
        Assert.Equal(before!.ContentJson, afterPreparation!.ContentJson);
        Assert.Equal(before.Version, afterPreparation.Version);
        Assert.Equal(before.ContentJson, afterFinalReplay!.ContentJson);
        Assert.Equal(before.Version, afterFinalReplay.Version);
        Assert.NotNull(await store.LoadAsync(
            ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind,
            $"prepared:{commit.CommitId}"));
    }

    [Fact]
    public async Task Legacy_Committed_Marker_Replay_Takes_Precedence_Over_A_Stale_Fence()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var commit = BuildCommit("commit-legacy-stale-fence");
        await CreateWriter(store).CommitAsync(commit, Decision);
        var stale = commit with { ExpectedFence = new RuntimeExecutionFence("missing-lease", "former-owner", 1) };

        var replay = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(stale));

        Assert.Equal(RuntimeCheckpointPreparationStatus.Replay, replay.Status);
        Assert.NotNull(replay.Token);
    }

    [Fact]
    public async Task Legacy_Committed_Marker_Rejects_A_New_Context_Claim()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var commit = BuildCommit("commit-legacy-context-conflict");
        await CreateWriter(store).CommitAsync(commit, Decision);
        var request = new RuntimeCheckpointPrepareRequest(
            commit,
            commit.Checkpoint.Name,
            commit.Checkpoint.CheckpointId,
            new RuntimeExecutionContextSnapshot(1, new Dictionary<string, string> { ["new"] = "context" }));

        var conflict = await CreateWriter(store).PrepareAsync(request);

        Assert.Equal(RuntimeCheckpointPreparationStatus.Conflict, conflict.Status);
        Assert.Null(await store.LoadAsync(
            ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind,
            $"prepared:{commit.CommitId}"));
    }

    [Fact]
    public async Task Marker_Created_Between_Prepare_And_Finalize_Reconciles_As_Replay()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var commit = BuildCommit("commit-marker-race");
        var prepared = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        var enriched = commit with { Checkpoint = commit.Checkpoint with { Provenance = prepared.Token!.Provenance } };
        await CreateWriter(store).CommitAsync(enriched, Decision);

        var replay = await CreateWriter(store).CommitPreparedAsync(prepared.Token, enriched, Decision);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Replay, replay.Status);
        var preparationReplay = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        Assert.Equal(RuntimeCheckpointPreparationStatus.Replay, preparationReplay.Status);
    }

    [Fact]
    public async Task Reconciled_Legacy_Token_Rejects_Changed_Raw_State()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var commit = BuildCommit("commit-legacy-changed-state", bookmarkNode: "node-v1");
        await CreateWriter(store).CommitAsync(commit, Decision);
        var replay = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        var changed = BuildCommit(commit.CommitId, bookmarkNode: "node-v2") with
        {
            Checkpoint = commit.Checkpoint with { Provenance = replay.Token!.Provenance }
        };

        var result = await CreateWriter(store).CommitPreparedAsync(replay.Token, changed, Decision);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task Reconciled_Legacy_Token_Rejects_A_Different_Workflow_Identity()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var commit = BuildCommit("commit-legacy-changed-workflow");
        await CreateWriter(store).CommitAsync(commit, Decision);
        var replay = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        var changed = commit with
        {
            Checkpoint = commit.Checkpoint with
            {
                WorkflowExecutionId = "wf-other",
                Provenance = replay.Token!.Provenance
            }
        };

        var result = await CreateWriter(store).CommitPreparedAsync(replay.Token, changed, Decision);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict, result.Status);
    }

    private static async Task<CoordinationSnapshot> LoadCoordinationAsync(
        IDocumentStore store,
        string workflowExecutionId)
    {
        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.RuntimeCheckpointCoordinationDocumentKind,
            $"coordination:{workflowExecutionId}");
        using var document = JsonDocument.Parse(envelope!.ContentJson);
        var root = document.RootElement;
        var context = root.GetProperty("executionContext");
        var entries = context.GetProperty("entries")
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal);
        return new CoordinationSnapshot(
            root.GetProperty("reservedOrder").GetInt64(),
            root.GetProperty("committedOrder").GetInt64(),
            root.GetProperty("orderRevision").GetInt64(),
            root.GetProperty("contextRevision").GetInt64(),
            new RuntimeExecutionContextSnapshot(context.GetProperty("version").GetInt32(), entries));
    }

    private sealed record CoordinationSnapshot(
        long ReservedOrder,
        long CommittedOrder,
        long OrderRevision,
        long ContextRevision,
        RuntimeExecutionContextSnapshot ExecutionContext);

    private sealed record PreparationIdentity(
        string LedgerToken,
        RuntimeCheckpointProvenance Provenance,
        long ExpectedOrderRevision,
        long ExpectedContextRevision,
        RuntimeExecutionFence? ExpectedFence,
        string CanonicalInputFingerprint,
        string CanonicalInputReference)
    {
        public static PreparationIdentity From(RuntimeCheckpointPreparationToken token) =>
            new(
                token.LedgerToken,
                token.Provenance,
                token.ExpectedOrderRevision,
                token.ExpectedContextRevision,
                token.ExpectedFence,
                token.CanonicalInputFingerprint,
                token.CanonicalInputReference);
    }
}
