using System.Diagnostics;
using Kernel.Sim;

namespace Kernel.Runtime.Conformance;

/// <summary>
/// What a pass costs with several scopes on one node (K1 slice 6), counted on layer 2's side of the seam: one node holds
/// every partition, on the default lease (6 s) and poll interval (1 s). An idle spell, then a burst of counter tokens
/// spread over the partitions, each added with a hint. The counts are the node's: calls to a scope's pass and the worker
/// passes they ran, its partition polls (one per held partition for each poll of the set), its commits with turns and
/// the partitions those spanned, and every store call (the built-in runtime's balancing heartbeats among them).
/// </summary>
public static class RuntimeCost
{
    public static async Task<int> RunAsync(IRuntimeProvider runtime, IStoreProvider store, int partitions, int seconds, int tokens, TextWriter? output = null)
    {
        output ??= Console.Out;
        var setup = new HarnessSetup(partitions, new RuntimeTiming(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(1)));
        await using var h = await Harness.CreateAsync(runtime, store, setup);
        var a = await h.StartAsync();
        if (!await h.WaitAsync(h.Settled, TimeSpan.FromSeconds(30)))
        {
            output.WriteLine("the node did not hold every partition within 30 s");
            return 1;
        }
        await Task.Delay(TimeSpan.FromSeconds(2));      // the takes' first passes
        var (host, node) = (h.HostOf(a.Name), h.StoreOf(a.Name));
        (long, long, long, long, long, long) Now() => (host.PassCounts.Calls, host.PassCounts.Passes, node.Counts.Polls, node.Counts.TurnCommits, node.Counts.TurnPartitions, node.Counts.Calls);
        string Row(string what, (long, long, long, long, long, long) from, (long, long, long, long, long, long) to, double per) =>
            $"| {runtime.Name} | {partitions} | {what} | {(to.Item1 - from.Item1) / per:0.#} | {(to.Item2 - from.Item2) / per:0.#} | {(to.Item3 - from.Item3) / per:0.#} | " +
            $"{(to.Item4 - from.Item4) / per:0.#} | {(to.Item4 == from.Item4 ? "-" : ((double)(to.Item5 - from.Item5) / (to.Item4 - from.Item4)).ToString("0.0"))} | {(to.Item6 - from.Item6) / per:0.#} |";

        var idle = Now();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        var busy = Now();
        output.WriteLine("| Runtime | Partitions | Window | Pass calls | Worker passes | Partition polls | Commits with turns | Partitions per commit | Store calls |");
        output.WriteLine("|---|---|---|---|---|---|---|---|---|");
        output.WriteLine(Row("idle, per second", idle, busy, seconds));

        var watch = Stopwatch.StartNew();
        for (var i = 0; i < tokens; i++)
        {
            var p = i % partitions;
            await h.AddAsync(new Message($"add-t{i}", InstanceRef.In(p, "c"), "add", Json.Write(new Add($"t{i}")), Start: Counter.Spec));
        }
        var done = await Harness.WaitAsync(async () =>
        {
            for (var p = 0; p < partitions; p++)
                if ((await h.RowAsync(InstanceRef.In(p, "c").Id)) is not { } row || Json.Read<CounterState>(row.State).Tokens.Count < (tokens - p + partitions - 1) / partitions) return false;
            return true;
        }, TimeSpan.FromSeconds(60));
        var end = Now();
        output.WriteLine(Row($"{tokens} tokens, in all", busy, end, 1));
        output.WriteLine($"{runtime.Name}: the burst {(done ? "applied" : "DID NOT APPLY")} in {watch.ElapsedMilliseconds} ms");
        return done ? 0 : 1;
    }
}
