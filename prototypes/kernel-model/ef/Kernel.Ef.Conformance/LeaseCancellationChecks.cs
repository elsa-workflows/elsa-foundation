using System.Data.Common;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kernel.Ef.Conformance;

internal static class LeaseCancellationChecks
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    internal static async Task Run(Func<KernelDb> create, Func<IInterceptor, EfStore> withInterceptor, EfStore store)
    {
        var beforeOpen = new ThrowingCallback();
        var heartbeat = withInterceptor(new HoldOpen(beforeOpen)).NodeHeartbeatAsync("timed-heartbeat", Lease, Lease).AsTask();
        await beforeOpen.Armed.Task.WaitAsync(Limit);
        await CheckFailureAsync(heartbeat, Failure.Unknown);

        var beforeTake = new ThrowingCallback();
        var take = withInterceptor(new HoldRenewal(beforeTake)).TakeAsync([0], "timed-take", Lease).AsTask();
        await beforeTake.Armed.Task.WaitAsync(Limit);
        await CheckFailureAsync(take, Failure.Transient);

        using var caller = new CancellationTokenSource();
        var beforeCaller = new ThrowingCallback();
        var cancelled = withInterceptor(new HoldOpen(beforeCaller)).NodeHeartbeatAsync("caller-heartbeat", Lease, Lease, caller.Token).AsTask();
        await beforeCaller.Armed.Task.WaitAsync(Limit);
        caller.Cancel();
        try
        {
            await cancelled.WaitAsync(Limit);
            throw new InvalidOperationException("The heartbeat should propagate caller cancellation.");
        }
        catch (OperationCanceledException) when (caller.IsCancellationRequested && beforeCaller.Invoked.Task.IsCompleted) { }
        Console.WriteLine("ok caller cancellation contained the callback failure and remained cancellation");

        await using var db = create();
        if (await db.Nodes.AnyAsync() || await db.Partitions.AnyAsync(row => row.Owner != null))
            throw new InvalidOperationException("An expired operation left a renewal or partition owner.");
        var resumed = await store.TakeAsync([0], "fresh-take", Lease).AsTask().WaitAsync(Limit);
        if (resumed.Taken.Count != 1)
            throw new InvalidOperationException("The next take must land after the expired transaction unwinds.");
        Console.WriteLine("ok timed operations left no renewal or owner and the next take landed");
    }

    private static async Task CheckFailureAsync(Task call, Failure failure)
    {
        try { await call.WaitAsync(Limit); }
        catch (StoreException error) when (error.Failure == failure && error.InnerException is AggregateException causes &&
            causes.Flatten().InnerExceptions.Any(cause => cause is CallbackFailure))
        {
            Console.WriteLine($"ok {failure} includes the provider callback failure");
            return;
        }
        throw new InvalidOperationException($"The elapsed operation should report {failure} with the callback failure.");
    }

    private sealed class CallbackFailure() : Exception("The provider cancellation callback failed.");

    private sealed class ThrowingCallback
    {
        internal TaskCompletionSource Armed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal async Task HoldAsync(CancellationToken token)
        {
            using var registration = token.Register(() =>
            {
                Invoked.TrySetResult();
                throw new CallbackFailure();
            });
            Armed.TrySetResult();
            await Invoked.Task.WaitAsync(Limit);
            token.ThrowIfCancellationRequested();
        }
    }

    private sealed class HoldOpen(ThrowingCallback callback) : DbConnectionInterceptor
    {
        public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            await callback.HoldAsync(cancellationToken);
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
