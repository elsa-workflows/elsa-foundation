using System.Diagnostics;
using System.Globalization;
using Kernel.EntityFramework;
using Kernel.Runtime.Hosting;
using Kernel.Sim;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// K1 slice 8: what ownership of a set costs in the integrated Worker, on a real store, at 1,024 virtual partitions and
/// four nodes (256 each), for scattered and for contiguous sets. Measurement only: it drives the kernel's own types
/// (<see cref="DurableHost.ForNode"/>, <see cref="Worker"/>, <see cref="PartitionRuntime"/>, <see cref="RuntimeNode"/>)
/// and changes none of them.
///   x setcost postgres|sqlserver|mysql|sqlite "connection" [shape=scattered|contiguous|both] [runs=5] [count=1024|4096] [proxy=1] [writes=1]
///   x setidle postgres "connection" [seconds=120] [count=1024] [writes=1]     four RuntimeNodes on the default lease, idle: the beat's WAL (D3)
/// Scattered sets are those of the fewest-moves balancer after four joins (<see cref="Balancer.Plan"/>, as
/// <c>sim --placement-check</c> runs it); contiguous sets are the fair ranges. Each row runs <c>runs</c> times from its own
/// setup, which is not counted; the counts are the most common run's (the runs whose counts differ are listed) and the time is the
/// median. Counted as the cost runs count: commands, BEGINs, COMMITs, the TCP round trips through the proxy and, on
/// PostgreSQL, the Execute frames (one per statement). "Rows" are the fence rows the operation wrote, read back from
/// the Partitions table before and after (owner, epoch, lease or position changed).
/// </summary>
static class SetCost
{
    private const int NodeCount = 4;
    private static int Count = VirtualPartitions.Small;      // count=4096 measures the large size
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    public static async Task<int> RunAsync(Target target)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;     // this flow only: 1,234.5 whatever the machine's locale
        var runs = target.Int("runs", 5);
        Count = target.Int("count", VirtualPartitions.Small);
        var counter = new Counting();
        var classes = new Classes();
        var create = target.Workers(counter, counter.Transactions, classes);
        var admin = target.Admins();
        var store = target.Store(create);
        var outside = target.Store(admin);
        var options = new WorkerOptions { Lease = Hour, Partitions = Count };
        var meter = new Meter(target, counter, classes, admin);
        Ranked.Admin = admin;
        var failures = 0;
        var serial = 0;
        string Name(string role) => $"{role}{++serial}";
        IActorType Actors() => target.Actors([new Counter()]);
        async Task Sql(string sql, params object[] values)
        {
            await using var db = admin();
            await db.Database.ExecuteSqlRawAsync(target.Quote(sql), values);
        }
        Task FreeAll() => Sql("""UPDATE "Partitions" SET "Owner" = NULL, "LeaseUntil" = 0""");
        Task AgeNodes() => Sql("""UPDATE "Nodes" SET "SeenAt" = 0""");
        Task Assign(IEnumerable<int> set, string owner) => Sql($"""UPDATE "Partitions" SET "Owner" = {"{0}"}, "LeaseUntil" = 0 WHERE "Id" IN ({string.Join(", ", set)})""", owner);
        void Expect(bool ok, string what)
        {
            if (ok) return;
            failures++;
            Console.WriteLine("    FAILED: " + what);
        }

        foreach (var shape in target.Get("shape", "both") switch { "both" => new[] { "scattered", "contiguous" }, var one => [one] })
        {
            await target.ResetAsync(Count);
            var four = shape == "scattered" ? Scattered(NodeCount) : Contiguous(NodeCount);
            var three = shape == "scattered" ? Scattered(NodeCount - 1) : Contiguous(NodeCount - 1);
            var flaws = shape == "scattered" ? Flaw.None : Flaw.ContiguousOnlyBalancer;
            // Node A: the most fragmented set of the four (all are one range when contiguous).
            var a = four.OrderByDescending(Ranges).First();
            var upper = a.OrderDescending().Take(a.Count / 2).ToHashSet();     // the half a balancer gives up first: the highest
            Console.WriteLine($"\n{target.Engine}: {await target.DescribeAsync()}; fence {(target.FenceInBatch ? "in the batch" : "checked")}; commit {target.CommitWriter}; {Count} virtual partitions, {NodeCount} nodes");
            Console.WriteLine($"shape {shape}: the four sets are {string.Join(", ", four.Select(s => $"{s.Count} in {Ranges(s)} ranges"))}; node A holds {a.Count} in {Ranges(a)} ranges ({Describe(a)})");
            Console.WriteLine($"{"run",-92} {"commands",8} {"begin",5} {"commit",6} {"counted",7} {"tcp",5} {"exec",5} {"rows",5} {"ms",9}");

            async Task<DurableHost> GainAsync(IReadOnlyCollection<int> set, string node, List<IScopeActivation>? activations = null)
            {
                var host = DurableHost.ForNode(node, store, Actors(), Count, options);
                foreach (var p in set)
                    if (await host.ActivateAsync(p) is { } activation) activations?.Add(activation);
                return host;
            }

            // 1. Take of a set.
            await meter.RowAsync($"take: the per-node host gains A's {a.Count}, one activation each (start-up)", runs, FreeAll, async () =>
            {
                var host = await GainAsync(a, Name("a"));
                Expect(host.NodeWorker!.Fences.Count == a.Count, "the node holds A");
            });
            await meter.RowAsync($"take: one set take of A's {a.Count} (a worker of the set, fixed host)", runs, FreeAll, async () =>
            {
                var worker = new Worker(Name("a"), a, store, Actors(), options);
                await new DurableHost([worker]).ActivateAsync(worker.Partition);
                Expect(worker.Fences.Count == a.Count, "the set take got A");
            });

            // PostgreSQL's set take locks every listed row (#4812): here half of the set is held, live, by another node.
            await meter.RowAsync($"take: one set take of A's {a.Count} while a live node holds the upper {upper.Count} (fixed host)", runs, async () =>
            {
                await FreeAll();
                await Sql($"""UPDATE "Partitions" SET "Owner" = 'live', "LeaseUntil" = {long.MaxValue / 2} WHERE "Id" IN ({string.Join(", ", upper)})""");
            }, async () =>
            {
                var worker = new Worker(Name("a"), a, store, Actors(), options);
                await new DurableHost([worker]).ActivateAsync(worker.Partition);
                Expect(worker.Fences.Count == a.Count - upper.Count, "the set take got the free half");
            });

            // 2. Beat, heartbeat and the idle set poll, on a node that holds A.
            await FreeAll();
            var held = await GainAsync(a, Name("a"));
            var node = held.NodeWorker!;
            await node.PollAsync();
            await meter.RowAsync($"beat: the node's worker renews its {a.Count} (an empty fenced commit)", runs, null, () => node.BeatAsync());
            await meter.RowAsync("heartbeat: the membership's node row, live count and free list", runs, null, async () => await store.HeartbeatAsync(node.Node, Hour));
            await meter.RowAsync($"poll: the set poll over {a.Count}, nothing to read", runs, null, () => node.PollAsync());

            // SQL Server (#4812): the set poll inlines each partition's epoch, so a take changes its text; count the cached plans.
            if (target.Engine == "sqlserver")
            {
                const string plans = """SELECT COUNT(*) AS "Value" FROM sys.dm_exec_cached_plans cp CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) t WHERE t.text LIKE '%"Seen" < CASE%' AND t.text NOT LIKE '%dm_exec%'""";
                async Task<int> PlansAsync() { await using var db = admin(); return (await db.Database.SqlQueryRaw<int>(plans).ToListAsync())[0]; }
                var activation = (await held.ActivateAsync(a[0]))!;
                var (cached, clock) = (await PlansAsync(), Stopwatch.StartNew());
                for (var i = 0; i < 20; i++)
                {
                    await activation.DisposeAsync();
                    activation = (await held.ActivateAsync(a[0]))!;      // a new epoch for one partition of the set
                    await node.PollAsync();
                }
                Console.WriteLine($"sql server: 20 rounds of a give-up, a take and a set poll over {a.Count}: cached ad hoc plans of the set poll {cached} before, {await PlansAsync()} after ({clock.Elapsed.TotalMilliseconds / 20:F1} ms a round)");
            }

            // 3. One 100-instance flush, and the same instances again, spread over A by the placement hash.
            var aSet = a.ToHashSet();
            var keep = 0;
            List<(int Vp, InstanceRef Ref)> instances = [];
            List<int> spread = [];
            await meter.StepsAsync(runs, async step =>
            {
                instances = [];
                while (instances.Count < 100)
                {
                    var name = $"w{++keep}";
                    var vp = VirtualPartitions.Of(name, Count);
                    if (aSet.Contains(vp)) instances.Add((vp, InstanceRef.In(vp, name)));
                }
                var k = instances.Select(i => i.Vp).Distinct().Count();
                spread.Add(k);
                foreach (var round in new[] { 1, 2 })
                {
                    foreach (var (_, to) in instances) await outside.AddAsync(new Message($"add-{to.Id}-{round}", to, "add", Json.Write(new Add($"t{round}")), Start: Counter.Spec));
                    var label = round == 1 ? "100 new instances over k of A" : "the same 100 again";
                    await node.WokenAsync(null);
                    await step($"flush, {label}: the set poll that reads them", () => node.PollAsync());
                    await step($"flush, {label}: the load", () => node.LoadAsync());
                    // Then flushes until nothing is left, each counted on its own: a message read for an instance held in
                    // memory is marked seen by one flush, and its turn's commit is the next.
                    for (var flush = 1; flush <= 4; flush++)
                    {
                        foreach (var id in node.Ready.Order(StringComparer.Ordinal).ToList()) node.RunTurn(id);
                        if (!node.NeedsFlush) break;
                        await step($"flush, {label}: flush {flush}", async () => await node.FlushAsync());
                    }
                }
            });
            Console.WriteLine($"    k, the distinct partitions of each run's 100 instances: {string.Join(" ", spread)}");
            await using (var db = admin())
            {
                var ids = instances.Select(i => i.Ref.Id).ToList();
                var rows = await db.Instances.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync();
                Expect(rows.Count == 100 && rows.All(r => Json.Read<CounterState>(r.State).Tokens.Count == 2), "the last run's 100 instances hold their two tokens");
                Expect(!await db.Messages.AnyAsync(m => !m.Done), "no message is left");
            }

            // 4. Takeover of a dead node's set: its rows are expired by hand (uncounted), as the experiment expired them.
            await meter.RowAsync($"takeover: one set take of a dead node's {a.Count} (fixed host)", runs, async () => { await FreeAll(); await Assign(a, "dead"); }, async () =>
            {
                var worker = new Worker(Name("c"), a, store, Actors(), options);
                await new DurableHost([worker]).ActivateAsync(worker.Partition);
                Expect(worker.Fences.Count == a.Count, "the takeover got the dead node's set");
            });
            await meter.RowAsync($"takeover: the per-node host gains a dead node's {a.Count}, one activation each", runs, async () => { await FreeAll(); await Assign(a, "dead"); }, async () =>
            {
                var host = await GainAsync(a, Name("c"));
                Expect(host.NodeWorker!.Fences.Count == a.Count, "the takeover got the dead node's set");
            });

            // 5. Release of a subset: the upper half of A, as the balancer gives it up.
            List<IScopeActivation> activations = [];
            DurableHost? releasing = null;
            await meter.RowAsync($"release: the per-node host gives up {upper.Count} of {a.Count}, one scope each", runs, async () =>
            {
                await FreeAll();
                activations = [];
                releasing = await GainAsync(a, Name("r"), activations);
            }, async () =>
            {
                foreach (var activation in activations.Where(x => upper.Contains(x.Scope))) await activation.DisposeAsync();
                Expect(releasing!.NodeWorker!.Fences.Count == a.Count - upper.Count, "the node kept the lower half");
            });
            IReadOnlyList<PartitionFence> leaving = [];
            await meter.RowAsync($"release: one set release of {upper.Count} (the store's, as a stand-down sends it)", runs, async () =>
            {
                await FreeAll();
                var host = await GainAsync(a, Name("r"));
                leaving = [.. host.NodeWorker!.Fences.Where(f => upper.Contains(f.Partition))];
            }, async () => await store.ReleaseAsync(leaving));

            // 6. A reset after a refused fence: a thief bumps one row; the next beat is refused and the worker retakes the rest.
            Worker? reset = null;
            await meter.RowAsync($"reset: a beat refused on 1 of {a.Count}, then the retake of the rest, one each", runs, async () =>
            {
                await FreeAll();
                reset = (await GainAsync(a, Name("t"))).NodeWorker!;
                await Sql($"""UPDATE "Partitions" SET "Owner" = 'thief', "Epoch" = "Epoch" + 1 WHERE "Id" = {a[0]}""");
            }, async () =>
            {
                await reset!.BeatAsync();
                Expect(reset.Fences.Count == a.Count - 1 && !reset.Holds(a[0]), "the reset kept the rest at new epochs");
            });

            // 7. Rebalance on join and on leave, through the built-in runtime's own rounds (PartitionRuntime.BalanceAsync).
            async Task<List<Member>> Cluster(List<int>[] sets, string prefix)
            {
                await FreeAll();
                await AgeNodes();
                var members = new List<Member>();
                for (var i = 0; i < sets.Length; i++) members.Add(await Member.StartAsync($"{prefix}-n{i}", store, Actors(), options, flaws));
                for (var i = 0; i < sets.Length; i++)
                    foreach (var p in sets[i]) await members[i].Runtime.ActivateAsync(p);
                foreach (var m in members) await store.HeartbeatAsync(m.Name, Hour);
                return members;
            }
            List<Member> live = [];
            Member? joiner = null, leaver = null;
            string?[] before = [];
            await meter.RowAsync("rebalance: a fourth node joins, the four run rounds until none changes", runs, async () =>
            {
                live = await Cluster(three, Name("j"));
                before = Owners(live);
            }, async () =>
            {
                joiner = await Member.StartAsync(live[0].Name[..^1] + "3", store, Actors(), options, flaws);
                await joiner.Runtime.BalanceAsync();      // its first round: its heartbeat, and nothing free to ask for
                live.Add(joiner);
                meter.Note(await RoundsAsync(live, before));
            });
            Expect(live.All(m => m.Held == Count / NodeCount), "the join reached the fair share");
            await meter.RowAsync("rebalance: a node leaves gracefully, its stop (one scope at a time)", runs, async () =>
            {
                live = await Cluster(four, Name("l"));
                before = Owners(live);
                leaver = live[1];
            }, async () =>
            {
                await leaver!.Runtime.StopAsync();
                Expect(leaver.Held == 0, "the leaver gave everything up");
            });
            await meter.RowAsync("rebalance: then the other three run rounds until none changes", runs, async () =>
            {
                live = await Cluster(four, Name("l"));
                before = Owners(live);
                await live[1].Runtime.StopAsync();
                await Sql("""UPDATE "Nodes" SET "SeenAt" = 0 WHERE "Id" = {0}""", live[1].Name);
                live.RemoveAt(1);
            }, async () => meter.Note(await RoundsAsync(live, before)));
            Expect(live.Sum(m => m.Held) == Count, "the leave left nothing unheld");
            await meter.RowAsync("takeover through the runtime: a node dies, the other three run rounds", runs, async () =>
            {
                live = await Cluster(four, Name("d"));
                before = Owners(live);
                await Sql("""UPDATE "Partitions" SET "LeaseUntil" = 0 WHERE "Owner" = {0}""", live[1].Name);
                await Sql("""UPDATE "Nodes" SET "SeenAt" = 0 WHERE "Id" = {0}""", live[1].Name);
                live.RemoveAt(1);      // dead: its worker is never called again
            }, async () => meter.Note(await RoundsAsync(live, before)));
            Expect(live.Sum(m => m.Held) == Count, "the takeover left nothing unheld");
        }
        Console.WriteLine(failures == 0 ? "\nevery check passed" : $"\n{failures} checks FAILED");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Rounds of every live node in name order until a round changes no owner. Returns what moved and how.</summary>
    private static async Task<string> RoundsAsync(List<Member> live, string?[] before)
    {
        var rounds = 0;
        for (var changed = true; changed && rounds < 32; rounds++)
        {
            var start = Owners(live);
            foreach (var m in live.OrderBy(m => m.Name, StringComparer.Ordinal)) await m.Runtime.BalanceAsync();
            changed = !Owners(live).SequenceEqual(start);
        }
        var after = Owners(live);
        var moved = Enumerable.Range(0, Count).Count(p => before[p] is not null && after[p] != before[p]);
        return $"{rounds} rounds (the last changes nothing), {moved} partitions moved, held [{string.Join(" ", live.Select(m => m.Held))}]";
    }

    private static string?[] Owners(List<Member> live) => [.. Enumerable.Range(0, Count).Select(p => live.FirstOrDefault(m => m.Runtime.Owns(p))?.Name)];

    /// <summary>A node of the built-in runtime driven by hand: its host (one worker for its set) and its runtime's rounds.</summary>
    private sealed class Member
    {
        public required string Name { get; init; }
        public required PartitionRuntime Runtime { get; init; }
        public int Held => Enumerable.Range(0, Count).Count(Runtime.Owns);

        public static async Task<Member> StartAsync(string name, EfStore store, IActorType actors, WorkerOptions options, Flaw flaws)
        {
            var runtime = new PartitionRuntime(name, new Ranked(store), Count, Hour, flaws: flaws);
            await runtime.StartAsync(DurableHost.ForNode(name, store, actors, Count, options));
            return new Member { Name = name, Runtime = runtime };
        }
    }

    /// <summary>
    /// The store's membership with each node's rank in name order among the nodes heard from, which only
    /// <see cref="Flaw.ContiguousOnlyBalancer"/> reads (the in-memory membership gives it; the EF store does not). The
    /// rank's read is the admin's, not counted.
    /// </summary>
    private sealed class Ranked(EfStore store) : IMembership
    {
        public static Func<KernelDb>? Admin;

        public async ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default)
        {
            var cluster = await store.HeartbeatAsync(node, window, cancellationToken);
            if (Admin is null) return cluster;
            await using var db = Admin();
            var names = await db.Nodes.AsNoTracking().Where(n => n.SeenAt >= cluster.Now - (long)window.TotalMilliseconds).Select(n => n.Id).ToListAsync(cancellationToken);
            return cluster with { Rank = names.Order(StringComparer.Ordinal).ToList().IndexOf(node) };
        }
    }

    /// <summary>The fewest-moves balancer's sets after <paramref name="nodes"/> joins, one at a time, as the placement witness runs them.</summary>
    public static List<int>[] Scattered(int nodes)
    {
        var owner = new string?[Count];
        var names = new List<string>();
        for (var i = 0; i < nodes; i++)
        {
            names.Add("n" + i);
            for (var round = 0; round < 16; round++)
                for (var rank = 0; rank < names.Count; rank++)
                {
                    var node = names[rank];
                    var free = Enumerable.Range(0, Count).Where(p => owner[p] is null).ToList();
                    var (release, take) = Balancer.Plan(Count, new Cluster(names.Count, free, 0, rank), Enumerable.Range(0, Count).Where(p => owner[p] == node).ToList(), []);
                    foreach (var p in release) owner[p] = null;
                    foreach (var p in take) owner[p] ??= node;
                }
        }
        return [.. names.Select(n => Enumerable.Range(0, Count).Where(p => owner[p] == n).ToList())];
    }

    public static List<int>[] Contiguous(int nodes) => [.. VirtualPartitions.FairRanges(Count, nodes).Select(r => Enumerable.Range(r.Lo, r.Count).ToList())];

    private static int Ranges(IReadOnlyList<int> set) => set.Where((p, i) => i == 0 || set[i - 1] != p - 1).Count();

    private static string Describe(IReadOnlyList<int> set)
    {
        var runs = new List<string>();
        for (var i = 0; i < set.Count;)
        {
            var j = i;
            while (j + 1 < set.Count && set[j + 1] == set[j] + 1) j++;
            runs.Add(set[i] == set[j] ? $"{set[i]}" : $"{set[i]}-{set[j]}");
            i = j + 1;
        }
        return string.Join(", ", runs);
    }

    /// <summary>Counts and prints one row: the counters around the body, the fence rows it wrote, the median time.</summary>
    private sealed class Meter(Target target, Counting counter, Classes classes, Func<KernelDb> admin)
    {
        private string? _note;

        public void Note(string note) => _note = note;

        private sealed record Sample(long Commands, long Begun, long Ended, long Counted, string Tcp, string Exec, int Rows, double Ms, string Classes, string? Wal)
        {
            public string Key => $"{Commands} {Begun} {Ended} {Tcp} {Exec} {Rows}";
        }

        private async Task<Dictionary<int, (string?, long, long, long)>> FencesAsync()
        {
            await using var db = admin();
            return await db.Partitions.AsNoTracking().ToDictionaryAsync(p => p.Id, p => (p.Owner, p.Epoch, p.LeaseUntil, p.Position));
        }

        private async Task<Sample> MeasureAsync(Func<Task> body, bool wal)
        {
            var before = await FencesAsync();
            var walFrom = wal && target.Writes is { } w ? await w.TakeAsync() : null;
            counter.Reset();
            classes.Reset();
            var (trips, executes) = (target.Proxy?.Trips ?? 0, target.Proxy?.Executes ?? 0);
            var clock = Stopwatch.StartNew();
            await body();
            clock.Stop();
            var (commands, begun, ended) = (counter.Commands, counter.Transactions.Begun, counter.Transactions.Ended);
            var (tcp, exec) = target.Proxy is { } proxy ? ((proxy.Trips - trips).ToString(CultureInfo.InvariantCulture), target.Engine == "postgres" ? (proxy.Executes - executes).ToString(CultureInfo.InvariantCulture) : "") : ("", "");
            var by = classes.Describe();
            string? walLine = null;
            if (walFrom is not null)
            {
                var (parts, kinds) = await target.Writes!.SplitAsync(walFrom, await target.Writes.TakeAsync());
                walLine = "    WAL by table and record type: " + string.Join("; ", kinds.GroupBy(k => k.Table).Select(g => $"{g.Key} " + string.Join(", ", g.Select(k => $"{k.Kind} {k.Records} x {k.Bytes:N0} B")))) + $"; aborted {parts.Sum(p => p.Aborted):N0} B";
            }
            var after = await FencesAsync();
            var rows = after.Count(p => !before.TryGetValue(p.Key, out var was) || was != p.Value);
            var counted = commands + ended + (target.Engine == "postgres" ? 0 : begun);
            return new(commands, begun, ended, counted, tcp, exec, rows, clock.Elapsed.TotalMilliseconds, by, walLine);
        }

        private void Print(string name, List<Sample> samples, string? note)
        {
            // The counts shown are the most common run's (the first run of a row may open connections: their handshakes count).
            var s = samples.GroupBy(x => x.Key).OrderByDescending(g => g.Count()).First().First();
            var median = samples.Select(x => x.Ms).Order().ElementAt(samples.Count / 2);
            Console.WriteLine($"{name,-92} {s.Commands,8} {s.Begun,5} {s.Ended,6} {s.Counted,7} {s.Tcp,5} {s.Exec,5} {s.Rows,5} {median,9:F1}");
            var keys = samples.Select(x => x.Key).Distinct().ToList();
            if (keys.Count > 1) Console.WriteLine("    the runs' counts differ (commands begin commit tcp exec rows): " + string.Join(" | ", samples.Select(x => x.Key)));
            Console.WriteLine($"    statements by kind: {s.Classes}; ms per run: {string.Join(" ", samples.Select(x => x.Ms.ToString("F1", CultureInfo.InvariantCulture)))}");
            if (note is not null) Console.WriteLine("    " + note);
            if (s.Wal is not null) Console.WriteLine(s.Wal);
        }

        /// <summary>One row, <paramref name="runs"/> times, each after its own setup (not counted).</summary>
        public async Task RowAsync(string name, int runs, Func<Task>? setup, Func<Task> body)
        {
            var samples = new List<Sample>();
            string? note = null;
            for (var run = 0; run < runs; run++)
            {
                if (setup is not null) await setup();
                _note = null;
                samples.Add(await MeasureAsync(body, wal: run == 0));
                note ??= _note;
            }
            Print(name, samples, note);
        }

        /// <summary>Rows that are steps of one sequence, run <paramref name="runs"/> times: each step is counted on its own.</summary>
        public async Task StepsAsync(int runs, Func<Func<string, Func<Task>, Task>, Task> sequence)
        {
            var rows = new List<(string Name, List<Sample> Samples)>();
            for (var run = 0; run < runs; run++)
            {
                var first = run == 0;
                await sequence(async (name, body) =>
                {
                    var sample = await MeasureAsync(body, wal: first);
                    if (rows.FirstOrDefault(r => r.Name == name) is { Name: not null } row) row.Samples.Add(sample);
                    else rows.Add((name, [sample]));
                });
            }
            foreach (var (name, samples) in rows) Print(name, samples, null);
        }
    }

    // ---- D3: the beat's WAL over an idle period ------------------------------------------------------------------

    /// <summary>
    /// Four nodes of the built-in runtime (<see cref="RuntimeNode"/>, default loop and lease) on one PostgreSQL database
    /// join one after another and settle at 256 each; then nothing happens for <c>seconds</c>. Counted over the idle
    /// window: the statements by kind, and with writes=1 the WAL by table and record type, tuple updates (HOT or not),
    /// dead tuples and vacuums of the tables.
    /// </summary>
    public static async Task<int> IdleAsync(Target target)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var seconds = target.Int("seconds", 120);
        Count = target.Int("count", VirtualPartitions.Small);
        var counter = new Counting();
        var classes = new Classes();
        var create = target.Workers(counter, counter.Transactions, classes);
        await target.ResetAsync(Count);
        var store = target.Store(create);
        var options = new WorkerOptions { Partitions = Count };      // the default lease: 10 s, a beat a third of it after the last renewal
        var nodes = new List<RuntimeNode>();
        Console.WriteLine($"\n{target.Engine}: {await target.DescribeAsync()}; {Count} virtual partitions, {NodeCount} RuntimeNodes, lease {options.Lease.TotalSeconds} s, loop defaults (balance and poll every second, window 3 s)");
        for (var i = 0; i < NodeCount; i++)
        {
            var node = new RuntimeNode($"idle-n{i}", store, Count);
            await node.StartAsync(DurableHost.ForNode(node.Node, store, target.Actors([new Counter()]), Count, options));
            nodes.Add(node);
            var share = (Count + i) / (i + 1);      // rounded up, as the balancer's share
            var settled = await Harnessless(() => nodes.Sum(Held) == Count && nodes.All(n => Held(n) <= share), TimeSpan.FromSeconds(120));
            Console.WriteLine($"  {node.Node} joined: held [{string.Join(" ", nodes.Select(Held))}]{(settled ? "" : " (NOT SETTLED)")}");
            if (!settled) return 1;
        }
        await Task.Delay(TimeSpan.FromSeconds(15));      // the joins' last passes and beats
        var writes = target.Writes;
        var from = writes is null ? null : await writes.TakeAsync();
        counter.Reset();
        classes.Reset();
        var clock = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        var elapsed = clock.Elapsed.TotalSeconds;
        var (commands, ended) = (counter.Commands, counter.Transactions.Ended);
        var by = classes.Describe(elapsed);
        var to = writes is null ? null : await writes.TakeAsync();
        Console.WriteLine($"  idle {elapsed:F1} s, held [{string.Join(" ", nodes.Select(Held))}], loop errors {nodes.Sum(n => n.Errors)}: {commands / elapsed:F1} statements and {ended / elapsed:F2} COMMITs a second");
        Console.WriteLine($"  statements a second by kind: {by}");
        if (writes is not null && from is not null && to is not null)
        {
            var (parts, kinds) = await writes.SplitAsync(from, to);
            var wal = PgWrites.Bytes(to.Lsn) - PgWrites.Bytes(from.Lsn);
            Console.WriteLine($"  WAL {wal:N0} bytes ({wal / elapsed:N0} a second, {wal / elapsed * 3600 / 1e6:N1} MB, {wal / elapsed * 3600 / 1024 / 1024:N1} MiB an hour), {to.WalRecords - from.WalRecords:N0} records, {to.WalFpi - from.WalFpi:N0} full-page images");
            foreach (var p in parts.Where(p => p.Bytes > 0)) Console.WriteLine($"    {p.Table,-12} {p.Part,-6} {p.Records,8:N0} records {p.Bytes,12:N0} B ({p.Bytes / elapsed,9:N0} B/s), first-block FPI {p.Fpi:N0} B");
            foreach (var group in kinds.GroupBy(k => k.Table)) Console.WriteLine($"    {group.Key,-12} " + string.Join(", ", group.Select(k => $"{k.Kind} {k.Records:N0} x {k.Bytes:N0} B")));
            Console.WriteLine($"  {"table",-11} {"upd",8} {"hot",8} {"dead before",11} {"dead after",10} {"autovac",7} {"heap B",9}");
            foreach (var name in new[] { "Partitions", "Nodes", "Messages", "Instances" })
                if (from.Tables.TryGetValue(name, out var b) && to.Tables.TryGetValue(name, out var t))
                    Console.WriteLine($"  {name,-11} {t.Upd - b.Upd,8:N0} {t.Hot - b.Hot,8:N0} {b.Dead,11:N0} {t.Dead,10:N0} {t.AutoVacuums - b.AutoVacuums,7} {t.Heap,9:N0}");
        }
        foreach (var node in nodes) await node.StopAsync();
        return 0;

        static int Held(RuntimeNode n) => Enumerable.Range(0, Count).Count(n.Runtime.Owns);
    }

    private static async Task<bool> Harnessless(Func<bool> condition, TimeSpan patience)
    {
        var until = DateTime.UtcNow + patience;
        while (DateTime.UtcNow < until)
        {
            if (condition()) return true;
            await Task.Delay(100);
        }
        return condition();
    }

    /// <summary>Counts the worker's statements by what they are, from their text.</summary>
    private sealed class Classes : DbCommandInterceptor
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _counts = new();

        public void Reset() => _counts.Clear();

        public string Describe(double per = 1) => _counts.IsEmpty ? "none" : string.Join(", ", _counts.OrderByDescending(c => c.Value).Select(c => per == 1 ? $"{c.Key} {c.Value}" : $"{c.Key} {c.Value / per:F2}"));

        private void Count(string text)
        {
            // One command may hold several statements (the owned commit's batch, MySQL's take): it is counted by its first kind.
            var kind = text switch
            {
                _ when text.Contains("\"Nodes\"") || text.Contains("`Nodes`") => "heartbeat",
                _ when text.Contains("\"Seen\" <") || text.Contains("`Seen` <") => "poll",
                _ when text.Contains("\"Attempts\" + 1") || text.Contains("`Attempts` + 1") => "death count",
                _ when text.Contains("SET \"Owner\" = NULL") || text.Contains("SET `Owner` = NULL") => "release",
                _ when (text.Contains("\"Owner\" =") || text.Contains("`Owner` =")) && (text.Contains("\"Epoch\" + 1") || text.Contains("`Epoch` + 1")) && (text.Contains("\"LeaseUntil\" <") || text.Contains("`LeaseUntil` <")) => "take",
                _ when text.Contains("\"Epoch\" + 1") || text.Contains("`Epoch` + 1") => "retake",
                _ when text.Contains("UPDATE \"Partitions\" AS p") || text.Contains("UPDATE `Partitions`") || text.Contains("UPDATE \"Partitions\" SET \"Position\"") => "commit (fenced)",
                _ when text.Contains("\"Partitions\"") || text.Contains("`Partitions`") => "partitions (other)",
                _ when text.Contains("FROM \"Instances\"") || text.Contains("FROM `Instances`") => "load",
                _ => "other",
            };
            _counts.AddOrUpdate(kind, 1, (_, n) => n + 1);
        }

        public override InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result) { Count(command.CommandText); return result; }
        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default) { Count(command.CommandText); return new(result); }
        public override InterceptionResult<int> NonQueryExecuting(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result) { Count(command.CommandText); return result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { Count(command.CommandText); return new(result); }
        public override InterceptionResult<object> ScalarExecuting(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<object> result) { Count(command.CommandText); return result; }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) { Count(command.CommandText); return new(result); }
    }
}
