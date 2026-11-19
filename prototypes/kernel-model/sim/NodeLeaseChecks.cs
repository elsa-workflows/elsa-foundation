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
