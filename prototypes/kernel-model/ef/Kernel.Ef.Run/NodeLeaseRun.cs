using System.Diagnostics;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

/// <summary>
/// K1b, NL-9 on the prototype engines (ownership-and-fencing.md, the node's heartbeat, "Lock order"): takes, a heartbeat
/// and a commit overlap, and no cycle may form. An outside transaction holds rows so that each interleaving is forced:
/// the probe waits until the engine reports the sessions waiting, then releases the hold (its write is rolled back).
///   x nodelease postgres|sqlserver|mysql "connection" case=heartbeat|mutual [mutation=subquery|rr] [rcsi=0]
/// case=heartbeat: r owns 1, p owns 2 and 3, both past their node leases. The hold is on partition row 0. p's heartbeat
///   is sent: it writes p's record, and its read of the partition rows waits at row 0 where reads take locks. Then q's
///   take of {1, 2} is sent; on release, p's commit in 3 and p's gain take of 5 are sent too.
/// case=mutual: a owns {1, 2} and b owns {3, 4}, both past their leases. The hold is on both records. a's take of {3, 4}
///   and b's take of {1, 2} are sent, then the hold is released.
/// mutation=subquery (EfStore.NodeReadInUpdate) reads the node records inside the take's update, after it locked rows;
/// mutation=rr (EfStore.RepeatableReadTake) runs the take at REPEATABLE READ. Each is expected to deadlock where its
/// engine's locks make the cycle (NL-9); the store as built must not deadlock on any engine.
/// </summary>
static class NodeLeaseRun
{
    private static readonly TimeSpan Brief = TimeSpan.FromSeconds(1), Lease = TimeSpan.FromSeconds(60), Window = TimeSpan.FromSeconds(3);

    public static async Task<int> RunAsync(Target target)
    {
        await target.ResetAsync(8);
        var (admin, store) = (target.Admins(), target.Store(target.Workers()));
        var (kind, mutation) = (target.Get("case", "heartbeat"), target.Get("mutation", ""));
        Console.WriteLine($"{target.Engine}: {await target.DescribeAsync()}; case {kind}; take {mutation switch { "subquery" => "READS THE NODE RECORDS IN ITS UPDATE (the negative)", "rr" => "AT REPEATABLE READ (the negative)", _ => "as built" }}");
        var held = new Dictionary<string, OwnedLease>();
        foreach (var (node, set) in kind == "mutual" ? new[] { ("a", new[] { 1, 2 }), ("b", [3, 4]) } : [("r", [1]), ("p", [2, 3])])
            if ((held[node] = await store.TakeAsync(set, node, Brief)).Taken.Count != set.Length) { Console.WriteLine($"the probe could not set up {node}"); return 2; }
        await Task.Delay(Brief + TimeSpan.FromMilliseconds(500));     // every node lease above has expired
        var deadlocks = await target.DeadlocksAsync(admin);
        var clock = Stopwatch.StartNew();
        var hold = await HoldAsync(target, kind == "mutual"
            ? """UPDATE "Nodes" SET "SeenAt" = "SeenAt" + 1 WHERE "Id" IN ('a', 'b')"""
            : """UPDATE "Partitions" SET "WindowStart" = 1 WHERE "Id" = 0""");
        var sent = new List<(string What, Task<string> Outcome)>();
        void Send(string what, Func<Task<string>> run) => sent.Add((what, Task.Run(async () =>
        {
            try { return await run(); }
            catch (Exception e) { return "failed: " + Tally.Code(e); }
        })));
        bool waited;
        if (kind == "mutual")
        {
            Send("a's take of {3, 4}", async () => "took " + Ids(await store.TakeAsync([3, 4], "a", Lease)));
            waited = await target.WaitForWaitersAsync(1);
            Send("b's take of {1, 2}", async () => "took " + Ids(await store.TakeAsync([1, 2], "b", Lease)));
            waited &= await target.WaitForWaitersAsync(2);
            await hold.DisposeAsync();
        }
        else
        {
            Send("p's heartbeat", async () => "lists " + string.Join(" ", (await store.NodeHeartbeatAsync("p", Lease, Window)).Owned.Select(o => o.Partition)));
            waited = await target.WaitForWaitersAsync(1);
            Send("q's take of {1, 2}", async () => "took " + Ids(await store.TakeAsync([1, 2], "q", Lease)));
            waited &= await target.WaitForWaitersAsync(2);
            await hold.DisposeAsync();
            var three = held["p"].Taken.Single(f => f.Partition == 3);
            Send("p's commit in 3", async () => (await store.CommitAsync(CommitBatch.Owned([three], Lease, false,
                [new TurnCommit("3/p", new InstanceRow("3/p", "m", "d", InstanceStatus.Running, "{}", 1, null), null, [], [], [], [], [], "[]")], [], []))).Landed ? "landed" : "not landed");
            Send("p's gain take of 5", async () => "took " + Ids(await store.TakeAsync([5], "p", Lease)));
        }
        var done = await Task.WhenAny(Task.WhenAll(sent.Select(s => s.Outcome)), Task.Delay(TimeSpan.FromSeconds(90))) is Task<string[]>;
        var outcomes = sent.Select(s => (s.What, Answer: s.Outcome.IsCompleted ? s.Outcome.Result : "HUNG")).ToList();
        var counted = await target.DeadlocksAsync(admin) - deadlocks;
        await using var db = admin();
        var owners = await db.Partitions.AsNoTracking().Where(p => p.Id >= 1 && p.Id <= 5).OrderBy(p => p.Id).Select(p => $"{p.Id}:{p.Owner ?? "-"}@{p.Epoch}").ToListAsync();
        Console.WriteLine($"sessions waiting before the release: {(waited ? "yes" : "NO (the interleaving was not forced)")}; {clock.ElapsedMilliseconds} ms");
        foreach (var (what, answer) in outcomes) Console.WriteLine($"  {what}: {answer}");
        Console.WriteLine($"engine deadlocks counted: {counted}; owners of 1 to 5: {string.Join(" ", owners)}");
        var clean = done && counted == 0 && outcomes.All(o => !o.Answer.StartsWith("failed", StringComparison.Ordinal) && o.Answer != "HUNG");
        Console.WriteLine(mutation != ""
            ? $"nodelease negative: {(counted > 0 ? "DEADLOCKED, broken by the engine" : clean ? "no deadlock" : "other")}"
            : $"nodelease: {(clean ? "no deadlock; every outcome landed or was refused" : "WRONG")}");
        return mutation != "" || clean ? 0 : 1;
    }

    private static string Ids(OwnedLease lease) => lease.Taken.Count == 0 ? "nothing" : string.Join(" ", lease.Taken.Select(f => f.Partition));

    /// <summary>An outside transaction makes this write and holds its row locks until disposed; the write is rolled back.</summary>
    private static async Task<IAsyncDisposable> HoldAsync(Target target, string write)
    {
        var db = target.Admins()();
        var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(target.Quote(write));
        return new Hold(db, transaction);
    }

    private sealed class Hold(KernelDb db, IDbContextTransaction transaction) : IAsyncDisposable
    {
        private bool _done;
        public async ValueTask DisposeAsync()
        {
            if (_done) return;
            _done = true;
            await transaction.RollbackAsync();
            await db.DisposeAsync();
        }
    }
}
