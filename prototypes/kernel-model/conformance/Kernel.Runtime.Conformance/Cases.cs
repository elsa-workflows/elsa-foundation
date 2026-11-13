using System.Diagnostics;
using Kernel.Sim;

namespace Kernel.Runtime.Conformance;

/// <param name="Setup">The case's own timing or partitions; the suite's default otherwise.</param>
public sealed record RuntimeCase(string Name, Level Level, Func<Harness, Task> Body, HarnessSetup? Setup = null, bool Fence = false);

public sealed record RuntimeOutcome(RuntimeCase Case, bool Passed, string Detail, TimeSpan Took);

/// <summary>
/// The runtime's semantic cases. Each runs on a fresh cluster over a fresh store. Required: the kernel depends on it.
/// Reference: what the built-in runtime does where the seam is silent; a runtime that differs is reported, not failed.
/// After every case the watcher's seam violations and the recorded commits' violations of invariant 1 fail it too.
/// </summary>
public static class Cases
{
    /// <summary>The suite's default: four partitions, a short lease, a short poll interval.</summary>
    public static readonly HarnessSetup Default = new(4, new RuntimeTiming(TimeSpan.FromMilliseconds(800), TimeSpan.FromMilliseconds(400)));

    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(3);

    private static HarnessSetup WithPoll(TimeSpan poll) => Default with { Timing = Default.Timing with { PollInterval = poll } };

    private static void That(bool condition, string message)
    {
        if (!condition) throw new ConformanceException(message);
    }

    private static string Holders(Harness h) => string.Join(", ", h.Watcher.Holders(h.Partitions).Select((n, p) => $"{p}: [{string.Join(" ", n)}]"));

    /// <summary>The live node that holds the partition now.</summary>
    private static IRuntimeNode Holder(Harness h, int partition)
    {
        var name = h.Watcher.Holders(h.Partitions)[partition].Single();
        return h.Alive.Single(n => n.Name == name);
    }

    private static async Task Settle(Harness h, string what = "every scope held by exactly one node")
    {
        That(await h.WaitAsync(h.Settled, h.Lease * 4 + Slack), $"not {what} in time: {Holders(h)}");
    }

    private static async Task<bool> Finished(Harness h, string id, TimeSpan within) =>
        await Harness.WaitAsync(async () => (await h.RowAsync(id))?.Status == InstanceStatus.Finished, within);

    private static Message Start(string id, int count = 1) => new("start-" + id, new InstanceRef(id), Kinds.Start, Start: Sequence.Spec(count, Sequence.Balanced));

    private static Message Token(int partition, string counter, string token) =>
        new("add-" + token, InstanceRef.In(partition, counter), "add", Json.Write(new Add(token)), Start: Counter.Spec);

    /// <summary>The counter applied every token once, in order.</summary>
    private static async Task Applied(Harness h, int partition, string counter, IReadOnlyList<string> tokens, TimeSpan within)
    {
        var id = InstanceRef.In(partition, counter).Id;
        List<string> applied = [];
        var done = await Harness.WaitAsync(async () =>
        {
            applied = (await h.RowAsync(id)) is { } row ? Json.Read<CounterState>(row.State).Tokens : [];
            return applied.Count >= tokens.Count;
        }, within);
        That(done, $"{id} applied {applied.Count} of {tokens.Count} tokens in time");
        That(applied.SequenceEqual(tokens), $"{id} applied [{string.Join(" ", applied)}], not [{string.Join(" ", tokens)}] once each in order");
    }

    public static IReadOnlyList<RuntimeCase> All { get; } =
    [
        new("placement: every scope is held by exactly one node, and an idle one keeps its lease", Level.Required, async h =>
        {
            for (var i = 0; i < 3; i++) await h.StartAsync();
            await Settle(h);
            await Task.Delay(h.Lease * 4);      // idle: no commit renews a lease, only the pass's beat
            That(h.Settled(), $"after an idle spell: {Holders(h)}");
            for (var p = 0; p < h.Partitions; p++)
                That(await h.Outside.TakeAsync(p, "probe", h.Lease) is null, $"partition {p}'s lease ran out while it was held and idle: another node could take it");
        }),

        new("placement: no activation comes or goes while the cluster is idle", Level.Reference, async h =>
        {
            for (var i = 0; i < 2; i++) await h.StartAsync();
            await Settle(h);
            await Task.Delay(h.Lease * 2);
            var before = h.Watcher.Activations;
            await Task.Delay(h.Lease * 4);
            That(h.Watcher.Activations == before, $"{h.Watcher.Activations - before} activations while idle");
        }),

        new("poll: a message added without a hint is handled within the poll interval", Level.Required, async h =>
        {
            await h.StartAsync();
            await Settle(h);
            var id = InstanceRef.In(1, "quiet").Id;
            await h.AddAsync(Start(id), wake: false);
            That(await Finished(h, id, h.Setup.Timing.PollInterval + Slack), $"{id} did not finish within the poll interval");
        }),

        new("hint: a message with a hint is handled well before the poll interval", Level.Reference, async h =>
        {
            await h.StartAsync();
            await Settle(h);
            var id = InstanceRef.In(2, "hinted").Id;
            await h.AddAsync(Start(id));
            That(await Finished(h, id, TimeSpan.FromSeconds(2)), $"{id} did not finish within 2 s of its hint (poll interval 20 s)");
        }, WithPoll(TimeSpan.FromSeconds(20))),

        new("timer: a delayed message is handled when due, without a hint or a poll", Level.Reference, async h =>
        {
            await h.StartAsync();
            await Settle(h);
            var id = InstanceRef.In(0, "timed").Id;
            await h.AddAsync(new Message("start-" + id, new InstanceRef(id), Kinds.Start, Start: new StartSpec("parent", "p", Json.Write(new ParentInput(1, 1500)))));
            That(await Finished(h, id, TimeSpan.FromMilliseconds(1500) + TimeSpan.FromSeconds(2.5)), $"{id} did not finish within 2.5 s of its child's timer (poll interval 20 s)");
        }, WithPoll(TimeSpan.FromSeconds(20))),

        new("crash: a crashed node's scopes are taken over, and their messages handled once", Level.Required, async h =>
        {
            await h.StartAsync();
            await h.StartAsync();
            await Settle(h);
            var a = Holder(h, 0);       // the node that holds partition 0 dies
            await h.KillAsync(a);
            var tokens = Enumerable.Range(0, 10).Select(i => "k" + i).ToList();
            for (var p = 0; p < h.Partitions; p++)
                foreach (var token in tokens) await h.AddAsync(Token(p, "c", $"{token}.{p}"));
            await Settle(h, "every scope taken over");
            for (var p = 0; p < h.Partitions; p++) await Applied(h, p, "c", tokens.Select(t => $"{t}.{p}").ToList(), h.Lease * 3 + Slack);
            That(h.Watcher.Holders(h.Partitions).All(n => n.All(name => name != a.Name)), $"the dead node still holds a scope: {Holders(h)}");
        }),

        new("stop: a stopped node's scopes are taken over before its lease would have run out", Level.Reference, async h =>
        {
            await h.StartAsync();
            await h.StartAsync();
            await Settle(h);
            var a = Holder(h, 0);
            var watch = Stopwatch.StartNew();
            await h.StopAsync(a);
            await Settle(h, "every scope taken over");
            That(watch.Elapsed < h.Lease, $"the takeover took {watch.ElapsedMilliseconds} ms, a lease is {h.Lease.TotalMilliseconds} ms");
        }, Default with { Timing = new RuntimeTiming(TimeSpan.FromSeconds(6), TimeSpan.FromMilliseconds(400)) }),

        new("fence: a node whose scope was taken while it stalled is told, and activates it again", Level.Required, async h =>
        {
            var a = await h.StartAsync();
            await Settle(h);
            h.StoreOf(a.Name).Stall(h.Lease * 2);       // the node pauses; its lease on partition 0 runs out meanwhile
            await Task.Delay(h.Lease + TimeSpan.FromMilliseconds(200));
            var thief = await h.Outside.TakeAsync(0, "thief", h.Lease);      // another process takes it, then dies without releasing it
            That(thief is not null, "the stalled node's lease on partition 0 did not run out");
            await Task.Delay(h.Lease * 2);
            That(h.Watcher.FencedOn(a.Name) > 0, "no pass of the stalled node learned that it lost partition 0");
            await Settle(h, "partition 0 activated again");
            var tokens = Enumerable.Range(0, 5).Select(i => "f" + i).ToList();
            foreach (var token in tokens) await h.AddAsync(Token(0, "c", token));
            await Applied(h, 0, "c", tokens, h.Lease * 3 + Slack);
        }, Fence: true),

        new("split: two activations of one scope at once, on two sides; the fence lets one of them write", Level.Required, async h =>
        {
            await h.StartAsync(side: 0);
            await h.StartAsync(side: 1);
            await Settle(h);
            var holder = h.Watcher.Holders(h.Partitions)[0].Single();
            var tokens = Enumerable.Range(0, 20).Select(i => "s" + i).ToList();
            // The holder's next commit is in flight when the network splits: it executes three leases later, under an
            // epoch that is no longer the partition's. Meanwhile the other side activates the scope and handles the same
            // messages.
            h.StoreOf(holder).StallNextCommit(h.Lease * 3, partition: 0);
            foreach (var token in tokens) await h.AddAsync(Token(0, "c", token));
            await Applied(h, 0, "c", tokens, h.Lease * 4 + Slack);
            await Task.Delay(h.Lease * 2);       // the stalled commit executes
            That(h.Watcher.Overlaps > 0, "no two activations of one scope were ever held at once: the case did not split");
            That(h.Recorded.Refused > 0 || h.Watcher.FencedOn(holder) > 0, "the stale side was never refused");
            await Applied(h, 0, "c", tokens, TimeSpan.Zero);
        }, Fence: true),

        new("join: a node that joins gets a share of the scopes", Level.Reference, async h =>
        {
            await h.StartAsync();
            await Settle(h);
            var b = await h.StartAsync();
            That(await h.WaitAsync(() => h.Watcher.Holders(h.Partitions).Any(n => n.Contains(b.Name)), h.Lease * 4 + Slack), $"the new node holds nothing: {Holders(h)}");
        }),
    ];

    public static async Task<List<RuntimeOutcome>> RunAsync(IRuntimeProvider runtime, IStoreProvider store, bool brokenFence = false, Func<RuntimeCase, bool>? filter = null, TextWriter? output = null)
    {
        output ??= Console.Out;
        var outcomes = new List<RuntimeOutcome>();
        foreach (var @case in All.Where(c => filter?.Invoke(c) ?? true))
        {
            var watch = Stopwatch.StartNew();
            string? failure = null;
            try
            {
                await using var harness = await Harness.CreateAsync(runtime, store, (@case.Setup ?? Default) with { BrokenFence = brokenFence });
                try { await @case.Body(harness); }
                catch (ConformanceException e) { failure = e.Message; }
                var wrong = harness.Watcher.Violations.Concat(harness.Recorded.CheckOneWriter()).Concat(harness.Provided.Ghosts.Select(g => "GHOST " + g)).Concat(harness.Reused.Select(n => $"F9 the runtime reused the node name {n}")).ToList();
                if (failure is null && wrong.Count > 0) failure = string.Join("; ", wrong.Take(5));
                if (failure is null && @case.Level == Level.Required) failure = await EndAsync(harness);
            }
            catch (Exception e) when (e is not ConformanceException) { failure = $"threw {e.GetType().Name}: {e.Message}"; }
            var outcome = new RuntimeOutcome(@case, failure is null, failure ?? "", watch.Elapsed);
            outcomes.Add(outcome);
            var mark = outcome.Passed ? "pass" : @case.Level == Level.Required ? "FAIL" : "differ";
            output.WriteLine($"{mark,-6} {@case.Name}  ({watch.ElapsedMilliseconds} ms){(outcome.Passed ? "" : "\n         " + outcome.Detail)}");
        }
        output.WriteLine($"{runtime.Name} on {store.Name}{(brokenFence ? ", FENCE BROKEN ON PURPOSE" : "")}: {outcomes.Count(o => o.Passed)} passed, " +
            $"{outcomes.Count(o => !o.Passed && o.Case.Level == Level.Required)} required failed, {outcomes.Count(o => !o.Passed && o.Case.Level == Level.Reference)} reference differ");
        return outcomes;
    }

    /// <summary>The case's end state: no message left behind, no instance parked.</summary>
    private static async Task<string?> EndAsync(Harness h)
    {
        for (var p = 0; p < h.Partitions; p++)
            if ((await h.Outside.PollAsync(p, long.MaxValue, TimeSpan.FromDays(3650), 1000)).Messages.FirstOrDefault(m => m.Message.Due is null) is { } left)
                if (!await Harness.WaitAsync(async () => (await h.Outside.PollAsync(p, long.MaxValue, TimeSpan.FromDays(3650), 1000)).Messages.All(m => m.Message.Id != left.Message.Id), h.Lease * 3 + Slack))
                    return $"END message {left.Message.Id} ({left.Message.Kind} to {left.Message.Target}) left behind";
        return null;
    }
}
