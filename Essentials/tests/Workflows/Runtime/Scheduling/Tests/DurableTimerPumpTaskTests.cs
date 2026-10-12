using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

public sealed class DurableTimerPumpTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sweep_FiresDueTimer_ThroughDispatcher_WithExpectedRequestShape()
    {
        var store = new InMemoryDurableTimerStore();
        await store.SaveAsync(Timer("timer-1", TimeSpan.FromMinutes(-1)));
        var dispatcher = new FakeDispatcher(Result(BookmarkResumeDispatchStatus.Duplicate));
        var (pump, _) = CreatePump(store, dispatcher);

        await pump.ExecuteAsync(CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal("wfexec-1", request.WorkflowExecutionId);
        Assert.Equal(DurableTimerConstants.TimerStimulusType, request.StimulusType);
        Assert.Equal("hash-timer-1", request.StimulusHash);
        Assert.Equal("timer:timer-1", request.IdempotencyKey);
        Assert.Equal(DurableTimerConstants.PumpRequestedBy, request.RequestedBy);
    }

    [Fact]
    public async Task Sweep_DoesNotFire_TimersNotYetDue()
    {
        var store = new InMemoryDurableTimerStore();
        await store.SaveAsync(Timer("future", TimeSpan.FromMinutes(10)));
        var dispatcher = new FakeDispatcher(Result(BookmarkResumeDispatchStatus.Duplicate));
        var (pump, _) = CreatePump(store, dispatcher);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Empty(dispatcher.Requests);
        Assert.NotNull(await store.FindAsync("wfexec-1", "future"));
    }

    [Theory]
    [InlineData(BookmarkResumeDispatchStatus.Duplicate)]
    [InlineData(BookmarkResumeDispatchStatus.WorkflowExecutionMissing)]
    [InlineData(BookmarkResumeDispatchStatus.ExecutableMissing)]
    public async Task Sweep_DeletesTimer_OnResumeEnqueuedOrOrphaned(BookmarkResumeDispatchStatus status)
    {
        var store = new InMemoryDurableTimerStore();
        await store.SaveAsync(Timer("timer-1", TimeSpan.FromMinutes(-1)));
        var (pump, _) = CreatePump(store, new FakeDispatcher(Result(status)));

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Null(await store.FindAsync("wfexec-1", "timer-1"));
    }

    [Fact]
    public async Task Sweep_KeepsTimer_OnNotFoundWithinGrace()
    {
        var store = new InMemoryDurableTimerStore();
        // Due 5s ago, grace is 30s: NotFound means the bookmark may still be committing — keep and retry.
        await store.SaveAsync(Timer("timer-1", TimeSpan.FromSeconds(-5)));
        var (pump, _) = CreatePump(store, new FakeDispatcher(Result(BookmarkResumeDispatchStatus.NotFound)));

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.NotNull(await store.FindAsync("wfexec-1", "timer-1"));
    }

    [Fact]
    public async Task Sweep_DeletesTimer_OnNotFoundPastGrace()
    {
        var store = new InMemoryDurableTimerStore();
        // Due 60s ago, grace 30s: NotFound means the bookmark was consumed by an earlier fire — delete.
        await store.SaveAsync(Timer("timer-1", TimeSpan.FromSeconds(-60)));
        var (pump, _) = CreatePump(store, new FakeDispatcher(Result(BookmarkResumeDispatchStatus.NotFound)));

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Null(await store.FindAsync("wfexec-1", "timer-1"));
    }

    [Theory]
    [InlineData(BookmarkResumeDispatchStatus.Rejected)]
    [InlineData(BookmarkResumeDispatchStatus.Deferred)]
    [InlineData(BookmarkResumeDispatchStatus.ResumeResolutionFailed)]
    public async Task Sweep_KeepsTimer_AndBacksOff_OnTransientOrFault(BookmarkResumeDispatchStatus status)
    {
        var store = new InMemoryDurableTimerStore();
        await store.SaveAsync(Timer("timer-1", TimeSpan.FromMinutes(-1)));
        var dispatcher = new FakeDispatcher(Result(status));
        var (pump, clock) = CreatePump(store, dispatcher);

        await pump.ExecuteAsync(CancellationToken.None);
        Assert.NotNull(await store.FindAsync("wfexec-1", "timer-1"));
        Assert.Single(dispatcher.Requests);

        // Immediately re-sweeping skips the backed-off timer (no second dispatch).
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Single(dispatcher.Requests);

        // After the backoff window elapses the timer is retried.
        clock.Advance(TimeSpan.FromMinutes(10));
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal(2, dispatcher.Requests.Count);
    }

    [Fact]
    public async Task Sweep_ClearsBackoff_AfterSuccessfulRetry()
    {
        var store = new InMemoryDurableTimerStore();
        await store.SaveAsync(Timer("timer-1", TimeSpan.FromMinutes(-1)));
        var dispatcher = new FakeDispatcher(Result(BookmarkResumeDispatchStatus.Rejected));
        var (pump, clock) = CreatePump(store, dispatcher);

        await pump.ExecuteAsync(CancellationToken.None); // rejected -> backoff
        clock.Advance(TimeSpan.FromMinutes(10));
        dispatcher.Next = Result(BookmarkResumeDispatchStatus.Duplicate);
        await pump.ExecuteAsync(CancellationToken.None); // duplicate -> delete + clear backoff

        Assert.Null(await store.FindAsync("wfexec-1", "timer-1"));
    }

    [Fact]
    public async Task Sweep_NeverThrows_WhenDispatchThrows_AndKeepsTimer()
    {
        var store = new InMemoryDurableTimerStore();
        await store.SaveAsync(Timer("timer-1", TimeSpan.FromMinutes(-1)));
        var dispatcher = new FakeDispatcher(Result(BookmarkResumeDispatchStatus.Duplicate)) { Throw = new InvalidOperationException("boom") };
        var (pump, _) = CreatePump(store, dispatcher);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.NotNull(await store.FindAsync("wfexec-1", "timer-1"));
    }

    [Fact]
    public async Task Sweep_NeverThrows_WhenStoreThrows_AndWidensInterval()
    {
        var store = new ThrowingTimerStore();
        var (pump, _) = CreatePump(store, new FakeDispatcher(Result(BookmarkResumeDispatchStatus.Duplicate)));

        Assert.Equal(TimeSpan.FromSeconds(10), pump.CurrentSweepInterval);
        await pump.ExecuteAsync(CancellationToken.None); // 1 failure -> 10s * 2^0 = 10s
        await pump.ExecuteAsync(CancellationToken.None); // 2 failures -> 10s * 2^1 = 20s
        Assert.True(pump.CurrentSweepInterval > TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Sweep_BoundsDispatches_ByMaxTimersPerTick()
    {
        var store = new InMemoryDurableTimerStore();
        for (var i = 0; i < 5; i++)
            await store.SaveAsync(Timer($"timer-{i}", TimeSpan.FromMinutes(-1)));
        var dispatcher = new FakeDispatcher(Result(BookmarkResumeDispatchStatus.NotFound)); // kept, so all remain due
        var (pump, _) = CreatePump(store, dispatcher, maxTimersPerTick: 2);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, dispatcher.Requests.Count);
    }

    [Fact]
    public async Task ConcurrentSweeps_DispatchOneSuccessfulOwner()
    {
        var store = new InMemoryDurableTimerStore();
        await store.SaveAsync(Timer("timer-1", TimeSpan.FromMinutes(-1)));
        var dispatcher = new FakeDispatcher(Result(BookmarkResumeDispatchStatus.Duplicate));
        var (first, _) = CreatePump(store, dispatcher);
        var (second, _) = CreatePump(store, dispatcher);

        await Task.WhenAll(
            first.ExecuteAsync(CancellationToken.None),
            second.ExecuteAsync(CancellationToken.None));

        Assert.Single(dispatcher.Requests);
        Assert.Null(await store.FindAsync("wfexec-1", "timer-1"));
    }

    [Fact]
    public async Task Sweep_DoesNotFireATimerWhoseClaimLapsedAndWasTakenWhileItsBatchRan()
    {
        // #2195. The first fire outlives the claim on the whole batch. Its own claim is kept renewed while it runs, but
        // the second timer's lapses, and a peer takes it and fires it meanwhile.
        var store = new ScriptedClaimTimerStore(new InMemoryDurableTimerStore());
        await store.SaveAsync(Timer("timer-1", TimeSpan.FromMinutes(-2)));
        await store.SaveAsync(Timer("timer-2", TimeSpan.FromMinutes(-1)));
        var dispatcher = new FakeDispatcher(Result(BookmarkResumeDispatchStatus.Duplicate));
        var (pump, clock) = CreatePump(store, dispatcher);
        var (peer, peerClock) = CreatePump(store, dispatcher);
        peerClock.Advance(TimeSpan.FromSeconds(61));
        dispatcher.WhileFiring = async request =>
        {
            if (request.IdempotencyKey != "timer:timer-1" || dispatcher.Requests.Count != 1)
                return;
            var renewals = store.Renewals;
            clock.Advance(TimeSpan.FromSeconds(20));
            await store.WaitForRenewalsAsync(renewals + 1);
            await peer.ExecuteAsync(CancellationToken.None);
        };

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(["timer:timer-1", "timer:timer-2"], dispatcher.Requests.Select(request => request.IdempotencyKey));
        Assert.Null(await store.FindAsync("wfexec-1", "timer-1"));
        Assert.Null(await store.FindAsync("wfexec-1", "timer-2"));
    }

    [Fact]
    public async Task Sweep_ContinuesPastATimerWhoseCompletionWentStale()
    {
        var store = new ScriptedClaimTimerStore(new InMemoryDurableTimerStore()) { StaleCompletionTimerId = "timer-1" };
        await store.SaveAsync(Timer("timer-1", TimeSpan.FromMinutes(-2)));
        await store.SaveAsync(Timer("timer-2", TimeSpan.FromMinutes(-1)));
        var dispatcher = new FakeDispatcher(Result(BookmarkResumeDispatchStatus.Duplicate));
        var (pump, _) = CreatePump(store, dispatcher);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(["timer:timer-1", "timer:timer-2"], dispatcher.Requests.Select(request => request.IdempotencyKey));
        Assert.NotNull(await store.FindAsync("wfexec-1", "timer-1"));
        Assert.Null(await store.FindAsync("wfexec-1", "timer-2"));
    }

    [Fact]
    public async Task CurrentSweepInterval_ResetsToBaseline_AfterCleanSweep()
    {
        var store = new ThrowingTimerStore();
        var dispatcher = new FakeDispatcher(Result(BookmarkResumeDispatchStatus.Duplicate));
        var (pump, _) = CreatePump(store, dispatcher);

        await pump.ExecuteAsync(CancellationToken.None); // fails -> widened
        await pump.ExecuteAsync(CancellationToken.None); // fails again -> 2^1 = 20s
        Assert.True(pump.CurrentSweepInterval > TimeSpan.FromSeconds(10));

        store.Healthy = true;
        await pump.ExecuteAsync(CancellationToken.None); // clean -> reset
        Assert.Equal(TimeSpan.FromSeconds(10), pump.CurrentSweepInterval);
    }

    private static (DurableTimerPumpTask Pump, FakeTimeProvider Clock) CreatePump(
        IDurableTimerStore store,
        FakeDispatcher dispatcher,
        int maxTimersPerTick = 100)
    {
        var clock = new FakeTimeProvider(Now);
        var options = Microsoft.Extensions.Options.Options.Create(new DurableTimerPumpOptions
        {
            SweepInterval = TimeSpan.FromSeconds(10),
            MaxBackoffInterval = TimeSpan.FromMinutes(5),
            MaxTimersPerTick = maxTimersPerTick,
            NotFoundGrace = TimeSpan.FromSeconds(30)
        });
        var pump = new DurableTimerPumpTask(store, dispatcher, options, clock, NullLogger<DurableTimerPumpTask>.Instance);
        return (pump, clock);
    }

    private static BookmarkResumeDispatchResult Result(BookmarkResumeDispatchStatus status) =>
        new(status, "wfexec-1", reason: status.ToString());

    private static DurableTimer Timer(string timerId, TimeSpan dueOffset) => new(
        TimerId: timerId,
        WorkflowExecutionId: "wfexec-1",
        StimulusType: DurableTimerConstants.TimerStimulusType,
        StimulusHash: $"hash-{timerId}",
        DueTime: Now + dueOffset,
        CreatedAt: Now);

    private sealed class FakeDispatcher(BookmarkResumeDispatchResult initial) : IBookmarkResumeDispatcher
    {
        public List<BookmarkResumeDispatchRequest> Requests { get; } = new();
        public BookmarkResumeDispatchResult Next { get; set; } = initial;
        public Exception? Throw { get; set; }
        public Func<BookmarkResumeDispatchRequest, Task>? WhileFiring { get; set; }

        public async ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request, WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Throw is not null)
                throw Throw;
            if (WhileFiring is { } whileFiring)
                await whileFiring(request);
            return Next;
        }
    }

    /// <summary>Counts claim renewals so a test can wait for one, and can answer one timer's completion as stale.</summary>
    private sealed class ScriptedClaimTimerStore(InMemoryDurableTimerStore inner) : IDurableTimerStore
    {
        private readonly object _gate = new();
        private readonly List<(int Count, TaskCompletionSource Signal)> _waiters = [];
        private int _renewals;

        public string? StaleCompletionTimerId { get; init; }

        public int Renewals
        {
            get
            {
                lock (_gate)
                    return _renewals;
            }
        }

        public bool SupportsClaimTransitions => true;

        public Task WaitForRenewalsAsync(int count)
        {
            lock (_gate)
            {
                if (_renewals >= count)
                    return Task.CompletedTask;
                var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((count, signal));
                return signal.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
        }

        public async ValueTask<RuntimeDurableTimerClaimTransitionResult> RenewClaimAsync(RuntimeDurableTimerClaim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default)
        {
            var result = await inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);
            lock (_gate)
            {
                _renewals++;
                foreach (var waiter in _waiters.Where(waiter => _renewals >= waiter.Count))
                    waiter.Signal.TrySetResult();
            }

            return result;
        }

        public ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimAsync(RuntimeDurableTimerClaim claim, CancellationToken cancellationToken = default) =>
            claim.Timer.TimerId == StaleCompletionTimerId
                ? new ValueTask<RuntimeDurableTimerClaimTransitionResult>(RuntimeDurableTimerClaimTransitionResult.Stale)
                : inner.CompleteClaimAsync(claim, cancellationToken);

        public ValueTask<IReadOnlyCollection<RuntimeDurableTimerClaim>> ClaimDueAsync(RuntimeDurableTimerClaimRequest request, CancellationToken cancellationToken = default) =>
            inner.ClaimDueAsync(request, cancellationToken);

        public ValueTask<RuntimeDurableTimerClaimTransitionResult> ReleaseClaimAsync(RuntimeDurableTimerClaim claim, DateTimeOffset visibleAt, CancellationToken cancellationToken = default) =>
            inner.ReleaseClaimAsync(claim, visibleAt, cancellationToken);

        public ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(timer, cancellationToken);

        public ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default) =>
            inner.ListDueAsync(asOf, limit, cancellationToken);

        public ValueTask<DurableTimer?> FindAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, timerId, cancellationToken);

        public ValueTask DeleteAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(workflowExecutionId, timerId, cancellationToken);
    }

    private sealed class ThrowingTimerStore : IDurableTimerStore
    {
        public bool Healthy { get; set; }

        public ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default) =>
            new(timer);

        public ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default) =>
            Healthy
                ? new ValueTask<IReadOnlyCollection<DurableTimer>>(Array.Empty<DurableTimer>())
                : throw new InvalidOperationException("store down");

        public ValueTask<DurableTimer?> FindAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) =>
            new((DurableTimer?)null);

        public ValueTask DeleteAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
