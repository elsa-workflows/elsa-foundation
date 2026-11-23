using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Kernel.VpExperiment;

/// <summary>
/// The measurement: for one engine, one fence variant and one virtual-partition count, with <c>nodes</c> nodes sharing
/// the count in fair ranges. Counted as the cost runs count (commands, BEGINs, COMMITs; on PostgreSQL also the round
/// trips on the TCP stream and the Execute frames, one per statement), plus the wall-clock time of each row.
/// </summary>
public static class Measure
{
    public sealed record Options(string Engine, string? Connection, int Count, int Nodes, Fence Fence, bool FenceInBatch, bool Proxy, long LeaseMs = 10_000);

    public static async Task<bool> RunAsync(Options o)
    {
        var postgres = o.Engine == "postgres";
        var counter = new Counting();
        Wire? wire = o.Proxy && postgres ? Wire.Start("postgres", o.Connection!) : null;
        var database = $"kernel_vp_{(o.Fence == Fence.PerVirtualPartition ? "vp" : "range")}_{o.Count}_{(o.FenceInBatch ? "batch" : "checked")}";
        Directory.CreateDirectory(".out");
        var connection = postgres ? (wire?.Rewrite(o.Connection!) ?? o.Connection!) + $";Database={database};Application Name=kernel-vp" : $"Data Source=.out/{database}.db";
        if (!postgres) { SqliteConnection.ClearAllPools(); foreach (var f in Directory.GetFiles(".out", $"{database}.db*")) File.Delete(f); }
        VpDb Create()
        {
            var builder = new DbContextOptionsBuilder<VpDb>().AddInterceptors(counter, counter.Transactions);
            if (postgres) builder.UseNpgsql(connection); else builder.UseSqlite(connection);
            return new VpDb(builder.Options, o.Fence, postgres);
        }
        var store = new RangeStore(Create, o.Fence, postgres, o.FenceInBatch);
        await store.CreateAsync(o.Count);
        if (!postgres) { await using var db = Create(); await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL"); }

        var ok = true;
        void Check(bool condition, string what) { Console.WriteLine($"    {(condition ? "ok" : "FAIL")} {what}"); ok &= condition; }
        var ranges = VirtualPartitions.FairRanges(o.Count, o.Nodes);
        var (rangeA, rangeB) = (ranges[0], ranges[1]);
        var R = rangeA.Count;
        Console.WriteLine($"\n{o.Engine}, {(o.Fence == Fence.PerVirtualPartition ? "(i) fence per virtual partition" : "(ii) fence per owned range")}, {o.Count} virtual partitions, {o.Nodes} nodes (ranges of {R}), fence {(o.FenceInBatch ? "in the batch" : "checked")}");
        Console.WriteLine($"{"run",-70} {"commands",8} {"begin",5} {"commit",6} {"counted",7} {"tcp",5} {"exec",5} {"rows",6} {"ms",8}");
        async Task<T> Row<T>(string name, Func<Task<T>> body, Func<T, string>? rows = null)
        {
            counter.Reset();
            var (trips, executes) = (wire?.Trips ?? 0, wire?.Executes ?? 0);
            var watch = Stopwatch.StartNew();
            var result = await body();
            watch.Stop();
            var counted = counter.Commands + counter.Transactions.Ended + (postgres ? 0 : counter.Transactions.Begun);
            Console.WriteLine($"{name,-70} {counter.Commands,8} {counter.Transactions.Begun,5} {counter.Transactions.Ended,6} {counted,7} {(wire is null ? "" : (wire.Trips - trips).ToString()),5} {(wire is null ? "" : (wire.Executes - executes).ToString()),5} {rows?.Invoke(result) ?? "",6} {watch.Elapsed.TotalMilliseconds,8:F1}");
            return result;
        }

        // Instances: new ones spread over a range by the placement hash, each with its start message consumed and a journal
        // row; later turns of the same instances at the next revision. The revision each instance is at is tracked here, so
        // a refused commit below is refused by the fence and not by an instance's revision token.
        var serial = 0;
        var revision = new Dictionary<string, long>(StringComparer.Ordinal);
        async Task<List<Turn>> SpreadAsync(int n, VpRange range)
        {
            var turns = new List<Turn>();
            while (turns.Count < n)
            {
                var name = $"w{++serial}";
                var vp = VirtualPartitions.Of(name, o.Count);
                if (!range.Contains(vp)) continue;
                var id = InstanceRef.In(vp, name).Id;
                revision[id] = 0;
                turns.Add(Next(vp, id) with { Consumed = $"m-{name}" });
                await store.AddAsync($"m-{name}", vp, id);       // the outside world's insert: not counted
            }
            return turns;
        }
        Turn Next(int vp, string id) => new(vp, id, revision[id] + 1, null, """{"kind":"turn","node":"n1","time":0,"payload":{"step":1}}""", """{"model":"sequence","count":10,"at":1,"mode":"balanced"}""");
        List<Turn> Again(IEnumerable<Turn> turns) => turns.Select(t => Next(t.Vp, t.Instance)).ToList();
        async Task<bool> Commit(Ownership owner, List<Turn> turns)
        {
            var landed = await store.CommitAsync(owner, turns, o.LeaseMs);
            if (landed) foreach (var t in turns) revision[t.Instance] = t.Revision;
            return landed;
        }

        // B takes its range and flushes once, uncounted: the JIT and the connection pool warm up on it. Then A's take is counted.
        await store.CarveAsync(rangeB);
        var b = await store.TakeAsync(rangeB, "B", o.LeaseMs) ?? throw new InvalidOperationException("B could not take its range");
        if (!await Commit(b, await SpreadAsync(2, rangeB))) throw new InvalidOperationException("B's warm-up flush did not land");
        if (o.Fence == Fence.PerRange) await Row($"carve the range out of the free row (bootstrap, (ii) only)", () => store.CarveAsync(rangeA), n => n.ToString());
        var a = await Row($"take a free range of {R} virtual partitions", () => store.TakeAsync(rangeA, "A", o.LeaseMs), r => r is null ? "none" : r.Position.Count.ToString()) ?? throw new InvalidOperationException("A could not take its range");

        var flushed = new List<Turn>();
        foreach (var n in new[] { 1, 10, 100 })
        {
            var turns = await SpreadAsync(n, rangeA);
            var k = turns.Select(t => t.Vp).Distinct().Count();
            var fenceRows = o.Fence == Fence.PerVirtualPartition ? k : 1 + k;
            Check(await Row($"flush: {n} new instance(s) over {k} vp(s); {fenceRows + 3 * n} statements", () => Commit(a, turns)), $"the flush of {n} landed");
            Check(await Row($"flush: the same {n} again (updates, no message) over {k} vp(s); {fenceRows + 2 * n} statements", () => Commit(a, Again(turns))), $"the second flush of {n} landed");
            flushed.AddRange(turns);
        }
        await Row("beat (an idle owner renews its lease)", () => store.BeatAsync(a, o.LeaseMs), rows => rows.ToString());

        // A dies: its lease runs out; C takes the range. A's picture is now stale and must be refused.
        await store.ExpireAsync("A");
        var c = await Row($"takeover of a dead owner's range of {R} virtual partitions", () => store.TakeAsync(rangeA, "C", o.LeaseMs), r => r is null ? "none" : r.Position.Count.ToString()) ?? throw new InvalidOperationException("C could not take over");
        Check(!await Row("the dead owner's commit of 1 instance (must be refused)", () => Commit(a, Again(flushed.Take(1)))), "a stale owner's commit after a takeover is refused");
        Check(await Row("the new owner's first flush: 10 instances it inherited", () => Commit(c, Again(flushed.Take(10)))), "the new owner's first flush landed at the inherited positions");

        // Rebalance: C gives the upper half of its range to B, cooperatively. A stale picture of C from before the move is kept to probe with.
        var at = rangeA.Lo + R / 2;
        var staleC = new Ownership("C", c.Range) { Epoch = c.Epoch };
        foreach (var pair in c.Position) staleC.Position[pair.Key] = pair.Value;
        foreach (var pair in c.VpEpoch) staleC.VpEpoch[pair.Key] = pair.Value;
        Check(await Row($"release the tail [{at}, {rangeA.Hi}) of the range ({R / 2} vps) cooperatively", () => store.ReleaseTailAsync(c, at)), "the release landed");
        var tail = await Row($"take the freed tail ({R / 2} vps)", () => store.TakeAsync(new VpRange(at, rangeA.Hi), "B", o.LeaseMs), r => r is null ? "none" : r.Position.Count.ToString()) ?? throw new InvalidOperationException("B could not take the tail");
        Check(await Row($"merge the tail into B's range ({R / 2 + R} vps)", () => store.MergeAsync(tail, b)), "the merge landed");
        b = tail;
        var moved = flushed.First(t => t.Vp >= at);
        var kept = flushed.First(t => t.Vp < at);
        Check(!await Row("a stale picture of C commits in the moved half (must be refused)", () => Commit(staleC, Again([moved]))), "a commit from before the move, into the moved half, is refused");
        var keptLands = await Row("a stale picture of C commits in the kept half", () => Commit(staleC, Again([kept])));
        Console.WriteLine($"    note: a commit from before the move, into the kept half, {(keptLands ? "LANDS: the rows of the kept virtual partitions did not change" : "is REFUSED: the split changed the range row, so the whole range's picture is stale")}");
        Check(keptLands == (o.Fence == Fence.PerVirtualPartition), "which is what this variant predicts");
        if (keptLands) c.Position[kept.Vp] = staleC.Position[kept.Vp];
        Check(await Row("C's own next flush after its split: 10 instances in the kept half", () => Commit(c, Again(flushed.Where(t => t.Vp < at && t.Vp != kept.Vp).Take(10)))), "the owner that split its own range commits on without a reset");
        var inherited = flushed.Where(t => t.Vp >= at).Take(10).ToList();
        Check(await Row($"B's flush after the merge: {inherited.Count} instances it took over", () => Commit(b, Again(inherited))), "the merged range's owner commits at the inherited positions");

        // Repeated takes of a dead owner's range, for the time: C expires, D takes, five times.
        var times = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            await store.ExpireAsync(i == 0 ? "C" : "D");
            var watch = Stopwatch.StartNew();
            var d = await store.TakeAsync(c.Range, "D", o.LeaseMs) ?? throw new InvalidOperationException("D could not take over");
            times.Add(watch.Elapsed.TotalMilliseconds);
            c = d;
        }
        Console.WriteLine($"    takeover time of a range of {c.Range.Count} virtual partitions, 5 takes: median {times.Order().ElementAt(2):F1} ms, min {times.Min():F1}, max {times.Max():F1}");

        var (gapless, report) = await store.VerifyJournalAsync(o.Count);
        Check(gapless, "journal positions: " + report);
        return ok;
    }
}
