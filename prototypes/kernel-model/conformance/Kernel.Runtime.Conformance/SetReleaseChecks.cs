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
        var busy = await BusySurplusAsync();
        var activating = await ActivatingSurplusAsync();
        var fixedBusy = await FixedBusySurplusAsync(passStartsDuringRelease: false);
        var fixedInterleaved = await FixedBusySurplusAsync(passStartsDuringRelease: true);
        return owned && fixedWorkers && expired && cancelled && busy && activating && fixedBusy && fixedInterleaved ? 0 : 1;
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
        Task<TimeSpan?>? surplusPass = null;
        var cancelled = false;
        var waited = false;
        var blockedNewPass = false;
        try
        {
            if (!wire.Idle)
            {
                balance = runtime.BalanceAsync(cancellation.Token);
                waited = !balance.IsCompleted;
                surplusPass = runtime.PassAsync(7).AsTask();
                blockedNewPass = surplusPass.IsCompletedSuccessfully && surplusPass.Result is null;
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
            if (surplusPass is not null) await surplusPass.WaitAsync(TimeSpan.FromSeconds(2));
        }
        if (balance is not null && !cancelled) await balance.WaitAsync(TimeSpan.FromSeconds(2));
        var expected = waited && blockedNewPass && cancelled && wire.SetReleases == 0
            && Enumerable.Range(0, Count).All(runtime.Owns)
            && store.Partitions.All(partition => partition.Owner == "first");
        Console.WriteLine($"{(expected ? "ok" : "FAIL")} cancellation while the handover waits preserves the registered activations and owners");
        await runtime.StopAsync();
        return expected;
    }

    private static async Task<bool> BusySurplusAsync()
    {
        var watcher = new Watcher();
        var (store, wire, _, runtime) = await ReadyToReleaseAsync(ownedSets: true, () => 1_000L, watcher);
        await runtime.Activation(7)!.WokenAsync(null);
        wire.Stall(TimeSpan.FromMinutes(1));
        var pass = runtime.PassAsync(7).AsTask();
        var wasBlocked = !wire.Idle;
        var balance = runtime.BalanceAsync();
        bool deferred;
        try
        {
            deferred = wasBlocked && balance.IsCompletedSuccessfully && wire.SetReleases == 0
                && store.Partitions.All(partition => partition.Owner == "first");
        }
        finally
        {
            wire.Resume();
            await pass.WaitAsync(TimeSpan.FromSeconds(2));
            await balance.WaitAsync(TimeSpan.FromSeconds(2));
        }
        await runtime.BalanceAsync();
        var expected = deferred && wire.SetReleases == 1 && watcher.Violations.Count == 0
            && Enumerable.Range(0, Count).All(partition => runtime.Owns(partition) == (partition < 4));
        Console.WriteLine($"{(expected ? "ok" : "FAIL")} a running surplus pass defers the group until a later round, " +
            $"seam violations [{string.Join(", ", watcher.Violations)}]");
        await runtime.StopAsync();
        return expected;
    }

    private static async Task<bool> ActivatingSurplusAsync()
    {
        var watcher = new Watcher();
        var (store, wire, _, runtime) = await ReadyToReleaseAsync(ownedSets: true, () => 1_000L, watcher);
        await runtime.Activation(0)!.WokenAsync(null);
        wire.Stall(TimeSpan.FromMinutes(1));
        var pass = runtime.PassAsync(0).AsTask();     // a retained partition holds the shared worker
        var activation = runtime.ActivateAsync(7);
        var balance = runtime.BalanceAsync();
        bool deferred;
        try
        {
            deferred = !wire.Idle && !activation.IsCompleted && runtime.Activating(7) && balance.IsCompletedSuccessfully &&
                wire.SetReleases == 0 && store.Partitions.All(partition => partition.Owner == "first");
        }
        finally
        {
            wire.Resume();
            await Task.WhenAll(pass, activation, balance).WaitAsync(TimeSpan.FromSeconds(3));
        }
        await runtime.BalanceAsync();
        var expected = deferred && activation.Result is not null && wire.SetReleases == 1 && watcher.Violations.Count == 0 &&
            Enumerable.Range(0, Count).All(p => runtime.Owns(p) == (p < 4) &&
                store.Partitions[p].Owner == (p < 4 ? "first" : null));
        Console.WriteLine($"{(expected ? "ok" : "FAIL")} an activating surplus scope defers the entire set release; " +
            $"seam violations [{string.Join(", ", watcher.Violations)}]");
        await runtime.StopAsync();
        return expected;
    }

    private static async Task<bool> FixedBusySurplusAsync(bool passStartsDuringRelease)
    {
        var watcher = new Watcher();
        var (store, wire, _, runtime) = await ReadyToReleaseAsync(ownedSets: false, () => 1_000L, watcher);
        var busy = passStartsDuringRelease ? 6 : 7;
        var poll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.HoldScalarPoll = (busy, poll.Task);
        await runtime.Activation(busy)!.WokenAsync(null);

        Task? balance = null;
        Task<IScopeActivation?>? competingTake = null;
        var releaseWaiting = true;
        if (passStartsDuringRelease)
        {
            wire.Stall(TimeSpan.FromMinutes(1));
            balance = runtime.BalanceAsync();
            var nextRound = runtime.BalanceAsync();
            var nextPass = runtime.PassAsync(7);
            competingTake = runtime.ActivateAsync(7);
            releaseWaiting = wire.ReleaseBegan(7) is not null && !balance.IsCompleted && nextRound.IsCompletedSuccessfully &&
                nextPass.IsCompletedSuccessfully && nextPass.Result is null && competingTake.IsCompletedSuccessfully && competingTake.Result is null;
        }
        var pass = runtime.PassAsync(busy).AsTask();
        var passWaiting = !pass.IsCompleted;
        balance ??= runtime.BalanceAsync();
        wire.Resume();
        bool gaveUpIdle;
        try
        {
            await balance.WaitAsync(TimeSpan.FromSeconds(3));
            if (competingTake is not null) await competingTake.WaitAsync(TimeSpan.FromSeconds(3));
            gaveUpIdle = !pass.IsCompleted && Enumerable.Range(0, Count).All(p =>
                runtime.Owns(p) == (p < 4 || p == busy) &&
                store.Partitions[p].Owner == (p < 4 || p == busy ? "first" : null) &&
                (wire.ReleaseBegan(p) is not null) == (p >= 4 && p != busy));
        }
        finally
        {
            poll.SetResult();
            await pass.WaitAsync(TimeSpan.FromSeconds(3));
        }
        await runtime.BalanceAsync();
        var expected = releaseWaiting && passWaiting && gaveUpIdle && watcher.Violations.Count == 0 &&
            wire.SetReleases == 0 && Enumerable.Range(0, Count).All(p =>
                runtime.Owns(p) == (p < 4) && store.Partitions[p].Owner == (p < 4 ? "first" : null));
        Console.WriteLine($"{(expected ? "ok" : "FAIL")} fixed workers released the idle surplus while scope {busy} was passing " +
            $"({(passStartsDuringRelease ? "pass began during release of 7" : "pass began before the round")}); " +
            $"seam violations [{string.Join(", ", watcher.Violations)}]");
        await runtime.StopAsync();
        return expected;
    }

    private static async Task<(InMemoryStore, NodeStore, DurableHost, PartitionRuntime)> ReadyToReleaseAsync(
        bool ownedSets, Func<long> clock, Watcher? watcher = null)
    {
        var store = new InMemoryStore(Count, clock);
        var wire = new NodeStore(store) { WithoutOwnedSets = !ownedSets };
        var host = DurableHost.ForNode("first", wire, new WorkflowInstance(Stress.Models()), Count,
            new WorkerOptions { Partitions = Count, Lease = Lease, Monotonic = clock });
        var runtime = new PartitionRuntime("first", wire, Count, Lease);
        await runtime.StartAsync(watcher?.Wrap("first", host) ?? host);
        await runtime.HeartbeatAsync();
        await runtime.BalanceAsync();
        await store.NodeHeartbeatAsync("joining", Lease, Lease);
        await runtime.HeartbeatAsync();
        return (store, wire, host, runtime);
    }
}
