static class ProviderWatchdog
{
    public static async Task<int> RunAsync(Target target)
    {
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
                SELECT CASE WHEN EXISTS (SELECT 1 FROM information_schema.INNODB_TRX t
                JOIN information_schema.PROCESSLIST p ON t.trx_mysql_thread_id = p.ID
                WHERE p.DB = DATABASE() AND p.USER = 'kernel_worker' AND t.trx_state = 'LOCK WAIT')
                THEN 1 ELSE 0 END
                """,
            _ => null
        };
        if (waiting is null)
        {
            Console.Error.WriteLine("watchdog requires PostgreSQL, SQL Server, or MySQL on a dedicated test database.");
            return 2;
        }

        await target.ResetAsync(1);
        var store = target.Store(target.Workers());
        var lease = TimeSpan.FromSeconds(2);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = limit.Token;
        var first = await store.TakeAsync(0, "watchdog-old", lease, token)
            ?? throw new InvalidOperationException("The old owner did not take the partition.");
        const string actor = "0/watchdog";
        long monotonic = 0;
        var ended = new List<string>();
        var watchdog = new Watchdog(lease, ended.Add, () => monotonic);

        CommitBatch Flush(Lease held) => new(0, held.Epoch, held.Position, lease, false,
            [new(actor, new(actor, "m", "d", InstanceStatus.Running, "{}", 1, null), null, [], [], [], [], [], "[]")], [], []);

        string result;
        await using (await target.HoldAsync(0))
        {
            var blocked = watchdog.Watch(store).CommitAsync(Flush(first), token).AsTask();
            await using var admin = target.Admins()();
            while (await Target.Text(admin, waiting) != "1")
            {
                Require(!blocked.IsCompleted, "The database reports a real lock wait for the worker commit.");
                await Task.Delay(25, token);
            }
            Require(!blocked.IsCompleted && watchdog.Check() == 0, "The physical commit waits before the watchdog deadline.");

            monotonic = (long)(lease.TotalMilliseconds * 2);
            Require(watchdog.Check() == 1, "The watchdog cancels the one blocked commit.");
            try
            {
                var committed = await blocked.WaitAsync(TimeSpan.FromSeconds(8), token);
                Require(!committed.Landed, "The cancelled commit must leave before the lock holder releases.");
                result = "refused";
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { result = "cancelled"; }
            catch (StoreException error) when (error.Failure == Failure.Unknown) { result = "unknown"; }
        }
        monotonic += (long)lease.TotalMilliseconds;
        Require(watchdog.Check() == 0 && watchdog.Closed == 1 && ended.Count == 0,
            "The commit ended without requiring watchdog process termination.");
        var before = await store.ReadJournalAsync(0, 0, 10, token);
        Require(before.Count == 0 && (await store.LoadAsync([actor], token)).Count == 0,
            "The cancelled transaction left no checkpoint or journal row.");

        Lease? second;
        while ((second = await store.TakeAsync(0, "watchdog-successor", lease, token)) is null)
            await Task.Delay(25, token);
        Require(second.Epoch > first.Epoch && second.Position == first.Position,
            "The successor took a later epoch at the unchanged journal position.");
        Require((await store.CommitAsync(Flush(second), token)).Landed, "The successor committed the same work.");
        var after = await store.ReadJournalAsync(0, 0, 10, token);
        Require(after is [{ Instance: actor, Revision: 1, Position: 1 }] &&
            (await store.LoadAsync([actor], token)) is [{ Revision: 1 }], "Only the successor's checkpoint and journal row are stored.");
        Console.WriteLine($"{target.Engine} {target.CommitWriter} {(target.FenceInBatch ? "in-batch" : "checked")} watchdog {result} under lock; one successor commit");
        return 0;
    }

    private static void Require(bool condition, string expectation)
    {
        if (!condition) throw new InvalidOperationException(expectation);
        Console.WriteLine($"ok {expectation}");
    }
}
