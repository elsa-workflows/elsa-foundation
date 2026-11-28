/// <summary>
/// K1b slice 6a (#4880 section 3, item 4): a take under the node lease sets the session's lock timeout on SQL Server and
/// MySQL (<c>EfStore.BeginNodeTakeAsync</c>). With the worker pool cut to one connection, the take and the reads around it
/// share one pooled session, whose lock timeout must read back at its default after the take.
///   x locktimeout sqlserver|mysql "connection"
/// </summary>
static class LockTimeout
{
    public static async Task<int> RunAsync(Target target)
    {
        if (target.Engine is not ("sqlserver" or "mysql")) { Console.Error.WriteLine("locktimeout: sqlserver or mysql"); return 2; }
        await target.ResetAsync(2);
        var workers = target.Workers();
        var (session, timeout) = target.Engine == "sqlserver" ? ("SELECT @@SPID", "SELECT @@LOCK_TIMEOUT") : ("SELECT CONNECTION_ID()", "SELECT @@SESSION.innodb_lock_wait_timeout");
        async Task<(string Session, string Timeout)> Read()
        {
            await using var db = workers();
            return (await Target.Text(db, session), await Target.Text(db, timeout));
        }
        var before = await Read();
        var taken = await target.Store(workers).TakeAsync([0, 1], "p", TimeSpan.FromSeconds(9));
        var after = await Read();
        var ok = before.Session == after.Session && before.Timeout == after.Timeout && taken.Taken.Count == 2;
        Console.WriteLine($"locktimeout {target.Engine}: session {before.Session}, then {after.Session}; lock timeout before the take {before.Timeout}, after it {after.Timeout}; took {taken.Taken.Count} of 2: {(ok ? "back at its default" : "WRONG")}");
        return ok ? 0 : 1;
    }
}
