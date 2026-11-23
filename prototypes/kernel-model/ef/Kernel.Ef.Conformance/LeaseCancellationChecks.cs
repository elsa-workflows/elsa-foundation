using System.Data.Common;
using System.Diagnostics;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kernel.Ef.Conformance;

internal static class LeaseCancellationChecks
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    internal static async Task Run(Func<KernelDb> create, Func<IInterceptor, EfStore> withInterceptor, EfStore store)
    {
        await CheckIndependentDispatchAsync();

        var beforeOpen = new ThrowingCallback();
        var heartbeatStarted = Stopwatch.GetTimestamp();
        var heartbeat = withInterceptor(new HoldOpen(beforeOpen)).NodeHeartbeatAsync("timed-heartbeat", Lease, Lease).AsTask();
        await beforeOpen.Armed.Task.WaitAsync(Limit);
        await CheckFailureAsync(heartbeat, Failure.Unknown, beforeOpen, heartbeatStarted);

        var beforeTake = new ThrowingCallback();
        var takeStarted = Stopwatch.GetTimestamp();
        var take = withInterceptor(new HoldRenewal(beforeTake)).TakeAsync([0], "timed-take", Lease).AsTask();
        await beforeTake.Armed.Task.WaitAsync(Limit);
        await CheckFailureAsync(take, Failure.Transient, beforeTake, takeStarted);

        await CheckCallerAsync("heartbeat open", (s, token) => s.NodeHeartbeatAsync("caller-heartbeat", Lease, Lease, token).AsTask());
        await CheckCallerAsync("take open", (s, token) => s.TakeAsync([0], "caller-take", Lease, token).AsTask(), registerLate: true);
        await using var db = create();
        if (await db.Nodes.AnyAsync() || await db.Partitions.AnyAsync(row => row.Owner != null))
            throw new InvalidOperationException("An expired operation left a renewal or partition owner.");
        var resumed = await store.TakeAsync([0], "fresh-take", Lease).AsTask().WaitAsync(Limit);
        if (resumed.Taken is not [var owner])
            throw new InvalidOperationException("The next take must land after the expired transaction unwinds.");
        Console.WriteLine("ok timed operations left no renewal or owner and the next take landed");

        await CheckCallerAsync("retake open", (s, token) => s.RetakeAsync(0, "fresh-take", owner.Epoch, Lease, token).AsTask());
        var afterRetake = await db.Partitions.AsNoTracking().SingleAsync();
        if (afterRetake.Owner != "fresh-take" || afterRetake.Epoch != owner.Epoch)
            throw new InvalidOperationException("Cancelling the retake changed the original fence.");
        await CheckCallerAsync("committed heartbeat's membership open",
            (s, token) => s.NodeHeartbeatAsync("membership", Lease, Lease, token).AsTask(), opening: 2, registerLate: true);
        if (!await db.Nodes.AnyAsync(node => node.Id == "membership"))
            throw new InvalidOperationException("Cancelling membership must preserve the already committed heartbeat.");
        Console.WriteLine("ok cancelled membership leaves the already committed renewal intact");

        async Task CheckCallerAsync(string path, Func<EfStore, CancellationToken, Task> run, int opening = 1, bool registerLate = false)
        {
            using var caller = new CancellationTokenSource();
            var callback = new ThrowingCallback(registerLate);
            var cancelled = run(withInterceptor(new HoldOpen(callback, opening)), caller.Token);
            await callback.Armed.Task.WaitAsync(Limit);
            caller.Cancel();
            try { await cancelled.WaitAsync(Limit); }
            catch (OperationCanceledException error) when (error.CancellationToken == caller.Token &&
                callback.Invoked.Task.IsCompleted && ContainsCallbackFailure(error))
            {
                Console.WriteLine($"ok {path} contains the callback failure and preserves the caller token");
                return;
            }
            throw new InvalidOperationException($"Cancelling {path} must preserve the original caller token and callback failure.");
        }
    }

    private static async Task CheckIndependentDispatchAsync()
    {
        var original = SynchronizationContext.Current;
        var blocked = new BlockedContext();
        LeaseIoDeadline deadline;
        try
        {
            SynchronizationContext.SetSynchronizationContext(blocked);
            deadline = new LeaseIoDeadline(CancellationToken.None, TimeSpan.FromMilliseconds(100), containCallbacks: true);
        }
        finally { SynchronizationContext.SetSynchronizationContext(original); }

        await using (deadline)
        {
            var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = deadline.Token.Register(() => invoked.TrySetResult());
            try { await invoked.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
            finally { blocked.Release(); }
        }
        Console.WriteLine("ok deadline cancellation dispatches while the initiating context is blocked");
    }

    private static async Task CheckFailureAsync(Task call, Failure failure, ThrowingCallback callback, long started)
    {
        try { await call.WaitAsync(Limit); }
        catch (StoreException error) when (error.Failure == failure && ContainsCallbackFailure(error))
        {
            var elapsed = Stopwatch.GetElapsedTime(started, callback.InvokedAt);
            if (elapsed < Lease / 3 - TimeSpan.FromMilliseconds(40) || elapsed >= Lease / 2 - TimeSpan.FromMilliseconds(100))
                throw new InvalidOperationException($"The one-third-lease callback ran at {elapsed.TotalMilliseconds:F0} ms.");
            Console.WriteLine($"ok {failure} includes the provider callback failure at {elapsed.TotalMilliseconds:F0} ms of a {Lease.TotalSeconds:F0} s lease");
            return;
        }
        throw new InvalidOperationException($"The elapsed operation should report {failure} with the callback failure.");
    }

    private static bool ContainsCallbackFailure(Exception? error) => error switch
    {
        CallbackFailure => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(ContainsCallbackFailure),
        { InnerException: { } inner } => ContainsCallbackFailure(inner),
        _ => false,
    };

    private sealed class CallbackFailure() : Exception("The provider cancellation callback failed.");

    private sealed class BlockedContext : SynchronizationContext
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Release() => _release.TrySetResult();

        public override void Post(SendOrPostCallback callback, object? state) => _ = Task.Run(async () =>
        {
            await _release.Task;
            callback(state);
        });
    }

    private sealed class ThrowingCallback(bool registerLate = false)
    {
        internal TaskCompletionSource Armed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal long InvokedAt { get; private set; }

        internal async Task HoldAsync(CancellationToken token)
        {
            if (registerLate)
            {
                Armed.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token).WaitAsync(Limit); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            }
            using var registration = token.Register(() =>
            {
                InvokedAt = Stopwatch.GetTimestamp();
                Invoked.TrySetResult();
                throw new CallbackFailure();
            });
            Armed.TrySetResult();
            await Invoked.Task.WaitAsync(Limit);
            token.ThrowIfCancellationRequested();
        }
    }

    private sealed class HoldOpen(ThrowingCallback callback, int opening = 1) : DbConnectionInterceptor
    {
        private int _openings;

        public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _openings) == opening) await callback.HoldAsync(cancellationToken);
            return result;
        }
    }

    private sealed class HoldRenewal(ThrowingCallback callback) : DbCommandInterceptor
    {
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("INSERT INTO `Nodes`", StringComparison.Ordinal)) return result;
            try
            {
                await callback.HoldAsync(cancellationToken);
                return result;
            }
            catch
            {
                await result.DisposeAsync();
                throw;
            }
        }
    }
}
