using Kernel.Sim;

namespace Kernel.Runtime.Conformance;

public static class SetReleaseChecks
{
    private const int Count = 8;
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(10);

    public static async Task<int> RunAsync()
    {
        var owned = await CheckAsync(ownedSets: true);
        var fixedWorkers = await CheckAsync(ownedSets: false);
        var expired = await ExpiredAsync();
        var cancelled = await CancelledAsync();
        return owned && fixedWorkers && expired && cancelled ? 0 : 1;
    }

    private static async Task<bool> CheckAsync(bool ownedSets)
    {
        var (store, wire, _, runtime) = await ReadyToReleaseAsync(ownedSets, () => 1_000L);
        var epochs = store.Partitions.Select(partition => partition.Epoch).ToArray();
        var heldAll = Enumerable.Range(0, Count).All(runtime.Owns);
        await runtime.BalanceAsync();

        var heldAfter = Enumerable.Range(0, Count).Where(runtime.Owns).ToArray();
        var expected = heldAll && heldAfter.SequenceEqual([0, 1, 2, 3])
            && store.Partitions.Select(partition => partition.Owner).SequenceEqual(["first", "first", "first", "first", null, null, null, null])
            && store.Partitions.Select(partition => partition.Epoch).SequenceEqual(epochs)
            && Enumerable.Range(0, Count).All(partition => (wire.ReleaseBegan(partition) is not null) == (partition >= 4))
            && wire.SetReleases == (ownedSets ? 1 : 0);
        Console.WriteLine($"{(expected ? "ok" : "FAIL")} {(ownedSets ? "set" : "fixed")} host gave up only its surplus, " +
            $"held [{string.Join(", ", heldAfter)}], made {wire.SetReleases} set release call(s)");
        await runtime.StopAsync();
        return expected;
    }

    private static async Task<bool> ExpiredAsync()
    {
        var now = 1_000L;
        var (store, wire, host, runtime) = await ReadyToReleaseAsync(ownedSets: true, () => now);
        var epochs = store.Partitions.Select(partition => partition.Epoch).ToArray();
        now += (long)Lease.TotalMilliseconds + 1;
        await runtime.BalanceAsync();
        var pending = host.NodeWorker!.Pending;
        var stoodDown = store.Partitions.All(partition => partition.Owner == "first")
            && Enumerable.Range(0, Count).All(partition => !runtime.Owns(partition)
                && pending.GetValueOrDefault(partition, -1) == epochs[partition] && wire.ReleaseBegan(partition) is null);
        await runtime.NodePassAsync();
        await runtime.HeartbeatAsync();
        await runtime.NodePassAsync();
        var retaken = host.NodeWorker.Pending.Count == 0
            && Enumerable.Range(0, Count).All(partition => wire.Retaken(partition) == epochs[partition] + 1
                && runtime.Owns(partition) == (partition < 4)
                && store.Partitions[partition].Owner == (partition < 4 ? "first" : null));
        var expected = stoodDown && retaken;
        Console.WriteLine($"{(expected ? "ok" : "FAIL")} expiry before an idle handover stands down at the original epochs " +
            $"({stoodDown}), then counting retake frees only the given-up partitions ({retaken})");
        await runtime.StopAsync();
        return expected;
    }

    private static async Task<bool> CancelledAsync()
    {
        var (store, wire, _, runtime) = await ReadyToReleaseAsync(ownedSets: true, () => 1_000L);
        using var cancellation = new CancellationTokenSource();
        await runtime.Activation(0)!.WokenAsync(null);
        wire.Stall(TimeSpan.FromMinutes(1));
        var pass = runtime.PassAsync(0).AsTask();
        Task? balance = null;
        var cancelled = false;
        var waited = false;
        try
        {
            if (!wire.Idle)
            {
                balance = runtime.BalanceAsync(cancellation.Token);
                waited = !balance.IsCompleted;
                cancellation.Cancel();
                try { await balance.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelled = true; }
                catch (TimeoutException) { }
            }
        }
        finally
        {
            wire.Resume();
            await pass.WaitAsync(TimeSpan.FromSeconds(2));
        }
        if (balance is not null && !cancelled) await balance.WaitAsync(TimeSpan.FromSeconds(2));
        var expected = waited && cancelled && wire.SetReleases == 0
            && Enumerable.Range(0, Count).All(runtime.Owns)
            && store.Partitions.All(partition => partition.Owner == "first");
        Console.WriteLine($"{(expected ? "ok" : "FAIL")} cancellation while the handover waits preserves the registered activations and owners");
        await runtime.StopAsync();
        return expected;
    }

    private static async Task<(InMemoryStore, NodeStore, DurableHost, PartitionRuntime)> ReadyToReleaseAsync(bool ownedSets, Func<long> clock)
    {
        var store = new InMemoryStore(Count, clock);
        var wire = new NodeStore(store) { WithoutOwnedSets = !ownedSets };
        var host = DurableHost.ForNode("first", wire, new WorkflowInstance(Stress.Models()), Count,
            new WorkerOptions { Partitions = Count, Lease = Lease, Monotonic = clock });
        var runtime = new PartitionRuntime("first", wire, Count, Lease);
        await runtime.StartAsync(host);
        await runtime.HeartbeatAsync();
        await runtime.BalanceAsync();
        await store.NodeHeartbeatAsync("joining", Lease, Lease);
        await runtime.HeartbeatAsync();
        return (store, wire, host, runtime);
    }
}
