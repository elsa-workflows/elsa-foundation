using System.Data.Common;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kernel.Ef.Conformance;

internal static class FormatMigrationRaceChecks
{
    internal static async Task Run(Func<KernelDb> create, Func<IInterceptor, EfStore> withInterceptor, EfStore store)
    {
        var current = StoreFormat.Current;
        var next = current with { JournalCodec = 2 };
        var lease = TimeSpan.FromMinutes(1);
        var holdHeartbeat = new HoldFormatRead();
        var heartbeating = withInterceptor(holdHeartbeat).NodeHeartbeatAsync("first", lease, lease).AsTask();
        var openingMigration = new ObserveOpen();
        await holdHeartbeat.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var migrating = Task.Run(() => withInterceptor(openingMigration).AdvanceStoreFormatAsync(current, next));
        try
        {
            await openingMigration.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await StaysPending(migrating, "format advance waits for the active heartbeat");
        }
        finally { holdHeartbeat.Release.TrySetResult(); }

        await heartbeating;
        await VersionTakeChecks.Refuses(() => migrating, "Live node 'first'", "a heartbeat that lands before migration");
        await using (var db = create())
        {
            var first = await db.Nodes.SingleAsync();
            first.LeaseUntil = 0;
            await db.SaveChangesAsync();
        }

        var insideMigration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMigration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var advancing = store.AdvanceStoreFormatAsync(current, next, async (_, token) =>
        {
            insideMigration.TrySetResult();
            await releaseMigration.Task.WaitAsync(token);
        });
        await insideMigration.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var openingHeartbeat = new ObserveOpen();
        var laterHeartbeat = Task.Run(async () =>
            await withInterceptor(openingHeartbeat).NodeHeartbeatAsync("late", lease, lease));
        try
        {
            await openingHeartbeat.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await StaysPending(laterHeartbeat, "an old heartbeat waits for the format advance");
        }
        finally { releaseMigration.TrySetResult(); }
        if (!await advancing) throw new InvalidOperationException("The coordinated advance should land.");
        await VersionTakeChecks.Refuses(() => laterHeartbeat, "journal codec=2", "a heartbeat that opens after migration");
        await using var read = create();
        if (await read.Nodes.AnyAsync(row => row.Id == "late"))
            throw new InvalidOperationException("A refused heartbeat must leave no node record.");
        Console.WriteLine("ok the old heartbeat did not register after migration");
    }

    private static async Task StaysPending(Task call, string description)
    {
        if (await Task.WhenAny(call, Task.Delay(TimeSpan.FromMilliseconds(200))) == call)
            throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }

    private sealed class ObserveOpen : DbConnectionInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            return new(result);
        }
    }

    private sealed class HoldFormatRead : DbCommandInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(StoreVersionEntity.Table, StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
