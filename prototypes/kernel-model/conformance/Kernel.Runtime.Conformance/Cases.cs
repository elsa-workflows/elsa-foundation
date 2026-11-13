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

    /// <summary>Every scope held by exactly one node, and no holder changed for two balancing rounds: the cluster has its shares.</summary>
    private static async Task Steady(Harness h)
    {
        await Settle(h);
        var (seen, since) = (Holders(h), Stopwatch.StartNew());
        That(await h.WaitAsync(() =>
        {
            var now = Holders(h);
            if (now != seen) (seen, since) = (now, Stopwatch.StartNew());
            return h.Settled() && since.Elapsed >= h.Lease / 2;
        }, h.Lease * 4 + Slack), $"the holders did not stay put for two balancing rounds: {Holders(h)}");
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
            var within = h.Setup.Timing.PollInterval * 2 + TimeSpan.FromMilliseconds(500);
            That(await Finished(h, id, within), $"{id} did not finish within {within.TotalMilliseconds} ms: twice the poll interval and half a second");
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
            await Steady(h);
            var a = Holder(h, 0);       // the node that holds partition 0 dies while it has its messages in hand
            var tokens = Enumerable.Range(0, 30).Select(i => "k" + i).ToList();
            var id = InstanceRef.In(0, "c").Id;
            var adding = Task.Run(async () =>
            {
                foreach (var token in tokens) await h.AddAsync(Token(0, "c", token));
            });
            That(await Harness.WaitAsync(async () => (await h.RowAsync(id)) is { } row && Json.Read<CounterState>(row.State).Tokens.Count >= 3, Slack),
                $"{a.Name} handled none of partition 0's messages before the crash");
            var held = Enumerable.Range(0, h.Partitions).Where(p => h.Watcher.Holders(h.Partitions)[p].Contains(a.Name)).ToList();
            var killing = Stopwatch.StartNew();
            await h.KillAsync(a);
            // A crash gives nothing up: right after the kill every scope the node held still has its lease, so a take is
            // refused. The node renewed it within a third of a lease before the kill, so two thirds are left at least;
            // the kill must return within that, with half a second to spare, or the case fails. The probe's own lease
            // is short, so a probe that wins delays the takeover little.
            var bound = h.Lease * 2 / 3 - TimeSpan.FromMilliseconds(500);
            That(killing.Elapsed < bound, $"the kill took {killing.ElapsedMilliseconds} ms: the check that a crash gives nothing up needs it under {bound.TotalMilliseconds} ms");
            foreach (var p in held)
                That(await h.Outside.TakeAsync(p, "probe", h.Lease / 6) is null, $"the killed node gave partition {p} up: a crash gives nothing up");
            await adding;
            await Settle(h, "every scope taken over");
            await Applied(h, 0, "c", tokens, h.Lease * 3 + Slack);
        }, Default with { Timing = Default.Timing with { Lease = TimeSpan.FromSeconds(6) } }),

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
                try
                {
                    await @case.Body(harness).WaitAsync(Timeout);
                    if (@case.Level == Level.Required) await EndAsync(harness);
                }
                catch (ConformanceException e) { failure = e.Message; }
                catch (TimeoutException) { failure = $"the case did not end within {Timeout.TotalSeconds} s"; }
                catch (Exception e) { failure = $"threw {e.GetType().Name}: {e.Message}"; }      // the teardown and the checks below still run
                // What the nodes do on their way out is checked too. A stop that hangs fails the case, not the suite.
                try { await harness.StopAllAsync().WaitAsync(Timeout); }
                catch (TimeoutException) { failure ??= $"stopping the nodes did not end within {Timeout.TotalSeconds} s"; }
                var wrong = harness.Violations();
                if (failure is null && wrong.Count > 0) failure = string.Join("; ", wrong.Take(5));
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

    /// <summary>How long a case may run, and its nodes take to stop.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    /// <summary>The case's end state: no message left behind that is due, in any partition.</summary>
    private static async Task EndAsync(Harness h)
    {
        Stored? left = null;
        async Task<bool> Done()
        {
            left = null;
            for (var p = 0; p < h.Partitions && left is null; p++)
            {
                var polled = await h.Outside.PollAsync(p, long.MaxValue, TimeSpan.FromDays(3650), 1000);
                left = polled.Messages.FirstOrDefault(m => m.Message.Due is not { } due || due <= polled.Now);
            }
            return left is null;
        }
        if (!await Harness.WaitAsync(Done, h.Lease * 3 + Slack))
            throw new ConformanceException($"END message {left!.Message.Id} ({left.Message.Kind} to {left.Message.Target}) left behind");
    }
}
