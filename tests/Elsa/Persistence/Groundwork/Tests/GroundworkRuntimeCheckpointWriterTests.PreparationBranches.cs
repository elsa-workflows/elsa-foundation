using System.Text.Json.Nodes;
using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed partial class GroundworkRuntimeCheckpointWriterTests
{
    public static TheoryData<string> TokenMismatchCases => new()
    {
        "ledger-token",
        "provenance",
        "order-revision",
        "context-revision",
        "fence",
        "fingerprint",
        "input-reference",
        "persistence-mode"
    };

    public static TheoryData<string> CandidateMismatchCases => new()
    {
        "commit-id",
        "workflow-id",
        "provenance",
        "fence"
    };

    public static TheoryData<string> CommitCoordinationConflictCases => new()
    {
        "missing",
        "order-revision",
        "context-revision",
        "reserved-order",
        "committed-order"
    };

    public static TheoryData<string> PrepareCoordinationConflictCases => new()
    {
        "missing",
        "context-revision",
        "reserved-order",
        "committed-order"
    };

    [Fact]
    public async Task Prepare_With_Stale_Fence_Returns_OwnershipLost_Without_Reservation()
    {
        var store = NewDocumentStore();
        var commit = BuildCommit("prepare-ownership-lost") with
        {
            ExpectedFence = new RuntimeExecutionFence("missing-lease", "former-owner", 1)
        };

        var result = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));

        Assert.Equal(RuntimeCheckpointPreparationStatus.OwnershipLost, result.Status);
        Assert.Null(await store.LoadAsync(
            ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind,
            $"prepared:{commit.CommitId}"));
        Assert.Null(await store.LoadAsync(
            ElsaRuntimeStorageManifest.RuntimeCheckpointCoordinationDocumentKind,
            $"coordination:{commit.WorkflowExecutionId}"));
    }

    [Fact]
    public async Task CommitPrepared_When_Fence_Becomes_Stale_Returns_OwnershipLost_Without_Finalization()
    {
        var store = NewDocumentStore();
        var now = DateTimeOffset.UtcNow;
        var lease = new RuntimeExecutionLease(
            "lease-current", "wf-1", "owner-current", now, now.AddHours(1), 1);
        await SaveOwnershipAsync(store, lease);
        var commit = BuildCommit("finalize-ownership-lost") with { ExpectedFence = lease.ToFence() };
        var prepared = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        await SaveOwnershipAsync(store, new RuntimeExecutionLease(
            "lease-successor", "wf-1", "owner-successor", now, now.AddHours(1), 2));
        var enriched = AttachProvenance(commit, prepared.Token!);

        var result = await CreateWriter(store).CommitPreparedAsync(prepared.Token!, enriched, Decision);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.OwnershipLost, result.Status);
        Assert.Null(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, commit.CommitId));
        Assert.Null(await new GroundworkWorkflowExecutionStateStore(
            store,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor).FindAsync(commit.WorkflowExecutionId));
    }

    [Fact]
    public async Task Prepare_Retries_A_Deterministic_Coordination_Create_Conflict()
    {
        var inner = NewDocumentStore();
        var store = new InterceptingDocumentStore(inner);
        var remaining = 1;
        store.OnSaveResult = request =>
            request.DocumentKind == ElsaRuntimeStorageManifest.RuntimeCheckpointCoordinationDocumentKind &&
            Interlocked.Exchange(ref remaining, 0) == 1
                ? DocumentStoreWriteResult.ConcurrencyConflict
                : null;

        var result = await CreateWriter(store).PrepareAsync(
            RuntimeCheckpointPrepareRequest.From(BuildCommit("prepare-concurrency-retry")));

        Assert.Equal(RuntimeCheckpointPreparationStatus.Prepared, result.Status);
        Assert.Equal(2, inner.BeginCount);
    }

    [Fact]
    public async Task CommitPrepared_Returns_Conflict_On_Deterministic_Coordination_Cas_Conflict()
    {
        var inner = NewDocumentStore();
        var store = new InterceptingDocumentStore(inner);
        var commit = BuildCommit("finalize-concurrency-conflict");
        var prepared = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        var remaining = 1;
        store.OnSaveResult = request =>
            request.DocumentKind == ElsaRuntimeStorageManifest.RuntimeCheckpointCoordinationDocumentKind &&
            Interlocked.Exchange(ref remaining, 0) == 1
                ? DocumentStoreWriteResult.ConcurrencyConflict
                : null;

        var result = await CreateWriter(store).CommitPreparedAsync(
            prepared.Token!,
            AttachProvenance(commit, prepared.Token!),
            Decision);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict, result.Status);
        Assert.Null(await inner.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, commit.CommitId));
        Assert.Null(await new GroundworkWorkflowExecutionStateStore(
            inner,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor).FindAsync(commit.WorkflowExecutionId));
    }

    [Fact]
    public async Task CommitPrepared_With_Missing_Ledger_Returns_Conflict()
    {
        var store = NewDocumentStore();
        var commit = BuildCommit("missing-ledger");
        var prepared = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        await store.DeleteAsync(new DeleteDocumentRequest(
            ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind,
            $"prepared:{commit.CommitId}"));

        var result = await CreateWriter(store).CommitPreparedAsync(
            prepared.Token!, AttachProvenance(commit, prepared.Token!), Decision);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict, result.Status);
    }

    [Theory]
    [MemberData(nameof(TokenMismatchCases))]
    public async Task CommitPrepared_Rejects_Each_Mismatched_Token_Field(string mismatch)
    {
        var store = NewDocumentStore();
        var commit = BuildCommit($"token-mismatch-{mismatch}");
        var prepared = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        var authoritative = prepared.Token!;
        var candidate = mismatch switch
        {
            "ledger-token" => authoritative with { LedgerToken = "other-ledger-token" },
            "provenance" => authoritative with
            {
                Provenance = new RuntimeCheckpointProvenance(
                    authoritative.Provenance.ExecutionContext,
                    authoritative.Provenance.WorkflowCheckpointOrder + 1)
            },
            "order-revision" => authoritative with { ExpectedOrderRevision = authoritative.ExpectedOrderRevision + 1 },
            "context-revision" => authoritative with { ExpectedContextRevision = authoritative.ExpectedContextRevision + 1 },
            "fence" => authoritative with { ExpectedFence = new RuntimeExecutionFence("other-lease", "other-owner", 2) },
            "fingerprint" => authoritative with { CanonicalInputFingerprint = new string('0', 64) },
            "input-reference" => authoritative with { CanonicalInputReference = "other-input-reference" },
            "persistence-mode" => authoritative with { InitialPersistenceMode = RuntimeCheckpointPersistenceMode.Deferred },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, null)
        };

        var result = await CreateWriter(store).CommitPreparedAsync(
            candidate, AttachProvenance(commit, authoritative), Decision);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict, result.Status);
    }

    [Theory]
    [MemberData(nameof(CandidateMismatchCases))]
    public async Task CommitPrepared_Rejects_Candidate_Identity_Or_Fence_Mismatch(string mismatch)
    {
        var store = NewDocumentStore();
        var commit = BuildCommit($"candidate-mismatch-{mismatch}");
        var prepared = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        var candidate = AttachProvenance(commit, prepared.Token!);
        candidate = mismatch switch
        {
            "commit-id" => candidate with { CommitId = "other-commit" },
            "workflow-id" => candidate with
            {
                Checkpoint = candidate.Checkpoint with { WorkflowExecutionId = "wf-other" }
            },
            "provenance" => candidate with
            {
                Checkpoint = candidate.Checkpoint with
                {
                    Provenance = new RuntimeCheckpointProvenance(RuntimeExecutionContextSnapshot.Empty, 2)
                }
            },
            "fence" => candidate with { ExpectedFence = new RuntimeExecutionFence("other-lease", "other-owner", 2) },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, null)
        };

        var result = await CreateWriter(store).CommitPreparedAsync(prepared.Token!, candidate, Decision);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task Terminal_Prepared_Ledger_Replays_Matching_Final_Commit_And_Rejects_Changed_Final_Commit()
    {
        var store = NewDocumentStore();
        var commit = BuildCommit("terminal-ledger");
        var prepared = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        var enriched = AttachProvenance(commit, prepared.Token!);
        Assert.Equal(
            RuntimeCheckpointCommitStoreStatus.Committed,
            (await CreateWriter(store).CommitPreparedAsync(prepared.Token!, enriched, Decision)).Status);

        var replay = await CreateWriter(store).CommitPreparedAsync(prepared.Token!, enriched, Decision);
        var changed = enriched with { Metadata = new Dictionary<string, string> { ["changed"] = "true" } };
        var conflict = await CreateWriter(store).CommitPreparedAsync(prepared.Token!, changed, Decision);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Replay, replay.Status);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict, conflict.Status);
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("commit")]
    public async Task Unknown_Ledger_Status_Returns_Conflict(string phase)
    {
        var store = NewDocumentStore();
        var commit = BuildCommit($"failed-ledger-{phase}");
        var request = RuntimeCheckpointPrepareRequest.From(commit);
        var prepared = await CreateWriter(store).PrepareAsync(request);
        await RewriteDocumentAsync(
            store,
            ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind,
            $"prepared:{commit.CommitId}",
            root => root["entry"]!["status"] = 999);

        if (phase == "prepare")
            Assert.Equal(RuntimeCheckpointPreparationStatus.Conflict, (await CreateWriter(store).PrepareAsync(request)).Status);
        else
            Assert.Equal(
                RuntimeCheckpointCommitStoreStatus.Conflict,
                (await CreateWriter(store).CommitPreparedAsync(
                    prepared.Token!, AttachProvenance(commit, prepared.Token!), Decision)).Status);
    }

    [Theory]
    [MemberData(nameof(CommitCoordinationConflictCases))]
    public async Task CommitPrepared_Rejects_Each_Coordination_Guard(string guard)
    {
        var store = NewDocumentStore();
        var commit = BuildCommit($"commit-coordination-{guard}");
        var prepared = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        await BreakCoordinationAsync(store, commit.WorkflowExecutionId, guard);

        var result = await CreateWriter(store).CommitPreparedAsync(
            prepared.Token!, AttachProvenance(commit, prepared.Token!), Decision);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict, result.Status);
    }

    [Theory]
    [MemberData(nameof(PrepareCoordinationConflictCases))]
    public async Task Prepare_Replay_Rejects_Each_Coordination_Guard(string guard)
    {
        var store = NewDocumentStore();
        var commit = BuildCommit($"prepare-coordination-{guard}");
        var request = RuntimeCheckpointPrepareRequest.From(commit);
        await CreateWriter(store).PrepareAsync(request);
        await BreakCoordinationAsync(store, commit.WorkflowExecutionId, guard);

        var result = await CreateWriter(store).PrepareAsync(request);

        Assert.Equal(RuntimeCheckpointPreparationStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task Committed_Context_Changes_Once_And_Remains_Unchanged_When_Already_Attached()
    {
        var store = NewDocumentStore();
        var context = new RuntimeExecutionContextSnapshot(1, new Dictionary<string, string> { ["session"] = "a" });
        var first = MinimalCommit("context-first");
        await PrepareAndCommitAsync(store, first, context);
        var afterFirst = await LoadCoordinationAsync(store, first.WorkflowExecutionId);

        var second = MinimalCommit("context-second");
        await PrepareAndCommitAsync(store, second, context);
        var afterSecond = await LoadCoordinationAsync(store, second.WorkflowExecutionId);

        Assert.Equal(context, afterFirst.ExecutionContext);
        Assert.Equal(1, afterFirst.ContextRevision);
        Assert.Equal(context, afterSecond.ExecutionContext);
        Assert.Equal(1, afterSecond.ContextRevision);
    }

    [Fact]
    public async Task CommitPrepared_Passes_Through_Scheduler_Work_Consume_Conflict()
    {
        var store = NewDocumentStore();
        var queue = new GroundworkWorkflowSchedulerWorkQueue(store, GroundworkTestSerialization.Serializer);
        await queue.EnqueueAsync(ConsumeWorkItem());
        var staleClaim = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
            "wf-1", "owner-a", ConsumeNow, TimeSpan.FromSeconds(30)));
        var commit = BuildConsumeCommit("prepared-consume-conflict", staleClaim!);
        var prepared = await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit));
        Assert.NotNull(await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
            "wf-1", "owner-b", ConsumeNow.AddMinutes(1), TimeSpan.FromSeconds(30))));

        await Assert.ThrowsAsync<RuntimeSchedulerWorkConsumeConflictException>(() =>
            CreateWriter(store).CommitPreparedAsync(
                prepared.Token!, AttachProvenance(commit, prepared.Token!), Decision).AsTask());
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("commit")]
    public async Task Infrastructure_Failure_Is_Translated_At_Each_Public_Protocol_Phase(string phase)
    {
        var inner = NewDocumentStore();
        var store = new InterceptingDocumentStore(inner);
        var commit = BuildCommit($"translated-{phase}");
        RuntimeCheckpointPreparationToken? token = null;
        if (phase == "commit")
            token = (await CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit))).Token;
        var injected = new InvalidOperationException("injected Groundwork failure");
        store.OnBeforeBegin = _ => throw injected;

        var exception = phase == "prepare"
            ? await Assert.ThrowsAsync<GroundworkRuntimeCheckpointWriterException>(() =>
                CreateWriter(store).PrepareAsync(RuntimeCheckpointPrepareRequest.From(commit)).AsTask())
            : await Assert.ThrowsAsync<GroundworkRuntimeCheckpointWriterException>(() =>
                CreateWriter(store).CommitPreparedAsync(
                    token!, AttachProvenance(commit, token!), Decision).AsTask());

        Assert.Same(injected, exception.InnerException);
    }

    [Fact]
    public async Task Corrupt_Persisted_Canonical_Payload_Is_Translated_On_Replay()
    {
        var store = NewDocumentStore();
        var commit = BuildCommit("corrupt-canonical-payload");
        var request = RuntimeCheckpointPrepareRequest.From(commit);
        await CreateWriter(store).PrepareAsync(request);
        await RewriteDocumentAsync(
            store,
            ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind,
            $"prepared:{commit.CommitId}",
            root => root["entry"]!["canonicalInputPayload"] = "{not-canonical-json");

        var exception = await Assert.ThrowsAsync<GroundworkRuntimeCheckpointWriterException>(() =>
            CreateWriter(store).PrepareAsync(request).AsTask());

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    private static InMemoryDocumentStore NewDocumentStore() =>
        new(ElsaRuntimeStorageManifest.CreatePhysicalized());

    private static RuntimeCheckpointCommit AttachProvenance(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPreparationToken token) =>
        commit with { Checkpoint = commit.Checkpoint with { Provenance = token.Provenance } };

    private static RuntimeCheckpointCommit MinimalCommit(string commitId) =>
        new(
            commitId,
            new RuntimeCheckpoint(
                $"cp-{commitId}",
                "checkpoint",
                "wf-1",
                DateTimeOffset.UnixEpoch,
                [],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            [],
            new Dictionary<string, string>());

    private static async Task PrepareAndCommitAsync(
        IDocumentStore store,
        RuntimeCheckpointCommit commit,
        RuntimeExecutionContextSnapshot context)
    {
        var prepared = await CreateWriter(store).PrepareAsync(new RuntimeCheckpointPrepareRequest(
            commit, commit.Checkpoint.Name, commit.Checkpoint.CheckpointId, context));
        var result = await CreateWriter(store).CommitPreparedAsync(
            prepared.Token!, AttachProvenance(commit, prepared.Token!), Decision);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, result.Status);
    }

    private static async Task SaveOwnershipAsync(InMemoryDocumentStore store, RuntimeExecutionLease lease)
    {
        var state = new ExecutionLivenessState(
            $"ownership:{lease.WorkflowExecutionId}",
            lease.WorkflowExecutionId,
            lease,
            heartbeat: null,
            drain: null,
            interruptedExecution: null);
        await new GroundworkExecutionLivenessStateStore(store, GroundworkTestSerialization.Serializer).SaveAsync(state);
    }

    private static async Task BreakCoordinationAsync(
        InMemoryDocumentStore store,
        string workflowExecutionId,
        string guard)
    {
        var id = $"coordination:{workflowExecutionId}";
        if (guard == "missing")
        {
            await store.DeleteAsync(new DeleteDocumentRequest(
                ElsaRuntimeStorageManifest.RuntimeCheckpointCoordinationDocumentKind,
                id));
            return;
        }

        await RewriteDocumentAsync(
            store,
            ElsaRuntimeStorageManifest.RuntimeCheckpointCoordinationDocumentKind,
            id,
            root =>
            {
                switch (guard)
                {
                    case "order-revision":
                        root["orderRevision"] = root["orderRevision"]!.GetValue<long>() + 1;
                        break;
                    case "context-revision":
                        root["contextRevision"] = root["contextRevision"]!.GetValue<long>() + 1;
                        break;
                    case "reserved-order":
                        root["reservedOrder"] = 0;
                        break;
                    case "committed-order":
                        root["committedOrder"] = 1;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(guard), guard, null);
                }
            });
    }

    private static async Task RewriteDocumentAsync(
        InMemoryDocumentStore store,
        string kind,
        string id,
        Action<JsonObject> rewrite)
    {
        var envelope = await store.LoadAsync(kind, id) ?? throw new InvalidOperationException($"Missing test document '{kind}/{id}'.");
        var root = JsonNode.Parse(envelope.ContentJson)!.AsObject();
        rewrite(root);
        var result = await store.SaveAsync(new SaveDocumentRequest(
            kind,
            id,
            envelope.SchemaVersion,
            root.ToJsonString(),
            envelope.Version));
        Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);
    }
}
