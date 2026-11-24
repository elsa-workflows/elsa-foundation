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
        var killedStop = await StopDuringDrainAsync(cancel: true);
        var overlappingStop = await StopDuringDrainAsync(cancel: false);
        var expired = await ExpiredAsync();
        var cancelled = await CancelledAsync();
        var partialDrain = await CancelPartialDrainAsync();
        var busy = await BusySurplusAsync();
        var activatingSurplus = await ReactivatingAsync(7);
        var activatingRetained = await ReactivatingAsync(1);
        var fixedBusy = await FixedBusySurplusAsync(passStartsDuringRelease: false);
        var fixedInterleaved = await FixedBusySurplusAsync(passStartsDuringRelease: true);
        return owned && fixedWorkers && killedStop && overlappingStop && expired && cancelled && partialDrain && busy &&
            activatingSurplus && activatingRetained && fixedBusy && fixedInterleaved ? 0 : 1;
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
        var beforeStop = wire.SetReleases;
        await runtime.StopAsync(new CancellationToken(canceled: true));
        var keptAfterCancelledStop = heldAfter.SequenceEqual(Enumerable.Range(0, Count).Where(runtime.Owns)) &&
            wire.SetReleases == beforeStop && store.Partitions.Take(4).All(partition => partition.Owner == "first");
        Task? joined = null;
        var waitedForRelease = false;
        wire.OnSetRelease = () =>
        {
            joined = runtime.StopAsync().AsTask();
            waitedForRelease = !joined.IsCompleted;
        };
        await runtime.StopAsync();
        if (joined is not null) await joined.WaitAsync(TimeSpan.FromSeconds(5));
        var stopCalls = wire.SetReleases - beforeStop;
        var stopped = keptAfterCancelledStop && stopCalls == (ownedSets ? 1 : 0) &&
            waitedForRelease == ownedSets &&
            !Enumerable.Range(0, Count).Any(runtime.Owns) &&
            store.Partitions.All(partition => partition.Owner is null) &&
            store.Partitions.Select(partition => partition.Epoch).SequenceEqual(epochs);
        Console.WriteLine($"{(stopped ? "ok" : "FAIL")} {(ownedSets ? "set" : "fixed")} stop released its remaining scopes " +
            $"at the same epochs, made {stopCalls} set release call(s)");
        return expected && stopped;
    }

    private static async Task<bool> StopDuringDrainAsync(bool cancel)
    {
        var (store, wire, host, runtime) = await ReadyToReleaseAsync(ownedSets: true, () => 1_000L);
        var worker = host.NodeWorker!;
        var actor = InstanceRef.In(0, "stop-watch");
        await ArmEphemeralAsync(worker, actor);
        var wasVolatile = worker.Unwritten.Contains(actor.Id) && !store.Instances.ContainsKey(actor.Id);
        var epochs = store.Partitions.Select(partition => partition.Epoch).ToArray();
        wire.StallNextCommit(Timeout.InfiniteTimeSpan, actor.Partition);
        using var cancellation = new CancellationTokenSource();
        var stopping = runtime.StopAsync(cancellation.Token).AsTask();
        var held = wire.Stalls == 1 && !stopping.IsCompleted && wire.SetReleases == 0;
        var secondStop = runtime.StopAsync().AsTask();
        var deactivating = runtime.DeactivateAsync(actor.Partition);
        var balancing = runtime.BalanceAsync();
        var noOverlap = !secondStop.IsCompleted && deactivating.IsCompletedSuccessfully && balancing.IsCompletedSuccessfully;
        if (cancel) cancellation.Cancel();
        wire.Resume();
        await Task.WhenAll(stopping, deactivating, balancing).WaitAsync(TimeSpan.FromSeconds(5));
        try { await secondStop.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) when (cancel && secondStop.IsCanceled) { }
        var joined = cancel ? secondStop.IsCanceled : secondStop.IsCompletedSuccessfully;
        var committed = wire.Held.IsCompletedSuccessfully && (cancel ||
            (await wire.Held == "landed" && store.Instances.TryGetValue(actor.Id, out var row) && row.Status == InstanceStatus.Running &&
             store.Messages.Values.Any(message => message.Message.Target == actor && message.Message.Kind == "timer") &&
             Enumerable.Range(0, Count).All(scope => wire.ReleaseBegan(scope) >= wire.HeldAt)));
        var release = cancel ? 0 : 1;
        var expected = wasVolatile && held && noOverlap && joined && committed && wire.SetReleases == release &&
            store.Partitions.All(partition => partition.Owner == (cancel ? "first" : null)) &&
            store.Partitions.Select(partition => partition.Epoch).SequenceEqual(epochs);
        Console.WriteLine($"{(expected ? "ok" : "FAIL")} {(cancel ? "cancel during" : "overlap during")} the shutdown flush, " +
            $"volatile {wasVolatile}, entered {held}, no overlapping release {noOverlap}, joined {joined}, " +
            $"observed drain {committed}, made {wire.SetReleases} set release call(s)");
        return expected;
    }

    private static async Task<bool> CancelPartialDrainAsync()
    {
        var watcher = new Watcher();
        var (store, wire, host, runtime) = await ReadyToReleaseAsync(ownedSets: true, () => 1_000L, watcher);
        var actor = InstanceRef.In(7, "partial-drain");
        await ArmEphemeralAsync(host.NodeWorker!, actor);
        wire.StallNextCommit(Timeout.InfiniteTimeSpan, actor.Partition);
        using var cancellation = new CancellationTokenSource();
        var balance = runtime.BalanceAsync(cancellation.Token);
        var held = wire.Stalls == 1 && !balance.IsCompleted;
        cancellation.Cancel();
        wire.Resume();
        try { await balance.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) when (balance.IsCanceled) { }
        var expected = held && balance.IsCanceled && wire.SetReleases == 0 &&
            Enumerable.Range(0, Count).All(runtime.Owns) && store.Partitions.All(partition => partition.Owner == "first") &&
            watcher.Violations.Count == 0;
        Console.WriteLine($"{(expected ? "ok" : "FAIL")} cancelling a partial drain retains the observed activations and owners");
        await runtime.StopAsync();
        return expected;
    }

    private static async Task ArmEphemeralAsync(Worker worker, InstanceRef actor)
    {
        worker.Post(new("arm", actor, Kinds.Start, Start: Http.Spec(Http.Timer, ephemeral: true)), fresh: true);
        worker.RunTurn(actor.Id);
        await worker.FlushAsync();
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

    private static async Task<bool> ReactivatingAsync(int scope)
    {
        var watcher = new Watcher();
        var (store, wire, _, runtime) = await ReadyToReleaseAsync(ownedSets: true, () => 1_000L, watcher);
        await runtime.Activation(0)!.WokenAsync(null);
        wire.Stall(TimeSpan.FromMinutes(1));
        var pass = runtime.PassAsync(0).AsTask();     // a retained partition holds the shared worker
        var activation = runtime.ActivateAsync(scope);
        var balance = runtime.BalanceAsync();
        var surplus = scope >= Count / 2;
        bool pending;
        try
        {
            pending = !wire.Idle && !activation.IsCompleted && runtime.Activating(scope) &&
                balance.IsCompletedSuccessfully == surplus && wire.SetReleases == 0 &&
                store.Partitions.All(partition => partition.Owner == "first");
        }
        finally
        {
            wire.Resume();
            await Task.WhenAll(pass, activation, balance).WaitAsync(TimeSpan.FromSeconds(3));
        }
        if (surplus) await runtime.BalanceAsync();
        var expected = pending && activation.Result is not null && wire.SetReleases == 1 && watcher.Violations.Count == 0 &&
            Enumerable.Range(0, Count).All(p => runtime.Owns(p) == (p < 4) &&
                store.Partitions[p].Owner == (p < 4 ? "first" : null) && (wire.ReleaseBegan(p) is not null) == (p >= 4));
        var rule = surplus ? "reactivating a surplus scope defers the entire set release" : "reactivating a retained scope counts it once in the same round";
        Console.WriteLine($"{(expected ? "ok" : "FAIL")} {rule}; held [{string.Join(", ", Enumerable.Range(0, Count).Where(runtime.Owns))}], " +
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
        var host = DurableHost.ForNode("first", wire, new WorkflowInstance([.. Stress.Models(), new Http(0, (_, _) => throw new InvalidOperationException("No job expected."))]), Count,
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
