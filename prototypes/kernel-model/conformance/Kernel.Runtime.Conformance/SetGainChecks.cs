using Kernel.Sim;

namespace Kernel.Runtime.Conformance;

public static class SetGainChecks
{
    public static async Task<int> RunAsync()
    {
        var set = await RunAsync(true);
        var fixedWorkers = await RunAsync(false);
        return set && fixedWorkers ? 0 : 1;
    }

    private static async Task<bool> RunAsync(bool ownedSets)
    {
        const int count = 8;
        var lease = TimeSpan.FromSeconds(10);
        var store = new InMemoryStore(count, () => 1_000L);
        var wire = new NodeStore(store) { WithoutOwnedSets = !ownedSets };
        var host = DurableHost.ForNode("first", wire, new WorkflowInstance(Stress.Models()), count,
            new WorkerOptions { Partitions = count, Lease = lease });
        var watcher = new Watcher();
        var runtime = new PartitionRuntime("first", wire, count, lease);
        await runtime.StartAsync(watcher.Wrap("first", host));
        var first = await runtime.ActivateAsync(0);
        var epoch = store.Partitions[0].Epoch;
        await runtime.HeartbeatAsync();
        var peer = await store.TakeAsync([3, 6], "peer", lease);
        await runtime.BalanceAsync();       // the heartbeat saw both peer partitions free
        var partial = Enumerable.Range(0, count).Where(runtime.Owns).SequenceEqual([0, 1, 2, 4, 5, 7])
            && store.Partitions[3].Owner == "peer" && store.Partitions[6].Owner == "peer"
            && wire.SetTakes == (ownedSets ? 1 : 0) && wire.SingleTakes == (ownedSets ? 1 : 8);
        var fences = store.Partitions.Select(p => p.Epoch).ToArray();
        await runtime.BalanceAsync();       // both remaining candidates refuse the request
        var refused = watcher.Declined == 4 && watcher.Activations == 6
            && store.Partitions.Select(p => p.Epoch).SequenceEqual(fences);
        await store.ReleaseAsync(peer.Taken);
        await runtime.BalanceAsync();
        var ok = partial && refused && watcher.Activations == 8 && watcher.Violations.Count == 0
            && Enumerable.Range(0, count).All(runtime.Owns)
            && store.Partitions.Select(p => p.Epoch).SequenceEqual(fences.Select((e, p) => p is 3 or 6 ? e + 1 : e))
            && first is not null && ReferenceEquals(first, runtime.Activation(0)) && epoch == store.Partitions[0].Epoch
            && wire.SetTakes == (ownedSets ? 3 : 0) && wire.SingleTakes == (ownedSets ? 1 : 12);
        Console.WriteLine($"{(ok ? "ok" : "FAIL")} {(ownedSets ? "set" : "fixed")} gain: partial, refused, then full; " +
            $"{wire.SetTakes} set takes, {wire.SingleTakes} single takes, {watcher.Declined} refused, {watcher.Activations} activated");
        await runtime.StopAsync();
        return ok;
    }
}
