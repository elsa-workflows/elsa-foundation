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
        [new Counter(), new Parent(), new Child(), new Waiter(), new Sequence(), new Runner((_, _) => new ValueTask<object?>((object?)null)), new Fragile(() => { }), new OnEvent()];

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
        long turns = 0, kills = 0, stalls = 0, releases = 0, takes = 0;
        var options = new WorkerOptions
        {
            Lease = o.Lease, Horizon = TimeSpan.FromSeconds(2), PollLimit = 40, MaxBatch = 40, MaxAttempts = 1_000_000,
            OnTurn = (_, _, _) => Interlocked.Increment(ref turns),
        };

        var loops = Enumerable.Range(0, o.Nodes).SelectMany(node => Enumerable.Range(0, o.Partitions).Select(partition => Task.Run(async () =>
        {
            var random = new Random(node * 31 + partition);
            // The node's name is unique per process start (finding F9): a restart is a new name.
            var generation = 0;
            Worker Fresh() => new($"node{node}.{generation++}", partition, store, new WorkflowInstance(Models()), options);
            var worker = Fresh();
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    if (!worker.Owns)
                    {
                        await new DurableHost([worker]).ActivateAsync(partition);
                        if (worker.Owns) Interlocked.Increment(ref takes);
                        else await Task.Delay(20);
                        continue;
                    }
                    await worker.PollAsync();
                    await worker.LoadAsync();
                    await worker.MeetAsync();
                    foreach (var id in worker.Ready.ToList()) worker.RunTurn(id);
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
                        await worker.DisposeAsync();
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

        var killErrors = new ConcurrentDictionary<string, int>();
        var chaos = Task.Run(async () =>
        {
            var random = new Random(7);
            while (!calm)
            {
                await Task.Delay(random.Next(o.KillEvery.Min, o.KillEvery.Max));
                try { kills += await provided.KillAsync(random); }
                catch (Exception e) { killErrors.AddOrUpdate($"{e.GetType().Name}: {e.Message.Split('\n')[0]}", 1, (_, n) => n + 1); }
            }
        });

        var workload = new Workload(o.Partitions, o.Messages, o.Seconds);
        var producer = Task.Run(() => workload.ProduceAsync(outside, other));

        await Task.Delay(o.Seconds * 1000);
        await producer;
        calm = true;
        faulty.Calm = true;
        await chaos;
        if (provided.Chaos.Length > 0) output.WriteLine("  " + provided.Chaos);
        foreach (var (error, n) in killErrors) output.WriteLine($"  the connection killer failed x{n}: {error}");
        output.WriteLine($"chaos over after {watch.Elapsed.TotalSeconds:0} s: {kills} connections killed, {faulty.Before} + {faulty.After} calls failed before + after executing, {stalls} stalls, {releases} releases, {turns} turns so far; settling");

        await workload.SettleAsync(outside, o.Settle);
        stop.Cancel();
        await Task.WhenAll(loops);

        // ---- the end state, read through the contract -------------------------------------------------------------
        var (wrong, instances, journal, tokens) = await workload.CheckAsync(outside, o.Lease, store.CheckOneWriter(), provided.Ghosts.Take(10).Select(g => "GHOST " + g), output);

        output.WriteLine($"round 3: {workload.Lifecycles} trees terminated ({workload.Deleted} deleted), {workload.OnEvents} instances started by an event");
        output.WriteLine($"{o.Messages} outside messages, {instances} instances, {turns} turns run, {store.Commits.Count} commits answered landed, {store.Refused} refused by the fence or a count, {journal} journal rows, {tokens} counter tokens checked, {takes} takes by workers");
        foreach (var (kind, n) in store.Failures.OrderByDescending(f => f.Value).Take(12)) output.WriteLine($"  commit failure x{n}: {kind}");
        output.WriteLine(wrong.Count == 0 ? "end state: all properties hold" : $"end state: {wrong.Count} violations\n  " + string.Join("\n  ", wrong.Take(25)));
        return wrong.Count == 0 ? 0 : 1;
    }
}
