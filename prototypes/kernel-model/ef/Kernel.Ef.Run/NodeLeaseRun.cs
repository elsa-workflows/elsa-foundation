using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

/// <summary>
/// K1b, NL-9 on the prototype engines (ownership-and-fencing.md, the node's heartbeat, "Lock order"): takes, a heartbeat
/// and a commit overlap, and no cycle may form. An outside transaction holds rows so that each interleaving is forced:
/// the probe waits until the engine reports the sessions waiting, then releases the hold (its write is rolled back).
///   x nodelease postgres|sqlserver|mysql "connection" case=heartbeat|mutual|heartbeats [mutation=subquery|rr|membership|rr-subquery|share] [expect=none|deadlock] [rcsi=0]
/// case=heartbeat: r owns 1, p owns 2 and 3, both past their node leases. The hold is on partition row 0. p's heartbeat
///   is sent: it writes p's record, and its read of the partition rows waits at row 0 where reads take locks. Then q's
///   take of {1, 2} is sent; on release, p's commit in 3 and p's gain take of 5 are sent too.
/// case=mutual: a owns {1, 2} and b owns {3, 4}, both past their leases. The hold is on both records. a's take of {3, 4}
///   and b's take of {1, 2} are sent, then the hold is released. The ordered locking-read SQL Server hold uses update locks.
/// case=heartbeats: the hold is on partition row 0; p's and r's heartbeats both write their records and queue at it.
/// mutation=subquery (EfStore.NodeReadInUpdate) reads the node records inside the take's update, after it locked rows;
/// mutation=rr (EfStore.RepeatableReadTake) runs the take at REPEATABLE READ; mutation=membership
/// (EfStore.MembershipInHeartbeat) reads the node count and the free list inside the heartbeat's transaction.
/// NL-C1 adds MySQL rr-subquery (both take mutations) and PostgreSQL share (FOR SHARE on the other owners' records).
/// The exit code is the verdict. expect=deadlock fails unless the run deadlocked; expect=none (the default as built) fails
/// if it did, or if any answer failed or hung. Ordered mutual takes must exchange both complete ranges at the next epochs. A negative (any mutation=) must deadlock: its default is expect=deadlock,
/// and a negative that does not deadlock fails whatever its expect=. A mutual run, and any run where reads take locks
/// (SQL Server without RCSI), fails too when the forced wait did not occur (its sessions were not waiting before the
/// release); the heartbeat cases elsewhere are recorded unforced. On PostgreSQL the victim's error decides, since its
/// counter lags.
/// </summary>
static class NodeLeaseRun
{
    private static readonly TimeSpan Brief = TimeSpan.FromSeconds(1), Lease = TimeSpan.FromSeconds(60), Window = TimeSpan.FromSeconds(3);

    public static async Task<int> RunAsync(Target target)
    {
        await target.ResetAsync(8);
        var starts = new TransactionLevels();
        var (admin, store) = (target.Admins(), target.Store(target.Workers(starts)));
        var (kind, mutation) = (target.Get("case", "heartbeat"), target.Get("mutation", ""));
        var (negative, expect) = (mutation != "", target.Get("expect", mutation != "" ? "deadlock" : "none"));
        var take = mutation switch
        {
            "subquery" => "reads the node records in its update",
            "rr" => "at repeatable read",
            "rr-subquery" => "at repeatable read with a subquery",
            "share" => "reads the node records FOR SHARE",
            "membership" => "with the heartbeat reading membership in its transaction",
            _ => "as built",
        };
        Console.WriteLine($"{target.Engine}: {await target.DescribeAsync()}; case {kind}; take {take}; expect {expect}");
        var verifyExchange = kind == "mutual" && !negative;
        if (verifyExchange) await SeedJournalAsync(target.Store(target.Workers()));
        var held = new Dictionary<string, OwnedLease>();
        foreach (var (node, set) in kind == "mutual" ? new[] { ("a", new[] { 1, 2 }), ("b", [3, 4]) } : [("r", [1]), ("p", [2, 3])])     // heartbeats: as heartbeat
        {
            held[node] = await store.TakeAsync(set, node, Brief);
            if (held[node].Taken.Select(fence => fence.Partition).SequenceEqual(set)) continue;
            Console.WriteLine($"the probe could not set up {node}");
            return 2;
        }
        await Task.Delay(Brief + TimeSpan.FromMilliseconds(500));     // every node lease above has expired
        var deadlocks = await target.DeadlocksAsync(admin);
        var clock = Stopwatch.StartNew();
        var lockingReads = target.Engine == "sqlserver" && target.Get("rcsi", "1") == "0";
        // Update locks on the ordered locking-read setup allow both peer reads to reach
        // their expiry decision. The two sessions then wait on renewal until the hold ends.
        var hold = await HoldAsync(target, kind != "mutual"
            ? """UPDATE "Partitions" SET "WindowStart" = 1 WHERE "Id" = 0"""
            : verifyExchange && lockingReads
                ? """SELECT 1 FROM "Nodes" WITH (UPDLOCK, ROWLOCK) WHERE "Id" IN ('a', 'b')"""
                : """UPDATE "Nodes" SET "SeenAt" = "SeenAt" + 1 WHERE "Id" IN ('a', 'b')""");
        var sent = new List<(string What, Task<string> Outcome)>();
        void Send(string what, Func<Task<string>> run) => sent.Add((what, Task.Run(async () =>
        {
            try { return await run(); }
            catch (Exception e) { return "failed: " + Tally.Code(e); }
        })));
        bool waited;
        OwnedLease? takenByA = null, takenByB = null;
        if (kind == "mutual")
        {
            Send("a's take of {3, 4}", async () => "took " + Ids(takenByA = await store.TakeAsync([3, 4], "a", Lease)));
            waited = await target.WaitForWaitersAsync(1);
            Send("b's take of {1, 2}", async () => "took " + Ids(takenByB = await store.TakeAsync([1, 2], "b", Lease)));
            waited &= await target.WaitForWaitersAsync(2);
            await hold.DisposeAsync();
        }
        else if (kind == "heartbeats")
        {
            Send("p's heartbeat", async () => "lists " + string.Join(" ", (await store.NodeHeartbeatAsync("p", Lease, Window)).Owned.Select(o => o.Partition)));
            waited = await target.WaitForWaitersAsync(1);
            Send("r's heartbeat", async () => "lists " + string.Join(" ", (await store.NodeHeartbeatAsync("r", Lease, Window)).Owned.Select(o => o.Partition)));
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
        var owners = await db.Partitions.AsNoTracking().Where(p => p.Id >= 1 && p.Id <= 5).OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Owner, p.Epoch, p.Position }).ToListAsync();
        // A mutual probe has exactly two eligible outgoing owners and one taker per partition.
        // A refusal can look deadlock-free while leaving the old owners and epochs in place.
        var exchanged = !verifyExchange ||
            ValidExchange(held["a"].Taken, takenByB, "b") && ValidExchange(held["b"].Taken, takenByA, "a");
        var takeLevels = starts.Levels.ToArray();
        var readCommitted = !verifyExchange || takeLevels.Length >= 4 && takeLevels.All(level => level == IsolationLevel.ReadCommitted);
        Console.WriteLine($"sessions waiting before the release: {(waited ? "yes" : "NO (the interleaving was not forced)")}; {clock.ElapsedMilliseconds} ms");
        foreach (var (what, answer) in outcomes) Console.WriteLine($"  {what}: {answer}");
        Console.WriteLine($"engine deadlocks counted: {counted}; owners of 1 to 5: {string.Join(" ", owners.Select(p => $"{p.Id}:{p.Owner ?? "-"}@{p.Epoch}"))}");
        if (verifyExchange)
        {
            Console.WriteLine($"both ranges changed owner at the next epochs: {(exchanged ? "yes" : "NO")}");
            Console.WriteLine($"take transaction levels: {string.Join(", ", takeLevels)}; {(readCommitted ? "as expected" : "NOT AS EXPECTED")}");
        }
        var victim = outcomes.Any(o => o.Answer.Contains(target.DeadlockCode, StringComparison.Ordinal));
        var deadlocked = target.Engine == "postgres" ? victim : counted > 0 || victim;
        var clean = done && !deadlocked && outcomes.All(o => !o.Answer.StartsWith("failed", StringComparison.Ordinal) && o.Answer != "HUNG");
        var expectDeadlock = expect == "deadlock";
        var forced = kind == "mutual" || lockingReads;      // where the hold makes the sessions wait
        var met = done && (waited || !forced) && (expectDeadlock ? deadlocked : clean) && (!negative || deadlocked) && exchanged && readCommitted;
        Console.WriteLine($"nodelease{(negative ? " negative" : "")}: {(deadlocked ? "DEADLOCKED, broken by the engine" : clean ? "no deadlock; every outcome landed or was refused" : "other")}; " +
            $"expected {(expectDeadlock ? "a deadlock" : "none")}{(forced ? ", forced" : "")}: {(met ? "as expected" : "NOT AS EXPECTED")}" +
            $"{(forced && !waited ? "; the forced wait did not occur" : "")}{(negative && !deadlocked ? "; a negative must deadlock" : "")}");
        return met ? 0 : 1;

        bool ValidExchange(IReadOnlyList<PartitionFence> before, OwnedLease? taken, string owner)
        {
            if (taken is null || !taken.Taken.SequenceEqual(before.Select(old => old with { Epoch = old.Epoch + 1 }))) return false;
            return before.All(old => old.Position == old.Partition &&
                owners.Any(row => row.Id == old.Partition && row.Owner == owner &&
                    row.Epoch == old.Epoch + 1 && row.Position == old.Position));
        }
    }

    private static async Task SeedJournalAsync(EfStore store)
    {
        var seed = await store.TakeAsync([1, 2, 3, 4], "fixture-journal", Lease);
        var turns = seed.Taken.SelectMany(fence => Enumerable.Range(0, fence.Partition).Select(index =>
        {
            var id = $"{fence.Partition}/fixture-{index}";
            var checkpoint = new InstanceRow(id, "m", "d", InstanceStatus.Running, "{}", 1, null);
            return new TurnCommit(id, checkpoint, null, [], [], [], [], [], """["seed"]""");
        })).ToArray();
        if (seed.Taken.Count != 4 || !(await store.CommitAsync(CommitBatch.Owned(seed.Taken, Lease, false, turns, [], []))).Landed)
            throw new InvalidOperationException("The ordered mutual fixture could not seed nonzero journal positions.");
        await store.ReleaseAsync(seed.Taken);
    }

    private sealed class TransactionLevels : DbTransactionInterceptor
    {
        internal ConcurrentQueue<IsolationLevel> Levels { get; } = new();

        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
            DbTransaction result, CancellationToken cancellationToken = default)
        {
            Levels.Enqueue(result.IsolationLevel);
            return new(result);
        }
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
