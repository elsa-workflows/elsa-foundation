using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class BackgroundActivityWorkLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2031, 2, 3, 4, 5, 6, TimeSpan.Zero);

    [Fact]
    public async Task In_memory_checkpoint_admission_worker_receipt_and_terminal_acceptance_share_scope_and_state()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var operational = new InMemoryExecutionLivenessStateStore();
        var executions = new InMemoryWorkflowExecutionStateStore(state, operational);
        await executions.SaveAsync(ExecutionState());
        var ownership = NewOwnership(operational, "background-work-test-owner");
        var owner = await ownership.AcquireAsync("workflow-a");
        var tenantA = new FixedAccessor("tenant-a");
        var tenantB = new FixedAccessor("tenant-b");
        var checkpoint = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: executions,
            operationalStateStore: operational,
            state: state,
            persistenceAccessContextAccessor: tenantA);
        var workerA = new InMemoryBackgroundActivityWorkStore(state, tenantA);
        var workerB = new InMemoryBackgroundActivityWorkStore(state, tenantB);
        var request = Request("work-memory");

        var admission = Commit("admit") with { ExpectedFence = owner.ToFence() };
        await checkpoint.CommitAsync(admission with
        {
            StateChanges = admission.StateChanges.WithBackgroundActivityWork(new BackgroundActivityWorkCheckpointChanges(admissions: [request]))
        }, Immediate);

        Assert.Equal(BackgroundActivityWorkStatus.Pending, (await workerA.FindAsync("workflow-a", request.WorkId))!.Status);
        Assert.Null(await workerB.FindAsync("workflow-a", request.WorkId));
        var claim = await workerA.ClaimNextAsync("memory-worker", Now.AddSeconds(1), TimeSpan.FromMinutes(1));
        Assert.NotNull(claim);

        var receipt = Complete("{\"second\":2, \"first\":1}");
        var ready = await workerA.RecordResultAsync(claim!, receipt, Now.AddSeconds(2));
        var replay = await workerA.RecordResultAsync(claim!, Complete("{\"first\":1,\"second\":2}"), Now.AddHours(1));
        Assert.Equal(BackgroundActivityWorkStatus.ResultReady, ready.Status);
        Assert.Equal(ready.ReceiptDigest, replay.ReceiptDigest);
        Assert.Single(await workerA.ListResultReadyAsync(10));
        Assert.Empty(await workerB.ListResultReadyAsync(10));

        var terminal = Commit("terminal");
        await checkpoint.CommitAsync(terminal with
        {
            StateChanges = terminal.StateChanges.WithBackgroundActivityWork(new BackgroundActivityWorkCheckpointChanges(
                acceptances: [new BackgroundActivityWorkAcceptance("workflow-a", request.WorkId, ready.ReceiptDigest!, Now.AddHours(1))],
                cancellations: [new BackgroundActivityWorkCancellationRequest("workflow-a")]))
        }, Immediate);

        Assert.Equal(BackgroundActivityWorkStatus.Accepted, (await workerA.FindAsync("workflow-a", request.WorkId))!.Status);
    }

    [Fact]
    public async Task Background_checkpoint_participants_override_skip_policy_with_immediate_decision()
    {
        var request = Request("work-immediate");
        var commit = Commit("mandatory") with
        {
            ExpectedFence = new RuntimeExecutionFence("lease", "owner", 1),
            StateChanges = Commit("mandatory").StateChanges.WithBackgroundActivityWork(new BackgroundActivityWorkCheckpointChanges(admissions: [request]))
        };
        var store = new RecordingStore();
        var committer = new RuntimeCheckpointCommitter(
            new FixedPolicy(RuntimeCheckpointPersistenceMode.Skip),
            store,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
            [], []);

        await committer.CommitAsync(commit);

        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, Assert.Single(store.Decisions).Mode);
    }

    [Fact]
    public async Task Initial_workflow_upsert_and_background_admission_can_share_the_first_checkpoint()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var operational = new InMemoryExecutionLivenessStateStore();
        var executions = new InMemoryWorkflowExecutionStateStore(state, operational);
        var owner = await NewOwnership(operational, "initial-background-owner").AcquireAsync("workflow-a");
        var accessor = new FixedAccessor("tenant-a");
        var checkpoint = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: executions,
            operationalStateStore: operational,
            rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance,
            state: state,
            persistenceAccessContextAccessor: accessor);
        var request = Request("work-initial-upsert");
        var commit = Commit("admit-initial-upsert") with
        {
            ExpectedFence = owner.ToFence(),
            StateChanges = new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(
                    "workflow-a", RuntimeStateChangeOperation.Upsert, ExecutionState(), new Dictionary<string, string>()),
                null, [], [], [], [], []).WithBackgroundActivityWork(
                new BackgroundActivityWorkCheckpointChanges(admissions: [request]))
        };

        await checkpoint.CommitAsync(commit, Immediate);

        Assert.NotNull(await executions.FindAsync("workflow-a"));
        Assert.Equal(BackgroundActivityWorkStatus.Pending,
            (await new InMemoryBackgroundActivityWorkStore(state, accessor).FindAsync("workflow-a", request.WorkId))!.Status);

        var orphanState = new InMemoryRuntimeCheckpointStoreState();
        var orphanAccessor = new FixedAccessor("tenant-a");
        var storeWithoutExecutionPersistence = new InMemoryRuntimeCheckpointCommitStore(
            operationalStateStore: operational,
            state: orphanState,
            persistenceAccessContextAccessor: orphanAccessor);
        var orphanRequest = Request("work-no-execution-store");
        var unpersistableUpsert = Commit("admit-no-execution-store") with
        {
            ExpectedFence = owner.ToFence(),
            StateChanges = new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(
                    "workflow-a", RuntimeStateChangeOperation.Upsert, ExecutionState(), new Dictionary<string, string>()),
                null, [], [], [], [], []).WithBackgroundActivityWork(
                new BackgroundActivityWorkCheckpointChanges(admissions: [orphanRequest]))
        };
        await Assert.ThrowsAsync<RuntimeCheckpointCommitValidationException>(() =>
            storeWithoutExecutionPersistence.CommitAsync(unpersistableUpsert, Immediate).AsTask());
        Assert.Null(await new InMemoryBackgroundActivityWorkStore(orphanState, orphanAccessor).FindAsync("workflow-a", orphanRequest.WorkId));
    }

    [Fact]
    public async Task Deleting_execution_cancels_claimed_work_and_blocks_stale_or_orphan_admission()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var operational = new InMemoryExecutionLivenessStateStore();
        var executions = new InMemoryWorkflowExecutionStateStore(state, operational);
        await executions.SaveAsync(ExecutionState());
        var ownership = NewOwnership(operational, "delete-test-owner");
        var oldOwner = await ownership.AcquireAsync("workflow-a");
        var accessor = new FixedAccessor("tenant-a");
        var accessorB = new FixedAccessor("tenant-b");
        var checkpoint = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: executions,
            operationalStateStore: operational,
            state: state,
            persistenceAccessContextAccessor: accessor);
        var worker = new InMemoryBackgroundActivityWorkStore(state, accessor);
        var workerB = new InMemoryBackgroundActivityWorkStore(state, accessorB);
        var request = Request("work-delete");
        var admission = Commit("admit-delete") with
        {
            ExpectedFence = oldOwner.ToFence(),
            StateChanges = Commit("admit-delete").StateChanges.WithBackgroundActivityWork(
                new BackgroundActivityWorkCheckpointChanges(admissions: [request]))
        };
        await checkpoint.CommitAsync(admission, Immediate);
        var checkpointB = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: executions,
            operationalStateStore: operational,
            state: state,
            persistenceAccessContextAccessor: accessorB);
        var requestB = Request("work-delete-tenant-b");
        var admissionB = Commit("admit-delete-tenant-b") with
        {
            ExpectedFence = oldOwner.ToFence(),
            StateChanges = Commit("admit-delete-tenant-b").StateChanges.WithBackgroundActivityWork(
                new BackgroundActivityWorkCheckpointChanges(admissions: [requestB]))
        };
        await checkpointB.CommitAsync(admissionB, Immediate);
        var claim = await worker.ClaimNextAsync("worker-delete", Now.AddSeconds(1), TimeSpan.FromMinutes(1));
        Assert.NotNull(claim);

        Assert.True(await executions.DeleteAsync("workflow-a"));
        var tombstone = await operational.FindAsync("workflow-a", RuntimeExecutionOwnershipStateId.For("workflow-a"));
        Assert.Equal("true", tombstone!.Metadata[RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone]);
        var cancelled = await worker.FindAsync("workflow-a", request.WorkId);
        Assert.Equal(BackgroundActivityWorkStatus.Cancelled, cancelled!.Status);
        Assert.True(cancelled.ClaimFence > claim!.ClaimFence);
        Assert.Empty(await worker.ListResultReadyAsync(10));
        // The in-memory execution store is globally keyed by execution ID, so deletion cancels both scope projections.
        Assert.Equal(BackgroundActivityWorkStatus.Cancelled, (await workerB.FindAsync("workflow-a", requestB.WorkId))!.Status);
        await Assert.ThrowsAsync<BackgroundActivityWorkConflictException>(() => worker.RecordResultAsync(
            claim, Complete("{}"), Now.AddSeconds(2)).AsTask());
        Assert.Null(await executions.FindAsync("workflow-a"));

        var staleAdmission = Commit("admit-after-delete-stale") with
        {
            ExpectedFence = oldOwner.ToFence(),
            StateChanges = Commit("admit-after-delete-stale").StateChanges.WithBackgroundActivityWork(
                new BackgroundActivityWorkCheckpointChanges(admissions: [Request("work-stale")]))
        };
        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => checkpoint.CommitAsync(staleAdmission, Immediate).AsTask());

        await Assert.ThrowsAsync<InvalidOperationException>(() => ownership.AcquireAsync("workflow-a").AsTask());
        var bareAdmission = Commit("admit-after-delete-bare") with
        {
            ExpectedFence = oldOwner.ToFence(),
            StateChanges = Commit("admit-after-delete-bare").StateChanges.WithBackgroundActivityWork(
                new BackgroundActivityWorkCheckpointChanges(admissions: [Request("work-bare")]))
        };
        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => checkpoint.CommitAsync(bareAdmission, Immediate).AsTask());

        var orphanAdmission = Commit("admit-after-delete-new-owner") with
        {
            ExpectedFence = oldOwner.ToFence(),
            StateChanges = new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(
                    "workflow-a", RuntimeStateChangeOperation.Upsert, ExecutionState(), new Dictionary<string, string>()),
                null, [], [], [], [], []).WithBackgroundActivityWork(
                new BackgroundActivityWorkCheckpointChanges(admissions: [Request("work-orphan")]))
        };
        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => checkpoint.CommitAsync(orphanAdmission, Immediate).AsTask());
        Assert.Null(await worker.FindAsync("workflow-a", "work-stale"));
        Assert.Null(await worker.FindAsync("workflow-a", "work-orphan"));
    }

    [Fact]
    public async Task Deleting_execution_without_a_prior_owner_still_tombstones_later_admission()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var operational = new InMemoryExecutionLivenessStateStore();
        var executions = new InMemoryWorkflowExecutionStateStore(state, operational);
        await executions.SaveAsync(ExecutionState());
        Assert.True(await executions.DeleteAsync("workflow-a"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewOwnership(operational, "owner-after-ownerless-delete").AcquireAsync("workflow-a").AsTask());
        var accessor = new FixedAccessor("tenant-a");
        var checkpoint = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: executions,
            operationalStateStore: operational,
            rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance,
            state: state,
            persistenceAccessContextAccessor: accessor);
        var recreateAndAdmit = Commit("admit-after-ownerless-delete") with
        {
            ExpectedFence = new RuntimeExecutionFence("unissued-lease", "unissued-owner", 1),
            StateChanges = new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(
                    "workflow-a", RuntimeStateChangeOperation.Upsert, ExecutionState(), new Dictionary<string, string>()),
                null, [], [], [], [], []).WithBackgroundActivityWork(
                new BackgroundActivityWorkCheckpointChanges(admissions: [Request("work-ownerless-delete")]))
        };

        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => checkpoint.CommitAsync(recreateAndAdmit, Immediate).AsTask());
        Assert.Null(await executions.FindAsync("workflow-a"));
        Assert.Null(await new InMemoryBackgroundActivityWorkStore(state, accessor).FindAsync("workflow-a", "work-ownerless-delete"));
    }

    [Fact]
    public void Exact_claim_replay_identity_includes_the_original_lease_revision()
    {
        var item = BackgroundActivityWorkLifecycle.CreatePending(Request("work-claim"), Now);
        var claimed = BackgroundActivityWorkLifecycle.TryClaim(item, "worker", Now, TimeSpan.FromMinutes(1), out var claim)!;
        var result = Complete("{}");
        var ready = BackgroundActivityWorkLifecycle.RecordResult(claimed, claim!, result, Now.AddSeconds(1));
        var forged = new BackgroundActivityWorkClaim(
            claim!.WorkflowExecutionId, claim.WorkId, claim.ClaimOwnerId, claim.ClaimFence,
            claim.ClaimedAt, claim.VisibleUntil, claim.Revision + 1);

        Assert.Throws<BackgroundActivityWorkConflictException>(() =>
            BackgroundActivityWorkLifecycle.RecordResult(ready, forged, result, Now.AddHours(1)));
        Assert.Same(ready, BackgroundActivityWorkLifecycle.RecordResult(ready, claim, result, Now.AddHours(1)));
    }

    private static BackgroundActivityWorkRequest Request(string workId)
    {
        var attempt = new ActivityAttempt($"attempt-{workId}", $"invocation-{workId}", 1, ActivityAttemptReason.Initial, Now);
        return new BackgroundActivityWorkRequest(
            workId, "workflow-a", $"activity-{workId}", attempt,
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            "node", "contract-fingerprint",
            new ActivityInputSnapshot(attempt.InvocationId, "contract-fingerprint", "binding-fingerprint", new Dictionary<string, ValueEnvelope>(), Now),
            new RuntimeSchedulerWorkItem($"coordinator-{workId}", "workflow-a", $"command-{workId}", WorkflowExecutionCommandKind.ScheduleActivity,
                $"envelope-{workId}", $"idempotency-{workId}", Now, Now, 1));
    }

    private static BackgroundActivityWorkTerminalResult Complete(string resultJson) =>
        new(BackgroundActivityWorkResultKind.Complete, resultJson: resultJson);

    private static RuntimeCheckpointCommit Commit(string commitId) => new(
        commitId,
        new RuntimeCheckpoint($"checkpoint-{commitId}", "BackgroundActivityWork", "workflow-a", Now, [], new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []), [], new Dictionary<string, string>());

    private static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate);

    private static WorkflowExecutionState ExecutionState() => new(
        "workflow-a",
        new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
        WorkflowExecutionStatus.Running,
        null,
        Now,
        Now,
        Now,
        null,
        null,
        null,
        "tenant-a",
        new Dictionary<string, string>());

    private static RuntimeExecutionOwnershipService NewOwnership(IExecutionLivenessStateStore operational, string ownerId) =>
        new(operational, new FixedTimeProvider(Now), new RuntimeExecutionOwnershipOptions { OwnerId = ownerId, LeaseDuration = TimeSpan.FromHours(1) });

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class FixedPolicy(RuntimeCheckpointPersistenceMode mode) : IRuntimeCheckpointPersistencePolicy
    {
        public ValueTask<RuntimeCheckpointPersistenceDecision> DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeCheckpointPersistenceDecision(mode));
    }

    private sealed class RecordingStore : IRuntimeCheckpointCommitStore
    {
        public List<RuntimeCheckpointPersistenceDecision> Decisions { get; } = [];

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            Decisions.Add(decision);
            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]));
        }
    }
}
