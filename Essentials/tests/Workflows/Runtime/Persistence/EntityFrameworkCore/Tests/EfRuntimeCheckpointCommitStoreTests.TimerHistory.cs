using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    private static readonly RuntimeHistoryCapturePolicy TimerHistoryPolicy = new("test-timer-history", 1, true,
        ["WorkflowStarted", "WorkflowCompleted", "WorkflowFaulted", "TimerScheduled", "TimerDispatched", "TimerRetired"]);

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Timer_schedule_ack_preserves_an_active_claim_and_terminal_source_survives_restart_until_ack()
    {
        await using var db = await TestDatabase.CreateAsync();
        RuntimeDurableTimerClaim claim;
        DurableTimer admitted;
        await using (var context = db.Open("tenant-a"))
        {
            await SeedNoRecordOwnerAsync(context);
            var store = TimerHistoryStore(context);
            admitted = await store.SaveAsync(HistoryTimer("timer-a"));
            Assert.True(store.SupportsHistoryResponsibility);
            Assert.Single((await store.ListPendingHistoryAsync(new())).Items);
            claim = Assert.Single(await store.ClaimDueAsync(new("owner", OccurredAt.AddMinutes(1), TimeSpan.FromMinutes(5), 1)));
            var scheduled = admitted.HistoryResponsibility!.PendingEvents.Single();
            await RecordTimerHistoryOwnerAsync(context);
            Assert.True((await HistoryStore(context, new FixedAccessor("tenant-a")).RecordAsync(new(TimerHistoryPolicy, scheduled))).Recorded);
            Assert.True(await store.AcknowledgeHistoryAsync("workflow-a", "timer-a", scheduled.HistoryEventId));
            var persisted = await context.DurableTimers.AsNoTracking().SingleAsync();
            Assert.Equal(claim.Revision, persisted.Revision);
            var dispatched = DurableTimerHistoryFacts.CreateDispatched(admitted, OccurredAt.AddMinutes(2), BookmarkResumeDispatchStatus.Dispatched)!;
            Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.Succeeded, (await store.CompleteClaimWithHistoryAsync(claim, dispatched)).Status);
            Assert.Null(await store.FindAsync("workflow-a", "timer-a"));
            Assert.Empty(await store.ListDueAsync(OccurredAt.AddHours(1), 100));
            Assert.Empty(await store.ClaimDueAsync(new("other", OccurredAt.AddHours(1), TimeSpan.FromMinutes(5), 1)));
            Assert.Null(await store.GetNextDueTimeAsync());
        }
        await using var restarted = db.Open("tenant-a");
        var reopened = TimerHistoryStore(restarted);
        var pending = Assert.Single((await reopened.ListPendingHistoryAsync(new())).Items);
        Assert.True(pending.HistoryResponsibility!.Retired);
        var terminal = Assert.Single(pending.HistoryResponsibility.PendingEvents);
        Assert.Equal("TimerDispatched", terminal.Name);
        Assert.True((await HistoryStore(restarted, new FixedAccessor("tenant-a")).RecordAsync(new(TimerHistoryPolicy, terminal))).Recorded);
        Assert.True(await reopened.AcknowledgeHistoryAsync("workflow-a", "timer-a", terminal.HistoryEventId));
        Assert.Empty((await reopened.ListPendingHistoryAsync(new())).Items);
        // A late replay returns the retired first winner and cannot create a new fireable deadline.
        var replay = await reopened.SaveAsync(HistoryTimer("timer-a") with { CreatedAt = OccurredAt.AddDays(1) });
        Assert.True(replay.HistoryResponsibility!.Retired);
        Assert.Equal(admitted.CreatedAt, replay.CreatedAt);
        Assert.Empty(await reopened.ListDueAsync(OccurredAt.AddDays(2), 100));
        Assert.Single(await restarted.DurableTimers.ToArrayAsync());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Checkpoint_scope_cleanup_retires_timer_and_marker_failure_rolls_back_both_responsibilities()
    {
        await using var db = await TestDatabase.CreateAsync();
        var failure = new FailMarkerInsertInterceptor();
        await using var context = db.Open("tenant-a", failure);
        await SeedNoRecordOwnerAsync(context);
        var timers = TimerHistoryStore(context);
        var timer = await timers.SaveAsync(HistoryTimer("timer-cleanup"));
        var commit = WithScopeCleanup("timer-cleanup");
        failure.Arm();
        await Assert.ThrowsAsync<DbUpdateException>(() => new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision()).AsTask());
        var afterFailure = Assert.Single((await timers.ListPendingHistoryAsync(new())).Items);
        Assert.False(afterFailure.HistoryResponsibility!.Retired);
        Assert.Single(afterFailure.HistoryResponsibility.PendingEvents);
        await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision());
        var afterCommit = Assert.Single((await timers.ListPendingHistoryAsync(new())).Items);
        Assert.True(afterCommit.HistoryResponsibility!.Retired);
        Assert.Equal(["TimerScheduled", "TimerRetired"], afterCommit.HistoryResponsibility.PendingEvents.Select(e => e.Name));
        Assert.Empty(await timers.ListDueAsync(OccurredAt.AddDays(1), 10));
        await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision());
        Assert.Equal(2, Assert.Single((await timers.ListPendingHistoryAsync(new())).Items).HistoryResponsibility!.PendingEvents.Count);
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Timer_pending_pages_are_bounded_scope_bound_and_legacy_first_winner_stays_uncaptured()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var context = db.Open("tenant-a");
        await SeedNoRecordOwnerAsync(context);
        var timers = TimerHistoryStore(context);
        var legacy = HistoryTimer("legacy") with { HistoryResponsibility = null };
        await timers.SaveAsync(legacy);
        Assert.Null((await timers.SaveAsync(HistoryTimer("legacy"))).HistoryResponsibility);
        await timers.SaveAsync(HistoryTimer("timer-a")); await timers.SaveAsync(HistoryTimer("timer-b"));
        var first = await timers.ListPendingHistoryAsync(new(1));
        Assert.Single(first.Items); Assert.NotNull(first.NextContinuationToken);
        var second = await timers.ListPendingHistoryAsync(new(1, first.NextContinuationToken));
        Assert.Single(second.Items); Assert.Null(second.NextContinuationToken);
        Assert.NotEqual(first.Items[0].TimerId, second.Items[0].TimerId);
        await Assert.ThrowsAsync<ArgumentException>(() => TimerHistoryStore(context, "tenant-b").ListPendingHistoryAsync(new(1, first.NextContinuationToken)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => timers.ListPendingHistoryAsync(new(2, first.NextContinuationToken)).AsTask());
    }

    private static async Task RecordTimerHistoryOwnerAsync(RuntimeDbContext context)
    {
        var state = Execution("workflow-a", "tenant-a") with { HistoryCapturePolicy = TimerHistoryPolicy, HistoryCaptureEnabledAtAdmission = true };
        context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a", EfWorkflowExecutionStateStore.CreateId("tenant-a", "workflow-a"), 1));
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var checkpoint = new RuntimeCheckpoint("timer-owner-start", "WorkflowStarted", "workflow-a", OccurredAt, [], new Dictionary<string, string>());
        var e = new RuntimeHistoryEvent("timer-owner-start-event", "workflow-a", 0, RuntimeHistoryEventCategory.WorkflowLifecycle, "WorkflowStarted", OccurredAt, checkpointId: checkpoint.CheckpointId)
        { Authority = RuntimeHistoryAuthority.CommittedTransition };
        var commit = new RuntimeCheckpointCommit("timer-owner-start-physical", checkpoint, new(null, null, [], [], [], [], []), [], new Dictionary<string, string>())
        { HistoryBatches = [new(TimerHistoryPolicy, checkpoint.CheckpointId, OccurredAt, true, [new(e, 0)])] };
        await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision());
    }

    private static EfDurableTimerStore TimerHistoryStore(RuntimeDbContext context, string scope = "tenant-a") => new(context, new FixedAccessor(scope),
        new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "run-history-tests-exact-signing-key-32" })), new FixedTimeProvider(OccurredAt));
    private static DurableTimer HistoryTimer(string id)
    {
        var timer = new DurableTimer(id, "workflow-a", "safe-type", "safe-hash", OccurredAt.AddSeconds(1), OccurredAt, ActivityExecutionId: "activity-a", ExecutionScopeId: "scope-a");
        var scheduled = DurableTimerHistoryFacts.CreateScheduled(timer, TimerHistoryPolicy)!;
        return timer with { HistoryResponsibility = new(TimerHistoryPolicy, [scheduled.Event], false) };
    }
}
