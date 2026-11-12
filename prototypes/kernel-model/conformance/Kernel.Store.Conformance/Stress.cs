using System.Collections.Concurrent;
using System.Diagnostics;
using Kernel.Sim;

namespace Kernel.Store.Conformance;

public sealed record StressOptions
{
    public int Seconds { get; init; } = 15;
    public int Messages { get; init; } = 1500;
    public int Partitions { get; init; } = 2;
    public int Nodes { get; init; } = 3;
    public TimeSpan Lease { get; init; } = TimeSpan.FromMilliseconds(150);
    /// <summary>The share of store calls whose connection breaks, half before the call and half after it executed.</summary>
    public double FaultRate { get; init; } = 0.01;
    /// <summary>How often the provider's own connection killer runs, in milliseconds (a range).</summary>
    public (int Min, int Max) KillEvery { get; init; } = (20, 60);
    public bool BrokenFence { get; init; }
    public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(180);
}

/// <summary>
/// Several workers that each believe they own the same partitions, each on its own loop, with stalls longer than the
/// lease, releases, injected connection failures and the provider's own connection killer; an outside producer that
/// retries its adds until they answer. Then calm, settle, and check the end state of FINDINGS section 4, reading
/// only through <see cref="IKernelStore"/>.
/// </summary>
public static class Stress
{
    public static IExecutionModel[] Models() =>
        [new Counter(), new Parent(), new Child(), new Waiter(), new Sequence(), new Runner((_, _) => new ValueTask<object?>((object?)null)), new Fragile(() => { })];

    public static async Task<int> RunAsync(IStoreProvider provider, StressOptions o, TextWriter? output = null)
    {
        output ??= Console.Out;
        await using var provided = await provider.CreateAsync(o.Partitions, new StoreSetup(BrokenFence: o.BrokenFence));
        var faulty = new FaultyStore(provided.Store, o.FaultRate, 5);
        var store = new RecordingStore(faulty);
        var outside = provided.NewClient();
        var other = provided.NewClient();
        var watch = Stopwatch.StartNew();

        var calm = false;
        var stop = new CancellationTokenSource();
        long steps = 0, kills = 0, stalls = 0, releases = 0, takes = 0;
        var options = new WorkerOptions
        {
            Lease = o.Lease, Horizon = TimeSpan.FromSeconds(2), PollLimit = 40, MaxBatch = 40, MaxAttempts = 1_000_000,
            OnStep = (_, _, _) => Interlocked.Increment(ref steps),
        };

        var loops = Enumerable.Range(0, o.Nodes).SelectMany(node => Enumerable.Range(0, o.Partitions).Select(partition => Task.Run(async () =>
        {
            var random = new Random(node * 31 + partition);
            // The node's name is unique per process start (finding F9): a restart is a new name.
            var generation = 0;
            Worker Fresh() => new($"node{node}.{generation++}", partition, store, Models(), options);
            var worker = Fresh();
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    if (!worker.Owns)
                    {
                        await worker.TakeAsync();
                        if (worker.Owns) Interlocked.Increment(ref takes);
                        else await Task.Delay(20);
                        continue;
                    }
                    await worker.PollAsync();
                    await worker.LoadAsync();
                    await worker.MeetAsync();
                    foreach (var id in worker.Ready.ToList()) worker.Run(id);
                    if (worker.NeedsFlush) await worker.FlushAsync();
                    else await worker.BeatAsync();
                    if (calm) await Task.Delay(2);
                    else if (random.Next(25) == 0)
                    {
                        // A stall longer than the lease: another node takes over; this one wakes with stale memory.
                        Interlocked.Increment(ref stalls);
                        await Task.Delay(random.Next(150, 500));
                    }
                    else if (random.Next(400) == 0)
                    {
                        Interlocked.Increment(ref releases);
                        await worker.ReleaseAsync();
                    }
                    else if (random.Next(600) == 0)
                    {
                        // A crash: the process is gone and a new one starts with a new name; nothing is released.
                        worker = Fresh();
                        await Task.Delay(random.Next(0, 300));
                    }
                    else await Task.Delay(2);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    lock (output) output.WriteLine($"worker node{node}/p{partition} threw: {e.GetType().Name}: {e.Message}");
                    worker = Fresh();
                }
            }
        }))).ToList();

        var chaos = Task.Run(async () =>
        {
            var random = new Random(7);
            while (!calm)
            {
                await Task.Delay(random.Next(o.KillEvery.Min, o.KillEvery.Max));
                try { kills += await provided.KillAsync(random); }
                catch (Exception) { }
            }
        });

        // The outside world: commands with start specs, retried until the insert answers; the id makes that safe.
        // Adds come from two clients in turn, as from two nodes.
        var keep = (await outside.PollAsync(0, 0, TimeSpan.Zero, 1)).Now + 3_600_000;
        var sent = new ConcurrentDictionary<string, List<string>>();
        var (parents, waiters, sequences) = (new List<string>(), new List<string>(), new List<(string Id, int Count)>());
        var producer = Task.Run(async () =>
        {
            var random = new Random(11);
            var clients = new[] { outside, other };
            async Task Retry(Func<IKernelStore, ValueTask> call, int n)
            {
                for (var attempt = 0; ; attempt++)
                    try
                    {
                        await call(clients[(n + attempt) % 2]);
                        return;
                    }
                    catch (Exception) { await Task.Delay(5); }
            }
            Task Add(Message message, int n) => Retry(async s => await s.AddAsync(message with { KeepUntil = keep }), n);
            for (var n = 0; n < o.Messages; n++)
            {
                var p = random.Next(o.Partitions);
                switch (random.Next(10))
                {
                    case < 6:
                        var target = InstanceRef.In(p, "c" + random.Next(3));
                        var forward = random.Next(4) == 0 ? InstanceRef.In(random.Next(o.Partitions), "c" + random.Next(3)).Id : null;
                        sent.GetOrAdd(target.Id, _ => []).Add("t" + n);
                        await Add(new Message("x" + n, target, "add", Json.Write(new Add("t" + n, forward)), Start: Counter.Spec), n);
                        break;
                    case 6:
                        parents.Add(InstanceRef.In(p, "par" + n).Id);
                        await Add(new Message("x" + n, InstanceRef.In(p, "par" + n), Kinds.Start, Start: new StartSpec("parent", "p", Json.Write(new ParentInput(random.Next(1, 4), random.Next(300))))), n);
                        break;
                    case 7:
                        waiters.Add(InstanceRef.In(p, "w" + n).Id);
                        await Add(new Message("x" + n, InstanceRef.In(p, "w" + n), Kinds.Start, Start: new StartSpec("waiter", "w", Json.Write(new List<string> { "k" + n }))), n);
                        // The event may be published before or after the subscription commits: the two must still meet.
                        await Retry(s => Events.PublishAsync(s, new EventRow("k" + n, "e" + n, "e" + n, keep)), n);
                        break;
                    default:
                        var count = random.Next(1, 6);
                        sequences.Add((InstanceRef.In(p, "s" + n).Id, count));
                        await Add(new Message("x" + n, InstanceRef.In(p, "s" + n), Kinds.Start, Start: Sequence.Spec(count, random.Next(2) == 0 ? Sequence.Balanced : Sequence.Durable)), n);
                        break;
                }
                await Task.Delay(random.Next(0, 2 * o.Seconds * 1000 / o.Messages));
            }
        });

        await Task.Delay(o.Seconds * 1000);
        await producer;
        calm = true;
        faulty.Calm = true;
        await chaos;
        output.WriteLine($"chaos over after {watch.Elapsed.TotalSeconds:0} s: {kills} connections killed, {faulty.Before} + {faulty.After} calls failed before + after executing, {stalls} stalls, {releases} releases, {steps} steps so far; settling");

        var deadline = DateTime.UtcNow + o.Settle;
        var keys = waiters.Select(w => "k" + w[(w.IndexOf("/w", StringComparison.Ordinal) + 2)..]).ToList();
        for (var left = 1; left > 0 && DateTime.UtcNow < deadline; await Task.Delay(500))
        {
            try { left = await Left(outside, o.Partitions, keys); }
            catch (Exception) { left = 1; }
        }
        stop.Cancel();
        await Task.WhenAll(loops);

        // ---- the end state, read through the contract -------------------------------------------------------------
        var wrong = new List<string>();
        await Task.Delay(o.Lease * 3);     // every lease has run out: a fresh node can take, and the take returns the position
        var journal = new List<JournalRow>();
        for (var p = 0; p < o.Partitions; p++)
        {
            var own = new List<JournalRow>();
            for (long after = 0; ;)
            {
                var page = await outside.ReadJournalAsync(p, after, 1000);
                own.AddRange(page);
                if (page.Count < 1000) break;
                after = page[^1].Position;
            }
            journal.AddRange(own);
            Lease? lease = null;
            for (var attempt = 0; lease is null && attempt < 50; attempt++)
                try { lease = await outside.TakeAsync(p, "checker-" + Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(30)); }
                catch (Exception) { await Task.Delay(100); }
            if (lease is null)
            {
                wrong.Add($"END partition {p} could not be taken after the workers stopped");
                continue;
            }
            output.WriteLine($"partition {p}: epoch {lease.Epoch} ({lease.Epoch - 2} takes before the checker's), position {lease.Position}, {own.Count} journal rows");
            if (own.Count != lease.Position || (own.Count > 0 && (own[^1].Position != lease.Position || own.Select(j => j.Position).Distinct().Count() != own.Count)))
                wrong.Add($"I6 partition {p}: {own.Count} journal rows, position {lease.Position}");
        }
        wrong.InsertRange(0, store.CheckOneWriter());
        wrong.AddRange(provided.Ghosts.Take(10).Select(g => "GHOST " + g));

        for (var p = 0; p < o.Partitions; p++)
            foreach (var message in (await outside.PollAsync(p, long.MaxValue, TimeSpan.FromDays(3650), 100_000)).Messages.Take(5))
                wrong.Add($"END message {message.Message.Id} ({message.Message.Kind} to {message.Message.Target}) left behind");
        foreach (var key in keys)
            if ((await outside.FindSubscriptionsAsync(key)).Count > 0) wrong.Add($"END a subscription of {key} is left behind");

        var ids = journal.Select(j => j.Instance).Concat(parents).Concat(waiters).Concat(sequences.Select(s => s.Id)).Concat(sent.Keys).Distinct().ToList();
        var rows = new Dictionary<string, InstanceRow>();
        foreach (var chunk in ids.Chunk(500))
            foreach (var row in await outside.LoadAsync(chunk)) rows[row.Id] = row;
        foreach (var group in journal.Where(j => j.Revision > 0).GroupBy(j => j.Instance))
            if (!rows.TryGetValue(group.Key, out var row) || row.Revision != group.Count() || group.Max(j => j.Revision) != row.Revision)
                wrong.Add($"I2 {group.Key}: row revision {rows.GetValueOrDefault(group.Key)?.Revision}, journal commits {group.Count()}");
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

        output.WriteLine($"{o.Messages} outside messages, {rows.Count} instances, {steps} steps run, {store.Commits.Count} commits answered landed, {store.Refused} refused by the fence or a count, {journal.Count} journal rows, {expected.Sum(e => e.Value.Count)} counter tokens checked, {takes} takes by workers");
        foreach (var (kind, n) in store.Failures.OrderByDescending(f => f.Value).Take(12)) output.WriteLine($"  commit failure x{n}: {kind}");
        output.WriteLine(wrong.Count == 0 ? "end state: all properties hold" : $"end state: {wrong.Count} violations\n  " + string.Join("\n  ", wrong.Take(25)));
        return wrong.Count == 0 ? 0 : 1;
    }

    private static async Task<int> Left(IKernelStore store, int partitions, IReadOnlyList<string> keys)
    {
        var left = 0;
        for (var p = 0; p < partitions; p++) left += (await store.PollAsync(p, long.MaxValue, TimeSpan.FromDays(3650), 100_000)).Messages.Count;
        if (left > 0) return left;
        foreach (var key in keys) left += (await store.FindSubscriptionsAsync(key)).Count;
        return left;
    }
}
