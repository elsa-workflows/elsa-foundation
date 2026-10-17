using Elsa.Workflows.Runtime.Contracts;
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
        var tenantA = new FixedAccessor("tenant-a");
        var tenantB = new FixedAccessor("tenant-b");
        var checkpoint = new InMemoryRuntimeCheckpointCommitStore(state: state, persistenceAccessContextAccessor: tenantA);
        var workerA = new InMemoryBackgroundActivityWorkStore(state, tenantA);
        var workerB = new InMemoryBackgroundActivityWorkStore(state, tenantB);
        var request = Request("work-memory");

        var admission = Commit("admit");
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
