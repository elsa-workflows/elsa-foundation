using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Batch claims that outlive their lease (#2195), on a real database, written once so SQLite and each native provider are
/// held to the same outcome. Two deliverers, two contexts, two connections: a batch claims every due row under one
/// visibility timeout and then works through it one row at a time, so its first row's side effect can outlast the claims
/// on the rest, and a peer re-claims them meanwhile. A row whose claim lapsed and was taken must be skipped rather than
/// acted on a second time, and a completion the store refuses as stale must not end the rest of the batch.
/// </summary>
internal static class ClaimLeaseOverrunContract
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly TimeSpan VisibilityTimeout = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The item whose claim lapsed while it waited its turn is dispatched once. The outbox renews each claim just before its
    /// dispatch, never during it, so the in-flight item's long dispatch may be repeated by the peer: that repeat is the
    /// documented residue, which each intent kind converges under by its own mechanism (the per-kind table under
    /// `IRuntimePostCommitOutboxStore` in Runtime EXTENSION_POINTS.md), and this deliverer's refused completion of it is what must not end the batch.
    /// </summary>
    public static async Task OutboxDispatchesAnItemWhoseClaimLapsedWhileWaitingOnceAsync(Func<RuntimeDbContext> createContext)
    {
        var scope = $"lease-overrun-{Guid.NewGuid():N}";
        await using (var setup = createContext())
        {
            await setup.Database.EnsureCreatedAsync();
            var store = new EfRuntimePostCommitOutboxStore(setup, new ScopeAccessor(scope));
            await store.SavePendingAsync(OutboxItem("outbox-a", "wfexec-a", Now.AddSeconds(-2)));
            await store.SavePendingAsync(OutboxItem("outbox-b", "wfexec-b", Now.AddSeconds(-1)));
        }

        await using var context = createContext();
        await using var peerContext = createContext();
        var dispatcher = new OverrunningIntentDispatcher("intent-outbox-a");
        var processor = new RuntimePostCommitOutboxProcessor(
            new EfRuntimePostCommitOutboxStore(context, new ScopeAccessor(scope)),
            dispatcher,
            new FakeTimeProvider(Now));
        var peer = new RuntimePostCommitOutboxProcessor(
            new EfRuntimePostCommitOutboxStore(peerContext, new ScopeAccessor(scope)),
            dispatcher,
            new FakeTimeProvider(Now + VisibilityTimeout + TimeSpan.FromSeconds(1)));
        dispatcher.WhileOverrunning = async () =>
            Assert.Equal(2, (await peer.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10))).DeliveredCount);

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10));

        Assert.Equal(["intent-outbox-a", "intent-outbox-a", "intent-outbox-b"], dispatcher.Dispatched.Order(StringComparer.Ordinal));
        Assert.Equal(0, result.DeliveredCount);
        Assert.Equal(2, result.SupersededCount);
        await using var verify = createContext();
        var verifyStore = new EfRuntimePostCommitOutboxStore(verify, new ScopeAccessor(scope));
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await verifyStore.FindAsync("outbox-a"))!.Status);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await verifyStore.FindAsync("outbox-b"))!.Status);
    }

    /// <summary>
    /// The timer pump keeps the claim on the timer it is firing renewed, so the peer finds only the second timer, whose
    /// claim nothing renewed. It fires it; this pump must then skip it.
    /// </summary>
    public static async Task TimerPumpFiresATimerWhoseClaimLapsedMidBatchExactlyOnceAsync(Func<RuntimeDbContext> createContext)
    {
        var scope = $"lease-overrun-{Guid.NewGuid():N}";
        await using (var setup = createContext())
        {
            await setup.Database.EnsureCreatedAsync();
            var setupStore = TimerStore(setup, scope);
            await setupStore.SaveAsync(Timer("timer-1", Now.AddMinutes(-2)));
            await setupStore.SaveAsync(Timer("timer-2", Now.AddMinutes(-1)));
        }

        await using var context = createContext();
        await using var peerContext = createContext();
        var store = new RenewalSignallingTimerStore(TimerStore(context, scope));
        var dispatcher = new RecordingResumeDispatcher();
        var clock = new FakeTimeProvider(Now);
        var pump = TimerPump(store, dispatcher, clock);
        var peer = TimerPump(TimerStore(peerContext, scope), dispatcher, new FakeTimeProvider(Now + VisibilityTimeout + TimeSpan.FromSeconds(1)));
        dispatcher.WhileFiring = async request =>
        {
            if (request.IdempotencyKey != "timer:timer-1" || dispatcher.Fired.Count != 1)
                return;
            var renewals = store.Renewals;
            clock.Advance(VisibilityTimeout / 3);
            await store.WaitForRenewalsAsync(renewals + 1);
            await peer.ExecuteAsync(CancellationToken.None);
        };

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(["timer:timer-1", "timer:timer-2"], dispatcher.Fired);
        Assert.Null(await store.FindAsync("wfexec-timers", "timer-1"));
        Assert.Null(await store.FindAsync("wfexec-timers", "timer-2"));
    }

    private static RuntimePostCommitOutboxItem OutboxItem(string outboxItemId, string workflowExecutionId, DateTimeOffset recordedAt) =>
        new(
            outboxItemId,
            new RuntimePostCommitIntent($"intent-{outboxItemId}", workflowExecutionId, "lease-overrun.outbox", recordedAt, null, null, null),
            RuntimePostCommitOutboxStatus.Pending,
            recordedAt,
            recordedAt,
            new RuntimePostCommitRetryPolicy(3, TimeSpan.FromSeconds(10)));

    private static DurableTimer Timer(string timerId, DateTimeOffset dueTime) =>
        new(TimerId: timerId, WorkflowExecutionId: "wfexec-timers", StimulusType: "Timer", StimulusHash: $"hash-{timerId}", DueTime: dueTime, CreatedAt: dueTime.AddMinutes(-1));

    private static EfDurableTimerStore TimerStore(RuntimeDbContext context, string scope) =>
        new(
            context,
            new ScopeAccessor(scope),
            new HmacRuntimeRecoveryContinuationCodec(
                Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) })));

    private static DurableTimerPumpTask TimerPump(IDurableTimerStore store, IBookmarkResumeDispatcher dispatcher, TimeProvider clock) =>
        new(
            store,
            dispatcher,
            Options.Create(new DurableTimerPumpOptions { ClaimVisibilityTimeout = VisibilityTimeout }),
            clock,
            NullLogger<DurableTimerPumpTask>.Instance);

    private sealed class ScopeAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    /// <summary>Answers every fire as an already-enqueued resume, after running a callback that stands in for a long fire.</summary>
    private sealed class RecordingResumeDispatcher : IBookmarkResumeDispatcher
    {
        public List<string> Fired { get; } = [];
        public Func<BookmarkResumeDispatchRequest, Task>? WhileFiring { get; set; }

        public async ValueTask<BookmarkResumeDispatchResult> DispatchAsync(
            BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            Fired.Add(request.IdempotencyKey!);
            if (WhileFiring is { } whileFiring)
                await whileFiring(request);
            return new BookmarkResumeDispatchResult(BookmarkResumeDispatchStatus.Duplicate, request.WorkflowExecutionId, reason: "already enqueued");
        }
    }

    /// <summary>Lets a scenario wait for the pump's heartbeat renewal before it lets the peer in.</summary>
    private sealed class RenewalSignallingTimerStore(IDurableTimerStore inner) : IDurableTimerStore
    {
        private readonly object _gate = new();
        private readonly List<(int Count, TaskCompletionSource Signal)> _waiters = [];
        private int _renewals;

        public int Renewals
        {
            get
            {
                lock (_gate)
                    return _renewals;
            }
        }

        public bool SupportsClaimTransitions => inner.SupportsClaimTransitions;

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

        public async ValueTask<RuntimeDurableTimerClaimTransitionResult> RenewClaimAsync(
            RuntimeDurableTimerClaim claim,
            DateTimeOffset now,
            TimeSpan visibilityTimeout,
            CancellationToken cancellationToken = default)
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

        public ValueTask<IReadOnlyCollection<RuntimeDurableTimerClaim>> ClaimDueAsync(RuntimeDurableTimerClaimRequest request, CancellationToken cancellationToken = default) =>
            inner.ClaimDueAsync(request, cancellationToken);

        public ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimAsync(RuntimeDurableTimerClaim claim, CancellationToken cancellationToken = default) =>
            inner.CompleteClaimAsync(claim, cancellationToken);

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
}
