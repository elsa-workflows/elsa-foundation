using System.Collections.Concurrent;
using System.Data.Common;
using Kernel.EntityFramework;
using Kernel.Sim;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Usage:
//   dotnet run -c Release --project ef/Kernel.Ef.Run -- cost sqlite
//   dotnet run -c Release --project ef/Kernel.Ef.Run -- cost postgres "<connection string>"
//   dotnet run -c Release --project ef/Kernel.Ef.Run -- probe "<connection string>"                   seq order inside one commit (finding F8)
//   dotnet run -c Release --project ef/Kernel.Ef.Run -- stress "<connection string>" [seconds of chaos] [messages]
//   dotnet run -c Release --project ef/Kernel.Ef.Run -- stress-unfenced "<connection string>" ...     the same with the fence broken: it must fail
//   dotnet run -c Release --project ef/Kernel.Ef.Run -- x <command> <engine> "<connection string>" [key=value ...]   any engine: EngineRun.cs, ENGINES.md

if (args is ["x", ..]) return await EngineRun.RunAsync(args[1..]);
if (args is ["cost", "sqlite"])
{
    var file = Path.Combine(Path.GetTempPath(), $"kernel-model-{Guid.NewGuid():N}.db");
    foreach (var inBatch in new[] { false, true }) await Cost(false, $"Data Source={file}", inBatch);
    File.Delete(file);
    return 0;
}
if (args is ["cost", "postgres", var cost])
{
    foreach (var inBatch in new[] { false, true }) await Cost(true, cost, inBatch);
    return 0;
}
if (args is ["probe", var probe])
{
    // Does seq follow the order in which one commit sent its messages? Twelve messages, one SaveChanges, ids whose
    // ordinal has no fixed width (as the kernel wrote them before finding F8). It does not: EF sends them in key order.
    var create = Factory(true, probe);
    await using (var db = create()) await db.Database.EnsureDeletedAsync();
    var store = new EfStore(create, postgres: true, fenceInBatch: false);
    await store.CreateAsync(2);
    var lease = (await store.TakeAsync(0, "probe", TimeSpan.FromSeconds(30)))!;
    var sent = Enumerable.Range(0, 12).Select(i => new Message($"0/a#1.{i}", InstanceRef.In(1, "b"), "add")).ToList();
    var row = new InstanceRow("0/a", "counter", "c", InstanceStatus.Running, "{}", 1, null);
    await store.CommitAsync(new CommitBatch(0, lease.Epoch, lease.Position, TimeSpan.FromSeconds(30), true, [new TurnCommit("0/a", row, null, [], [], sent, [], [], "[]")], [], []));
    var polled = await store.PollAsync(1, 1, TimeSpan.Zero, 100);
    Console.WriteLine("sent:     " + string.Join(" ", sent.Select(m => m.Id)));
    Console.WriteLine("by seq:   " + string.Join(" ", polled.Messages.Select(m => m.Message.Id)));
    return polled.Messages.Select(m => m.Message.Id).SequenceEqual(sent.Select(m => m.Id)) ? 0 : 1;
}
if (args is ["stress" or "stress-unfenced", var stress, ..])
    return await Stress(stress, args.Length > 2 ? int.Parse(args[2]) : 15, args.Length > 3 ? int.Parse(args[3]) : 1500, unfenced: args[0] != "stress");
Console.Error.WriteLine("cost sqlite | cost postgres <connection> | stress <connection> [seconds] [messages]");
return 2;

static Func<KernelDb> Factory(bool postgres, string connection, params IInterceptor[] interceptors)
{
    var builder = new DbContextOptionsBuilder<KernelDb>();
    if (postgres) builder.UseNpgsql(connection);
    else builder.UseSqlite(connection);
    var options = builder.AddInterceptors(interceptors).Options;
    return () => new KernelDb(options);
}

static IExecutionModel[] Models() =>
    [new Counter(), new Parent(), new Child(), new Waiter(), new Sequence(), new Correlation(), new CWaiter(2), new Runner((_, _) => new ValueTask<object?>((object?)null)), new Fragile(() => { })];

// A host that runs passes of one scope until two in a row find nothing to do.
static async Task Drain(IScopeActivation scope, bool poll)
{
    if (poll) await scope.WokenAsync(null);      // the first pass reads the inbox
    for (var idle = 0; idle < 2;) idle = await scope.RunAsync() == TimeSpan.Zero ? 0 : idle + 1;
}

// ---- what a run costs ------------------------------------------------------------------------------------------------

static async Task Cost(bool postgres, string connection, bool fenceInBatch)
{
    var counter = new Counting();
    var create = Factory(postgres, connection, counter, counter.Transactions);
    await using (var db = create()) await db.Database.EnsureDeletedAsync();
    var store = new EfStore(create, postgres, fenceInBatch);
    await store.CreateAsync(2);
    var owner = new Worker("owner", 0, store, new WorkflowInstance(Models()));
    await new DurableHost([owner]).ActivateAsync(owner.Partition);
    await owner.PollAsync();    // the owner is caught up: from here it hands over in memory
    var n = 0;
    var keep = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000;
    Message Start(string mode, int count = 10) => new($"m{++n}", InstanceRef.In(0, $"{mode}{n}"), Kinds.Start, Start: Sequence.Spec(count, mode));

    Console.WriteLine($"\n{(postgres ? "PostgreSQL" : "SQLite")}, fence {(fenceInBatch ? "in the SaveChanges batch (concurrency tokens)" : "as its own checked statement")}");
    Console.WriteLine($"{"run",-66} {"commands",8} {"begin",6} {"commit",6} {"round trips",11}");
    async Task Row(string name, Func<Task> body)
    {
        counter.Reset();
        await body();
        // PostgreSQL: Npgsql sends BEGIN with the first command of the transaction, so a round trip is a command or a COMMIT/ROLLBACK.
        // SQLite: there is no wire; BEGIN, each command and COMMIT are each one call into the engine.
        var trips = counter.Commands + counter.Transactions.Ended + (postgres ? 0 : counter.Transactions.Begun);
        Console.WriteLine($"{name,-66} {counter.Commands,8} {counter.Transactions.Begun,6} {counter.Transactions.Ended,6} {trips,11}");
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
            await store.AddAsync(Start(mode) with { KeepUntil = keep });    // the other node's insert is counted too
            await Drain(owner, poll: true);
        });
    await Row("parent with three children, same partition", async () =>
    {
        owner.Post(new Message($"m{++n}", InstanceRef.In(0, $"parent{n}"), Kinds.Start, Start: new StartSpec("parent", "p", Json.Write(new ParentInput(3, 0)))), fresh: true);
        await Drain(owner, poll: false);
    });
    // Waiting for an event: through the subscription and event tables, and through a correlation instance per key.
    var other = new Worker("other", 1, store, new WorkflowInstance(Models()));
    await new DurableHost([other]).ActivateAsync(other.Partition);
    await other.PollAsync();
    string Key(int partition) => Enumerable.Range(0, 1000).Select(i => $"k{partition}-{n}-{i}").First(k => Correlation.Of(k, 2).Partition == partition);
    Message Waiter(string model, string key) => new($"m{++n}", InstanceRef.In(0, $"{model}{n}"), Kinds.Start, Start: new StartSpec(model, "w", Json.Write(new List<string> { key })));
    foreach (var first in new[] { "subscribe", "publish" })
    {
        await Row($"wait by table: {first} first", async () =>
        {
            var key = Key(0);
            if (first == "publish") await Events.PublishAsync(store, new EventRow(key, $"e{++n}", null, keep));
            owner.Post(Waiter("waiter", key), fresh: true);
            await Drain(owner, poll: false);
            if (first == "subscribe") await Events.PublishAsync(store, new EventRow(key, $"e{++n}", null, keep));
            await Drain(owner, poll: true);
        });
        foreach (var hub in new[] { 0, 1 })
            await Row($"wait by instance: {first} first, key in {(hub == 0 ? "the same" : "another")} partition", async () =>
            {
                var (key, hubOwner) = (Key(hub), hub == 0 ? owner : other);
                var publish = Correlation.Published($"m{++n}", key, 2, new Publish($"e{n}", null, 3_600_000), keep);
                if (first == "publish")
                {
                    await store.AddAsync(publish);
                    await Drain(hubOwner, poll: true);
                }
                owner.Post(Waiter("cwaiter", key), fresh: true);
                await Drain(owner, poll: false);
                if (hub == 1) await Drain(other, poll: true);       // the subscribe message crosses to the key's partition
                if (first == "subscribe")
                {
                    await store.AddAsync(publish);
                    await Drain(hubOwner, poll: true);
                }
                if (hub == 1) await Drain(owner, poll: true);       // the event crosses back to the waiter
                if (hub == 1) await Drain(other, poll: true);       // and the unsubscribe crosses to the key again
            });
    }
    await Row("100 balanced instances started at once", async () =>
    {
        for (var i = 0; i < 100; i++) owner.Post(Start(Sequence.Balanced), fresh: true);
        await Drain(owner, poll: false);
    });

    await using var check = create();
    var unfinished = await check.Instances.CountAsync(i => i.Status != InstanceStatus.Finished && i.Model != "correlation");
    var left = await check.Messages.CountAsync(m => !m.Done && m.Kind != "expire");
    Console.WriteLine($"check: {await check.Instances.CountAsync()} instances, {unfinished} not finished, {left} messages left, position {(await check.Partitions.SingleAsync(p => p.Id == 0)).Position}");
}

// ---- several owners of the same partitions, takeovers and killed transactions, on PostgreSQL -----------------------------

static async Task<int> Stress(string connection, int seconds, int messages, bool unfenced)
{
    const int partitions = 2, nodes = 3;
    var create = Factory(true, connection + ";Application Name=kernel-worker;Maximum Pool Size=40");
    var admin = Factory(true, connection + ";Application Name=kernel-admin");
    await using (var db = admin()) await db.Database.EnsureDeletedAsync();
    var store = new EfStore(create, postgres: true, fenceInBatch: false) { Unfenced = unfenced };     // unfenced: the mutation that the test must notice
    await new EfStore(admin, postgres: true).CreateAsync(partitions);

    var calm = false;
    var stop = new CancellationTokenSource();
    long turns = 0, kills = 0, stalls = 0;
    var options = new WorkerOptions
    {
        Lease = TimeSpan.FromMilliseconds(150), Horizon = TimeSpan.FromSeconds(2), PollLimit = 40, MaxBatch = 40, MaxAttempts = 1_000_000,
        OnTurn = (_, _, _) => Interlocked.Increment(ref turns),
    };

    // Each worker believes it may own its partition and is driven by its own loop. A stall longer than the lease lets
    // another node take over; the stalled one wakes up with stale memory and finds out at its next commit.
    var loops = Enumerable.Range(0, nodes).SelectMany(node => Enumerable.Range(0, partitions).Select(partition => Task.Run(async () =>
    {
        var (random, worker) = (new Random(node * 31 + partition), new Worker("node" + node, partition, store, new WorkflowInstance(Models()), options));
        while (!stop.IsCancellationRequested)
        {
            if (!worker.Owns)
            {
                await new DurableHost([worker]).ActivateAsync(worker.Partition);
                if (!worker.Owns) await Task.Delay(20);
                continue;
            }
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.MeetAsync();
            foreach (var id in worker.Ready.ToList()) worker.RunTurn(id);
            if (worker.NeedsFlush) await worker.FlushAsync();
            else await worker.BeatAsync();
            if (!calm && random.Next(25) == 0)
            {
                Interlocked.Increment(ref stalls);
                await Task.Delay(random.Next(150, 500));
            }
            else await Task.Delay(2);
        }
    }))).ToList();

    // Transactions killed mid-way: a backend of a random worker connection is terminated, whatever it is doing.
    var chaos = Task.Run(async () =>
    {
        var random = new Random(7);
        while (!calm)
        {
            await Task.Delay(random.Next(20, 60));
            try
            {
                await using var db = admin();
                kills += await db.Database.SqlQueryRaw<bool>("""SELECT pg_terminate_backend(pid) AS "Value" FROM (SELECT pid FROM pg_stat_activity WHERE application_name = 'kernel-worker' AND state <> 'idle' ORDER BY random() LIMIT 1) busy""").CountAsync();
            }
            catch (Exception) { }
        }
    });

    // The outside world: commands with start specs, retried until the insert answers (the id makes that safe).
    var outside = new EfStore(admin, postgres: true);
    var keep = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000;
    var sent = new ConcurrentDictionary<string, List<string>>();
    var (parents, waiters, sequences) = (new List<string>(), new List<string>(), new List<(string Id, int Count)>());
    var producer = Task.Run(async () =>
    {
        var random = new Random(11);
        async Task Add(Message message)
        {
            for (;;)
                try { await outside.AddAsync(message with { KeepUntil = keep }); return; }
                catch (Exception) { await Task.Delay(5); }
        }
        for (var n = 0; n < messages; n++)
        {
            var p = random.Next(partitions);
            switch (random.Next(10))
            {
                case < 6:
                    var target = InstanceRef.In(p, "c" + random.Next(3));
                    var forward = random.Next(4) == 0 ? InstanceRef.In(random.Next(partitions), "c" + random.Next(3)).Id : null;
                    sent.GetOrAdd(target.Id, _ => []).Add("t" + n);
                    await Add(new Message("x" + n, target, "add", Json.Write(new Add("t" + n, forward)), Start: Counter.Spec));
                    break;
                case 6:
                    parents.Add(InstanceRef.In(p, "par" + n).Id);
                    await Add(new Message("x" + n, InstanceRef.In(p, "par" + n), Kinds.Start, Start: new StartSpec("parent", "p", Json.Write(new ParentInput(random.Next(1, 4), random.Next(300))))));
                    break;
                case 7:
                    waiters.Add(InstanceRef.In(p, "w" + n).Id);
                    await Add(new Message("x" + n, InstanceRef.In(p, "w" + n), Kinds.Start, Start: new StartSpec("waiter", "w", Json.Write(new List<string> { "k" + n }))));
                    for (;;)    // the event may be published before or after the subscription commits: the two must still meet
                        try { await Events.PublishAsync(outside, new EventRow("k" + n, "e" + n, "e" + n, keep)); break; }
                        catch (Exception) { await Task.Delay(5); }
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

    await Task.Delay(seconds * 1000);
    await producer;
    calm = true;
    await chaos;
    Console.WriteLine($"chaos over: {kills} backends terminated, {stalls} stalls, {turns} turns so far; letting it settle");
    var patience = DateTime.UtcNow.AddSeconds(180);
    for (var left = 1; left > 0 && DateTime.UtcNow < patience; await Task.Delay(500))
    {
        await using var db = admin();
        left = await db.Messages.CountAsync(m => !m.Done) + await db.Subscriptions.CountAsync();
    }
    stop.Cancel();
    await Task.WhenAll(loops);

    // The same end-state properties the simulation checks, read from the tables.
    var wrong = new List<string>();
    await using var check = admin();
    var rows = await check.Instances.AsNoTracking().ToDictionaryAsync(i => i.Id);
    var journal = await check.Journal.AsNoTracking().Select(j => new { j.Partition, j.Position, j.Instance, j.Revision }).ToListAsync();
    foreach (var partition in await check.Partitions.AsNoTracking().OrderBy(p => p.Id).ToListAsync())
    {
        var own = journal.Where(j => j.Partition == partition.Id).ToList();
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

    Console.WriteLine($"{messages} outside messages, {rows.Count} instances, {turns} turns run, {journal.Count} commits of instances, {expected.Sum(e => e.Value.Count)} counter tokens checked");
    Console.WriteLine(wrong.Count == 0 ? "end state: all properties hold" : $"end state: {wrong.Count} violations\n  " + string.Join("\n  ", wrong.Take(20)));
    return wrong.Count == 0 ? 0 : 1;
}

/// <summary>Counts what Entity Framework sends: commands, and the begin and end of transactions.</summary>
sealed class Counting : DbCommandInterceptor
{
    public sealed class TransactionCounting : DbTransactionInterceptor
    {
        public int Begun, Ended;
        public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result) { Begun++; return result; }
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default) { Begun++; return new(result); }
        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Ended++;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) { Ended++; return Task.CompletedTask; }
        public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Ended++;
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) { Ended++; return Task.CompletedTask; }
    }

    public readonly TransactionCounting Transactions = new();
    public int Commands;

    public void Reset() => (Commands, Transactions.Begun, Transactions.Ended) = (0, 0, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) { Commands++; return result; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { Commands++; return new(result); }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) { Commands++; return result; }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { Commands++; return new(result); }
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) { Commands++; return result; }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) { Commands++; return new(result); }
}
