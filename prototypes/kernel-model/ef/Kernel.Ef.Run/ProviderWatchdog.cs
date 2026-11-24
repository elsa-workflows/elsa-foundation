using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

static class ProviderWatchdog
{
    private const string Actor = "0/watchdog";
    private const string ChildConnection = "KERNEL_WATCHDOG_TEST_CONNECTION";
    private const int Escalated = 71;
    private static readonly TimeSpan LeaseTime = TimeSpan.FromSeconds(2);

    public static async Task<int> RunAsync(Target target, string connection, string[] options)
    {
        var stage = target.Get("stage", "before-fence");
        if (stage == "owned-fence") return await RunPostgresOwnedFenceAsync(target, connection);
        if (stage != "before-fence")
        {
            Console.Error.WriteLine("Choose stage=before-fence or stage=owned-fence.");
            return 2;
        }

        var waiting = target.Engine switch
        {
            "postgres" => """
                SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_stat_activity
                WHERE datname = current_database() AND application_name = 'kernel-worker'
                  AND wait_event_type = 'Lock') THEN 1 ELSE 0 END
                """,
            "sqlserver" => """
                SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.dm_exec_requests r
                JOIN sys.dm_exec_sessions s ON r.session_id = s.session_id
                WHERE r.database_id = DB_ID() AND s.program_name = 'kernel-worker'
                  AND r.blocking_session_id > 0 AND LEFT(r.wait_type, 4) = 'LCK_') THEN 1 ELSE 0 END
                """,
            "mysql" => """
                SELECT CASE WHEN EXISTS (SELECT 1 FROM performance_schema.data_lock_waits w
                JOIN performance_schema.data_locks l ON l.ENGINE = w.ENGINE AND l.ENGINE_LOCK_ID = w.REQUESTING_ENGINE_LOCK_ID
                JOIN performance_schema.threads t ON t.THREAD_ID = w.REQUESTING_THREAD_ID
                WHERE l.OBJECT_SCHEMA = DATABASE() AND t.PROCESSLIST_USER = 'kernel_worker')
                THEN 1 ELSE 0 END
                """,
            _ => null
        };
        if (waiting is null)
        {
            Console.Error.WriteLine("Watchdog requires a disposable PostgreSQL, SQL Server, or MySQL server.");
            return 2;
        }

        await target.ResetAsync(1);
        var store = target.Store(target.Workers());
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = limit.Token;
        var first = await store.TakeAsync(0, "watchdog-old", LeaseTime, token)
            ?? throw new InvalidOperationException("The old owner did not take the partition.");

        string result;
        await using (await target.HoldAsync(0))
        {
            if (target.Engine == "mysql")
            {
                result = await IsolatedWorkerAsync(target, connection, options, first, WaitForLockAsync, token);
            }
            else
            {
                long monotonic = 0;
                var ended = new List<string>();
                var watchdog = new Watchdog(LeaseTime, ended.Add, () => monotonic);
                var blocked = watchdog.Watch(store).CommitAsync(Flush(first), token).AsTask();
                await WaitForLockAsync(() => blocked.IsCompleted);
                Require(!blocked.IsCompleted && watchdog.Check() == 0, "The physical commit waits before the watchdog deadline.");
                monotonic = (long)(LeaseTime.TotalMilliseconds * 2);
                Require(watchdog.Check() == 1, "The watchdog requests cancellation of the blocked commit.");
                result = await AwaitInterruptedAsync(blocked, LeaseTime / 3, token);
                monotonic += (long)(LeaseTime.TotalMilliseconds / 3);
                Require(watchdog.Check() == 0 && watchdog.Closed == 1 && ended.Count == 0,
                    "The commit ended before watchdog process escalation.");
            }
        }

        await VerifySuccessorAsync(store, first, token);
        Console.WriteLine($"{target.Engine} {target.CommitWriter} {(target.FenceInBatch ? "in-batch" : "checked")} watchdog {result} under lock; one successor commit");
        return 0;

        async Task WaitForLockAsync(Func<bool> ended)
        {
            await using var admin = target.Admins()();
            while (await Target.Text(admin, waiting) != "1")
            {
                if (ended()) throw new InvalidOperationException("The worker stopped before the database reported a lock wait.");
                await Task.Delay(25, token);
            }
        }
    }

    private static async Task<int> RunPostgresOwnedFenceAsync(Target target, string connection)
    {
        if (target.Engine != "postgres" || new NpgsqlConnectionStringBuilder(connection).SslMode != SslMode.VerifyFull)
        {
            Console.Error.WriteLine("The held-fence stage requires PostgreSQL and SSL Mode=VerifyFull.");
            return 2;
        }

        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = limit.Token;
        await using (var probe = target.Admins()())
            Require(await probe.Database.SqlQueryRaw<bool>("""
                SELECT ssl AS "Value" FROM pg_stat_ssl WHERE pid = pg_backend_pid()
                """).SingleAsync(token), "the preflight used a verified TLS session before resetting the disposable database");

        await target.ResetAsync(1);
        var store = target.Store(target.Workers());
        var first = await store.TakeAsync(0, "watchdog-old", LeaseTime, token)
            ?? throw new InvalidOperationException("The old owner did not take the partition.");
        string interrupted;
        await using (var blocker = target.Admins()())
        await using (var hold = await blocker.Database.BeginTransactionAsync(token))
        {
            var blockerPid = await blocker.Database.SqlQueryRaw<int>("""SELECT pg_backend_pid() AS "Value" """).SingleAsync(token);
            await blocker.Database.ExecuteSqlRawAsync(AwaitAdvisoryLock, token);
            var owner = target.Store(target.Workers(new PauseBeforeCommit()));
            long monotonic = 0;
            List<string> escalated = [];
            var watchdog = new Watchdog(LeaseTime, escalated.Add, () => monotonic);
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
            var blocked = watchdog.Watch(owner).CommitAsync(Flush(first), attempt.Token).AsTask();
            try
            {
                await using var observer = target.Admins()();
                while (!await observer.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (
                        SELECT 1 FROM pg_stat_activity activity JOIN pg_stat_ssl ssl ON ssl.pid = activity.pid
                        WHERE activity.datname = current_database() AND activity.application_name = 'kernel-worker'
                            AND ssl.ssl AND {blockerPid} = ANY(pg_blocking_pids(activity.pid))
                    ) AS "Value"
                    """).SingleAsync(token))
                {
                    if (blocked.IsCompleted)
                        throw new InvalidOperationException("The owner stopped before its TLS session reached the advisory lock.");
                    await Task.Delay(25, token);
                }
                Require(!await FenceCanBeLockedAsync(target, token), "the waiting TLS worker owns the physical partition row lock");
                Require(!blocked.IsCompleted && watchdog.Check() == 0, "the transaction is still open before the watchdog threshold");

                monotonic = (long)(LeaseTime.TotalMilliseconds * 2);
                Require(watchdog.Check() == 1, "the watchdog requests cancellation of the open transaction");
                interrupted = await AwaitInterruptedAsync(blocked, LeaseTime / 3, token);
                monotonic += (long)(LeaseTime.TotalMilliseconds / 3);
                Require(watchdog.Check() == 0 && escalated.Count == 0, "the provider returned before process escalation");
                Require(await FenceCanBeLockedAsync(target, token), "the fence lock cleared while the advisory blocker was still held");
            }
            finally
            {
                await attempt.CancelAsync();
                await ((Task)blocked).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        await VerifySuccessorAsync(store, first, token);
        Console.WriteLine($"PostgreSQL {target.CommitWriter} {(target.FenceInBatch ? "in-batch" : "checked")} watchdog {interrupted} under TLS with a held fence");
        return 0;
    }

    private const string AwaitAdvisoryLock = "SELECT pg_advisory_xact_lock(4229, 5)";

    private sealed class PauseBeforeCommit : DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            await using var command = transaction.Connection!.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = AwaitAdvisoryLock;
            await command.ExecuteNonQueryAsync(cancellationToken);
            return result;
        }
    }

    private static async Task<bool> FenceCanBeLockedAsync(Target target, CancellationToken token)
    {
        await using var db = target.Admins()();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        try
        {
            await db.Database.ExecuteSqlRawAsync("""SELECT 1 FROM "Partitions" WHERE "Id" = 0 FOR UPDATE NOWAIT""", token);
            return true;
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            return false;
        }
    }

    private static async Task VerifySuccessorAsync(IKernelStore store, Lease first, CancellationToken token)
    {
        var before = await store.ReadJournalAsync(0, 0, 10, token);
        Require(before.Count == 0 && (await store.LoadAsync([Actor], token)).Count == 0,
            "The interrupted transaction left no checkpoint or journal row.");

        Lease? second;
        while ((second = await store.TakeAsync(0, "watchdog-successor", LeaseTime, token)) is null)
            await Task.Delay(25, token);
        Require(second.Epoch > first.Epoch && second.Position == first.Position,
            "The successor took a later epoch at the unchanged journal position.");
        Require((await store.CommitAsync(Flush(second), token)).Landed, "The successor committed the same work.");
        var after = await store.ReadJournalAsync(0, 0, 10, token);
        Require(after is [{ Instance: Actor, Revision: 1, Position: 1 }] &&
            (await store.LoadAsync([Actor], token)) is [{ Revision: 1 }], "Only the successor's checkpoint and journal row are stored.");
    }

    private static async Task<string> IsolatedWorkerAsync(Target target, string connection, string[] options, Lease lease,
        Func<Func<bool>, Task> waitForLock, CancellationToken token)
    {
        // Some drivers may leave a cancelled command blocked. Only the disposable child
        // takes the runtime's process-escalation callback in this fixture.
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true };
        string[] arguments = [typeof(ProviderWatchdog).Assembly.Location, "x", "watchdog-worker", target.Engine, "isolated", .. options,
            $"epoch={lease.Epoch.ToString(CultureInfo.InvariantCulture)}", $"position={lease.Position.ToString(CultureInfo.InvariantCulture)}"];
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment[ChildConnection] = connection;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("The disposable worker did not start.");
        try
        {
            await waitForLock(() => child.HasExited);
            Require(!child.HasExited, "The database reports the child worker's real lock wait.");
            await child.StandardInput.WriteLineAsync("cancel".AsMemory(), token);
            child.StandardInput.Close();
            await child.WaitForExitAsync(token);
            Require(child.ExitCode is 0 or Escalated, "The child stopped while the competing transaction held the lock.");
            return child.ExitCode == Escalated ? "process escalation" : "call ended without escalation";
        }
        finally
        {
            if (!child.HasExited)
            {
                try { child.Kill(); }
                catch (InvalidOperationException) when (child.HasExited) { }
                await child.WaitForExitAsync();
            }
        }
    }

    internal static async Task<int> RunWorkerAsync(string engine, Dictionary<string, string> options)
    {
        var connection = Environment.GetEnvironmentVariable(ChildConnection) ??
            throw new InvalidOperationException("The watchdog coordinator must start the isolated worker.");
        var target = new Target(engine, connection, options);
        var lease = new Lease(long.Parse(options["epoch"], CultureInfo.InvariantCulture),
            long.Parse(options["position"], CultureInfo.InvariantCulture), 0);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = limit.Token;
        long monotonic = 0;
        var watchdog = new Watchdog(LeaseTime, message =>
        {
            Console.WriteLine(message);
            Environment.Exit(Escalated);
        }, () => monotonic);
        var provider = new Tally(target.Store(target.Workers()));
        var commit = watchdog.Watch(provider).CommitAsync(Flush(lease), token).AsTask();
        if (await Console.In.ReadLineAsync(token) is not "cancel") return 2;
        Require(!commit.IsCompleted && watchdog.Check() == 0, "The child commit waits before the watchdog deadline.");
        monotonic = (long)(LeaseTime.TotalMilliseconds * 2);
        Require(watchdog.Check() == 1, "The watchdog requests cancellation of the child commit.");
        try
        {
            await AwaitInterruptedAsync(commit, LeaseTime / 3, token);
            return 0;
        }
        catch (TimeoutException)
        {
            monotonic += (long)(LeaseTime.TotalMilliseconds / 3);
            watchdog.Check();
            // The provider records its return before the watchdog removes the call and publishes its task.
            Require(provider.HasCompletedCall("commit"), "A still-blocked provider commit must trigger process escalation.");
            await AwaitInterruptedAsync(commit, Timeout.InfiniteTimeSpan, token);
            return 0;
        }
    }

    private static CommitBatch Flush(Lease held) => new(0, held.Epoch, held.Position, LeaseTime, false,
        [new(Actor, new(Actor, "m", "d", InstanceStatus.Running, "{}", 1, null), null, [], [], [], [], [], "[]")], [], []);

    private static async Task<string> AwaitInterruptedAsync(Task<Committed> commit, TimeSpan patience, CancellationToken token)
    {
        try
        {
            await commit.WaitAsync(patience, token);
            throw new InvalidOperationException("A current-fence commit returned without reporting an interruption.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return "cancelled"; }
        catch (StoreException error) when (error.Failure == Failure.Unknown)
        {
            var cause = error.InnerException;
            while (cause is not null)
            {
                Console.WriteLine(cause switch
                {
                    PostgresException postgres => $"Interruption diagnostic: PostgreSQL {postgres.SqlState}",
                    SqlException sql => $"Interruption diagnostic: SQL Server {sql.Number}",
                    _ => $"Interruption diagnostic: {cause.GetType().Name}"
                });
                cause = cause.InnerException;
            }
            return "unknown";
        }
    }

    private static void Require(bool condition, string expectation)
    {
        if (!condition) throw new InvalidOperationException(expectation);
        Console.WriteLine($"ok {expectation}");
    }
}
