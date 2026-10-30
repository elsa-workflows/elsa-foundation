using Elsa.Tasks.Core;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

public sealed class DurableTimerDeadlineTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Startup_discovers_future_timer_and_fires_at_five_seconds_without_poll_wait()
    {
        var store = new InMemoryDurableTimerStore();
        await store.SaveAsync(Timer("one", Now.AddSeconds(5)));
        await using var rig = new Rig(store);
        await rig.AdvanceAsync(TimeSpan.FromMilliseconds(1));
        Assert.Empty(rig.Dispatcher.Fires);
        await rig.AdvanceAsync(TimeSpan.FromMilliseconds(4998));
        Assert.Empty(rig.Dispatcher.Fires);
        await rig.AdvanceAsync(TimeSpan.FromMilliseconds(1));
        Assert.Equal(Now.AddSeconds(5), Assert.Single(rig.Dispatcher.Fires));
        Assert.Null(await store.GetNextDueTimeAsync());
    }

    [Fact]
    public async Task Earlier_local_timer_rearms_wait_and_existing_wins_deadline_is_not_shifted()
    {
        var store = new InMemoryDurableTimerStore();
        await using var rig = new Rig(store);
        await rig.AdvanceAsync(TimeSpan.FromMilliseconds(1));
        var scheduler = new DurableTimerScheduler(store, new ForwardingPublisher(rig.Wakeup));
        await scheduler.ScheduleAsync(Timer("later", Now.AddSeconds(8)));
        await rig.AdvanceAsync(TimeSpan.FromSeconds(1));
        await scheduler.ScheduleAsync(Timer("earlier", Now.AddSeconds(3)));
        await scheduler.ScheduleAsync(Timer("earlier", Now.AddSeconds(9)));
        await rig.AdvanceAsync(Now.AddSeconds(3) - rig.Clock.GetUtcNow());
        Assert.Equal(Now.AddSeconds(3), Assert.Single(rig.Dispatcher.Fires));
        await rig.AdvanceAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { Now.AddSeconds(3), Now.AddSeconds(8) }, rig.Dispatcher.Fires);
    }

    [Fact]
    public async Task Recovery_discovers_unnotified_peer_timer_then_uses_its_deadline()
    {
        var store = new InMemoryDurableTimerStore();
        await using var rig = new Rig(store);
        await rig.AdvanceAsync(TimeSpan.FromMilliseconds(1));
        await store.SaveAsync(Timer("peer", Now.AddSeconds(12)));
        await rig.AdvanceAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(rig.Dispatcher.Fires);
        await rig.AdvanceAsync(Now.AddSeconds(12) - rig.Clock.GetUtcNow());
        Assert.Equal(Now.AddSeconds(12), Assert.Single(rig.Dispatcher.Fires));
    }

    [Fact]
    public async Task Claim_visibility_prevents_busy_loop_and_recovers_after_lease_expiry()
    {
        var store = new InMemoryDurableTimerStore();
        await store.SaveAsync(Timer("leased", Now));
        var claim = Assert.Single(await store.ClaimDueAsync(new("peer", Now, TimeSpan.FromSeconds(7), 1)));
        Assert.Equal(Now.AddSeconds(7), await store.GetNextDueTimeAsync());
        await using var rig = new Rig(store);
        await rig.AdvanceAsync(TimeSpan.FromMilliseconds(1));
        await rig.AdvanceAsync(TimeSpan.FromMilliseconds(6998));
        Assert.Empty(rig.Dispatcher.Fires);
        await rig.AdvanceAsync(TimeSpan.FromMilliseconds(1));
        Assert.Single(rig.Dispatcher.Fires);
        Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.AlreadyApplied, (await store.CompleteClaimAsync(claim)).Status);
    }

    [Fact]
    public void Notification_racing_query_reconciliation_is_retained_even_before_schedule_exists()
    {
        var wakeup = new DurableTimerWakeup();
        var version = wakeup.Snapshot.Version;
        wakeup.TimerScheduled(Now.AddSeconds(2));
        wakeup.Reconcile(version, Now.AddSeconds(8));
        Assert.Equal(Now.AddSeconds(2), wakeup.Snapshot.Deadline);
        var clock = new FakeTimeProvider(Now);
        var calls = 0;
        using var execution = new DurableTimerDeadlineSchedule(wakeup, () => TimeSpan.FromSeconds(10),
            () => true, clock, NullLogger.Instance).ScheduleExecution(() =>
            {
                calls++;
                if (calls == 1) wakeup.Reconcile(wakeup.Snapshot.Version, Now.AddSeconds(2));
                else wakeup.Reconcile(wakeup.Snapshot.Version, null);
                return Task.CompletedTask;
            });
        clock.Advance(TimeSpan.FromMilliseconds(1));
        clock.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Later_and_duplicate_hints_keep_race_generation_without_rearming()
    {
        var wakeup = new DurableTimerWakeup();
        var changes = 0;
        wakeup.Changed += () => changes++;
        wakeup.TimerScheduled(Now.AddSeconds(5));
        var version = wakeup.Snapshot.Version;
        wakeup.TimerScheduled(Now.AddSeconds(5));
        wakeup.TimerScheduled(Now.AddSeconds(8));
        Assert.Equal(1, changes);
        Assert.True(wakeup.Snapshot.Version > version);
        wakeup.Reconcile(version, Now.AddSeconds(9));
        Assert.Equal(Now.AddSeconds(5), wakeup.Snapshot.Deadline);
    }

    [Fact]
    public async Task Notification_during_sweep_never_overlaps_and_disposal_waits_for_running_work()
    {
        var wakeup = new DurableTimerWakeup();
        var clock = new FakeTimeProvider(Now);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var execution = new DurableTimerDeadlineSchedule(wakeup, () => TimeSpan.FromSeconds(10),
            () => true, clock, NullLogger.Instance).ScheduleExecution(async () => { calls++; await release.Task; });
        clock.Advance(TimeSpan.FromMilliseconds(1));
        wakeup.TimerScheduled(Now);
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(1, calls);
        var disposing = execution.DisposeAsync().AsTask();
        Assert.False(disposing.IsCompleted);
        release.SetResult();
        await disposing.WaitAsync(TimeSpan.FromSeconds(30));
        wakeup.TimerScheduled(Now);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Long_recovery_interval_does_not_overflow_or_hang_disposal()
    {
        var clock = new FakeTimeProvider(Now);
        var calls = 0;
        var execution = new DurableTimerDeadlineSchedule(new DurableTimerWakeup(), () => TimeSpan.MaxValue,
            () => true, clock, NullLogger.Instance).ScheduleExecution(() => { calls++; return Task.CompletedTask; });
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await execution.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Notification_enqueue_failure_cannot_fault_a_successful_timer_save()
    {
        var store = new InMemoryDurableTimerStore();
        var scheduler = new DurableTimerScheduler(store, new FailingPublisher());
        var timer = Timer("saved", Now.AddSeconds(5));
        Assert.Equal(timer, await scheduler.ScheduleAsync(timer));
        Assert.Equal(timer, await store.FindAsync("workflow", "saved"));
    }

    [Fact]
    public async Task Whole_sweep_failure_suppresses_deadline_acceleration_including_first_failure()
    {
        var store = new FailingStore();
        await using var rig = new Rig(store);
        rig.Wakeup.TimerScheduled(Now);
        await rig.AdvanceAsync(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, store.Calls);
        rig.Wakeup.TimerScheduled(Now);
        await rig.AdvanceAsync(TimeSpan.FromSeconds(9));
        Assert.Equal(1, store.Calls);
        await rig.AdvanceAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(2, store.Calls);
        await rig.AdvanceAsync(TimeSpan.FromSeconds(19));
        Assert.Equal(2, store.Calls);
        await rig.AdvanceAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(3, store.Calls);
    }

    [Fact]
    public async Task Legacy_store_still_uses_periodic_recovery()
    {
        var store = new FailingStore { Fail = false };
        Assert.Null(await ((IDurableTimerStore)store).GetNextDueTimeAsync());
        await using var rig = new Rig(store);
        await rig.AdvanceAsync(TimeSpan.FromMilliseconds(1));
        await rig.AdvanceAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, store.Calls);
    }

    [Fact]
    public async Task Nonclaim_deadline_provider_does_not_spin_during_private_timer_backoff()
    {
        var store = new NonclaimStore();
        await using var rig = new Rig(store);
        rig.Dispatcher.Status = BookmarkResumeDispatchStatus.Rejected;
        await rig.AdvanceAsync(TimeSpan.FromMilliseconds(1));
        await rig.AdvanceAsync(TimeSpan.FromSeconds(9));
        Assert.Equal(1, store.Calls);
        Assert.Single(rig.Dispatcher.Fires);
        await rig.AdvanceAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(2, store.Calls);
        Assert.Equal(2, rig.Dispatcher.Fires.Count);
    }

    [Fact]
    public async Task Claimed_timer_failure_uses_provider_retry_visibility_without_a_busy_loop()
    {
        var store = new InMemoryDurableTimerStore();
        await store.SaveAsync(Timer("retry", Now));
        await using var rig = new Rig(store);
        rig.Dispatcher.Status = BookmarkResumeDispatchStatus.Rejected;
        await rig.AdvanceAsync(TimeSpan.FromMilliseconds(1));
        await rig.AdvanceAsync(TimeSpan.FromSeconds(9));
        Assert.Single(rig.Dispatcher.Fires);
        rig.Dispatcher.Status = BookmarkResumeDispatchStatus.Duplicate;
        await rig.AdvanceAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(2, rig.Dispatcher.Fires.Count);
        Assert.Null(await store.GetNextDueTimeAsync());
    }

    private static DurableTimer Timer(string id, DateTimeOffset due) => new(id, "workflow", "Delay", id, due, Now);

    private sealed class ForwardingPublisher(DurableTimerWakeup wakeup) : IDeferredEventPublisher
    {
        public Task Publish(IEvent notification, CancellationToken cancellationToken = default) =>
            new DurableTimerScheduledHandler(wakeup).Handle(Assert.IsType<DurableTimerScheduled>(notification), cancellationToken);
    }

    private sealed class FailingPublisher : IDeferredEventPublisher
    {
        public Task Publish(IEvent notification, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("event queue unavailable");
    }

    private sealed class Rig : IAsyncDisposable
    {
        public FakeTimeProvider Clock { get; } = new(Now);
        public DurableTimerWakeup Wakeup { get; } = new();
        public Dispatcher Dispatcher { get; }
        private readonly IScheduledTaskExecution _execution;
        public Rig(IDurableTimerStore store)
        {
            Dispatcher = new Dispatcher(Clock);
            var pump = new DurableTimerPumpTask(store, Dispatcher, Microsoft.Extensions.Options.Options.Create(new DurableTimerPumpOptions()),
                Clock, NullLogger<DurableTimerPumpTask>.Instance, Wakeup);
            _execution = pump.GetSchedule().ScheduleExecution(() => pump.ExecuteAsync(CancellationToken.None));
        }
        public async Task AdvanceAsync(TimeSpan elapsed)
        {
            Clock.Advance(elapsed);
            await ((DurableTimerDeadlineSchedule.Execution)_execution).WhenIdle.WaitAsync(TimeSpan.FromSeconds(30));
        }
        public ValueTask DisposeAsync() => _execution.DisposeAsync();
    }

    private sealed class Dispatcher(FakeTimeProvider clock) : IBookmarkResumeDispatcher
    {
        public List<DateTimeOffset> Fires { get; } = [];
        public BookmarkResumeDispatchStatus Status { get; set; } = BookmarkResumeDispatchStatus.Duplicate;
        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? options = null, CancellationToken cancellationToken = default)
        {
            Fires.Add(clock.GetUtcNow());
            return ValueTask.FromResult(new BookmarkResumeDispatchResult(Status, "workflow", reason: Status.ToString()));
        }
    }

    private sealed class NonclaimStore : IDurableTimerStore
    {
        public int Calls { get; private set; }
        public ValueTask<DateTimeOffset?> GetNextDueTimeAsync(CancellationToken cancellationToken = default) => new(Now);
        public ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult<IReadOnlyCollection<DurableTimer>>([Timer("poison", Now)]);
        }
        public ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default) => new(timer);
        public ValueTask<DurableTimer?> FindAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) => new((DurableTimer?)null);
        public ValueTask DeleteAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FailingStore : IDurableTimerStore
    {
        public bool Fail { get; init; } = true;
        public int Calls { get; private set; }
        public ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fail) throw new InvalidOperationException("store down");
            return ValueTask.FromResult<IReadOnlyCollection<DurableTimer>>([]);
        }
        public ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default) => new(timer);
        public ValueTask<DurableTimer?> FindAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) => new((DurableTimer?)null);
        public ValueTask DeleteAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
