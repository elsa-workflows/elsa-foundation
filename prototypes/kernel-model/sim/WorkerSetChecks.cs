namespace Kernel.Sim;

/// <summary>
/// K1 slice 2a: one worker owns two virtual partitions on the in-memory store. Turns in both flush as one owned batch,
/// and after a takeover of one of them a refused flush rolls back whole and the worker keeps the other.
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
        check(worker.Owns && worker.Fences.SequenceEqual([new PartitionFence(0, e0, p0)]) && store.Partitions[0].Owner == "a" && store.Partitions[0].Epoch == e0,
            $"the keeping reset keeps partition 0 at its epoch and drops 1: held [{string.Join(", ", worker.Fences)}]");

        // Memory went with the reset. What the worker marked in 0 at e0 comes back only with Seen <= epoch.
        for (var i = 0; i < 20 && store.Instances[x.Id].Revision < 2; i++) await worker.RunAsync();
        var journal0 = store.Journal.Keys.Where(k => k.Partition == 0).Select(k => k.Position).Order().ToList();
        check(store.Instances[x.Id].Revision == 2 && !store.Messages.ContainsKey("three") && worker.PositionOf(0) == store.Partitions[0].Position
              && journal0.SequenceEqual(Enumerable.Range(1, (int)store.Partitions[0].Position).Select(p => (long)p)),
            $"the kept partition continues from the store with no journal hole: x at revision {store.Instances[x.Id].Revision}, journal [{string.Join(" ", journal0)}]");
        check(store.Partitions[1].Position == p1 && store.Partitions[1].Owner == "b" && store.Instances[y.Id].Revision == 1 && !store.Messages["four"].Done,
            "the moved partition is left to its new owner");
    }
}
