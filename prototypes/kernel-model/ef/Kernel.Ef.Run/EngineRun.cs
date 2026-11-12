using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using Kernel;
using Kernel.EntityFramework;
using Kernel.Sim;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySql.Data.MySqlClient;
using Npgsql;

/// <summary>
/// The runs of ENGINES.md: the stress test, the cost count and the probes of Program.cs, on any of the four engines.
///   x stress|cost|probe|block|definite|deadlock postgres|sqlserver|mysql|sqlite "connection" [key=value ...]
/// Options: fence=checked|batch  unfenced=1  seconds=40  messages=5000  partitions=2  nodes=3
///          kill=0 (no sessions killed)   rcsi=0 (locking; default on) readpast=1 xactabort=1 (SQL Server)   isolation=rc (MySQL)   wal=0|1 busy=ms timeout=s (SQLite)
///          role=workers name=b signal=path (a second process on the same database)   locks=1 (cost)
///          savepoints=1 (leave Entity Framework's savepoint on, SQL Server and MySQL)   batchsize=n (statements per command, SQL Server and MySQL; default 1000)
///          many=100,1000 (cost: the sizes of the large flushes)   proxy=1 wire=1 (cost: count round trips on the TCP stream; MySQL: list what the server ran)
/// </summary>
static class EngineRun
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 3) return 2;
        var target = new Target(args[1], args[2], args.Skip(3).Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "1"));
        return args[0] switch
        {
            "stress" => await Stress(target),
            "cost" => await Cost(target),
            "probe" => await Probe(target),
            "block" => await Block(target),
            "definite" => await Definite(target),
            "deadlock" => await Deadlock(target),
            _ => 2,
        };
    }

    private static IExecutionModel[] Models() =>
        [new Counter(), new Parent(), new Child(), new Waiter(), new Sequence(), new Correlation(), new CWaiter(2), new Runner((_, _) => new ValueTask<object?>((object?)null)), new Fragile(() => { })];

    private static async Task Drain(Worker worker, bool poll)
    {
        if (poll) await worker.PollAsync();
        for (var idle = 0; idle < 2;)
        {
            var busy = worker.NeedsLoad || worker.NeedsMeet || worker.NeedsFlush || worker.Ready.Any();
            await worker.LoadAsync();
            await worker.MeetAsync();
            foreach (var id in worker.Ready.ToList()) worker.Run(id);
            await worker.FlushAsync();
            idle = busy ? 0 : idle + 1;
        }
    }

    // ---- several owners of the same partitions, takeovers and killed sessions ---------------------------------------

    private static async Task<int> Stress(Target target)
    {
        int partitions = target.Int("partitions", 2), nodes = target.Int("nodes", 3), seconds = target.Int("seconds", 40), messages = target.Int("messages", 5000);
        var (workersOnly, name, signal) = (target.Get("role", "all") == "workers", target.Get("name", "node"), target.Get("signal", ""));
        var (create, admin) = (target.Workers(), target.Admins());
        if (!workersOnly)
        {
            if (signal != "") { File.Delete(signal + ".calm"); File.Delete(signal + ".stop"); }
            await target.ResetAsync(partitions);
        }
        var ef = target.Store(create);
        var tally = new Tally(ef);
        IKernelStore store = tally;
        Console.WriteLine($"{target.Engine}: {await target.DescribeAsync()}; fence {(target.Has("unfenced") ? "BROKEN ON PURPOSE" : target.FenceInBatch ? "in the batch" : "checked")}; {nodes} nodes, {partitions} partitions{(workersOnly ? "; workers only, as " + name : "")}");

        var (calm, stopped) = (false, false);
        bool Calm() => calm || (signal != "" && File.Exists(signal + ".calm"));
        bool Stopped() => stopped || (signal != "" && File.Exists(signal + ".stop"));
        long steps = 0, kills = 0, stalls = 0;
        var options = new WorkerOptions
        {
            Lease = TimeSpan.FromMilliseconds(target.Int("lease", 150)), Horizon = TimeSpan.FromSeconds(2), PollLimit = 40, MaxBatch = 40, MaxAttempts = 1_000_000,
            OnStep = (_, _, _) => Interlocked.Increment(ref steps),
        };

        var loops = Enumerable.Range(0, nodes).SelectMany(node => Enumerable.Range(0, partitions).Select(partition => Task.Run(async () =>
        {
            var (random, worker) = (new Random(node * 31 + partition + name.GetHashCode()), new Worker(name + node, partition, store, Models(), options));
            while (!Stopped())
            {
                if (!worker.Owns)
                {
                    await worker.TakeAsync();
                    if (!worker.Owns) await Task.Delay(20);
                    continue;
                }
                await worker.PollAsync();
                await worker.LoadAsync();
                await worker.MeetAsync();
                foreach (var id in worker.Ready.ToList()) worker.Run(id);
                if (worker.NeedsFlush) await worker.FlushAsync();
                else await worker.BeatAsync();
                if (!Calm() && random.Next(25) == 0)
                {
                    Interlocked.Increment(ref stalls);
                    await Task.Delay(random.Next(150, 500));
                }
                else await Task.Delay(2);
            }
        }))).ToList();

        if (workersOnly)
        {
            await Task.WhenAll(loops);
            Console.WriteLine($"{name}: {stalls} stalls, {steps} steps run");
            tally.Print();
            return 0;
        }

        var deadlocksBefore = await target.DeadlocksAsync(admin);
        var chaos = Task.Run(async () =>
        {
            var random = new Random(7);
            while (!calm)
            {
                await Task.Delay(random.Next(20, 60));
                try { if (target.Get("kill", "1") == "1") kills += await target.KillAsync(admin); }
                catch (Exception) { }
            }
        });

        var outside = target.Store(admin);
        var keep = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000;
        var sent = new ConcurrentDictionary<string, List<string>>();
        var (parents, waiters, sequences) = (new List<string>(), new List<string>(), new List<(string Id, int Count)>());
        var (addRetries, addFailures) = (0, new ConcurrentDictionary<string, int>());
        var producer = Task.Run(async () =>
        {
            var random = new Random(11);
            async Task Add(Message message)
            {
                for (;;)
                    try { await outside.AddAsync(message with { KeepUntil = keep }); return; }
                    catch (Exception e) { addRetries++; addFailures.AddOrUpdate(Tally.Code(e), 1, (_, n) => n + 1); await Task.Delay(5); }
            }
            for (var n = 0; n < messages; n++)
            {
                var p = random.Next(partitions);
                switch (random.Next(10))
                {
                    case < 6:
                        var to = InstanceRef.In(p, "c" + random.Next(3));
                        var forward = random.Next(4) == 0 ? InstanceRef.In(random.Next(partitions), "c" + random.Next(3)).Id : null;
                        sent.GetOrAdd(to.Id, _ => []).Add("t" + n);
                        await Add(new Message("x" + n, to, "add", Json.Write(new Add("t" + n, forward)), Start: Counter.Spec));
                        break;
                    case 6:
                        parents.Add(InstanceRef.In(p, "par" + n).Id);
                        await Add(new Message("x" + n, InstanceRef.In(p, "par" + n), Kinds.Start, Start: new StartSpec("parent", "p", Json.Write(new ParentInput(random.Next(1, 4), random.Next(300))))));
                        break;
                    case 7:
                        waiters.Add(InstanceRef.In(p, "w" + n).Id);
                        await Add(new Message("x" + n, InstanceRef.In(p, "w" + n), Kinds.Start, Start: new StartSpec("waiter", "w", Json.Write(new List<string> { "k" + n }))));
                        for (;;)
                            try { await Events.PublishAsync(outside, new EventRow("k" + n, "e" + n, "e" + n, keep)); break; }
                            catch (Exception e) { addRetries++; addFailures.AddOrUpdate(Tally.Code(e), 1, (_, c) => c + 1); await Task.Delay(5); }
                        break;
                    default:
                        var count = random.Next(1, 6);
                        sequences.Add((InstanceRef.In(p, "s" + n).Id, count));
                        await Add(new Message("x" + n, InstanceRef.In(p, "s" + n), Kinds.Start, Start: Sequence.Spec(count, random.Next(2) == 0 ? Sequence.Balanced : Sequence.Durable)));
                        break;
                }
                await Task.Delay(random.Next(0, 2 * seconds * 1000 / messages));
            }
        });

        var clock = Stopwatch.StartNew();
        await Task.Delay(seconds * 1000);
        await producer;
        calm = true;
        if (signal != "") File.WriteAllText(signal + ".calm", "");
        await chaos;
        Console.WriteLine($"chaos over after {clock.Elapsed.TotalSeconds:F0} s: {kills} sessions killed, {stalls} stalls, {steps} steps so far; letting it settle");
        var patience = DateTime.UtcNow.AddSeconds(target.Int("patience", 180));
        for (var left = 1; left > 0 && DateTime.UtcNow < patience; await Task.Delay(500))
            try
            {
                await using var db = admin();
                left = await db.Messages.CountAsync(m => !m.Done) + await db.Subscriptions.CountAsync();
            }
            catch (Exception) { }
        Console.WriteLine($"settled after {clock.Elapsed.TotalSeconds:F0} s");
        stopped = true;
        if (signal != "") File.WriteAllText(signal + ".stop", "");
        await Task.WhenAll(loops);
        if (signal != "") await Task.Delay(1500);   // the other process stops its loops

        var wrong = new List<string>();
        await using var check = admin();
        var rows = await check.Instances.AsNoTracking().ToDictionaryAsync(i => i.Id);
        var journal = await check.Journal.AsNoTracking().Select(j => new { j.Partition, j.Position, j.Instance, j.Revision }).ToListAsync();
        var takeovers = new List<long>();
        foreach (var partition in await check.Partitions.AsNoTracking().OrderBy(p => p.Id).ToListAsync())
        {
            var own = journal.Where(j => j.Partition == partition.Id).ToList();
            takeovers.Add(partition.Epoch - 1);
            Console.WriteLine($"partition {partition.Id}: epoch {partition.Epoch} (so {partition.Epoch - 1} takeovers), position {partition.Position}, {own.Count} journal rows");
            if (own.Count != partition.Position || (own.Count > 0 && own.Max(j => j.Position) != partition.Position)) wrong.Add($"I6 partition {partition.Id}: journal rows {own.Count}, position {partition.Position}");
        }
        foreach (var message in await check.Messages.AsNoTracking().Where(m => !m.Done).Take(5).ToListAsync()) wrong.Add($"END message {message.Id} ({message.Kind} to {message.Target}) left behind");
        if (await check.Subscriptions.AnyAsync()) wrong.Add("END a subscription is left behind");
        foreach (var group in journal.Where(j => j.Revision > 0).GroupBy(j => j.Instance))
            if (!rows.TryGetValue(group.Key, out var row) || row.Revision != group.Count() || group.Max(j => j.Revision) != row.Revision) wrong.Add($"I2 {group.Key}: row revision {rows.GetValueOrDefault(group.Key)?.Revision}, journal commits {group.Count()}");
        var counters = rows.Values.Where(r => r.Model == "counter").ToDictionary(r => r.Id, r => Json.Read<CounterState>(r.State));
        var expected = sent.ToDictionary(s => s.Key, s => s.Value.ToList());
        foreach (var forward in counters.Values.SelectMany(c => c.Forwarded)) (expected.TryGetValue(forward.Target, out var list) ? list : expected[forward.Target] = []).Add(forward.Token);
        foreach (var (id, tokens) in expected)
        {
            var applied = counters.GetValueOrDefault(id)?.Tokens ?? [];
            if (applied.Count != applied.Distinct().Count()) wrong.Add($"I4 {id} applied a message twice");
            if (!applied.Order().SequenceEqual(tokens.Order())) wrong.Add($"I3/I4 {id}: {applied.Count} applied, {tokens.Count} sent; missing [{string.Join(", ", tokens.Except(applied).Take(5))}], extra [{string.Join(", ", applied.Except(tokens).Take(5))}]");
            var outsideOrder = sent.GetValueOrDefault(id) ?? [];
            if (!applied.Where(outsideOrder.Contains).SequenceEqual(outsideOrder.Where(applied.Contains))) wrong.Add($"I11 {id} applied outside messages out of order");
        }
        foreach (var (sender, state) in counters)
            foreach (var group in state.Forwarded.GroupBy(f => f.Target))
            {
                var (order, applied) = (group.Select(f => f.Token).ToList(), counters.GetValueOrDefault(group.Key)?.Tokens ?? []);
                if (!applied.Where(order.Contains).SequenceEqual(order.Where(applied.Contains))) wrong.Add($"I11 {group.Key} applied the messages of {sender} out of order");
            }
        foreach (var id in parents.Concat(waiters).Concat(sequences.Select(s => s.Id)))
            if (rows.GetValueOrDefault(id)?.Status != InstanceStatus.Finished) wrong.Add($"END {id} ended as {rows.GetValueOrDefault(id)?.Status.ToString() ?? "no row"}");
        foreach (var id in waiters)
            if (rows.TryGetValue(id, out var row) && Json.Read<WaiterState>(row.State).Got.Count != 1) wrong.Add($"I13 {id} was given {Json.Read<WaiterState>(row.State).Got.Count} events");
        foreach (var (id, count) in sequences)
            if (rows.TryGetValue(id, out var row) && !Json.Read<SequenceState>(row.State).Done.SequenceEqual(Enumerable.Range(1, count))) wrong.Add($"I4 {id} did [{string.Join(", ", Json.Read<SequenceState>(row.State).Done)}] of {count}");
        foreach (var row in rows.Values.Where(r => r.Status == InstanceStatus.Faulted).Take(5)) wrong.Add($"I14 {row.Id} was parked: {row.Fault}");

        Console.WriteLine($"{messages} outside messages, {rows.Count} instances, {steps} steps run, {journal.Count} commits of instances, {expected.Sum(e => e.Value.Count)} counter tokens checked");
        Console.WriteLine($"producer: {addRetries} retried inserts {string.Join(", ", addFailures.Select(f => $"{f.Key} x{f.Value}"))}");
        tally.Print();
        if (target.FenceInBatch) Console.WriteLine($"E1: {ef.KeyViolationsAsFence} key violations were the fence (counted in 'commit fenced')");
        Console.WriteLine($"deadlocks: the engine counted {await target.DeadlocksAsync(admin) - deadlocksBefore}, the store saw {tally.Sum(target.DeadlockCode)} (commit {tally.Sum("commit", target.DeadlockCode)}, poll {tally.Sum("poll", target.DeadlockCode)}, other {tally.Sum(target.DeadlockCode) - tally.Sum("commit", target.DeadlockCode) - tally.Sum("poll", target.DeadlockCode)})");
        Console.WriteLine($"SUMMARY | {target.Engine} | {await target.DescribeAsync()} | {(target.Has("unfenced") ? "broken" : target.FenceInBatch ? "batch" : "checked")} | {seconds} s, {messages} | kills {kills} | stalls {stalls} | takeovers {string.Join(", ", takeovers)} | steps {steps} | commits {journal.Count} | deadlocks {tally.Sum(target.DeadlockCode)} | retries {tally.Sum("commit", "Transient")} | resets {tally.Sum("commit", "fenced") + tally.Sum("commit", "Unknown")} (fenced {tally.Sum("commit", "fenced")}, unknown {tally.Sum("commit", "Unknown")}) | definite {tally.Sum("commit", "Definite")} | {(wrong.Count == 0 ? "holds" : wrong.Count + " violations")}");
        Console.WriteLine(wrong.Count == 0 ? "end state: all properties hold" : $"end state: {wrong.Count} violations\n  " + string.Join("\n  ", wrong.Take(20)));
        return wrong.Count == 0 ? 0 : 1;
    }

    // ---- what a run costs ----------------------------------------------------------------------------------------------

    private static async Task<int> Cost(Target target)
    {
        var counter = new Counting();
        var (locks, opens, saves) = (new Locks(target), new Opens(), new Saves());
        var create = target.Has("locks") ? target.Workers(counter, counter.Transactions, opens, saves, locks) : target.Workers(counter, counter.Transactions, opens, saves);
        await target.ResetAsync(2);
        var store = target.Store(create);
        var owner = new Worker("owner", 0, store, Models(), new WorkerOptions { PollLimit = 5000, MaxBatch = 5000 });
        await owner.TakeAsync();
        await owner.PollAsync();
        var n = 0;
        var keep = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000;
        Message Start(string mode, int count = 10) => new($"m{++n}", InstanceRef.In(0, $"{mode}{n}"), Kinds.Start, Start: Sequence.Spec(count, mode));

        Console.WriteLine($"\n{target.Engine}: {await target.DescribeAsync()}; fence {(target.FenceInBatch ? "in the batch" : "checked")}");
        Console.WriteLine("counted: commands, COMMITs and (but for PostgreSQL) BEGINs, as FINDINGS.md section 5 counts; sent: what the driver sends, one per request (see ENGINES.md)");
        Console.WriteLine($"{"run",-58} {"commands",8} {"begin",6} {"commit",6} {"opens",6} {"saves",6} {"counted",8} {"sent",6} {"server",8} {"tcp",6}");
        async Task Row(string name, Func<Task> body, Func<Task>? before = null)
        {
            if (before is not null) await before();
            counter.Reset();
            (opens.Count, saves.Set, saves.Released, locks.Largest) = (0, 0, 0, "");
            var trips = target.Proxy?.Trips ?? 0;
            var kinds = await target.WireByKindAsync();
            var wire = await target.WireAsync();
            await body();
            wire = wire < 0 ? -1 : await target.WireAsync() - wire;
            var delta = (await target.WireByKindAsync()).Select(k => (k.Key, n: k.Value - kinds.GetValueOrDefault(k.Key))).Where(k => k.n > 0).ToList();
            var counted = counter.Commands + counter.Transactions.Ended + (target.Engine == "postgres" ? 0 : counter.Transactions.Begun);
            var sent = counter.Commands + counter.Transactions.Ended + target.BeginTrips * counter.Transactions.Begun + target.OpenTrips * opens.Count + target.SaveTrips * (saves.Set + saves.Released);
            var tcp = target.Proxy is { } proxy ? (proxy.Trips - trips).ToString() : "";
            Console.WriteLine($"{name,-58} {counter.Commands,8} {counter.Transactions.Begun,6} {counter.Transactions.Ended,6} {opens.Count,6} {saves.Set + saves.Released,6} {counted,8} {sent,6} {(wire < 0 ? "" : wire.ToString()),8} {tcp,6}");
            if (locks.Largest != "") Console.WriteLine("    locks held at the largest commit: " + locks.Largest);
            if (target.Has("wire") && target.Engine == "mysql") Console.WriteLine("    server: " + string.Join(", ", delta.OrderBy(k => k.Key).Select(k => $"{k.Key.Replace("statement/", "")} x{k.n}")));
        }

        foreach (var mode in new[] { Sequence.Ephemeral, Sequence.Balanced, Sequence.Durable })
            await Row($"ten no-ops, {mode}, started on the owner", async () =>
            {
                owner.Post(Start(mode), fresh: true);
                await Drain(owner, poll: false);
            });
        foreach (var mode in new[] { Sequence.Ephemeral, Sequence.Balanced, Sequence.Durable })
            await Row($"ten no-ops, {mode}, started from another node", async () =>
            {
                await store.AddAsync(Start(mode) with { KeepUntil = keep });
                await Drain(owner, poll: true);
            });
        await Row("parent with three children, same partition", async () =>
        {
            owner.Post(new Message($"m{++n}", InstanceRef.In(0, $"parent{n}"), Kinds.Start, Start: new StartSpec("parent", "p", Json.Write(new ParentInput(3, 0)))), fresh: true);
            await Drain(owner, poll: false);
        });
        foreach (var many in target.Get("many", "100,1000").Split(",").Select(int.Parse))
            await Row($"{many} balanced instances started at once", async () =>
            {
                for (var i = 0; i < many; i++) owner.Post(Start(Sequence.Balanced), fresh: true);
                await Drain(owner, poll: false);
            });
        foreach (var many in target.Get("many", "100,1000").Split(",").Select(int.Parse))
            await Row($"{many} balanced instances, their starts arriving by poll", () => Drain(owner, poll: true), async () =>
            {
                for (var i = 0; i < many; i++) await store.AddAsync(Start(Sequence.Balanced) with { KeepUntil = keep });
            });

        await using var check = create();
        var unfinished = await check.Instances.CountAsync(i => i.Status != InstanceStatus.Finished);
        var left = await check.Messages.CountAsync(m => !m.Done);
        Console.WriteLine($"check: {await check.Instances.CountAsync()} instances, {unfinished} not finished, {left} messages left, position {(await check.Partitions.SingleAsync(p => p.Id == 0)).Position}");
        return unfinished == 0 && left == 0 ? 0 : 1;
    }

    // ---- seq inside one commit (finding F8), and what an UPDATE that changes nothing reports -------------------------------

    private static async Task<int> Probe(Target target)
    {
        var create = target.Admins();
        var store = target.Store(create);
        var ok = true;
        foreach (var (count, format) in new[] { (12, "0"), (12, "0000"), (1000, "0000"), (3000, "0000") })
        {
            await target.ResetAsync(2);
            var lease = (await store.TakeAsync(0, "probe", TimeSpan.FromSeconds(30)))!;
            var sent = Enumerable.Range(0, count).Select(i => new Message($"0/a#1.{i.ToString(format)}", InstanceRef.In(1, "b"), "add")).ToList();
            var row = new InstanceRow("0/a", "counter", "c", InstanceStatus.Running, "{}", 1, null);
            var committed = await store.CommitAsync(new CommitBatch(0, lease.Epoch, lease.Position, TimeSpan.FromSeconds(30), true, [new StepCommit("0/a", row, null, [], [], sent, [], [], "[]")], [], []));
            var polled = await store.PollAsync(1, 1, TimeSpan.Zero, 10000);
            var same = polled.Messages.Select(m => m.Message.Id).SequenceEqual(sent.Select(m => m.Id));
            var returned = committed.Seqs.SequenceEqual(polled.Messages.OrderBy(m => sent.FindIndex(s => s.Id == m.Message.Id)).Select(m => m.Seq));
            var first = polled.Messages.Select((m, i) => (m, i)).FirstOrDefault(x => x.m.Message.Id != sent[x.i].Id);
            Console.WriteLine($"{count} messages in one commit, ordinal format {format}: seq order {(same ? "is" : "IS NOT")} the order sent{(same ? "" : $" (first difference at {first.i}: {first.m?.Message.Id})")}; the seqs the commit returned {(returned ? "match" : "DO NOT match")} the rows");
            if (format == "0000") ok &= same && returned;
        }
        // An UPDATE that sets a row to the values it has: does the driver report it as one row?
        await target.ResetAsync(2);
        await using var db = create();
        var unchanged = await db.Database.ExecuteSqlRawAsync(target.Quote("""UPDATE "Partitions" SET "Position" = "Position" WHERE "Id" = 0"""));
        Console.WriteLine($"an UPDATE that changes no value reports {unchanged} row(s)");
        return ok && unchanged == 1 ? 0 : 1;
    }

    // ---- does the poll wait behind somebody else's uncommitted rows? -------------------------------------------------------

    private static async Task<int> Block(Target target)
    {
        var create = target.Admins();
        var store = target.Store(create);
        await target.ResetAsync(2);
        var keep = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000;
        for (var i = 0; i < target.Int("fill", 0); i++) await store.AddAsync(new Message($"fill{i:00000}", InstanceRef.In(i % 2, "f"), "add", KeepUntil: keep));
        foreach (var id in new[] { "m1", "m2", "m3" }) await store.AddAsync(new Message(id, InstanceRef.In(0, "a"), "add", KeepUntil: keep));
        var lease = (await store.TakeAsync(0, "block", TimeSpan.FromSeconds(30)))!;
        Console.WriteLine($"{target.Engine}: {await target.DescribeAsync()}; {target.Int("fill", 0)} other rows in the table");

        async Task Poll(string what, int partition)
        {
            var clock = Stopwatch.StartNew();
            using var patience = new CancellationTokenSource(3000);
            try
            {
                var polled = await store.PollAsync(partition, 1_000_000, TimeSpan.Zero, 5, patience.Token);
                Console.WriteLine($"  {what}: poll of partition {partition} answered in {clock.ElapsedMilliseconds} ms with [{string.Join(" ", polled.Messages.Select(m => m.Message.Id))}]");
            }
            catch (Exception e) { Console.WriteLine($"  {what}: poll of partition {partition} did not answer: {Tally.Code(e)} after {clock.ElapsedMilliseconds} ms"); }
        }

        await Poll("nothing in flight", 0);
        await using (var other = create())
        {
            await using var transaction = await other.Database.BeginTransactionAsync();
            other.Add(new MessageEntity { Id = "0/z#1.0000", Partition = 1, Target = "1/b", Kind = "add" });
            other.Add(new MessageEntity { Id = "0/z#1.0001", Partition = 0, Target = "0/a", Kind = "add" });
            await SaveAsync(other, target);
            Console.WriteLine(" another transaction has inserted one message for each partition and not committed");
            await Poll("uncommitted insert", 0);
            await Poll("uncommitted insert", 1);
            await transaction.RollbackAsync();
        }
        await using (var other = create())
        {
            await using var transaction = await other.Database.BeginTransactionAsync();
            await other.Messages.Where(m => m.Id == "m1").ExecuteUpdateAsync(s => s.SetProperty(m => m.Seen, 1));
            Console.WriteLine(" another transaction has marked m1 seen (the oldest row of partition 0) and not committed");
            await Poll("uncommitted mark", 0);
            await transaction.RollbackAsync();
        }
        await using (var other = create())
        {
            await using var transaction = await other.Database.BeginTransactionAsync();
            await other.Messages.Where(m => m.Id == "m1").ExecuteDeleteAsync();
            Console.WriteLine(" another transaction has deleted m1 and not committed");
            await Poll("uncommitted delete", 0);
            await transaction.RollbackAsync();
        }
        return 0;
    }

    private static async Task SaveAsync(KernelDb db, Target target)
    {
        if (target.Engine == "sqlite")
        {
            var seq = await db.Database.SqlQueryRaw<long>("""SELECT COALESCE(MAX("Seq"), 0) AS "Value" FROM "Messages" """).SingleAsync();
            foreach (var entry in db.ChangeTracker.Entries<MessageEntity>()) entry.Entity.Seq = ++seq;
        }
        await db.SaveChangesAsync();
    }

    // ---- a key violation and a value too long inside the commit's transaction ---------------------------------------------

    private static async Task<int> Definite(Target target)
    {
        var create = target.Admins();
        var store = target.Store(create);
        await target.ResetAsync(2);
        var lease = (await store.TakeAsync(0, "definite", TimeSpan.FromSeconds(30)))!;
        var position = lease.Position;
        StepCommit Unit(string id) => new(id, new InstanceRow(id, "counter", "c", InstanceStatus.Running, "{}", 1, null), null, [], [], [new Message(id + "#1.0000", InstanceRef.In(1, "b"), "add")], [], [], "[]");
        async Task<string> Commit(params string[] ids)
        {
            try
            {
                var result = await store.CommitAsync(new CommitBatch(0, lease.Epoch, position, TimeSpan.FromSeconds(30), true, ids.Select(Unit).ToList(), [], []));
                if (result.Landed) position += ids.Length;
                return result.Landed ? "landed" : "fenced";
            }
            catch (StoreException e) { return $"{e.Failure}: {Tally.Code(e)} {e.Message.Split('\n')[0]}"; }
            catch (Exception e) { return $"NOT CLASSIFIED: {e.GetType().Name} {e.Message.Split('\n')[0]}"; }
        }
        async Task<string> State()
        {
            await using var db = create();
            return $"instances [{string.Join(" ", await db.Instances.OrderBy(i => i.Id).Select(i => i.Id.Length > 20 ? "(long)" : i.Id).ToListAsync())}], {await db.Messages.CountAsync()} messages, {await db.Journal.CountAsync()} journal rows, position {(await db.Partitions.SingleAsync(p => p.Id == 0)).Position}";
        }
        Console.WriteLine($"{target.Engine}: {await target.DescribeAsync()}; fence {(target.FenceInBatch ? "in the batch" : "checked")}");
        Console.WriteLine($"  [a b c]: {await Commit("0/a", "0/b", "0/c")}; {await State()}");
        Console.WriteLine($"  [d b e], b exists: {await Commit("0/d", "0/b", "0/e")}; {await State()}");
        Console.WriteLine($"  [d]: {await Commit("0/d")}; [e]: {await Commit("0/e")}; {await State()}");
        var longId = "0/" + new string('L', 600);
        Console.WriteLine($"  [f (an id of 602 characters) g]: {await Commit("0/f", longId, "0/g")}; {await State()}");
        Console.WriteLine($"  [f]: {await Commit("0/f")}; {await State()}");
        return 0;
    }

    private static async Task<int> Deadlock(Target target)
    {
        Console.WriteLine(await target.DeadlockReportAsync());
        return 0;
    }
}

/// <summary>One engine in one configuration: connections, session settings, how to kill a session, what its errors are called.</summary>
sealed class Target(string engine, string connection, Dictionary<string, string> options)
{
    public string Engine => engine;
    public string Get(string key, string fallback) => options.GetValueOrDefault(key, fallback);
    public int Int(string key, int fallback) => int.Parse(Get(key, fallback.ToString()));
    public bool Has(string key) => Get(key, "0") != "0";
    public bool FenceInBatch => Get("fence", "checked") == "batch";

    /// <summary>The engine's code for a deadlock victim (for SQLite: BUSY), as <see cref="Tally.Code"/> writes it.</summary>
    public string DeadlockCode => engine switch { "postgres" => "pg 40P01", "sqlserver" => "mssql 1205", "mysql" => "mysql 1213", _ => "sqlite 5" };

    /// <summary>Round trips per BEGIN. Npgsql sends it with the next command; SqlClient sends it alone; Connector/NET sends SET TRANSACTION ISOLATION LEVEL and BEGIN as two commands; SQLite has no wire and counts the call.</summary>
    public int BeginTrips => engine switch { "postgres" => 0, "mysql" => 2, _ => 1 };

    /// <summary>Round trips per connection taken from the pool: Connector/NET sends Ping and Init DB every time (both seen in performance_schema and on the TCP stream); the others send nothing of their own.</summary>
    public int OpenTrips => engine == "mysql" ? 2 : 0;

    /// <summary>Round trips per savepoint set or released: every driver sends it alone (ENGINES.md E4, counted on the TCP stream).</summary>
    public int SaveTrips => 1;

    public string Quote(string sql) => engine == "mysql" ? sql.Replace('"', '`') : sql;

    /// <summary>With proxy=1 the workers connect through <see cref="Wire"/>, which counts round trips on the TCP stream.</summary>
    public Wire? Proxy { get; } = options.ContainsKey("proxy") && engine != "sqlite" ? Wire.Start(engine, connection) : null;

    private string Worker => engine switch
    {
        "postgres" => (Proxy?.Rewrite(connection) ?? connection) + ";Application Name=kernel-worker;Maximum Pool Size=40",
        "sqlserver" => (Proxy?.Rewrite(connection) ?? connection) + ";Application Name=kernel-worker;Max Pool Size=40",
        "mysql" => (Proxy?.Rewrite(connection) ?? connection) + ";uid=kernel_worker;pwd=kernel;maximumpoolsize=40",
        _ => connection + (options.TryGetValue("timeout", out var timeout) ? ";Default Timeout=" + timeout : ""),
    };

    private string Admin => engine switch
    {
        "postgres" or "sqlserver" => connection + ";Application Name=kernel-admin",
        "mysql" => connection + ";uid=root;pwd=kernel",
        _ => connection,
    };

    private IInterceptor[] Session => engine switch
    {
        "sqlserver" when Has("xactabort") => [new OnOpen("SET XACT_ABORT ON")],
        "sqlite" when options.TryGetValue("busy", out var busy) => [new OnOpen("PRAGMA busy_timeout = " + busy)],
        _ => [],
    };

    public Func<KernelDb> Workers(params IInterceptor[] interceptors) => Factory(Worker, interceptors);
    public Func<KernelDb> Admins() => Factory(Admin);

    private Func<KernelDb> Factory(string to, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<KernelDb>();
        _ = engine switch
        {
            "postgres" => builder.UseNpgsql(to),
            // ENGINES.md E7. Both providers put at most 42 statements in one command, so a flush of 100 instances was five
            // commands. At 1,000, SQL Server's provider still splits at its limit of 2,100 parameters.
            "sqlserver" => builder.UseSqlServer(to, o => o.MaxBatchSize(Int("batchsize", 1000))),
            "mysql" => builder.UseMySQL(to, o => o.MaxBatchSize(Int("batchsize", 1000))),
            "sqlite" => builder.UseSqlite(to),
            _ => throw new ArgumentException(engine),
        };
        var built = builder.AddInterceptors([.. interceptors, .. Session]).Options;
        return () => new KernelDb(built);
    }

    public EfStore Store(Func<KernelDb> create) => new(create, postgres: engine != "sqlite", FenceInBatch)
    {
        Unfenced = Has("unfenced"),
        Engine = engine switch
        {
            "sqlserver" => new SqlServerEngine { ReadPast = Has("readpast"), Savepoints = Has("savepoints"), ReadCommittedSnapshot = Get("rcsi", "1") == "1" },
            "mysql" => new MySqlEngine { Isolation = Get("isolation", "") == "rc" ? System.Data.IsolationLevel.ReadCommitted : null, Savepoints = Has("savepoints") },
            _ => null,
        },
    };

    public async Task ResetAsync(int partitions)
    {
        var admin = Admins();
        await using (var db = admin()) await db.Database.EnsureDeletedAsync();
        await Store(admin).CreateAsync(partitions);
        await using var settings = admin();
        switch (engine)
        {
            case "sqlserver":
                break;      // READ_COMMITTED_SNAPSHOT: SqlServerEngine.CreatedAsync (ENGINES.md E5)
            case "mysql":
                await settings.Database.ExecuteSqlRawAsync("CREATE USER IF NOT EXISTS 'kernel_worker'@'%' IDENTIFIED BY 'kernel'");
                await settings.Database.ExecuteSqlRawAsync("GRANT ALL ON *.* TO 'kernel_worker'@'%'");
                break;
            case "sqlite":
                await settings.Database.ExecuteSqlRawAsync($"PRAGMA journal_mode = {(Get("wal", "1") == "1" ? "WAL" : "DELETE")}");
                break;
        }
    }

    public async Task<string> DescribeAsync()
    {
        await using var db = Admins()();
        return engine switch
        {
            "postgres" => "isolation " + await Text(db, "SHOW transaction_isolation"),
            "sqlserver" => $"READ COMMITTED, {(await Text(db, "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE name = DB_NAME()") == "1" ? "READ_COMMITTED_SNAPSHOT ON" : "locking")}{(Has("readpast") ? ", poll WITH (READPAST)" : "")}{(Has("xactabort") ? ", XACT_ABORT ON" : "")}",
            "mysql" => (Get("isolation", "") == "rc" ? "READ COMMITTED for the kernel's transactions" : "REPEATABLE READ") + $" (server default {await Text(db, "SELECT @@global.transaction_isolation")})",
            _ => $"journal_mode {await Text(db, "PRAGMA journal_mode")}{(options.TryGetValue("busy", out var busy) ? ", busy_timeout " + busy + " ms" : "")}{(options.TryGetValue("timeout", out var timeout) ? ", command timeout " + timeout + " s" : "")}",
        };
    }

    private static async Task<string> Text(KernelDb db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await db.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync()) lines.Add(Convert.ToString(reader.GetValue(reader.FieldCount - 1)) ?? "");
        return string.Join("\n", lines);
    }

    /// <summary>Ends one busy worker session, whatever it is doing. SQLite has no sessions to end.</summary>
    public async Task<int> KillAsync(Func<KernelDb> admin)
    {
        await using var db = admin();
        switch (engine)
        {
            case "postgres":
                return await db.Database.SqlQueryRaw<bool>("""SELECT pg_terminate_backend(pid) AS "Value" FROM (SELECT pid FROM pg_stat_activity WHERE application_name = 'kernel-worker' AND state <> 'idle' ORDER BY random() LIMIT 1) busy""").CountAsync();
            case "sqlserver":
                var sessions = await Text(db, "SELECT TOP 1 s.session_id FROM sys.dm_exec_sessions s WHERE s.program_name = 'kernel-worker' AND (s.open_transaction_count > 0 OR s.status = 'running') ORDER BY NEWID()");
                if (sessions == "") return 0;
                await Text(db, "KILL " + int.Parse(sessions));
                return 1;
            case "mysql":
                var threads = await Text(db, "SELECT p.ID FROM information_schema.PROCESSLIST p WHERE p.USER = 'kernel_worker' AND (p.COMMAND <> 'Sleep' OR p.ID IN (SELECT trx_mysql_thread_id FROM information_schema.INNODB_TRX)) ORDER BY RAND() LIMIT 1");
                if (threads == "") return 0;
                await Text(db, "KILL CONNECTION " + long.Parse(threads));
                return 1;
            default:
                return 0;
        }
    }

    /// <summary>The engine's own count of deadlocks so far.</summary>
    public async Task<long> DeadlocksAsync(Func<KernelDb> admin)
    {
        await using var db = admin();
        var text = engine switch
        {
            "postgres" => await Text(db, "SELECT deadlocks FROM pg_stat_database WHERE datname = current_database()"),
            "sqlserver" => await Text(db, "SELECT cntr_value FROM sys.dm_os_performance_counters WHERE counter_name = 'Number of Deadlocks/sec' AND instance_name = '_Total'"),
            "mysql" => await Text(db, "SELECT `COUNT` FROM information_schema.INNODB_METRICS WHERE NAME = 'lock_deadlocks'"),
            _ => "0",
        };
        return long.TryParse(text, out var count) ? count : 0;
    }

    public async Task<string> DeadlockReportAsync()
    {
        await using var db = Admins()();
        return engine switch
        {
            "sqlserver" => await Text(db, "SELECT TOP 30 CAST(event_data AS nvarchar(max)) FROM sys.fn_xe_file_target_read_file('system_health*.xel', NULL, NULL, NULL) WHERE object_name = 'xml_deadlock_report' ORDER BY timestamp_utc DESC"),
            "mysql" => await Text(db, "SHOW ENGINE INNODB STATUS"),
            _ => "see the server's log",
        };
    }

    /// <summary>What the server itself counted as requests from the workers, where it keeps such a count; -1 where it does not.</summary>
    public async Task<long> WireAsync() => (await WireByKindAsync()).Values.Sum() is var sum && engine is "sqlserver" or "mysql" ? sum : -1;

    /// <summary>
    /// SQL Server: network reads on the workers' connections (a request larger than a TDS packet is several reads, one
    /// round trip). MySQL: every command the server executed for the worker user, by kind (COM_QUERY statements,
    /// Init DB, Reset Connection, ...), from performance_schema: one per request.
    /// </summary>
    public async Task<Dictionary<string, long>> WireByKindAsync()
    {
        await using var db = Admins()();
        var sql = engine switch
        {
            "sqlserver" => "SELECT 'reads', COALESCE(SUM(c.num_reads), 0) FROM sys.dm_exec_connections c JOIN sys.dm_exec_sessions s ON s.session_id = c.session_id WHERE s.program_name = 'kernel-worker'",
            "mysql" => "SELECT e.EVENT_NAME, SUM(e.COUNT_STAR) FROM performance_schema.events_statements_summary_by_thread_by_event_name e JOIN performance_schema.threads t ON t.THREAD_ID = e.THREAD_ID WHERE t.PROCESSLIST_USER = 'kernel_worker' AND e.COUNT_STAR > 0 GROUP BY e.EVENT_NAME",
            _ => null,
        };
        var counts = new Dictionary<string, long>();
        if (sql is null) return counts;
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) counts[reader.GetString(0)] = Convert.ToInt64(reader.GetValue(1));
        return counts;
    }
}

/// <summary>
/// A TCP proxy in front of the database that counts round trips: a round trip begins each time the client sends after
/// the server last spoke on that connection (or for the first time). Requests the driver pipelines without waiting
/// for an answer count as one. Connection handshakes count too. Admin connections do not go through it.
/// </summary>
sealed class Wire
{
    private readonly System.Net.Sockets.TcpListener _listener = new(System.Net.IPAddress.Loopback, 0);
    private readonly string _host;
    private readonly int _port;
    private long _trips;

    private Wire(string host, int port) => (_host, _port) = (host, port);

    public long Trips => Interlocked.Read(ref _trips);

    public static Wire Start(string engine, string connection)
    {
        var match = engine == "sqlserver"
            ? System.Text.RegularExpressions.Regex.Match(connection, @"Server=([^,;]+),(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            : System.Text.RegularExpressions.Regex.Match(connection, @"(?:Host|server)=([^;]+);.*?Port=(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var wire = new Wire(match.Groups[1].Value, int.Parse(match.Groups[2].Value));
        wire._listener.Start();
        _ = Task.Run(wire.AcceptAsync);
        return wire;
    }

    public string Rewrite(string connection)
    {
        var port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;
        return System.Text.RegularExpressions.Regex.Replace(connection, @"(Server=[^,;]+,)\d+|(Port=)\d+", m => (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) + port, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            var client = await _listener.AcceptTcpClientAsync();
            _ = Task.Run(async () =>
            {
                using var server = new System.Net.Sockets.TcpClient();
                using var _ = client;
                client.NoDelay = true;
                server.NoDelay = true;
                await server.ConnectAsync(_host, _port);
                var serverSpoke = 1;     // so the client's first send is a round trip
                async Task Pump(System.Net.Sockets.NetworkStream from, System.Net.Sockets.NetworkStream to, bool fromClient)
                {
                    var buffer = new byte[65536];
                    try
                    {
                        for (int read; (read = await from.ReadAsync(buffer)) > 0;)
                        {
                            if (fromClient) { if (Interlocked.Exchange(ref serverSpoke, 0) == 1) Interlocked.Increment(ref _trips); }
                            else Interlocked.Exchange(ref serverSpoke, 1);
                            await to.WriteAsync(buffer.AsMemory(0, read));
                        }
                    }
                    catch (Exception) { }
                    finally { client.Close(); server.Close(); }
                }
                await Task.WhenAll(Pump(client.GetStream(), server.GetStream(), true), Pump(server.GetStream(), client.GetStream(), false));
            });
        }
    }
}

/// <summary>Counts connections taken from the pool.</summary>
sealed class Opens : DbConnectionInterceptor
{
    public int Count;
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Count++;
    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default) { Count++; return Task.CompletedTask; }
}

/// <summary>Counts the savepoints Entity Framework sets around a SaveChanges inside a transaction.</summary>
sealed class Saves : DbTransactionInterceptor
{
    public int Set, Released;
    public override void CreatedSavepoint(DbTransaction transaction, TransactionEventData eventData) => Set++;
    public override Task CreatedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default) { Set++; return Task.CompletedTask; }
    public override void ReleasedSavepoint(DbTransaction transaction, TransactionEventData eventData) => Released++;
    public override Task ReleasedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default) { Released++; return Task.CompletedTask; }
}

/// <summary>Runs one statement on every connection as it opens: a session setting.</summary>
sealed class OnOpen(string sql) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>Just before COMMIT, asks the engine which locks this transaction holds, and keeps the answer of the largest one.</summary>
sealed class Locks(Target target) : DbTransactionInterceptor
{
    public string Largest = "";
    private long _most;

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        var sql = target.Engine switch
        {
            "sqlserver" => "SELECT resource_type + ' ' + request_mode + CASE WHEN resource_type = 'OBJECT' THEN ' ' + OBJECT_NAME(resource_associated_entity_id) ELSE '' END, COUNT(*) FROM sys.dm_tran_locks WHERE request_session_id = @@SPID AND resource_type <> 'DATABASE' GROUP BY resource_type + ' ' + request_mode + CASE WHEN resource_type = 'OBJECT' THEN ' ' + OBJECT_NAME(resource_associated_entity_id) ELSE '' END ORDER BY 1",
            "mysql" => "SELECT CONCAT(OBJECT_NAME, '.', COALESCE(INDEX_NAME, '(table)'), ' ', LOCK_MODE), COUNT(*) FROM performance_schema.data_locks WHERE THREAD_ID = PS_CURRENT_THREAD_ID() GROUP BY 1 ORDER BY 1",
            _ => null,
        };
        if (sql is null) return result;
        await using var command = transaction.Connection!.CreateCommand();
        (command.Transaction, command.CommandText) = (transaction, sql);
        var (parts, total) = (new List<string>(), 0L);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                parts.Add($"{reader.GetValue(0)} x{reader.GetValue(1)}");
                total += Convert.ToInt64(reader.GetValue(1));
            }
        if (total >= _most) (_most, Largest) = (total, string.Join(", ", parts));
        return result;
    }
}

/// <summary>Counts what every store call came to: landed, fenced, or failed and with which of the engine's errors.</summary>
sealed class Tally(IKernelStore inner) : IKernelStore
{
    private readonly ConcurrentDictionary<string, int> _counts = new();
    private readonly ConcurrentDictionary<string, string> _samples = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<double>> _times = new();

    public static string Code(Exception e)
    {
        var kind = e is StoreException s ? s.Failure + " " : "";
        for (var inner = e; inner is not null; inner = inner.InnerException)
            switch (inner)
            {
                case PostgresException pg: return kind + "pg " + pg.SqlState;
                case SqlException sql: return kind + "mssql " + sql.Number;
                case MySqlException my: return kind + "mysql " + my.Number;
                case SqliteException lite: return kind + "sqlite " + lite.SqliteErrorCode + (lite.SqliteExtendedErrorCode != lite.SqliteErrorCode ? "/" + lite.SqliteExtendedErrorCode : "");
            }
        for (var inner = e; inner is not null; inner = inner.InnerException)
            if (inner.InnerException is null) return kind + inner.GetType().Name;
        return kind;
    }

    private void Count(string key) => _counts.AddOrUpdate(key, 1, (_, n) => n + 1);

    public int Sum(params string[] parts) => _counts.Where(c => parts.All(c.Key.Contains)).Sum(c => c.Value);

    private async ValueTask<T> Call<T>(string op, Func<ValueTask<T>> call)
    {
        var clock = Stopwatch.StartNew();
        try { return await call(); }
        catch (Exception e)
        {
            var key = $"{op} {Code(e)}";
            Count(key);
            _samples.TryAdd(key, (e.InnerException?.InnerException ?? e.InnerException ?? e).Message.Split('\n')[0]);
            throw;
        }
        finally { _times.GetOrAdd(op, _ => []).Add(clock.Elapsed.TotalMilliseconds); }
    }

    public void Print()
    {
        foreach (var (key, count) in _counts.OrderBy(c => c.Key)) Console.WriteLine($"  {key,-44} {count,7}{(_samples.TryGetValue(key, out var sample) ? "   " + (sample.Length > 150 ? sample[..150] : sample) : "")}");
        foreach (var (op, times) in _times.OrderBy(t => t.Key))
        {
            var sorted = times.Order().ToList();
            Console.WriteLine($"  {op,-8} {sorted.Count,7} calls, ms: median {sorted[sorted.Count / 2]:F1}, 99% {sorted[(int)(sorted.Count * 0.99)]:F1}, longest {sorted[^1]:F0}");
        }
    }

    public async ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        var taken = await Call("take", () => inner.TakeAsync(partition, node, lease, cancellationToken));
        Count(taken is null ? "take refused" : "take taken");
        return taken;
    }

    public async ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
    {
        // A batch that names one message twice is the worker's doing, not the engine's: say which lists it is in.
        var named = batch.Seen.Select(id => (id, list: "seen"))
            .Concat(batch.Steps.SelectMany(s => s.Consumed.Select(id => (id, list: "consumed")).Concat(s.Done.Select(id => (id, list: "done"))).Concat(s.Sent.Select(m => (id: m.Id, list: "sent")))))
            .GroupBy(x => x.id).Where(g => g.Count() > 1).ToList();
        if (named.Count > 0)
        {
            Count("commit names a message twice");
            _samples.TryAdd("commit names a message twice", string.Join("; ", named.Take(3).Select(g => $"{g.Key} in {string.Join("+", g.Select(x => x.list))}")));
        }
        var committed = await Call("commit", () => inner.CommitAsync(batch, cancellationToken));
        Count(committed.Landed ? "commit landed" : "commit fenced");
        return committed;
    }

    public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) => Call("poll", () => inner.PollAsync(partition, epoch, horizon, limit, cancellationToken));
    public ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default) => Call("add", () => inner.AddAsync(message, cancellationToken));
    public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => Call("load", () => inner.LoadAsync(ids, cancellationToken));
    public async ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default) => await Call("publish", async () => { await inner.PublishAsync(@event, cancellationToken); return 0; });
    public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default) => Call("find", () => inner.FindSubscriptionsAsync(key, cancellationToken));
    public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) => Call("events", () => inner.FindEventsAsync(keys, cancellationToken));
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default) => inner.ReadJournalAsync(partition, after, take, cancellationToken);
    public ValueTask<int> SweepAsync(CancellationToken cancellationToken = default) => inner.SweepAsync(cancellationToken);
    public async ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default) => await Call("release", async () => { await inner.ReleaseAsync(partition, epoch, cancellationToken); return 0; });
}
