namespace Kernel.Sim;

/// <summary>
/// K1 slice 2a: one worker owns two virtual partitions on the in-memory store. Turns in both flush as one owned batch,
/// and after a takeover of one of them a refused flush rolls back whole and the worker keeps the other. Slice 2b: suspects,
/// trials and child ids per virtual-partition epoch.
/// </summary>
internal static class WorkerSetChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(2, () => time);
        var commits = 0;
        var worker = new Worker("a", [0, 1], new CountingStore(store, () => commits++), new WorkflowInstance([new Counter()]),
            new() { Monotonic = () => time, Partitions = 2 });
        await store.ReleaseAsync(1, (await store.TakeAsync(1, "earlier", Defaults.Lease))!.Epoch);    // so that the two epochs differ
        await new DurableHost([worker]).ActivateAsync(0);
        var (x, y) = (InstanceRef.In(0, "x"), InstanceRef.In(1, "y"));
        var (e0, e1) = (worker.EpochOf(0), worker.EpochOf(1));
        check(worker.Owns && worker.Fences.Count == 2 && e0 > 0 && e1 == e0 + 1, "a worker of two partitions takes both, each at its own epoch");

        async Task<List<string>> Deliver(string one, string two)
        {
            await store.AddAsync(new(one, x, "add", Json.Write(new Add(one)), Start: Counter.Spec));
            await store.AddAsync(new(two, y, "add", Json.Write(new Add(two)), Start: Counter.Spec));
            await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();     // the marks, in both partitions
            var ready = worker.Ready.Order(StringComparer.Ordinal).ToList();
            foreach (var id in ready) worker.RunTurn(id);
            return ready;
        }

        // Turns in both partitions flush as one batch; each partition's position and journal move on their own.
        var ready = await Deliver("one", "two");
        var before = commits;
        await worker.FlushAsync();
        var (p0, p1) = (store.Partitions[0].Position, store.Partitions[1].Position);
        check(ready.SequenceEqual([x.Id, y.Id]) && commits == before + 1 && store.Instances[x.Id].Revision == 1 && store.Instances[y.Id].Revision == 1
              && p0 > 0 && p1 > 0 && worker.PositionOf(0) == p0 && worker.PositionOf(1) == p1
              && store.Journal.Values.Where(j => j.Partition == 0).All(j => j.Instance == x.Id) && store.Journal.Values.Where(j => j.Partition == 1).All(j => j.Instance == y.Id),
            $"turns in two partitions flush as one owned batch: ready [{string.Join(", ", ready)}], commits {commits - before}, positions {p0}/{worker.PositionOf(0)} and {p1}/{worker.PositionOf(1)}");

        // The marks land at each partition's epoch. Then partition 1's lease runs out for another node, which takes it.
        ready = await Deliver("three", "four");
        check(store.Messages["three"].Seen == e0 && store.Messages["four"].Seen == e1, "each mark records its own partition's epoch");
        store.Partitions[1].LeaseUntil = 0;
        var taken = await store.TakeAsync(1, "b", Defaults.Lease);
        before = commits;
        await worker.FlushAsync();      // the turns in 0 and 1: the fence of 1 is stale
        var rolledBack = commits == before + 1 && store.Instances[x.Id].Revision == 1 && store.Instances[y.Id].Revision == 1
            && store.Partitions[0].Position == p0 && store.Partitions[1].Position == p1 && store.Messages.ContainsKey("three") && store.Messages.ContainsKey("four");
        check(taken is not null && ready.Count == 2 && rolledBack, "one stale fence rolls the whole flush back");
        check(worker.Owns && worker.Fences.SequenceEqual([new PartitionFence(0, e0, p0)]) && store.Partitions[0].Owner == "a" && store.Partitions[0].Epoch == e0
              && Unserved(store, worker, time) == "", $"the keeping reset keeps partition 0 at its epoch and drops 1: held [{string.Join(", ", worker.Fences)}]{Unserved(store, worker, time)}");

        // Memory went with the reset. What the worker marked in 0 at e0 comes back only with Seen <= epoch.
        for (var i = 0; i < 20 && store.Instances[x.Id].Revision < 2; i++) await worker.RunAsync();
        var journal0 = store.Journal.Keys.Where(k => k.Partition == 0).Select(k => k.Position).Order().ToList();
        check(store.Instances[x.Id].Revision == 2 && !store.Messages.ContainsKey("three") && worker.PositionOf(0) == store.Partitions[0].Position
              && journal0.SequenceEqual(Enumerable.Range(1, (int)store.Partitions[0].Position).Select(p => (long)p)),
            $"the kept partition continues from the store with no journal hole: x at revision {store.Instances[x.Id].Revision}, journal [{string.Join(" ", journal0)}]");
        check(store.Partitions[1].Position == p1 && store.Partitions[1].Owner == "b" && store.Instances[y.Id].Revision == 1 && !store.Messages["four"].Done,
            "the moved partition is left to its new owner");

        // A release clears the owner and keeps the epoch: the reset's read reports it as no one's.
        var released = new InMemoryStore(1, () => time);
        var held = (await released.TakeAsync(0, "a", Defaults.Lease))!.For(0);
        await released.ReleaseAsync(0, held.Epoch);
        var read = await released.RenewAsync([held], "a", Defaults.Lease);
        check(read.Rows.Single() == new FenceRead(0, null, held.Epoch, 0), "the keeping reset's read reports a released row with no owner at its old epoch");

        await LostAnswer(check);
        await WithoutOwnedSets(check);
        await SuspectsAndTrials(check);
        await ChildIds(check);
    }

    /// <summary>The partitions the store shows live under this worker's node at an epoch the worker does not hold.</summary>
    private static string Unserved(InMemoryStore store, Worker worker, long now)
    {
        var unserved = Enumerable.Range(0, store.Partitions.Length).Where(p => store.Partitions[p] is var row && row.Owner == worker.Node
            && row.LeaseUntil >= now && worker.EpochOf(p) != row.Epoch).ToList();
        return unserved.Count == 0 ? "" : $"; unserved: {string.Join(" ", unserved)}";
    }

    // An answer lost after the commit landed: the worker takes back each partition still its own or free, at a new
    // epoch, and drops the one another node holds now; one take that failed as a whole would strand the rest.
    private static async Task LostAnswer(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(3, () => time);
        var wire = new DelayedCommit(store);
        var worker = new Worker("c", [0, 1, 2], wire, new WorkflowInstance([new Counter()]), new() { Monotonic = () => time, Partitions = 3 });
        await new DurableHost([worker]).ActivateAsync(0);
        var epochs = worker.Fences.ToDictionary(f => f.Partition, f => f.Epoch);
        var (x, y) = (InstanceRef.In(0, "x"), InstanceRef.In(1, "y"));
        await store.AddAsync(new("one", x, "add", Json.Write(new Add("one")), Start: Counter.Spec));
        await store.AddAsync(new("two", y, "add", Json.Write(new Add("two")), Start: Counter.Spec));
        await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
        foreach (var id in worker.Ready.ToList()) worker.RunTurn(id);
        store.Partitions[2].LeaseUntil = 0;
        var moved = await store.TakeAsync(2, "b", Defaults.Lease);
        wire.LoseNext = true;
        await worker.FlushAsync();      // fences 0 and 1, lands, and the answer is lost
        check(moved is not null && store.Instances.ContainsKey(x.Id) && store.Instances.ContainsKey(y.Id) && wire.Batches[^1].Fenced.Select(f => f.Partition).SequenceEqual([0, 1]),
            "the flush whose answer is lost landed over partitions 0 and 1");
        check(worker.Fences.Select(f => (f.Partition, f.Epoch)).SequenceEqual([(0, epochs[0] + 1), (1, epochs[1] + 1)])
              && worker.PositionOf(0) == store.Partitions[0].Position && worker.PositionOf(1) == store.Partitions[1].Position
              && store.Partitions[2].Owner == "b" && Unserved(store, worker, time) == "",
            $"after a lost answer the worker retakes 0 and 1 at new epochs and drops 2: held [{string.Join(", ", worker.Fences)}]{Unserved(store, worker, time)}");
    }

    // A store without owned sets gets only today's positional batches; a worker of several partitions cannot take on it.
    private static async Task WithoutOwnedSets(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(2, () => time);
        var wire = new DelayedCommit(store) { WithoutOwnedSets = true };
        var one = new Worker("d", 0, wire, new WorkflowInstance([new Counter()]), new() { Monotonic = () => time, Partitions = 2 });
        await new DurableHost([one]).ActivateAsync(0);
        await store.AddAsync(new("one", InstanceRef.In(0, "x"), "add", Json.Write(new Add("one")), Start: Counter.Spec));
        await one.PollAsync(); await one.LoadAsync(); await one.FlushAsync();
        foreach (var id in one.Ready.ToList()) one.RunTurn(id);
        await one.FlushAsync(); await one.BeatAsync();
        check(wire.Batches.Count >= 3 && wire.Batches.All(b => b.Fences is null && b.Partition == 0) && store.Instances.ContainsKey("0/x"),
            $"a store without owned sets gets only positional one-partition batches ({wire.Batches.Count} batches)");
        var several = new Worker("e", [0, 1], wire, new WorkflowInstance([new Counter()]), new() { Monotonic = () => time, Partitions = 2 });
        var refused = false;
        try { await new DurableHost([several]).ActivateAsync(0); } catch (NotSupportedException) { refused = true; }
        check(refused && !several.Owns && store.Partitions[1].Owner is null, "a worker of several partitions does not take on a store without owned sets");
    }

    // Epochs count per partition, so the same number can be this worker's in one partition and another owner's in the
    // next: the suspect test compares a row only with the epochs this process held in the row's own partition.
    private static async Task SuspectsAndTrials(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(2, () => time);
        store.Partitions[0].Epoch = 1;      // "a" takes 0 at epoch 2
        (store.Partitions[1].Owner, store.Partitions[1].Epoch, store.Partitions[1].LeaseUntil) = ("b", 2, 0);    // "b" held 1 at epoch 2 and was lost
        var (x, z) = (InstanceRef.In(0, "x"), InstanceRef.In(1, "z"));
        await store.AddAsync(new("m", z, "add", Json.Write(new Add("m")), Start: Counter.Spec));
        store.Messages["m"].Seen = 2;       // what "b" marked before it was lost
        await store.AddAsync(new("n", x, "add", Json.Write(new Add("n")), Start: Counter.Spec));
        var worker = new Worker("a", [0, 1], store, new WorkflowInstance([new Counter()]), new() { Monotonic = () => time, Partitions = 2 });
        await new DurableHost([worker]).ActivateAsync(0);
        var (e0, e1) = (worker.EpochOf(0), worker.EpochOf(1));
        await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
        check(e0 == 2 && e1 == 3 && worker.Trial.SequenceEqual(["m"]) && store.Messages["m"].Trial == e1 && store.Messages["n"] is { Seen: 2, Trial: 0 },
            $"a row another owner marked at epoch 2 of partition 1 is a suspect while this worker holds partition 0 at epoch 2: on trial [{string.Join(", ", worker.Trial)}]");
        check(worker.Ready.SequenceEqual([z.Id]),
            $"a trial in partition 1 holds back partition 0 of the same worker, as it would any partition of its process: ready [{string.Join(", ", worker.Ready)}]");

        // Partition 1 moves while its trial is in flight. The beat's fence is refused and the keeping reset keeps 0.
        store.Partitions[1].LeaseUntil = 0;
        var moved = await store.TakeAsync(1, "c", Defaults.Lease);
        await worker.BeatAsync();
        check(moved is not null && !worker.Trial.Any() && worker.Fences.Select(f => (f.Partition, f.Epoch)).SequenceEqual([(0, e0)]),
            $"the keeping reset drops partition 1 and its trial: held [{string.Join(", ", worker.Fences)}], on trial [{string.Join(", ", worker.Trial)}]");
        await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
        var ready = worker.Ready.ToList();
        foreach (var id in ready) worker.RunTurn(id);
        await worker.FlushAsync();
        check(ready.SequenceEqual([x.Id]) && !worker.Trial.Any() && store.Instances.GetValueOrDefault(x.Id)?.Revision == 1 && !store.Messages.ContainsKey("n"),
            $"the trial in the dropped partition gates nothing kept, and what this worker marked in 0 at its epoch is no suspect: ready [{string.Join(", ", ready)}]");
    }

    // A child id names the (partition, epoch) of its parent's fenced commit. Each held partition has its own issuer, a
    // keeping reset keeps the epoch and the issuer's sequence with it, and a retake starts a new epoch.
    private static async Task ChildIds(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(2, () => time);
        var wire = new DelayedCommit(store);
        await store.ReleaseAsync(1, (await store.TakeAsync(1, "earlier", Defaults.Lease))!.Epoch);    // so that the two epochs differ
        var worker = new Worker("a", [0, 1], wire, new WorkflowInstance([new Parent(), new Child()]), new() { Monotonic = () => time, Partitions = 2 });
        await new DurableHost([worker]).ActivateAsync(0);
        var (e0, e1) = (worker.EpochOf(0), worker.EpochOf(1));
        var (children, starts) = (new List<string>(), new HashSet<InboxKey>());

        async Task Start(params string[] parents)
        {
            foreach (var parent in parents)
                await store.AddAsync(new($"start-{parent}", new InstanceRef(parent), Kinds.Start, Start: new StartSpec("parent", "p", Json.Write(new ParentInput(1, -1)))));
            await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
            foreach (var parent in parents) worker.RunTurn(parent);
        }
        void Collect()      // every child start, once per message: two starts to one id would show it twice
        {
            foreach (var row in store.Messages.Values.Where(m => m.Message.Target.ChildAddress).OrderBy(m => m.Seq))
                if (starts.Add(row.Message.InboxKey)) children.Add(row.Message.Target.Id);
        }

        await Start("0/p", "1/q");
        await worker.FlushAsync();
        Collect();
        check(children.SequenceEqual([$"g0/child/0.{e0}.1", $"g1/child/1.{e1}.1"]),
            $"each partition issues its children's ids at its own epoch: [{string.Join(", ", children)}]");

        // The answer of a flush with a child in it is lost after it landed: the retake starts new epochs and new issuers.
        await Start("0/s");
        wire.LoseNext = true;
        await worker.FlushAsync();
        Collect();
        await Start("0/t");
        await worker.FlushAsync();
        Collect();
        var retaken = worker.EpochOf(0);

        // Partition 1 moves; the beat is refused and the keeping reset keeps partition 0 at its epoch, and its issuer.
        store.Partitions[1].LeaseUntil = 0;
        await store.TakeAsync(1, "b", Defaults.Lease);
        await worker.BeatAsync();
        var kept = worker.Fences.Select(f => (f.Partition, f.Epoch)).SequenceEqual([(0, retaken)]);
        await Start("0/r");
        await worker.FlushAsync();
        Collect();
        check(retaken == e0 + 1 && kept && children.Distinct().Count() == children.Count
              && children.SequenceEqual([$"g0/child/0.{e0}.1", $"g1/child/1.{e1}.1", $"g0/child/0.{e0}.2", $"g0/child/0.{retaken}.1", $"g0/child/0.{retaken}.2"]),
            $"child ids stay unique across a retake and a keeping reset: [{string.Join(", ", children)}]");
    }
}
