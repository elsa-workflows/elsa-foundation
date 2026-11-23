using Kernel.Sim;

namespace Kernel.Runtime.Conformance;

public static class SetReleaseChecks
{
    public static async Task<int> RunAsync()
    {
        var owned = await CheckAsync(ownedSets: true);
        var fixedWorkers = await CheckAsync(ownedSets: false);
        return owned && fixedWorkers ? 0 : 1;
    }

    private static async Task<bool> CheckAsync(bool ownedSets)
    {
        const int count = 8;
        var lease = TimeSpan.FromSeconds(10);
        var store = new InMemoryStore(count, () => 1_000L);
        var wire = new NodeStore(store) { WithoutOwnedSets = !ownedSets };
        var host = DurableHost.ForNode("first", wire, new WorkflowInstance(Stress.Models()), count,
            new WorkerOptions { Partitions = count, Lease = lease });
        var runtime = new PartitionRuntime("first", wire, count, lease);
        await runtime.StartAsync(host);
        await runtime.HeartbeatAsync();
        await runtime.BalanceAsync();
        var epochs = store.Partitions.Select(partition => partition.Epoch).ToArray();
        var heldAll = Enumerable.Range(0, count).All(runtime.Owns);

        await store.NodeHeartbeatAsync("joining", lease, lease);
        await runtime.HeartbeatAsync();
        await runtime.BalanceAsync();

        var heldAfter = Enumerable.Range(0, count).Where(runtime.Owns).ToArray();
        var expected = heldAll && heldAfter.SequenceEqual([0, 1, 2, 3])
            && store.Partitions.Select(partition => partition.Owner).SequenceEqual(["first", "first", "first", "first", null, null, null, null])
            && store.Partitions.Select(partition => partition.Epoch).SequenceEqual(epochs)
            && Enumerable.Range(0, count).All(partition => (wire.ReleaseBegan(partition) is not null) == (partition >= 4))
            && wire.SetReleases == (ownedSets ? 1 : 0);
        Console.WriteLine($"{(expected ? "ok" : "FAIL")} {(ownedSets ? "set" : "fixed")} host gave up only its surplus, " +
            $"held [{string.Join(", ", heldAfter)}], made {wire.SetReleases} set release call(s)");
        await runtime.StopAsync();
        return expected;
    }
}
