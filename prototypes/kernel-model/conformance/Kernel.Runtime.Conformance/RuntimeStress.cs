using System.Diagnostics;

namespace Kernel.Runtime.Conformance;

public sealed record RuntimeStressOptions
{
    public int Seconds { get; init; } = 20;
    public int Messages { get; init; } = 2000;
    public int Partitions { get; init; } = 4;

    /// <summary>Nodes alive on each side of the split at any time, give or take the one being restarted.</summary>
    public int NodesPerSide { get; init; } = 2;

    public TimeSpan Lease { get; init; } = TimeSpan.FromMilliseconds(600);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(300);
    public double FaultRate { get; init; } = 0.005;

    /// <summary>How often the chaos does something, in milliseconds (a range).</summary>
    public (int Min, int Max) ChaosEvery { get; init; } = (100, 400);
    public bool BrokenFence { get; init; }
    public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(120);
}

/// <summary>
/// The store suite's workload (<see cref="Workload"/>) driven through a runtime instead of by hand: nodes on two sides of
/// a network split for the whole run, so that each side's runtime activates every scope; nodes that crash, stop or
/// restart under a new name; stalls longer than the lease; commits held in flight while the other side takes over; and
/// injected connection failures. Then calm, settle, stop every node, and check the end state of FINDINGS section 4.
/// </summary>
public static class RuntimeStress
{
    public static async Task<int> RunAsync(IRuntimeProvider runtime, IStoreProvider store, RuntimeStressOptions o, TextWriter? output = null)
    {
        output ??= Console.Out;
        var setup = new HarnessSetup(o.Partitions, new RuntimeTiming(o.Lease, o.PollInterval), o.FaultRate, o.BrokenFence);
        await using var h = await Harness.CreateAsync(runtime, store, setup);
        var watch = Stopwatch.StartNew();
        for (var side = 0; side < 2; side++)
            for (var i = 0; i < o.NodesPerSide; i++) await h.StartAsync(side);

        var calm = false;
        int kills = 0, stops = 0, stalls = 0, inFlight = 0;
        var chaos = Task.Run(async () =>
        {
            var random = new Random(13);
            while (!Volatile.Read(ref calm))
            {
                await Task.Delay(random.Next(o.ChaosEvery.Min, o.ChaosEvery.Max));
                var alive = h.Alive;
                if (alive.Count == 0) continue;
                var node = alive[random.Next(alive.Count)];
                switch (random.Next(10))
                {
                    case 0:     // a crash, and a new process on the same side
                        kills++;
                        await h.KillAsync(node);
                        await h.StartAsync(node.Side);
                        break;
                    case 1:     // a graceful stop, and a new process
                        stops++;
                        await h.StopAsync(node);
                        await h.StartAsync(node.Side);
                        break;
                    case < 5:   // a pause longer than the lease
                        stalls++;
                        h.StoreOf(node.Name).Stall(o.Lease * (1 + random.NextDouble() * 2));
                        break;
                    case < 8:   // the node's next commit in some partition is in flight when the network splits
                        inFlight++;
                        h.StoreOf(node.Name).StallNextCommit(o.Lease * (1 + random.NextDouble() * 2), random.Next(o.Partitions));
                        break;
                    default:    // a hint from outside, through some node
                        await node.WakeAsync(InstanceRef.In(random.Next(o.Partitions), ""));
                        break;
                }
            }
        });

        var workload = new Workload(o.Partitions, o.Messages, o.Seconds);
        var producer = Task.Run(() => workload.ProduceAsync(h.Outside, h.Provided.NewClient()));
        await Task.Delay(o.Seconds * 1000);
        await producer;
        Volatile.Write(ref calm, true);
        h.Faulty.Calm = true;
        await chaos;
        output.WriteLine($"chaos over after {watch.Elapsed.TotalSeconds:0} s: {kills} crashes, {stops} stops, {stalls} stalls, {inFlight} commits held in flight, " +
            $"{h.Faulty.Before} + {h.Faulty.After} calls failed before + after executing, {h.Turns} turns so far; settling");

        await workload.SettleAsync(h.Outside, o.Settle);
        foreach (var node in h.Alive) await h.StopAsync(node);

        var seam = h.Watcher.Violations.Concat(h.Reused.Select(n => $"F9 the runtime reused the node name {n}"));
        var (wrong, instances, journal, tokens) = await workload.CheckAsync(h.Outside, o.Lease, h.Recorded.CheckOneWriter(), h.Provided.Ghosts.Take(10).Select(g => "GHOST " + g).Concat(seam), output);

        output.WriteLine($"round 3: {workload.Lifecycles} trees terminated ({workload.Deleted} deleted), {workload.OnEvents} instances started by an event");
        output.WriteLine($"{o.Messages} outside messages, {instances} instances, {h.Turns} turns run, {h.Recorded.Commits.Count} commits answered landed, {h.Recorded.Refused} refused by the fence or a count, {journal} journal rows, {tokens} counter tokens checked");
        output.WriteLine($"the runtime: {h.Watcher.Activations} activations, {h.Watcher.Second} asked for while another node held the scope, {h.Watcher.Declined} declined by layer 2, " +
            $"{h.Watcher.Overlaps} pairs held at once, {h.Watcher.Fenced} passes told by the fence that they had lost the scope");
        foreach (var (kind, n) in h.Recorded.Failures.OrderByDescending(f => f.Value).Take(12)) output.WriteLine($"  commit failure x{n}: {kind}");
        if (h.Watcher.Overlaps == 0) wrong.Add("SPLIT no two activations of one scope were ever held at once: the run did not test the fence");
        output.WriteLine(wrong.Count == 0 ? "end state: all properties hold" : $"end state: {wrong.Count} violations\n  " + string.Join("\n  ", wrong.Take(25)));
        return wrong.Count == 0 ? 0 : 1;
    }
}
