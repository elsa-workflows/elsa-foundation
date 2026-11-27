namespace Kernel.Sim;

internal static class SetGainTimingChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await HeartbeatBeforeFirstGain(check);
        await OlderHeartbeatAfterRetake(check, queued: true, peerTakes: false);
        await OlderHeartbeatAfterRetake(check, queued: false, peerTakes: false);
        await OlderHeartbeatAfterRetake(check, queued: true, peerTakes: true);
        await CancelGrantedGain(check);
        await LateResetRetake(check);
        await LateSingleAfterHeartbeat(check, countingRetake: false);
        await LateSingleAfterHeartbeat(check, countingRetake: true);
        await LateGainAfterHeartbeat(check, alreadyHolding: false);
        await LateGainAfterHeartbeat(check, alreadyHolding: true);
    }

    private static async Task HeartbeatBeforeFirstGain(Action<bool, string> check)
    {
        long now = 100_000;
        var store = new InMemoryStore(3, () => now);
        var wire = new DelayedCommit(store) { DelayTake = true };
        var worker = Worker.ForNode("a", wire, new WorkflowInstance([new Counter()]), new() { Monotonic = () => now });
        var sent = worker.BeforeHeartbeat();
        var empty = await store.NodeHeartbeatAsync("a", Defaults.Lease, Defaults.Lease);
        var gain = worker.GainSetAsync([0, 2], default);
        await wire.Taken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        now += (long)Defaults.Lease.TotalMilliseconds;
        wire.TakeReply.SetResult();
        await gain;
        worker.HeartbeatAnswered(sent, empty);
        await worker.RunAsync();
        var carried = worker.Pending.Keys.Order().SequenceEqual([0, 2]);
        sent = worker.BeforeHeartbeat();
        worker.HeartbeatAnswered(sent, await store.NodeHeartbeatAsync("a", Defaults.Lease, Defaults.Lease));
        await worker.RunAsync();
        check(carried && worker.Pending.Count == 0 && worker.EpochOf(0) == 2 && worker.EpochOf(2) == 2,
            "a heartbeat sent before the first gain preserves both new pending claims for the fresh confirming heartbeat");
    }

    private static async Task OlderHeartbeatAfterRetake(Action<bool, string> check, bool queued, bool peerTakes)
    {
        long now = 100_000;
        var store = new InMemoryStore(2, () => now);
        var wire = new DelayedCommit(store);
        var worker = Worker.ForNode("a", wire, new WorkflowInstance([new Counter()]), new() { Monotonic = () => now });
        await worker.GainSetAsync([0, 1], default);
        await store.AddAsync(new Message("trial", InstanceRef.In(0, "waiting"), "work"));
        var trial = store.Messages["trial"];
        trial.Trial = worker.EpochOf(0);
        now += (long)Defaults.Lease.TotalMilliseconds + 1;
        await worker.PollAsync();       // both epoch-1 claims are pending
        var sibling = await store.TakeAsync(1, "peer", Defaults.Lease);
        var sent = worker.BeforeHeartbeat();
        var old = await store.NodeHeartbeatAsync("a", Defaults.Lease, Defaults.Lease);
        if (queued) worker.HeartbeatAnswered(sent, old);

        wire.DelayRetake = true;
        var taking = worker.GainAsync(0);
        await wire.Retaken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        now += (long)Defaults.Lease.TotalMilliseconds + 1;
        wire.RetakeReply.SetResult();
        await taking;
        if (!queued) worker.HeartbeatAnswered(sent, old);
        await worker.RunAsync();
        var carried = worker.Pending.Count == 1 && worker.Pending.GetValueOrDefault(0) == 2
            && store.Partitions[0].Owner == "a" && store.Partitions[0].Epoch == 2;
        if (peerTakes) await store.TakeAsync(0, "peer", Defaults.Lease);
        sent = worker.BeforeHeartbeat();
        worker.HeartbeatAnswered(sent, await store.NodeHeartbeatAsync("a", Defaults.Lease, Defaults.Lease));
        await worker.RunAsync();
        check(sibling?.Epoch == 2 && old.Owned is [{ Partition: 0, Epoch: 1 }] && carried && worker.Pending.Count == 0 && worker.Holds(0) == !peerTakes && !worker.Holds(1)
            && store.Partitions[0].Owner == (peerTakes ? "peer" : "a") && store.Partitions[0].Epoch == 3
            && store.Partitions[1].Owner == "peer" && store.Partitions[1].Epoch == 2 && trial.Attempts == 1,
            $"the {(queued ? "queued" : "delayed")} old heartbeat removes the unchanged missing claim, preserves the new pending epoch, and a fresh heartbeat {(peerTakes ? "confirms the peer" : "brings it back")}");
    }

    private static async Task LateGainAfterHeartbeat(Action<bool, string> check, bool alreadyHolding)
    {
        long now = 100_000;
        var store = new InMemoryStore(3, () => now);
        var wire = new DelayedCommit(store);
        var worker = Worker.ForNode("a", wire, new WorkflowInstance([new Counter()]), new() { Monotonic = () => now });
        if (alreadyHolding) await worker.GainAsync(1);
        var retained = worker.Fences;
        wire.DelayTake = true;
        var gain = worker.GainSetAsync([0, 2], default);
        await wire.Taken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        now = 109_900;
        var sent = worker.BeforeHeartbeat();
        now = 110_010;
        var peer = await store.TakeAsync([0, 2], "b", Defaults.Lease);
        var heartbeat = await store.NodeHeartbeatAsync("a", Defaults.Lease, Defaults.Lease);
        worker.HeartbeatAnswered(sent, heartbeat);
        now = 110_020;
        wire.TakeReply.SetResult();
        await gain;
        var safe = worker.Fences.SequenceEqual(retained) && worker.Pending.Keys.Order().SequenceEqual([0, 2]);
        await CheckPendingPassGate(worker, wire, check, alreadyHolding ? "incremental grouped gain" : "cold grouped gain");
        await worker.RunAsync();       // the current answer drops the two stale pending claims
        check(safe && worker.Pending.Count == 0 && worker.Fences.SequenceEqual(retained)
            && heartbeat.Owned.Select(p => p.Partition).SequenceEqual(retained.Select(p => p.Partition))
            && peer.Taken.Count == 2 && peer.Taken.All(p => p.Epoch == 2 && store.Partitions[p.Partition].Epoch == p.Epoch
                && store.Partitions[p.Partition].Owner == "b"),
            $"a heartbeat during a late {(alreadyHolding ? "incremental" : "cold")} gain preserves only confirmed fences and the peer's epochs");
    }

    private static async Task LateSingleAfterHeartbeat(Action<bool, string> check, bool countingRetake)
    {
        long now = 100_000;
        var store = new InMemoryStore(1, () => now);
        var wire = new DelayedCommit(store);
        var worker = new Worker("a", 0, wire, new WorkflowInstance([new Counter()]), new() { Monotonic = () => now });
        if (countingRetake)
        {
            await worker.TakeAsync();
            now += (long)Defaults.Lease.TotalMilliseconds;
            await worker.PollAsync();
            wire.DelayRetake = true;
        }
        else wire.DelayTake = true;
        var request = worker.TakeAsync();
        await (countingRetake ? wire.Retaken.Task : wire.Taken.Task).WaitAsync(TimeSpan.FromSeconds(5));
        now += 9_900;
        var sent = worker.BeforeHeartbeat();
        now += 110;
        var peer = await store.TakeAsync(0, "b", Defaults.Lease);
        worker.HeartbeatAnswered(sent, await store.NodeHeartbeatAsync("a", Defaults.Lease, Defaults.Lease));
        now += 10;
        (countingRetake ? wire.RetakeReply : wire.TakeReply).SetResult();
        await request;
        var pending = !worker.Owns && worker.Pending.GetValueOrDefault(0) == (countingRetake ? 2 : 1);
        await CheckPendingPassGate(worker, wire, check, countingRetake ? "counted retake" : "fixed take");
        await worker.RunAsync();
        check(pending && !worker.Owns && worker.Pending.Count == 0 && peer?.Epoch == (countingRetake ? 3 : 2)
            && store.Partitions[0].Owner == "b" && store.Partitions[0].Epoch == peer.Epoch,
            $"the late {(countingRetake ? "counting retake" : "fixed-host take")} stays pending across a newer heartbeat until the peer's ownership is confirmed");
    }

    private static async Task CheckPendingPassGate(Worker worker, IKernelStore store, Action<bool, string> check, string request)
    {
        var gate = new NodeHeartbeat(worker.Node, store, [worker], Defaults.Lease, Defaults.Lease);
        var withheld = await gate.BeatAsync() is null && gate.Withheld == 1;
        await worker.RunAsync();
        var renewed = await gate.BeatAsync() is not null;
        check(withheld && renewed, $"a late {request} leaves the heartbeat withheld until a completed pass");
    }

    private static async Task LateResetRetake(Action<bool, string> check)
    {
        long now = 100_000;
        var store = new InMemoryStore(2, () => now);
        var wire = new DelayedCommit(store);
        var worker = new Worker("a", [0, 1], wire, new WorkflowInstance([new Counter()]), new() { Monotonic = () => now, Partitions = 2 });
        await worker.TakeAsync();
        store.Partitions[0].Position++;     // force a reset and conditional retakes after the refused commit
        await store.AddAsync(new Message("m", InstanceRef.In(0, "x"), "add", Json.Write(new Add("m")), Start: Counter.Spec));
        await worker.PollAsync();
        wire.DelayRetake = true;
        var flush = worker.FlushAsync();
        await wire.Retaken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        now = 109_900;
        var sent = worker.BeforeHeartbeat();
        now = 110_010;
        var peer = await store.TakeAsync(0, "b", Defaults.Lease);
        worker.HeartbeatAnswered(sent, await store.NodeHeartbeatAsync("a", Defaults.Lease, Defaults.Lease));
        now = 110_020;
        wire.RetakeReply.SetResult();
        await flush;
        var safe = !worker.Holds(0);
        sent = worker.BeforeHeartbeat();
        worker.HeartbeatAnswered(sent, await store.NodeHeartbeatAsync("a", Defaults.Lease, Defaults.Lease));
        await worker.RunAsync();
        check(safe && !worker.Holds(0) && worker.Holds(1) && worker.Pending.Count == 0
            && peer?.Epoch == 3 && store.Partitions[0].Epoch == peer.Epoch && store.Partitions[0].Owner == "b",
            "a heartbeat during a late reset retake keeps the peer's epoch and reconciles the remaining partition");
    }

    // Hold the queued invocation at its continuation, after the semaphore wakes it and before host code resumes.
    private sealed class GrantedTurn : SynchronizationContext
    {
        public TaskCompletionSource<Action> Continuation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state) => Continuation.SetResult(() => callback(state));
    }

    private static async Task CancelGrantedGain(Action<bool, string> check)
    {
        var store = new InMemoryStore(3, () => 100_000L);
        var wire = new DelayedCommit(store) { DelayTake = true };
        var host = DurableHost.ForNode("a", wire, new WorkflowInstance([new Counter()]), 3, new() { Monotonic = () => 100_000, Partitions = 3 });
        var holding = host.ActivateAsync(0).AsTask();
        await wire.Taken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var stop = new CancellationTokenSource();
        var granted = new GrantedTurn();
        var previous = SynchronizationContext.Current;
        Task<IReadOnlyList<IScopeActivation>> queued;
        try
        {
            SynchronizationContext.SetSynchronizationContext(granted);
            queued = ((IScopeSetGain)host).ActivateAsync([1, 2], stop.Token).AsTask();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        wire.TakeReply.SetResult();
        await holding;
        var enter = await granted.Continuation.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        enter();
        var cancelled = false;
        try { await queued; }
        catch (OperationCanceledException) { cancelled = true; }
        check(cancelled && host.NodeWorker!.Fences.Select(f => f.Partition).SequenceEqual([0])
            && store.Partitions[1].Epoch == 0 && store.Partitions[2].Epoch == 0,
            "a cancelled gain after the host turn is granted starts no take and retains the previous scope");
    }
}
