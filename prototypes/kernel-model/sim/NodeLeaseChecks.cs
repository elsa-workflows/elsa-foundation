namespace Kernel.Sim;

/// <summary>
/// K1b slice 4a: the Worker under the node lease, on the in-memory store with the switch on and a clock the check moves.
/// The guard (decisions, "The local lease guard"): one deadline, the latest send time plus the lease over the heartbeats
/// and takes whose reply came within their send time plus the lease; a commit renews nothing; a late reply restarts
/// nothing. The heartbeat's answer (ownership and fencing, "The node's heartbeat"): a partition held at the send that the
/// answer omits, or lists at another epoch, is stood down; an answer from before a reset is discarded.
/// </summary>
internal static class NodeLeaseChecks
{
    private static readonly TimeSpan Lease = Defaults.Lease, Window = TimeSpan.FromSeconds(3);

    internal static async Task Run(Action<bool, string> check)
    {
        await CommitRenewsNothing(check);
        await HeartbeatRenews(check, late: false);
        await HeartbeatRenews(check, late: true);
        await StandsDownWhatTheAnswerOmits(check);
        await DiscardsAnAnswerFromBeforeAReset(check);
        await LateTakeReply(check, heartbeat: true);
        await LateTakeReply(check, heartbeat: false);
        await LostRelease(check);
        await RetakesAfterAReset(check);
        await FixedHostDividesTheAnswer(check);
        await LateHeartbeat(check);
        await WedgedWorker(check);
        await RenewsDuringARetake(check);
    }

    // Review of #4868: a heartbeat answered while a retake is in flight renews the guard; only its stand-down side waits.
    // The retake sent at 0 lands at 9 s, so its own renewal ends at 10 s; the heartbeat sent at 8 s carries the guard to 18 s.
    private static async Task RenewsDuringARetake(Action<bool, string> check)
    {
        var (store, clock, advance) = World(2, out _);
        var wire = new DelayedCommit(store);
        var worker = Owner(wire, [0, 1], clock);
        await new DurableHost([worker]).ActivateAsync(0);
        store.Partitions[0].Position++;     // the next flush is refused: a reset, then the retakes
        await store.AddAsync(new Message("m", InstanceRef.In(0, "x"), "add", Json.Write(new Add("m")), Start: Counter.Spec));
        await worker.PollAsync();
        wire.DelayRetake = true;
        var flush = worker.FlushAsync();
        await wire.Retaken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        advance(8_000);
        await Beat(store, worker);
        advance(1_000);
        wire.RetakeReply.SetResult();
        await flush;
        advance(3_000);         // 12 s: past the retake's own renewal, within the heartbeat's
        var pass = await worker.RunAsync();
        check(pass is not null && worker.Owns && worker.Fences.Count == 2,
            $"a heartbeat answered during a retake renews the guard: owns at 12 s {worker.Owns}, held [{string.Join(", ", worker.Fences)}]");
    }

    private static InMemoryStore.MessageRow OnTrial(InMemoryStore store, string id, int partition)
    {
        store.AddAsync(new Message(id, InstanceRef.In(partition, "t" + id), "add", Json.Write(new Add(id)), Start: Counter.Spec)).AsTask().Wait();
        var row = store.Messages[id];
        row.Trial = store.Partitions[partition].Epoch;
        return row;
    }

    // NL-8, first run: p gives 1 up and the release never reaches the store; p stays live. Its next answer lists 1, which p
    // no longer holds, so p releases it: no row stays p's while p does not work it.
    private static async Task LostRelease(Action<bool, string> check)
    {
        var (store, clock, _) = World(2, out _);
        var wire = new DelayedCommit(store);
        var host = DurableHost.ForNode("p", wire, new WorkflowInstance([new Counter()]), 2, new() { Monotonic = clock, Partitions = 2 });
        await host.ActivateAsync(0);
        var scope = (await host.ActivateAsync(1))!;
        var worker = host.NodeWorker!;
        var beat = new NodeHeartbeat("p", wire, [worker], Lease, Window);
        wire.FailRelease = true;
        await scope.DisposeAsync();     // the host gives 1 up
        var stranded = store.Partitions[1].Owner == "p" && !worker.Holds(1);
        await worker.RunAsync();
        await beat.BeatAsync();
        check(stranded && store.Partitions[1].Owner is null && worker.Holds(0),
            $"NL-8: a release whose call failed is released by the next answer: stranded first {stranded}, owner of 1 now {store.Partitions[1].Owner ?? "none"}");
    }

    // NL-8, third run: p resets and retakes 0, 1 and 2 one at a time; a heartbeat sent before the reset, and one sent during
    // the retakes, are answered. Neither releases or stands down a retaken partition.
    private static async Task RetakesAfterAReset(Action<bool, string> check)
    {
        var (store, clock, _) = World(3, out _);
        var wire = new DelayedCommit(store);
        var worker = Owner(wire, [0, 1, 2], clock);
        await new DurableHost([worker]).ActivateAsync(0);
        var before = worker.BeforeHeartbeat();
        var early = await store.NodeHeartbeatAsync("p", Lease, Window);
        store.Partitions[0].Position++;     // the next flush is refused: a reset, then the retakes
        await store.AddAsync(new Message("m", InstanceRef.In(0, "x"), "add", Json.Write(new Add("m")), Start: Counter.Spec));
        await worker.PollAsync();
        wire.DelayRetake = true;
        var flush = worker.FlushAsync();
        await wire.Retaken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var during = worker.BeforeHeartbeat();
        var middle = await store.NodeHeartbeatAsync("p", Lease, Window);
        await worker.HeartbeatAnsweredAsync(during, middle);
        await worker.HeartbeatAnsweredAsync(before, early);
        wire.RetakeReply.SetResult();
        await flush;
        await worker.HeartbeatAnsweredAsync(before, early);
        var owned = Enumerable.Range(0, 3).All(p => store.Partitions[p].Owner == "p" && worker.EpochOf(p) == store.Partitions[p].Epoch && store.Partitions[p].Epoch == 2);
        check(owned, $"NL-8: answers from before and during the retakes release and stand down nothing: held [{string.Join(", ", worker.Fences)}]");
    }

    // NL-8, fourth run: a fixed host with a Worker per partition; one answer lists both. Neither Worker releases the other's.
    private static async Task FixedHostDividesTheAnswer(Action<bool, string> check)
    {
        var (store, clock, _) = World(2, out _);
        var (w0, w1) = (Owner(store, [0], clock), Owner(store, [1], clock));
        var host = new DurableHost([w0, w1]);
        await host.ActivateAsync(0); await host.ActivateAsync(1);
        var beat = new NodeHeartbeat("p", store, [w0, w1], Lease, Window);
        await beat.BeatAsync();
        check(w0.Holds(0) && w1.Holds(1) && store.Partitions[0].Owner == "p" && store.Partitions[1].Owner == "p",
            $"NL-8: a fixed host divides the answer and neither Worker releases the other's partition: owners {store.Partitions[0].Owner ?? "none"}, {store.Partitions[1].Owner ?? "none"}");
    }

    // NL-10, first run: p's heartbeat is delayed past its lease while p holds work. Its guard expires: p starts nothing,
    // stands every partition down and keeps them pending. Its next completed pass and heartbeat restart, and it retakes the
    // pending partitions at new epochs, counting a death for each one's previous-epoch trial messages.
    private static async Task LateHeartbeat(Action<bool, string> check)
    {
        var (store, clock, advance) = World(2, out _);
        var wire = new DelayedCommit(store);
        var worker = Owner(wire, [0, 1], clock);
        await new DurableHost([worker]).ActivateAsync(0);
        var trial = OnTrial(store, "t0", 0);
        var epochs = worker.Fences.Select(f => f.Epoch).ToList();
        var beat = new NodeHeartbeat("p", wire, [worker], Lease, Window);
        wire.HeartbeatReply = async answer => { advance(11_000); await Task.Yield(); return answer; };     // arrives past its send time plus the lease
        await beat.BeatAsync();
        wire.HeartbeatReply = null;
        await worker.RunAsync();        // the guard has expired: the pass stands everything down
        var pending = worker.Pending;
        var stoodDown = !worker.Owns && pending.Count == 2;
        await worker.RunAsync();        // a pass completes without the pending partitions
        await beat.BeatAsync();         // then the heartbeat, then the retake
        var retaken = worker.Fences;
        check(stoodDown && retaken.Count == 2 && retaken.Zip(epochs).All(t => t.First.Epoch == t.Second + 1) && worker.Pending.Count == 0 && trial.Attempts == 1,
            $"NL-10: a late heartbeat stands every partition down, kept pending; the next pass and heartbeat retake them at new epochs, counting the death: pending {pending.Count}, retaken [{string.Join(", ", retaken)}], attempts {trial.Attempts}");
    }

    // NL-10, second run: p's commit hangs while its host keeps running, and r is dead. p's host sends no heartbeat once a
    // lease has passed since p's last completed pass, and takes none of r's partitions. When p's guard expires its
    // partitions stay pending and no answer releases them; p's node lease expires, and q takes p's and r's partitions,
    // counting a death for p's trial messages.
    private static async Task WedgedWorker(Action<bool, string> check)
    {
        var (store, clock, advance) = World(3, out _);
        var wire = new DelayedCommit(store);
        var host = DurableHost.ForNode("p", wire, new WorkflowInstance([new Counter()]), 3, new() { Monotonic = clock, Partitions = 3 });
        await host.ActivateAsync(0);
        await store.TakeAsync([2], "r", Lease);     // r takes 2, then dies
        var worker = host.NodeWorker!;
        var beat = new NodeHeartbeat("p", wire, [worker], Lease, Window);
        var trial = OnTrial(store, "t0", 0);
        await store.AddAsync(new Message("m", InstanceRef.In(0, "x"), "add", Json.Write(new Add("m")), Start: Counter.Spec));
        await worker.WokenAsync(null);
        wire.DelayNext = true;          // the pass's commit executes and never answers
        var hung = worker.RunAsync().AsTask();
        await wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        advance(4_000);
        var renewed = await beat.BeatAsync() is not null;      // within a lease of the last completed pass (the take's)
        advance(7_000);                 // 11 s: no pass has completed for a lease
        var withheld = await beat.BeatAsync() is null && beat.Withheld == 1;
        var gained = await host.ActivateAsync(2) is not null || worker.Holds(2);
        advance(4_000);                 // 15 s: past the last renewal's lease
        var expired = !worker.Live && await beat.BeatAsync() is null;
        var stillP = store.Partitions[0].Owner == "p";
        var q = await store.TakeAsync([0, 1, 2], "q", Lease);
        check(renewed && withheld && !gained && expired && stillP && q.Taken.Count == 3 && trial.Attempts == 1,
            $"NL-10: a wedged Worker's host stops renewing and taking, and q takes p's and r's partitions, counting the death: renewed {renewed}, withheld {withheld}, gained r's {gained}, still p's {stillP}, q took {q.Taken.Count}, attempts {trial.Attempts}");
        wire.Reply.SetResult();
        await hung.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static (InMemoryStore Store, Func<long> Clock, Action<long> Advance) World(int partitions, out long start)
    {
        var time = start = 100_000L;
        var store = new InMemoryStore(partitions, () => time) { NodeLeaseAvailable = true };
        return (store, () => time, by => time += by);
    }

    private static Worker Owner(IKernelStore store, int[] partitions, Func<long> clock) =>
        new("p", partitions, store, new WorkflowInstance([new Counter()]), new WorkerOptions { Monotonic = clock, Partitions = partitions.Length });

    private static async Task<Worker.HeartbeatSent> Beat(InMemoryStore store, Worker worker, Func<NodeBeat, NodeBeat>? alter = null)
    {
        var sent = worker.BeforeHeartbeat();
        var answer = await store.NodeHeartbeatAsync(worker.Node, Lease, Window);
        await worker.HeartbeatAnsweredAsync(sent, alter?.Invoke(answer) ?? answer);
        return sent;
    }

    // A flush at five seconds lands and renews nothing: past the take's send time plus the lease the worker stands down.
    private static async Task CommitRenewsNothing(Action<bool, string> check)
    {
        var (store, clock, advance) = World(1, out _);
        var worker = Owner(store, [0], clock);
        await new DurableHost([worker]).ActivateAsync(0);
        await store.AddAsync(new Message("m", InstanceRef.In(0, "x"), "add", Json.Write(new Add("m")), Start: Counter.Spec));
        advance(5_000);
        for (var i = 0; i < 10 && !store.Instances.ContainsKey("0/x"); i++) { await worker.WokenAsync(null); await worker.RunAsync(); }
        var committed = store.Instances.ContainsKey("0/x");
        advance(5_500);
        var after = await worker.RunAsync();
        check(committed && after is null && !worker.Owns, $"under the node lease a landed commit renews nothing: committed {committed}, owns at 10.5 s {worker.Owns}");
    }

    // A heartbeat sent at five seconds and answered at once renews the guard to fifteen; one answered after its send time
    // plus the lease restarts nothing.
    private static async Task HeartbeatRenews(Action<bool, string> check, bool late)
    {
        var (store, clock, advance) = World(1, out _);
        var worker = Owner(store, [0], clock);
        await new DurableHost([worker]).ActivateAsync(0);
        advance(5_000);
        var sent = worker.BeforeHeartbeat();
        var answer = await store.NodeHeartbeatAsync(worker.Node, Lease, Window);
        if (late) advance(10_500);      // the answer arrives at 15.5 s: past 5 s plus the lease
        await worker.HeartbeatAnsweredAsync(sent, answer);
        if (!late) advance(7_000);      // 12 s: past the take's deadline, within the heartbeat's
        var pass = await worker.RunAsync();
        check(late ? pass is null && !worker.Owns : pass is not null && worker.Owns,
            late ? $"a heartbeat answered after its send time plus the lease restarts nothing: owns {worker.Owns}"
                 : $"a heartbeat answered in time renews the guard to its send time plus the lease: owns at 12 s {worker.Owns}");
    }

    // NL-3's Worker part: another process takes partition 1 while p renews; p's answer omits it. p stands 1 down within
    // the heartbeat and keeps 0 and 2 at their epochs, since nothing of 1 is in flight.
    private static async Task StandsDownWhatTheAnswerOmits(Action<bool, string> check)
    {
        var (store, clock, advance) = World(3, out _);
        var worker = Owner(store, [0, 1, 2], clock);
        await new DurableHost([worker]).ActivateAsync(0);
        var before = worker.Fences;
        (store.Partitions[1].Owner, store.Partitions[1].Epoch) = ("q", store.Partitions[1].Epoch + 1);     // a forced take by q
        advance(1_000);
        await Beat(store, worker);
        var after = worker.Fences;
        check(after.SequenceEqual(before.Where(f => f.Partition != 1)) && !worker.Holds(1),
            $"a partition the answer omits is stood down and the rest keep their epochs: before [{string.Join(", ", before)}], after [{string.Join(", ", after)}]");
    }

    // An answer sent before a reset is discarded on both sides: the retaken partitions are neither stood down nor renewed by it.
    private static async Task DiscardsAnAnswerFromBeforeAReset(Action<bool, string> check)
    {
        var (store, clock, advance) = World(2, out _);
        var worker = Owner(store, [0, 1], clock);
        await new DurableHost([worker]).ActivateAsync(0);
        var sent = worker.BeforeHeartbeat();
        var answer = await store.NodeHeartbeatAsync(worker.Node, Lease, Window);
        // A refused fence resets the worker, and it takes 0 and 1 back one at a time at new epochs.
        store.Partitions[0].Position++;
        await store.AddAsync(new Message("m", InstanceRef.In(0, "x"), "add", Json.Write(new Add("m")), Start: Counter.Spec));
        await worker.PollAsync(); await worker.FlushAsync();
        var retaken = worker.Fences;
        await worker.HeartbeatAnsweredAsync(sent, answer);      // lists 0 and 1 at the epochs from before the retakes
        advance(1_000);
        check(retaken.Count > 0 && worker.Fences.SequenceEqual(retaken) && sent.Generation != worker.BeforeHeartbeat().Generation,
            $"an answer sent before a reset is discarded: retaken [{string.Join(", ", retaken)}], after the answer [{string.Join(", ", worker.Fences)}]");
    }

    // NL-7's schedules: q's gain of partition 1 is sent at 0 and answered at 11 s. A heartbeat answered at 5 s set the guard
    // to 15 s, which stands; with no newer heartbeat the late reply restarts nothing and q stays expired.
    private static async Task LateTakeReply(Action<bool, string> check, bool heartbeat)
    {
        var (store, clock, advance) = World(2, out _);
        var wire = new DelayedCommit(store);
        var host = DurableHost.ForNode("q", wire, new WorkflowInstance([new Counter()]), 2, new() { Monotonic = clock, Partitions = 2 });
        await host.ActivateAsync(0);
        var worker = host.NodeWorker!;
        wire.DelayTake = true;
        var gain = host.ActivateAsync(1).AsTask();
        await wire.Taken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        advance(5_000);
        if (heartbeat) await Beat(store, worker);
        advance(6_000);
        wire.TakeReply.SetResult();
        await gain;
        advance(1_000);     // 12 s
        var pass = await worker.RunAsync();
        check(heartbeat ? pass is not null && worker.Holds(0) : pass is null && !worker.Owns,
            heartbeat ? $"a late take reply restarts nothing and the heartbeat's later deadline stands: holds 0 {worker.Holds(0)}, 1 {worker.Holds(1)}"
                      : $"a late take reply with no newer heartbeat leaves the worker expired: owns {worker.Owns}");
    }
}
