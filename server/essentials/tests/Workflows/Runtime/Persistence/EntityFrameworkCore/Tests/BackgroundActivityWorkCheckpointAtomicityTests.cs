using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class BackgroundActivityWorkCheckpointAtomicityTests
{
    private static readonly DateTimeOffset Now = new(2032, 4, 5, 6, 7, 8, TimeSpan.Zero);

    [Fact]
    public async Task Marker_failure_rolls_back_receipt_acceptance_activity_state_and_continuation_intent_together()
    {
        await using var database = await TestDatabase.CreateAsync();
        var request = Request("work-atomic", "activity-atomic");
        var owner = await AcquireAsync(database, "owner-atomic", Now);
        string receiptDigest;

        await using (var context = database.Open("tenant-a"))
        {
            var checkpoints = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
            var admission = AdmissionCommit("commit-admit-atomic", request) with { ExpectedFence = owner.ToFence() };
            await checkpoints.CommitAsync(admission, Decision());

            var worker = new EfBackgroundActivityWorkStore(context, new FixedAccessor("tenant-a"));
            var claim = await worker.ClaimNextAsync("worker-atomic", Now.AddSeconds(1), TimeSpan.FromMinutes(1));
            Assert.NotNull(claim);
            var ready = await worker.RecordResultAsync(claim!, Complete("{\"value\":42}"), Now.AddSeconds(2));
            receiptDigest = ready.ReceiptDigest!;
        }

        var interceptor = new FailCheckpointMarkerInsertInterceptor();
        await using (var failing = database.Open("tenant-a", interceptor))
        {
            interceptor.Arm();
            var terminal = TerminalCommit(
                "commit-accept-atomic",
                "activity-atomic",
                new BackgroundActivityWorkAcceptance("workflow-a", request.WorkId, receiptDigest, Now.AddSeconds(3)));
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new EfRuntimeCheckpointCommitStore(failing, new FixedAccessor("tenant-a"))
                    .CommitAsync(terminal, Decision()).AsTask());
        }

        await using var verification = database.Open("tenant-a");
        var stored = await new EfBackgroundActivityWorkStore(verification, new FixedAccessor("tenant-a"))
            .FindAsync("workflow-a", request.WorkId);
        Assert.Equal(BackgroundActivityWorkStatus.ResultReady, stored!.Status);
        Assert.Equal(receiptDigest, stored.ReceiptDigest);
        Assert.Empty(await verification.ActivityExecutionStates.ToArrayAsync());
        Assert.Empty(await verification.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Equal(1, await verification.RuntimeCheckpointCommits.CountAsync());
    }

    [Fact]
    public async Task Old_coordinator_fence_cannot_accept_ready_receipt_or_write_native_participants_after_successor_acquires_ownership()
    {
        await using var database = await TestDatabase.CreateAsync();
        var oldOwner = await AcquireAsync(database, "owner-old", Now);
        var request = Request("work-fenced-accept", "activity-fenced-accept");
        string receiptDigest;

        await using (var context = database.Open("tenant-a"))
        {
            var admission = AdmissionCommit("commit-fenced-admission", request) with { ExpectedFence = oldOwner.ToFence() };
            await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"))
                .CommitAsync(admission, Decision());

            var worker = new EfBackgroundActivityWorkStore(context, new FixedAccessor("tenant-a"));
            var claim = await worker.ClaimNextAsync("worker-fenced", Now.AddSeconds(1), TimeSpan.FromMinutes(1));
            Assert.NotNull(claim);
            receiptDigest = (await worker.RecordResultAsync(claim!, Complete("{\"accepted\":true}"), Now.AddSeconds(2))).ReceiptDigest!;
        }

        var newOwner = await AcquireAsync(database, "owner-new", Now.AddMinutes(2));
        Assert.Equal(oldOwner.FencingToken + 1, newOwner.FencingToken);

        var staleCommit = TerminalCommit(
            "commit-stale-accept",
            "activity-fenced-accept",
            new BackgroundActivityWorkAcceptance("workflow-a", request.WorkId, receiptDigest, Now.AddMinutes(2).AddSeconds(1))) with
        {
            ExpectedFence = oldOwner.ToFence()
        };
        Assert.Equal(oldOwner.ToFence(), staleCommit.ExpectedFence);

        await using (var staleContext = database.Open("tenant-a"))
        {
            var exception = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() =>
                new EfRuntimeCheckpointCommitStore(staleContext, new FixedAccessor("tenant-a"), new FixedTimeProvider(Now.AddMinutes(2)))
                    .CommitAsync(staleCommit, Decision()).AsTask());
            Assert.Equal(oldOwner.FencingToken, exception.PresentedFencingToken);
            Assert.Equal(newOwner.FencingToken, exception.CurrentFencingToken);
        }

        await using var verification = database.Open("tenant-a");
        var workerStore = new EfBackgroundActivityWorkStore(verification, new FixedAccessor("tenant-a"));
        var item = await workerStore.FindAsync("workflow-a", request.WorkId);
        Assert.Equal(BackgroundActivityWorkStatus.ResultReady, item!.Status);
        Assert.Equal(receiptDigest, item.ReceiptDigest);
        Assert.Empty(await verification.ActivityExecutionStates.ToArrayAsync());
        Assert.Empty(await verification.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Equal(1, await verification.RuntimeCheckpointCommits.CountAsync());

        var currentOwnership = await new EfExecutionLivenessStateStore(
                verification,
                new FixedAccessor("tenant-a"),
                new NoopContinuationCodec())
            .FindAsync("workflow-a", "ownership:workflow-a");
        Assert.Equal(newOwner.LeaseId, currentOwnership!.ExecutionLease!.LeaseId);
        Assert.Equal(newOwner.OwnerId, currentOwnership.ExecutionLease.OwnerId);
        Assert.Equal(newOwner.FencingToken, currentOwnership.ExecutionLease.FencingToken);
        Assert.Equal(newOwner.ExpiresAt, currentOwnership.ExecutionLease.ExpiresAt);
    }

    [Fact]
    public async Task Result_ready_cancellation_is_provider_atomic_and_limited_to_the_requested_subtree()
    {
        await using var database = await TestDatabase.CreateAsync();
        var requests = new[]
        {
            Request("work-root", "activity-root"),
            Request("work-child", "activity-child"),
            Request("work-grandchild", "activity-grandchild"),
            Request("work-sibling", "activity-sibling")
        };
        var owner = await AcquireAsync(database, "owner-subtree", Now);

        await using (var context = database.Open("tenant-a"))
        {
            var checkpoints = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
            var admission = AdmissionCommit("commit-subtree-admission", requests) with { ExpectedFence = owner.ToFence() };
            await checkpoints.CommitAsync(admission, Decision());

            var worker = new EfBackgroundActivityWorkStore(context, new FixedAccessor("tenant-a"));
            for (var index = 0; index < requests.Length; index++)
            {
                var claim = await worker.ClaimNextAsync($"worker-{index}", Now.AddSeconds(index + 1), TimeSpan.FromMinutes(1));
                Assert.NotNull(claim);
                await worker.RecordResultAsync(claim!, Complete($"{{\"worker\":{index}}}"), Now.AddSeconds(index + 1));
            }

            Assert.Equal(4, (await worker.ListResultReadyAsync(10)).Count);
            var cancel = Commit("commit-subtree-cancel") with
            {
                StateChanges = Commit("commit-subtree-cancel").StateChanges.WithBackgroundActivityWork(
                    new BackgroundActivityWorkCheckpointChanges(cancellations:
                    [new BackgroundActivityWorkCancellationRequest("workflow-a", ["activity-child", "activity-grandchild"])]))
            };
            await checkpoints.CommitAsync(cancel, Decision());

            var root = await worker.FindAsync("workflow-a", "work-root");
            var child = await worker.FindAsync("workflow-a", "work-child");
            var grandchild = await worker.FindAsync("workflow-a", "work-grandchild");
            var sibling = await worker.FindAsync("workflow-a", "work-sibling");
            Assert.Equal(BackgroundActivityWorkStatus.ResultReady, root!.Status);
            Assert.Equal(BackgroundActivityWorkStatus.Cancelled, child!.Status);
            Assert.Equal(BackgroundActivityWorkStatus.Cancelled, grandchild!.Status);
            Assert.Equal(BackgroundActivityWorkStatus.ResultReady, sibling!.Status);
            Assert.NotNull(child.Result);
            Assert.NotNull(grandchild.Result);
            Assert.Equal(2, child.ClaimFence);
            Assert.Equal(2, grandchild.ClaimFence);

            var rejectedAcceptance = Commit("commit-cancel-wins-receipt-race") with
            {
                StateChanges = Commit("commit-cancel-wins-receipt-race").StateChanges.WithBackgroundActivityWork(
                    new BackgroundActivityWorkCheckpointChanges(acceptances:
                    [new BackgroundActivityWorkAcceptance("workflow-a", child.Request.WorkId, child.ReceiptDigest!, Now.AddSeconds(10))]))
            };
            await Assert.ThrowsAsync<BackgroundActivityWorkConflictException>(() => checkpoints.CommitAsync(rejectedAcceptance, Decision()).AsTask());
            Assert.Equal(2, await context.RuntimeCheckpointCommits.CountAsync());
        }

        await using var restarted = database.Open("tenant-a");
        var persisted = new EfBackgroundActivityWorkStore(restarted, new FixedAccessor("tenant-a"));
        Assert.Equal(BackgroundActivityWorkStatus.Cancelled, (await persisted.FindAsync("workflow-a", "work-child"))!.Status);
        Assert.Equal(BackgroundActivityWorkStatus.ResultReady, (await persisted.FindAsync("workflow-a", "work-sibling"))!.Status);
    }

    [Fact]
    public async Task Concurrent_equivalent_receipt_write_returns_success_only_after_the_winning_receipt_is_durable()
    {
        await using var database = await TestDatabase.CreateAsync();
        var request = Request("work-receipt-race", "activity-receipt-race");
        var claim = await AdmitAndClaimAsync(database, request);
        var pause = new PauseBeforeSaveChangesInterceptor();
        await using var firstContext = database.Open("tenant-a", pause);
        await using var secondContext = database.Open("tenant-a");
        var first = new EfBackgroundActivityWorkStore(firstContext, new FixedAccessor("tenant-a"));
        var second = new EfBackgroundActivityWorkStore(secondContext, new FixedAccessor("tenant-a"));

        var delayedRecord = first.RecordResultAsync(claim, Complete("{\"result\":1}"), Now.AddSeconds(2)).AsTask();
        BackgroundActivityWorkItem winner;
        try
        {
            await pause.WaitUntilPausedAsync();
            winner = await second.RecordResultAsync(claim, Complete("{ \"result\" : 1 }"), Now.AddSeconds(3));
        }
        finally
        {
            pause.Resume();
        }
        var replay = await delayedRecord;

        Assert.Equal(BackgroundActivityWorkStatus.ResultReady, winner.Status);
        Assert.Equal(winner.ReceiptDigest, replay.ReceiptDigest);
        Assert.Equal(winner.ReceiptDigest, BackgroundActivityWorkReceipt.ComputeDigest(request, replay.Result!));
    }

    [Fact]
    public async Task Concurrent_claim_renewal_cannot_turn_an_unrecorded_stale_receipt_into_success()
    {
        await using var database = await TestDatabase.CreateAsync();
        var request = Request("work-unrecorded-race", "activity-unrecorded-race");
        var claim = await AdmitAndClaimAsync(database, request);
        var pause = new PauseBeforeSaveChangesInterceptor();
        await using var firstContext = database.Open("tenant-a", pause);
        await using var secondContext = database.Open("tenant-a");
        var first = new EfBackgroundActivityWorkStore(firstContext, new FixedAccessor("tenant-a"));
        var second = new EfBackgroundActivityWorkStore(secondContext, new FixedAccessor("tenant-a"));

        var delayedRecord = first.RecordResultAsync(claim, Complete("{\"result\":1}"), Now.AddSeconds(2)).AsTask();
        BackgroundActivityWorkClaim? renewedClaim;
        try
        {
            await pause.WaitUntilPausedAsync();
            renewedClaim = await second.RenewClaimAsync(claim, Now.AddSeconds(3), TimeSpan.FromMinutes(2));
        }
        finally
        {
            pause.Resume();
        }
        Assert.NotNull(renewedClaim);

        await Assert.ThrowsAsync<BackgroundActivityWorkConflictException>(() => delayedRecord);
        var current = await second.FindAsync("workflow-a", request.WorkId);
        Assert.Equal(BackgroundActivityWorkStatus.Claimed, current!.Status);
        Assert.Equal(renewedClaim!.Revision, current.Revision);
        Assert.Null(current.Result);
        Assert.Null(current.ReceiptDigest);
    }

    private static RuntimeCheckpointCommit TerminalCommit(
        string commitId,
        string activityExecutionId,
        BackgroundActivityWorkAcceptance acceptance)
    {
        var intent = new RuntimePostCommitIntent(
            $"intent-{commitId}", "workflow-a", "test.continue", Now,
            activityExecutionId, $"idempotency-{commitId}", null);
        var outboxId = RuntimePostCommitOutboxIdentity.CreateLogicalValue(commitId, intent.IntentId);
        var outbox = new RuntimePostCommitOutboxItem(outboxId, intent, RuntimePostCommitOutboxStatus.Pending, Now, Now);
        var stateChanges = new RuntimeCheckpointStateChangeSet(
            null,
            null,
            [new RuntimeStateChange<ActivityExecutionState>(activityExecutionId, RuntimeStateChangeOperation.Upsert, Activity(activityExecutionId), new Dictionary<string, string>())],
            [], [], [], [],
            postCommitOutbox:
            [new RuntimeStateChange<RuntimePostCommitOutboxItem>(outboxId, RuntimeStateChangeOperation.Upsert, outbox, new Dictionary<string, string>())])
            .WithBackgroundActivityWork(new BackgroundActivityWorkCheckpointChanges(acceptances: [acceptance]));

        return Commit(commitId) with { PostCommitIntents = [intent], StateChanges = stateChanges };
    }

    private static ActivityExecutionState Activity(string activityExecutionId) => new(
        new ActivityExecution(activityExecutionId, "workflow-a", $"node-{activityExecutionId}", $"authored-{activityExecutionId}", "Test.Activity", "1"),
        ActivityExecutionStatus.Completed, null, Now, Now, Now, null, null, null, null, null, [], [], 0, 0,
        new Dictionary<string, string>());

    private static async Task<RuntimeExecutionLease> AcquireAsync(TestDatabase database, string ownerId, DateTimeOffset now)
    {
        await using var context = database.Open("tenant-a");
        var accessor = new FixedAccessor("tenant-a");
        return await BackgroundActivityWorkTestSupport.SeedLiveExecutionAsync(
            context, accessor, "workflow-a", "tenant-a", new FixedTimeProvider(now), ownerId);
    }

    private static async Task<BackgroundActivityWorkClaim> AdmitAndClaimAsync(TestDatabase database, BackgroundActivityWorkRequest request)
    {
        await using var context = database.Open("tenant-a");
        var owner = await BackgroundActivityWorkTestSupport.SeedLiveExecutionAsync(
            context, new FixedAccessor("tenant-a"), request.WorkflowExecutionId, "tenant-a",
            new FixedTimeProvider(Now), $"owner-{request.WorkId}");
        var checkpoint = AdmissionCommit($"commit-admit-{request.WorkId}", request) with { ExpectedFence = owner.ToFence() };
        await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(checkpoint, Decision());
        return (await new EfBackgroundActivityWorkStore(context, new FixedAccessor("tenant-a"))
            .ClaimNextAsync($"worker-{request.WorkId}", Now.AddSeconds(1), TimeSpan.FromMinutes(1)))!;
    }

    private static BackgroundActivityWorkRequest Request(string workId, string activityExecutionId)
    {
        var attempt = new ActivityAttempt($"attempt-{workId}", $"invocation-{workId}", 1, ActivityAttemptReason.Initial, Now);
        return new BackgroundActivityWorkRequest(
            workId, "workflow-a", activityExecutionId, attempt,
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            $"node-{activityExecutionId}", "contract-fingerprint",
            new ActivityInputSnapshot(attempt.InvocationId, "contract-fingerprint", "binding-fingerprint", new Dictionary<string, ValueEnvelope>(), Now),
            new RuntimeSchedulerWorkItem($"coordinator-{workId}", "workflow-a", $"command-{workId}", WorkflowExecutionCommandKind.ScheduleActivity,
                $"envelope-{workId}", $"idempotency-{workId}", Now, Now, 1));
    }

    private static BackgroundActivityWorkTerminalResult Complete(string resultJson) =>
        new(BackgroundActivityWorkResultKind.Complete, resultJson: resultJson);

    private static RuntimeCheckpointCommit Commit(string commitId) => new(
        commitId,
        new RuntimeCheckpoint($"checkpoint-{commitId}", "BackgroundActivityWork", "workflow-a", Now, [], new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
        [], new Dictionary<string, string>());

    private static RuntimeCheckpointCommit AdmissionCommit(string commitId, params BackgroundActivityWorkRequest[] requests)
    {
        var commit = Commit(commitId);
        return commit with
        {
            StateChanges = commit.StateChanges.WithBackgroundActivityWork(
                new BackgroundActivityWorkCheckpointChanges(admissions: requests))
        };
    }

    private static RuntimeCheckpointPersistenceDecision Decision() => new(RuntimeCheckpointPersistenceMode.Immediate);

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoopContinuationCodec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }

    private sealed class TestDatabase(SqliteConnection keeper, string connectionString) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:bg-atomic-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(keeper).Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(keeper, connectionString);
        }

        public RuntimeSqliteDbContext Open(string scope, params IInterceptor[] interceptors)
        {
            var builder = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connectionString);
            if (interceptors.Length > 0)
                builder.AddInterceptors(interceptors);
            return new RuntimeSqliteDbContext(builder.Options);
        }

        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }

    private sealed class FailCheckpointMarkerInsertInterceptor : DbCommandInterceptor
    {
        private int armed;

        public void Arm() => Interlocked.Exchange(ref armed, 1);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed(command);
            return ValueTask.FromResult(result);
        }

        private void ThrowIfArmed(System.Data.Common.DbCommand command)
        {
            if (command.CommandText.Contains("elsa_runtime_checkpoint_commit", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref armed, 0) == 1)
                throw new DbUpdateException("Simulated checkpoint marker insert failure.");
        }
    }

    private sealed class PauseBeforeSaveChangesInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource<bool> _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _armed = 1;

        public Task WaitUntilPausedAsync() => _paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        public void Resume() => _resume.TrySetResult(true);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                _paused.TrySetResult(true);
                await _resume.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
