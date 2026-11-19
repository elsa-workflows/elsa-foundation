using System.Diagnostics;
using Kernel.Sim;

namespace Kernel.Runtime.Conformance;

/// <param name="Setup">The case's own timing or partitions; the suite's default otherwise.</param>
/// <param name="OwnedSet">The case reads the node's one worker (K1 D1): it runs only for a runtime with <see cref="IRuntimeProvider.OneWorkerPerNode"/>.</param>
/// <param name="Placement">The case tests this placement: it runs only for a runtime that places scopes so (<see cref="IRuntimeProvider.Placement"/>).</param>
/// <param name="AnyCount">The case holds at any partition count (semantic <c>--partitions</c>); false for one that names the partitions of its count.</param>
public sealed record RuntimeCase(string Name, Level Level, Func<Harness, Task> Body, HarnessSetup? Setup = null, bool Fence = false, bool OwnedSet = false, Placement? Placement = null, bool AnyCount = true);

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

    // K1 slice 7: a takeover above 1,024 partitions takes longer than the bounds were set for (a crash at 4,096 settles in
    // minutes, slice 8 measures why), so the settling bounds and a case's time grow with the count above 1,024.
    private static TimeSpan Scaled(int partitions, TimeSpan bound) => partitions > 1024 ? bound * (partitions / 1024.0) : bound;

    private static HarnessSetup WithPoll(TimeSpan poll) => Default with { Timing = Default.Timing with { PollInterval = poll } };

    private static void That(bool condition, string message)
    {
        if (!condition) throw new ConformanceException(message);
    }

    private static string Set(Worker worker) => string.Join(" ", worker.Fences.Select(f => f.Partition));

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
        }, Scaled(h.Partitions, h.Lease * 4 + Slack)), $"the holders did not stay put for two balancing rounds: {Holders(h)}");
    }

    private static async Task Settle(Harness h, string what = "every scope held by exactly one node")
    {
        That(await h.WaitAsync(h.Settled, Scaled(h.Partitions, h.Lease * 4 + Slack)), $"not {what} in time: {Holders(h)}");
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
            var holders = h.Watcher.Holders(h.Partitions);       // once: it reads every watched activation
            var held = Enumerable.Range(0, h.Partitions).Where(p => holders[p].Contains(a.Name)).ToList();
            var killing = Stopwatch.StartNew();
            var killedAt = Environment.TickCount64;
            await h.KillAsync(a);
            // A crash gives nothing up: right after the kill every scope the node held still has its lease, so a take is
            // refused. The node renewed it within a third of a lease before the kill, so two thirds are left at least;
            // the kill must return within that, with half a second to spare, or the case fails. The probe's own lease
            // is short, so a probe that wins delays the takeover little.
            var bound = h.Lease * 2 / 3 - TimeSpan.FromMilliseconds(500);
            That(killing.Elapsed < bound, $"the kill took {killing.ElapsedMilliseconds} ms: the check that a crash gives nothing up needs it under {bound.TotalMilliseconds} ms");
            foreach (var p in held)
                if (await h.Outside.TakeAsync(p, "probe", h.Lease / 6) is not null)
                    // A release the node had begun before the kill was its own decision, not the crash's.
                    That(h.StoreOf(a.Name).ReleaseBegan(p) is { } began && began < killedAt, $"the killed node gave partition {p} up: a crash gives nothing up");
            await adding;
            await Settle(h, "every scope taken over");
            Console.WriteLine($"         takeover: {held.Count} of {h.Partitions} scopes held by the crashed node, settled {Environment.TickCount64 - killedAt} ms after the kill");
            await Applied(h, 0, "c", tokens, Scaled(h.Partitions, h.Lease * 3 + Slack));
        }, Default with { Timing = Default.Timing with { Lease = TimeSpan.FromSeconds(6) } }),

        new("count: a node whose workers place another count than its store and runtime is refused at its start", Level.Required, async h =>
        {
            string? refused = null;
            try { await h.StartAsync(); }
            catch (Exception e) { refused = e.ToString(); }
            That(refused?.Contains("The partition counts disagree", StringComparison.Ordinal) == true, $"the node started, or failed otherwise: {refused ?? "started"}");
            That(h.Watcher.Holders(h.Partitions).All(n => n.Count == 0), $"a scope was activated: {Holders(h)}");
        }, Default with { WorkerPartitions = 8 }, OwnedSet: true, AnyCount: false),

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
            // Hold an already admitted write on its way to the store. A stall before admission instead permits
            // the local lease deadline to prevent the stale write entirely, which cannot exercise this database fence.
            store.StallNextCommit(System.Threading.Timeout.InfiniteTimeSpan, partition: 0);
            var tokens = Enumerable.Range(0, 5).Select(i => "f" + i).ToList();
            try
            {
                await h.AddAsync(Token(0, "c", tokens[0]));
                That(await h.WaitAsync(() => store.Stalls > 0, h.Lease * 3), "no admitted turn commit entered the hold");
                That(await Harness.WaitAsync(async () => await h.Outside.TakeAsync(0, "thief", h.Lease) is not null, h.Lease * 3),
                    "the stalled node's lease on partition 0 did not run out");
            }
            finally { store.Resume(); }
            var held = store.Held;
            That(await Task.WhenAny(held, Task.Delay(h.Lease * 3)) == held, "the admitted stale commit never returned");
            if (await held != "refused" || store.HeldRefused != 1)
                throw new FenceException($"the admitted stale commit was not refused by the store: {await held}");
            // An owned set: the refusal reset the worker, and it took every other partition back by a guarded retake (at
            // their epochs, to new ones), not by a release and a fresh take. The thief's 0 was left alone.
            var all = Enumerable.Range(0, h.Partitions).ToList();
            if (h.WorkerOf(a.Name) is not null)
                That(await h.WaitAsync(() => all.Skip(1).All(p => store.Retaken(p) is not null), Slack) && store.Retaken(0) is null && all.All(p => store.ReleaseBegan(p) is null),
                    $"after the refusal: retaken [{string.Join(", ", all.Select(p => $"{p}: {store.Retaken(p)}"))}], released [{string.Join(" ", all.Where(p => store.ReleaseBegan(p) is not null))}]");
            await Settle(h, "partition 0 activated again");
            foreach (var token in tokens.Skip(1)) await h.AddAsync(Token(0, "c", token));
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

        new("owned set: one worker serves a scattered set, and gains partitions while it runs", Level.Required, async h =>
        {
            // A probe holds 1 and 3, so the node's set is {0, 2}.
            var probe = new Dictionary<int, long>();
            foreach (var p in new[] { 1, 3 }) probe[p] = (await h.Outside.TakeAsync(p, "probe", TimeSpan.FromHours(1)))!.Epoch;
            var a = await h.StartAsync();
            var worker = h.WorkerOf(a.Name)!;
            That(await h.WaitAsync(() => Set(worker) == "0 2", h.Lease * 4 + Slack), $"the node does not hold {{0, 2}}: [{Set(worker)}]");
            var kept = (worker.EpochOf(0), worker.EpochOf(2));
            foreach (var p in new[] { 0, 2 }) await h.AddAsync(Token(p, "c", "s" + p));
            foreach (var p in new[] { 0, 2 }) await Applied(h, p, "c", ["s" + p], Slack);
            foreach (var (p, epoch) in probe) await h.Outside.ReleaseAsync(p, epoch);
            await Settle(h, "the probe's partitions taken by the node");
            That(Set(worker) == "0 1 2 3" && (worker.EpochOf(0), worker.EpochOf(2)) == kept,
                $"gaining 1 and 3 changed the epochs of 0 and 2 (before {kept}): [{string.Join(", ", worker.Fences)}]");
            foreach (var p in Enumerable.Range(0, 4)) await h.AddAsync(Token(p, "c", "t" + p));
            foreach (var p in Enumerable.Range(0, 4)) await Applied(h, p, "c", p % 2 == 0 ? ["s" + p, "t" + p] : ["t" + p], Slack);
        }, OwnedSet: true, AnyCount: false),

        new("owned set: a node hands part of its set over, and keeps the rest as it was", Level.Required, async h =>
        {
            var a = await h.StartAsync();
            await Settle(h);
            foreach (var p in Enumerable.Range(0, 4)) await h.AddAsync(Token(p, "c", "k" + p));
            foreach (var p in Enumerable.Range(0, 4)) await Applied(h, p, "c", ["k" + p], Slack);
            var worker = h.WorkerOf(a.Name)!;
            var before = worker.Fences;
            var b = await h.StartAsync();
            await Steady(h);
            var holders = h.Watcher.Holders(h.Partitions);
            var kept = before.Where(f => holders[f.Partition].Contains(a.Name)).ToList();
            var moved = before.Where(f => holders[f.Partition].Contains(b.Name)).Select(f => f.Partition).ToList();
            That(kept.Count == 2 && moved.Count == 2, $"the join did not split the set two and two: {Holders(h)}");
            // A release of the moved fences only: the kept ones keep their epochs and positions, and their memory.
            var store = h.StoreOf(a.Name);
            That(worker.Fences.SequenceEqual(kept) && moved.All(p => store.ReleaseBegan(p) is not null) && kept.All(f => store.ReleaseBegan(f.Partition) is null),
                $"the hand-over touched more than the moved partitions {string.Join(" ", moved)}: before [{string.Join(", ", before)}], now [{string.Join(", ", worker.Fences)}]");
            var memory = worker.Instances.Select(i => i.Id).ToHashSet();
            That(kept.All(f => memory.Contains(InstanceRef.In(f.Partition, "c").Id)) && moved.All(p => !memory.Contains(InstanceRef.In(p, "c").Id)),
                $"memory after the hand-over of {string.Join(" ", moved)}: [{string.Join(" ", memory)}]");
            foreach (var p in Enumerable.Range(0, 4)) await h.AddAsync(Token(p, "c", "m" + p));
            foreach (var p in Enumerable.Range(0, 4)) await Applied(h, p, "c", ["k" + p, "m" + p], Slack);
        }, OwnedSet: true, AnyCount: false, Placement: Placement.Balancer),

        new("owned set: the runtime moves one of a node's scopes while a flush over several is in flight", Level.Required, async h =>
        {
            var a = await h.StartAsync();
            await Settle(h);
            var b = await h.StartAsync();
            var (store, worker) = (h.StoreOf(a.Name), h.WorkerOf(a.Name)!);
            That(Set(worker) == "0 1 2 3", $"the first node does not hold every partition: [{Set(worker)}]");
            foreach (var p in new[] { 1, 3 }) await h.AddAsync(Token(p, "c", "k" + p));
            foreach (var p in new[] { 1, 3 }) await Applied(h, p, "c", ["k" + p], Slack);
            var kept = new[] { 0, 1, 3 }.Select(worker.EpochOf).ToList();
            // Two tokens, in 0 and in 2, reach the store while the node's calls wait, so the first poll that reads one
            // reads both and one flush spans both partitions. That flush is held on its way to the store.
            store.Stall(TimeSpan.FromMilliseconds(200));
            store.StallNextCommit(h.Lease / 4, partition: 0);
            foreach (var p in new[] { 0, 2 }) await h.AddAsync(Token(p, "c", "f" + p));
            That(await h.WaitAsync(() => store.Stalls > 1, Slack), "no commit with a turn in 0 entered the hold");
            var askedAt = Environment.TickCount64;
            await a.MoveAsync(2, b);
            That(await h.WaitAsync(() => h.Watcher.Holders(h.Partitions)[2].SequenceEqual([b.Name]), Slack), $"partition 2 did not move: {Holders(h)}");
            var held = await store.Held;
            var landedAt = store.HeldAt;      // recorded by the store before Held completed
            That(held == "landed" && store.HeldPartitions.SequenceEqual([0, 2]) && askedAt < landedAt && store.ReleaseBegan(2) >= landedAt,
                $"the held commit over [{string.Join(" ", store.HeldPartitions)}] {held}; the move was asked {landedAt - askedAt} ms before it returned, " +
                $"and 2 was given up {store.ReleaseBegan(2) - landedAt} ms after");
            // A quiet loss: the partitions kept keep their epochs and their memory, so nothing was reset.
            That(Set(worker) == "0 1 3" && new[] { 0, 1, 3 }.Select(worker.EpochOf).SequenceEqual(kept), $"the first node now holds [{string.Join(", ", worker.Fences)}], before {string.Join(" ", kept)}");
            var memory = worker.Instances.Select(i => i.Id).ToHashSet();
            That(new[] { 0, 1, 3 }.All(p => memory.Contains(InstanceRef.In(p, "c").Id)) && !memory.Contains(InstanceRef.In(2, "c").Id),
                $"memory after the move of 2: [{string.Join(" ", memory)}]");
            foreach (var p in Enumerable.Range(0, 4)) await h.AddAsync(Token(p, "c", "g" + p));
            foreach (var p in Enumerable.Range(0, 4)) await Applied(h, p, "c", p % 2 == 0 ? ["f" + p, "g" + p] : ["k" + p, "g" + p], Slack);
        }, Default with { Timing = Default.Timing with { Lease = TimeSpan.FromSeconds(4) } }, OwnedSet: true, AnyCount: false, Placement: Placement.Runtime),

        new("owned set: partitions join and leave the set between flushes, never during one", Level.Required, async h =>
        {
            var probe = (await h.Outside.TakeAsync(3, "probe", TimeSpan.FromHours(1)))!.Epoch;
            var a = await h.StartAsync();
            var (host, store, worker) = (h.HostOf(a.Name), h.StoreOf(a.Name), h.WorkerOf(a.Name)!);
            That(await h.WaitAsync(() => Set(worker) == "0 1 2", h.Lease * 4 + Slack), $"the node does not hold {{0, 1, 2}}: [{Set(worker)}]");
            var kept = (worker.EpochOf(0), worker.EpochOf(1));
            // A commit with a turn in 0 is held on its way to the store; meanwhile 3 is added and 2 removed, as a runtime
            // would, straight through the node's host.
            store.StallNextCommit(h.Lease / 4, partition: 0);
            await h.AddAsync(Token(0, "c", "f0"));
            That(await h.WaitAsync(() => store.Stalls > 0, Slack), "no commit with a turn in 0 entered the hold");
            await h.Outside.ReleaseAsync(3, probe);
            var gaining = Task.Run(async () => (await host.ActivateAsync(3), Environment.TickCount64));
            var losing = Task.Run(async () =>
            {
                await (await host.ActivateAsync(2))!.DisposeAsync();
                return Environment.TickCount64;
            });
            var (gained, gainedAt) = await gaining;
            var lostAt = await losing;
            var held = await store.Held;
            var landedAt = store.HeldAt;      // recorded by the store before Held completed
            That(held == "landed" && gained is not null && gainedAt >= landedAt && lostAt >= landedAt && store.ReleaseBegan(2) >= landedAt,
                $"the held commit {held}; the gain of 3 ({gained is not null}) and the loss of 2 ended {gainedAt - landedAt} and {lostAt - landedAt} ms after it returned");
            That((worker.EpochOf(0), worker.EpochOf(1)) == kept, $"the change of the set changed the epochs of 0 and 1 (before {kept}): [{string.Join(", ", worker.Fences)}]");
            await h.AddAsync(Token(3, "c", "g3"));
            await Applied(h, 0, "c", ["f0"], Slack);
            await Applied(h, 3, "c", ["g3"], Slack);
        }, Default with { Timing = Default.Timing with { Lease = TimeSpan.FromSeconds(4) } }, OwnedSet: true, AnyCount: false),

        new("balance: a join and a leave move the fewest partitions", Level.Required, async h =>
        {
            await h.StartAsync();
            await Steady(h);
            async Task<int> Moved(Func<Task> change)
            {
                var before = h.Watcher.Activations;
                await change();
                await Steady(h);
                return h.Watcher.Activations - before;
            }
            string Shares() => string.Join(" ", h.Alive.Select(n => h.Watcher.Holders(h.Partitions).Count(x => x.Contains(n.Name))));
            // Eight partitions: a join of a second node moves 4, of a third 2 (shares of 3), and the second's leave its 3.
            IRuntimeNode? b = null;
            var joined = await Moved(async () => b = await h.StartAsync());
            var third = await Moved(() => h.StartAsync());
            var left = await Moved(() => h.StopAsync(b!));
            That((joined, third, left) == (4, 2, 3), $"moves on join, join and leave: {joined}, {third}, {left}; the fewest are 4, 2 and 3; shares now [{Shares()}]");
        }, Default with { Partitions = 8 }, OwnedSet: true, Placement: Placement.Balancer),

        new("projector: rounds run per partition whose journal moved, nudged by the node's commits", Level.Required, async h =>
        {
            var a = await h.StartAsync();
            await Settle(h);
            var projection = h.ProjectionOf(a.Name)!;
            foreach (var p in new[] { 1, 3 })
                for (var i = 0; i < 5; i++) await h.AddAsync(Token(p, "c", $"j{p}.{i}"));
            foreach (var p in new[] { 1, 3 }) await Applied(h, p, "c", [.. Enumerable.Range(0, 5).Select(i => $"j{p}.{i}")], Slack);
            async Task<bool> CaughtUp() => (await h.Outside.ProjectedAsync(1)) is { Written: > 0 } one && one.Position == one.Written
                && (await h.Outside.ProjectedAsync(3)) is { Written: > 0 } three && three.Position == three.Written;
            That(await Harness.WaitAsync(CaughtUp, Slack), "the journals of 1 and 3 were not projected in time");
            await projection.Idle;
            // A take nudges once, and finds nothing; after that only the partitions whose journal moved have rounds (pages
            // of two, so a written partition also gets continuation rounds). No timer sweeps 0 and 2.
            That(projection.RoundsOf(0) == 1 && projection.RoundsOf(2) == 1 && projection.RoundsOf(1) > 1 && projection.RoundsOf(3) > 1,
                $"rounds per partition: {string.Join(", ", Enumerable.Range(0, 4).Select(p => $"{p}: {projection.RoundsOf(p)}"))}");
        }, OwnedSet: true),

        new("fairness: a hot partition does not hold back the rest of the node's set", Level.Required, async h =>
        {
            var a = await h.StartAsync();
            await Settle(h);
            // 600 messages for 60 counters in partition 0, then one for a counter in 3, then 600 more in 0.
            async Task Burst(string round)
            {
                for (var i = 0; i < 600; i++) await h.AddAsync(Token(0, "h" + i % 60, round + i), wake: false);
            }
            await Burst("a");
            await h.AddAsync(Token(3, "q", "q0"));
            await Burst("b");
            await Applied(h, 3, "q", ["q0"], h.Lease * 4 + Slack);
            var quiet = InstanceRef.In(3, "q").Id;
            var log = h.Log.Where(e => e.Node == a.Name).ToList();
            var seen = log.FindIndex(e => e.What == "seen" && e.Actor == quiet);
            var turn = log.FindIndex(Math.Max(0, seen), e => e.What == "turn" && e.Actor == quiet);
            That(seen >= 0 && turn > seen, $"the quiet message was read at {seen} and turned at {turn} of {log.Count}");
            // The set's poll is first in, first out: nothing that arrived after the quiet message was read before it. Once
            // read, it takes its turn in the next pass at the latest (the pass that reads it marks it): no instance takes
            // more than two turns between its read and its turn.
            var overtaken = log.Take(seen).Count(e => e.What == "seen" && e.Seq > log[seen].Seq);
            var most = log.Skip(seen).Take(turn - seen).Where(e => e.What == "turn").GroupBy(e => e.Actor).Select(g => g.Count()).DefaultIfEmpty(0).Max();
            var ahead = log.Take(seen).Count(e => e.What == "seen");
            That(overtaken == 0 && most <= 2, $"{overtaken} later rows were read before the quiet one; an instance took {most} turns between its read and its turn ({ahead} rows read ahead of it)");
        }, OwnedSet: true),

        new("join: a node that joins gets a share of the scopes", Level.Reference, async h =>
        {
            await h.StartAsync();
            await Settle(h);
            var b = await h.StartAsync();
            That(await h.WaitAsync(() => h.Watcher.Holders(h.Partitions).Any(n => n.Contains(b.Name)), h.Lease * 4 + Slack), $"the new node holds nothing: {Holders(h)}");
        }),
    ];

    public static async Task<List<RuntimeOutcome>> RunAsync(IRuntimeProvider runtime, IStoreProvider store, bool brokenFence = false, Func<RuntimeCase, bool>? filter = null, TextWriter? output = null, int? partitions = null)
    {
        output ??= Console.Out;
        var outcomes = new List<RuntimeOutcome>();
        foreach (var @case in All.Where(c => (!c.OwnedSet || runtime.OneWorkerPerNode) && (c.Placement is not { } placement || placement == runtime.Placement) && (filter?.Invoke(c) ?? true)))
        {
            var watch = Stopwatch.StartNew();
            string? failure = null;
            var caught = false;
            try
            {
                var setup = @case.Setup ?? Default;
                if (partitions is { } count && @case.AnyCount && setup.Partitions == Default.Partitions) setup = setup with { Partitions = count };     // a case that chose its own count keeps it
                var timeout = Scaled(setup.Partitions, Timeout);
                await using var harness = await Harness.CreateAsync(runtime, store, setup with { BrokenFence = brokenFence });
                try
                {
                    await @case.Body(harness).WaitAsync(timeout);
                    if (@case.Level == Level.Required) await EndAsync(harness);
                }
                catch (FenceException e) { (failure, caught) = (e.Message, true); }
                catch (ConformanceException e) { failure = e.Message; }
                catch (TimeoutException) { failure = $"the case did not end within {timeout.TotalSeconds} s"; }
                catch (Exception e) { failure = $"threw {e.GetType().Name}: {e.Message}"; }      // the teardown and the checks below still run
                // What the nodes do on their way out is checked too. A stop that hangs fails the case, not the suite.
                try { await harness.StopAllAsync().WaitAsync(timeout); }
                catch (TimeoutException) { failure ??= $"stopping the nodes did not end within {timeout.TotalSeconds} s"; }
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
