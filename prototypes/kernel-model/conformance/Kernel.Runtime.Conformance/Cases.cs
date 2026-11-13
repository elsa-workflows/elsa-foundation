using System.Diagnostics;
using Kernel.Sim;

namespace Kernel.Runtime.Conformance;

/// <param name="Setup">The case's own timing or partitions; the suite's default otherwise.</param>
public sealed record RuntimeCase(string Name, Level Level, Func<Harness, Task> Body, HarnessSetup? Setup = null, bool Fence = false);

/// <param name="Caught">The case failed on what the fence guards (a check that throws <see cref="FenceException"/>, or a violation of invariant 1 or of the store's ghosts), not on a precondition or a timeout.</param>
public sealed record RuntimeOutcome(RuntimeCase Case, bool Passed, string Detail, TimeSpan Took, bool Caught = false);

/// <summary>A check of what the fence guards failed: a stale writer was not refused, or its writes show.</summary>
public sealed class FenceException(string message) : Exception(message);

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
        if (!applied.SequenceEqual(tokens)) throw new FenceException($"{id} applied [{string.Join(" ", applied)}], not [{string.Join(" ", tokens)}] once each in order");
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
            var a = Holder(h, 0);       // the node that holds partition 0 dies while it has its messages in hand
            var held = Enumerable.Range(0, h.Partitions).Where(p => h.Watcher.Holders(h.Partitions)[p].Contains(a.Name)).ToList();
            var tokens = Enumerable.Range(0, 30).Select(i => "k" + i).ToList();
            var id = InstanceRef.In(0, "c").Id;
            var adding = Task.Run(async () =>
            {
                foreach (var token in tokens) await h.AddAsync(Token(0, "c", token));
            });
            That(await Harness.WaitAsync(async () => (await h.RowAsync(id)) is { } row && Json.Read<CounterState>(row.State).Tokens.Count >= 3, Slack),
                $"{a.Name} handled none of partition 0's messages before the crash");
            var killing = Stopwatch.StartNew();
            await h.KillAsync(a);
            // A crash gives nothing up: right after the kill every scope the node held still has its lease, so a take is
            // refused. The node renewed it within a third of a lease before the kill, so two thirds are left at least;
            // the check counts only if the kill took less than that, with half a second to spare.
            var bound = h.Lease * 2 / 3 - TimeSpan.FromMilliseconds(500);
            That(killing.Elapsed < bound, $"the kill took {killing.ElapsedMilliseconds} ms: the check that a crash gives nothing up needs it under {bound.TotalMilliseconds} ms");
            foreach (var p in held)
                That(await h.Outside.TakeAsync(p, "probe", h.Lease) is null, $"the killed node gave partition {p} up: a crash gives nothing up");
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

        new("fence: a node whose scope was taken while it stalled is told, and activates it again", Level.Required, async h =>
        {
            var a = await h.StartAsync();
            await Settle(h);
            var store = h.StoreOf(a.Name);
            store.Stall(System.Threading.Timeout.InfiniteTimeSpan);      // the node pauses until the thief has its lease, however slow the store
            int commits, refused;
            try
            {
                await Task.Delay(h.Lease + TimeSpan.FromMilliseconds(200));
                var thief = await h.Outside.TakeAsync(0, "thief", h.Lease);      // another process takes it, then dies without releasing it
                That(thief is not null, "the stalled node's lease on partition 0 did not run out");
                // The stalled node must try to write after its scope was taken (its renewal beat at the latest); only
                // then is the store's answer the fence's.
                (commits, refused) = (store.Commits, store.Refused);
            }
            finally { store.Resume(); }
            That(await h.WaitAsync(() => store.Commits > commits, h.Lease * 3), "the stalled node made no commit after its scope was taken: the case did not test the fence");
            if (!await h.WaitAsync(() => store.Refused > refused, h.Lease))
                throw new FenceException("the store did not refuse the stalled node's commit: it did not learn that it lost partition 0");
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
            var held = h.StoreOf(holder).Held;
            That(await Task.WhenAny(held, Task.Delay(h.Lease * 5)) == held, "the holder's commit was never held: it handled no message in partition 0");
            That(h.Watcher.OverlapsOn(0, holder) > 0, "partition 0 was never held on both sides at once: the case did not split");
            if (held.Result != "refused") throw new FenceException($"the stale side's commit was not refused by the fence: it {held.Result}");
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
            var caught = false;
            try
            {
                await using var harness = await Harness.CreateAsync(runtime, store, (@case.Setup ?? Default) with { BrokenFence = brokenFence });
                try
                {
                    await @case.Body(harness).WaitAsync(Timeout);
                    if (@case.Level == Level.Required) await EndAsync(harness);
                }
                catch (FenceException e) { (failure, caught) = (e.Message, true); }
                catch (ConformanceException e) { failure = e.Message; }
                catch (TimeoutException) { failure = $"the case did not end within {Timeout.TotalSeconds} s"; }
                catch (Exception e) { failure = $"threw {e.GetType().Name}: {e.Message}"; }      // the teardown and the checks below still run
                // What the nodes do on their way out is checked too. A stop that hangs fails the case, not the suite.
                try { await harness.StopAllAsync().WaitAsync(Timeout); }
                catch (TimeoutException) { failure ??= $"stopping the nodes did not end within {Timeout.TotalSeconds} s"; }
                var wrong = harness.Violations();
                if (failure is null && wrong.Count > 0) failure = string.Join("; ", wrong.Take(5));
                caught |= wrong.Any(w => w.StartsWith("I1 ", StringComparison.Ordinal) || w.StartsWith("GHOST ", StringComparison.Ordinal));
            }
            catch (Exception e) when (e is not ConformanceException and not FenceException) { failure = $"threw {e.GetType().Name}: {e.Message}"; }
            var outcome = new RuntimeOutcome(@case, failure is null, failure ?? "", watch.Elapsed, failure is not null && caught);
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
