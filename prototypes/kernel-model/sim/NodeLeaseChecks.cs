namespace Kernel.Sim;

/// <summary>
/// K1b slice 4a: the Worker under the node lease, on the in-memory store with a clock the check moves.
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
        await AnswerDuringAPass(check);
        await SiblingTakeInFlight(check);
        await OwnTakeInFlight(check);
        await MarksOnlyFence(check);
        await HungReleaseDrain(check);
        await GivenUpDuringItsRetake(check);
        await DisposeOfAPendingPartition(check);
        await HandOverOfAPendingPartition(check);
        await HungResetRetakeClosesTheGate(check);
        await HungFirstTakeClosesTheGate(check);
        await TimelyFirstTakeKeepsTheGateOpen(check);
        await FixedHostPassesAGivenUpPending(check);
        await LazyFixedHostHeartbeats(check);
        await FixedHostAnswerJudgesOnce(check);
        await WatchdogClosesAStuckCommit(check, abortable: true);
        await WatchdogClosesAStuckCommit(check, abortable: false);
        await WatchdogClosesAStuckCommit(check, abortable: false, "throws", () => throw new InvalidOperationException("the abort failed"));
        await WatchdogClosesAStuckCommit(check, abortable: false, "blocks", () => Thread.Sleep(4_000));     // past the 2 s the look may take
    }

    // K1b slice 5b, the watchdog: p's commit is stuck on a live connection. At twice the lease since it was sent the
    // watchdog closes that call's connection (its token); a call that aborts on it ends, and the process lives on. One whose
    // client never resumes is still in flight a third of a lease later: then, and only then, the process ends. The commit
    // lands and its reply is stuck, so its outcome is unknown; that the server rolls an open transaction back is K4's.
    // A3: an abort that throws or blocks neither ends the watchdog nor holds back the end of the process.
    private static async Task WatchdogClosesAStuckCommit(Action<bool, string> check, bool abortable, string abort = "", Action? onAbort = null)
    {
        var (store, clock, advance) = World(1, out _);
        var wire = new DelayedCommit(store) { AbortOnToken = abortable, OnAbort = onAbort };
        var ended = new List<string>();
        var watchdog = new Watchdog(Lease, ended.Add, clock);
        var worker = Owner(watchdog.Watch(wire), [0], clock);
        await new DurableHost([worker]).ActivateAsync(0);
        await store.AddAsync(new Message("m", InstanceRef.In(0, "x"), "add", Json.Write(new Add("m")), Start: Counter.Spec));
        await worker.WokenAsync(null);
        wire.DelayNext = true;          // the pass's commit lands and its answer never comes
        var pass = worker.RunAsync().AsTask();
        await wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        advance(19_000);
        var early = watchdog.Check();       // 19 s: under twice the lease, untouched
        advance(1_000);
        int closed;
        try { closed = await Task.Run(watchdog.Check).WaitAsync(TimeSpan.FromSeconds(2)); }        // 20 s: closed
        catch (Exception e) { check(false, $"the watchdog survives an abort that {abort}: {e.GetType().Name}"); return; }
        if (abortable) await Task.WhenAny(pass, Task.Delay(TimeSpan.FromSeconds(5)));
        advance(1_000);
        watchdog.Check();                   // within a third of a lease of the close: the process waits for the call
        var waited = watchdog.Ended == 0;
        advance(2_400);
        watchdog.Check();                   // a third of a lease after the close
        var passEnded = pass.IsCompleted;
        wire.Reply.TrySetResult();
        await pass.WaitAsync(TimeSpan.FromSeconds(5));
        check(early == 0 && closed == 1 && waited && passEnded == abortable && (abortable ? ended.Count == 0 && watchdog.Ended == 0 : ended.Count == 1 && ended[0].Contains("commit", StringComparison.Ordinal)),
            abort != "" ? $"the watchdog ends the process when the abort {abort}: closed {closed}, waited {waited}, ended {ended.Count}" :
            abortable ? $"the watchdog closes a stuck commit's connection at twice the lease, and the process lives on: closed {closed}, pass ended {passEnded}, ended {watchdog.Ended}"
                      : $"the watchdog ends the process only when the stuck call outlives its closed connection: closed {closed}, waited {waited}, ended [{string.Join("; ", ended)}]");
    }

    // CodeRabbit 4636945436 (review B of #4870): an empty Worker's first take hangs. Once it has been in flight for a lease
    // the Worker is not live: the host withholds its heartbeat and takes nothing more. The late reply leaves 0 pending (a
    // reply past its send time plus the lease renews nothing), and the next heartbeat and pass retake it.
    private static async Task HungFirstTakeClosesTheGate(Action<bool, string> check)
    {
        var (store, clock, advance) = World(1, out _);
        var wire = new DelayedCommit(store) { DelayTake = true };
        var worker = Owner(wire, [0], clock);
        var beat = new NodeHeartbeat("p", wire, [worker], Lease, Window);
        var take = new DurableHost([worker]).ActivateAsync(0).AsTask();
        await wire.Taken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        advance(11_000);
        var closed = !worker.Live && !beat.Open && await beat.BeatAsync() is null;
        wire.TakeReply.SetResult();
        await take;
        var pending = !worker.Holds(0) && worker.Pending.ContainsKey(0);
        var late = pending ? worker.Pending[0] : 0L;
        var live = worker.Live;
        await beat.BeatAsync();
        await worker.RunAsync();        // the answer's retake of 0
        check(closed && pending && live && worker.Holds(0) && worker.EpochOf(0) > late,
            $"a first take in flight for a lease closes the gate: closed {closed}, unheld and pending after the late reply {pending}, live after {live}, holds 0 at last {worker.Holds(0)} epoch {worker.EpochOf(0)} (late {late})");
    }

    // The same take answered within the lease: the gate stays open while it is in flight, so a fresh node heartbeats and takes.
    private static async Task TimelyFirstTakeKeepsTheGateOpen(Action<bool, string> check)
    {
        var (store, clock, advance) = World(1, out _);
        var wire = new DelayedCommit(store) { DelayTake = true };
        var worker = Owner(wire, [0], clock);
        var beat = new NodeHeartbeat("p", wire, [worker], Lease, Window);
        var take = new DurableHost([worker]).ActivateAsync(0).AsTask();
        await wire.Taken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        advance(3_000);
        var open = worker.Live && beat.Open && await beat.BeatAsync() is not null;
        wire.TakeReply.SetResult();
        await take;
        check(open && worker.Holds(0) && worker.Live, $"a first take within the lease keeps the gate open: open {open}, holds 0 {worker.Holds(0)}");
    }

    // Review A of #4870, round 2 (binding on 5a): the fixed host keeps passing a Worker with pending partitions, one whose
    // activation was disposed included, until its pending set is empty (ownership-and-fencing, "The host therefore keeps
    // passing a Worker whose only partitions are pending"). w0 is disposed while the gate is closed, so its release cannot
    // retake its pending 0 and leaves it pending and given up. The host's own loop (its node pass, its heartbeat, its node
    // pass again) reopens the gate, counts 0's trial at the retake and releases it, takes 1 back for w1, and keeps renewing.
    private static async Task FixedHostPassesAGivenUpPending(Action<bool, string> check)
    {
        var (store, clock, advance) = World(2, out _);
        var (w0, w1) = (Owner(store, [0], clock), new Worker("p", 1, store, new WorkflowInstance([new Counter()]), new WorkerOptions { Monotonic = clock, Partitions = 1 }));
        var host = new DurableHost([w0, w1]);
        var scope = (await host.ActivateAsync(0))!;
        await host.ActivateAsync(1);
        await host.HeartbeatAsync(Window);      // the host's heartbeat and gate exist from here
        var trial = OnTrial(store, "t0", 0);
        advance(11_000);
        await w0.RunAsync(); await w1.RunAsync();       // both guards have expired: 0 and 1 are pending
        advance(11_000);                // no pass for a lease: the gate is closed
        var closed = host.Heartbeat is { Open: false };
        await scope.DisposeAsync();     // the host gives 0 up; its counting retake cannot run now
        var stranded = w0.Pending.ContainsKey(0) && store.Partitions[0].Owner == "p";
        await host.PassAsync();         // the passes no active scope gives: both Workers, pending only
        var renewed = await host.HeartbeatAsync(Window) is not null;
        await host.PassAsync();         // the answer's retakes
        var back = w1.Holds(1);
        advance(3_000);
        await host.PassAsync();         // w0 holds nothing and nothing is pending now; w1 passes as its scope would
        await w1.RunAsync();
        var still = await host.HeartbeatAsync(Window) is not null && w1.Holds(1);
        check(closed && stranded && renewed && trial.Attempts == 1 && store.Partitions[0].Owner is null && w0.Pending.Count == 0 && back && still,
            $"the fixed host keeps passing a Worker whose given-up partition is pending, until it is retaken and released: closed {closed}, stranded {stranded}, renewed {renewed}, attempts {trial.Attempts}, owner of 0 {store.Partitions[0].Owner ?? "none"}, w1 holds 1 {back}, renews after {still}");
    }

    // Architect F1 of #4877: ForNode on a node-lease store without owned sets has no Worker until ActivateAsync.
    // NodeLease and the first heartbeat must not wait for one; after a take the heartbeat still renews and the scope
    // survives past one lease.
    private static async Task LazyFixedHostHeartbeats(Action<bool, string> check)
    {
        var (store, clock, advance) = World(1, out _);
        var wire = new DelayedCommit(store) { WithoutOwnedSets = true };
        var host = DurableHost.ForNode("n1", wire, new WorkflowInstance([new Counter()]), 1, new() { Monotonic = clock, Partitions = 1 });
        var before = host.NodeLease is not null && await host.HeartbeatAsync(Window) is not null;
        var scope = await host.ActivateAsync(0);
        if (scope is not null) await scope.RunAsync();
        advance(5_000);
        var mid = await host.HeartbeatAsync(Window) is not null;
        if (scope is not null) await scope.RunAsync();
        advance(6_000);
        // Probe at t=11 s, before the last beat: the node record is valid to 15 s after the t=5 s renewal, expired at 10 s without it.
        var survived = scope is { Active: true } && await store.TakeAsync(0, "probe", Lease) is null;
        var renewed = await host.HeartbeatAsync(Window) is not null;
        check(before && mid && renewed && survived,
            $"a lazy fixed host heartbeats before any Worker and keeps a scope past one lease: before {before}, mid {mid}, renewed {renewed}, survived {survived}");
    }

    // N1, N5 and N6: a ForNode fixed host without owned sets, 256 Workers. One answer asks each Worker for its claimed
    // partitions once, so it is judged over one map: a rewrite that asks per sibling candidate makes this quadratic. The
    // heartbeat must be answered, and every Worker asked, or the count proves nothing.
    private static async Task FixedHostAnswerJudgesOnce(Action<bool, string> check)
    {
        const int n = 256;
        var (store, clock, _) = World(n, out _);
        var wire = new DelayedCommit(store) { WithoutOwnedSets = true };
        var host = DurableHost.ForNode("n1", wire, new WorkflowInstance([new Counter()]), n, new() { Monotonic = clock, Partitions = n });
        var workers = new Worker[n];
        for (var p = 0; p < n; p++) workers[p] = (Worker)(await host.ActivateAsync(p))!;
        var before = workers.Select(w => w.ClaimedLookups).ToArray();
        var answered = await host.HeartbeatAsync(Window) is not null;
        var waiting = workers.All(w => w.AnswerWaiting);       // each answer lists partitions the Worker does not hold: it waits
        foreach (var w in workers) await w.RunAsync();          // where each Worker judges it, asking about its siblings
        var judged = workers.All(w => !w.AnswerWaiting);
        var asked = workers.Select((w, i) => w.ClaimedLookups - before[i]).ToArray();
        check(answered && waiting && judged && asked.All(a => a == 1) && workers.All(w => w.Holds(w.Partition)),
            $"one answer over {n} Workers asks each once: answered {answered}, every Worker judged {waiting && judged}, asked {asked.Sum()} times (each once {asked.All(a => a == 1)}), held {workers.Count(w => w.Holds(w.Partition))}");
    }

    // Review round 2 of #4870, the coordinator's rule: a fixed Worker's pending partition, with a trial on it, is given up by
    // its release. The release runs the counting retake now, then releases at the epoch it returns: the trial is counted.
    private static async Task DisposeOfAPendingPartition(Action<bool, string> check)
    {
        var (store, clock, advance) = World(1, out _);
        var worker = Owner(store, [0], clock);
        await new DurableHost([worker]).ActivateAsync(0);
        var trial = OnTrial(store, "t0", 0);
        advance(11_000);
        await worker.RunAsync();        // the guard has expired: 0 is pending
        var pending = worker.Pending.ContainsKey(0);
        await worker.DisposeAsync();
        check(pending && trial.Attempts == 1 && store.Partitions[0].Owner is null && worker.Pending.Count == 0 && !worker.Holds(0),
            $"a release of a pending partition retakes it counting the death, then releases it: attempts {trial.Attempts}, owner {store.Partitions[0].Owner ?? "none"}, pending {worker.Pending.Count}");
    }

    // The same rule for a hand-over: the node's Worker gives up pending 0 and keeps pending 1. 0 stays pending and given up;
    // the next answer's retake counts its trial and releases it, and installs 1.
    private static async Task HandOverOfAPendingPartition(Action<bool, string> check)
    {
        var (store, clock, advance) = World(2, out _);
        var host = DurableHost.ForNode("p", store, new WorkflowInstance([new Counter()]), 2, new() { Monotonic = clock, Partitions = 2, Flaws = Flaws });
        var scope = (await host.ActivateAsync(0))!;
        await host.ActivateAsync(1);
        var worker = host.NodeWorker!;
        var beat = new NodeHeartbeat("p", store, [worker], Lease, Window);
        var trial = OnTrial(store, "t0", 0);
        advance(11_000);
        await worker.RunAsync();        // the guard has expired: 0 and 1 are pending
        var pending = worker.Pending.Count == 2;
        await scope.DisposeAsync();     // the host gives 0 up
        var kept = worker.Pending.ContainsKey(0) && store.Partitions[0].Owner == "p";
        await worker.RunAsync();
        await beat.BeatAsync();
        await worker.RunAsync();        // the answer's retakes
        check(pending && kept && trial.Attempts == 1 && store.Partitions[0].Owner is null && worker.Holds(1) && !worker.Holds(0) && worker.Pending.Count == 0,
            $"a hand-over of a pending partition leaves it pending until its counting retake, which releases it: kept {kept}, attempts {trial.Attempts}, owner of 0 {store.Partitions[0].Owner ?? "none"}, holds 1 {worker.Holds(1)}");
    }

    // CodeRabbit 5984758539: a refused fence over two partitions resets the Worker, and its retake hangs. The Worker holds
    // nothing and nothing is pending, yet it is not live once a lease has passed: the host withholds its heartbeat.
    private static async Task HungResetRetakeClosesTheGate(Action<bool, string> check)
    {
        var (store, clock, advance) = World(2, out _);
        var wire = new DelayedCommit(store);
        var worker = Owner(wire, [0, 1], clock);
        await new DurableHost([worker]).ActivateAsync(0);
        var beat = new NodeHeartbeat("p", wire, [worker], Lease, Window);
        store.Partitions[0].Position++;     // the next flush is refused: a reset, then the retakes
        await store.AddAsync(new Message("m", InstanceRef.In(0, "x"), "add", Json.Write(new Add("m")), Start: Counter.Spec));
        await worker.PollAsync();
        wire.DelayRetake = true;
        var flush = worker.FlushAsync();
        await wire.Retaken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        advance(11_000);
        var closed = !worker.Live && !beat.Open && await beat.BeatAsync() is null;
        wire.RetakeReply.SetResult();
        await flush;
        check(closed, $"a reset's retake that hangs keeps the pass gate closed: live {worker.Live}, withheld {beat.Withheld}");
    }

    // Review A of #4870, item 2: a fixed host's w1 has taken 1 in the store and not yet installed it when w0 judges an answer
    // that lists 1. w1 claims 1 until its install, so w0 does not release it, and w1 then holds 1 with the row its own.
    private static async Task SiblingTakeInFlight(Action<bool, string> check)
    {
        var (store, clock, _) = World(2, out _);
        var wire = new DelayedCommit(store);
        var (w0, w1) = (Owner(store, [0], clock), new Worker("p", 1, wire, new WorkflowInstance([new Counter()]), new WorkerOptions { Monotonic = clock, Partitions = 1 }));
        var host = new DurableHost([w0, w1]);
        await host.ActivateAsync(0);
        var beat = new NodeHeartbeat("p", store, [w0, w1], Lease, Window);
        wire.DelayTake = true;
        var gain = host.ActivateAsync(1).AsTask();
        await wire.Taken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var listed = await beat.BeatAsync() is { } answer && answer.Owned.Any(o => o.Partition == 1);
        await w0.RunAsync();        // w0 judges the answer while w1's take is in flight
        var kept = store.Partitions[1].Owner == "p";
        wire.TakeReply.SetResult();
        await gain;
        check(listed && kept && w1.Holds(1) && store.Partitions[1].Owner == "p" && store.Partitions[1].Epoch == w1.EpochOf(1),
            $"NL-8: a sibling's take in flight keeps its partition from the other Worker's release side: listed {listed}, kept {kept}, w1 holds 1 {w1.Holds(1)}");
    }

    // NL-8, slice 6a: an answer sent while the Worker's own take is in flight lists the partition the take wrote; its release
    // side frees nothing of it (mutation ReleaseDuringTake).
    private static async Task OwnTakeInFlight(Action<bool, string> check)
    {
        var (store, clock, _) = World(2, out _);
        var wire = new DelayedCommit(store);
        var host = DurableHost.ForNode("p", wire, new WorkflowInstance([new Counter()]), 2, new() { Monotonic = clock, Partitions = 2, Flaws = Flaws });
        await host.ActivateAsync(0);
        var worker = host.NodeWorker!;
        wire.DelayTake = true;
        var gain = host.ActivateAsync(1).AsTask();
        await wire.Taken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Beat(store, worker);
        await worker.RunAsync();        // judges the answer while the take is in flight
        wire.TakeReply.SetResult();
        await gain;
        check(worker.Holds(1) && store.Partitions[1].Owner == "p" && store.Partitions[1].Epoch == worker.EpochOf(1),
            $"NL-8: an answer sent during the Worker's own take frees nothing it took: owner of 1 {store.Partitions[1].Owner ?? "none"}, holds 1 {worker.Holds(1)}");
    }

    // NL-5, slice 6a: a batch of seen marks only over 0 and 1, with 1 taken on the wire after it locked 0 (its epoch moves),
    // is refused whole (mutation PlainReadEmptyFence: the fence reads without locking, so it lands).
    private static async Task MarksOnlyFence(Action<bool, string> check)
    {
        var (store, _, _) = World(2, out _);
        await store.AddAsync(new Message("m", InstanceRef.In(1, "x"), "add", Json.Write(new Add("m")), Start: Counter.Spec));
        var held = (await store.TakeAsync([0, 1], "p", Lease)).Taken;
        store.Locking = p => { if (p == 0) store.Partitions[1].Epoch++; };
        var landed = (await store.CommitAsync(CommitBatch.Owned(held, Lease, false, [], ["m"], []))).Landed;
        store.Locking = null;
        check(!landed, $"NL-5: a batch of marks only, with a partition taken on the wire, is refused whole: landed {landed}");
    }

    // Review A of #4870, item 3: a fixed host's Worker whose release drain hangs on its commit runs no pass, and none counts:
    // a lease later its host withholds the heartbeat.
    private static async Task HungReleaseDrain(Action<bool, string> check)
    {
        var (store, clock, advance) = World(1, out _);
        var wire = new DelayedCommit(store);
        var worker = Owner(wire, [0], clock);
        await new DurableHost([worker]).ActivateAsync(0);
        var beat = new NodeHeartbeat("p", wire, [worker], Lease, Window);
        await store.AddAsync(new Message("m", InstanceRef.In(0, "x"), "add", Json.Write(new Add("m")), Start: Counter.Spec));
        await worker.PollAsync();
        await worker.LoadAsync();
        for (var i = 0; i < 5 && !worker.Ready.Any(); i++) await worker.FlushAsync();     // the seen mark first
        foreach (var id in worker.Ready.ToList()) worker.RunTurn(id);       // its outcome waits in memory for a flush
        wire.DelayNext = true;          // the drain's commit lands and its answer waits
        var release = worker.DisposeAsync().AsTask();
        await wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        advance(4_000);
        var during = await worker.RunAsync();       // blocked by the release in flight: no pass ran
        advance(7_000);                 // 11 s: a lease since the last completed pass (the take's)
        var withheld = await beat.BeatAsync() is null && beat.Withheld == 1;
        wire.Reply.SetResult();
        await release.WaitAsync(TimeSpan.FromSeconds(5));
        check(during is null && withheld, $"a pass blocked by a release in flight does not count: the host withholds the heartbeat a lease later: withheld {withheld}");
    }

    // Review B of #4870, CR-5: a fixed Worker's pending partition is given up (its release) while its counting retake is in
    // flight. The reply is released at the epoch it returns, not installed, and nothing stays pending.
    private static async Task GivenUpDuringItsRetake(Action<bool, string> check)
    {
        var (store, clock, advance) = World(1, out _);
        var wire = new DelayedCommit(store);
        var worker = Owner(wire, [0], clock);
        await new DurableHost([worker]).ActivateAsync(0);
        var beat = new NodeHeartbeat("p", wire, [worker], Lease, Window);
        advance(11_000);
        await worker.RunAsync();        // the guard has expired: 0 is pending
        var pending = worker.Pending.ContainsKey(0);
        await beat.BeatAsync();
        wire.DelayRetake = true;
        var pass = worker.RunAsync().AsTask();      // the answer's retake of 0, counting the death
        await wire.Retaken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.DisposeAsync();    // the host gives 0 up meanwhile
        wire.RetakeReply.SetResult();
        await pass.WaitAsync(TimeSpan.FromSeconds(5));
        check(pending && !worker.Holds(0) && worker.Pending.Count == 0 && store.Partitions[0].Owner is null,
            $"CR-5: a pending partition given up during its counting retake is released, not installed: holds {worker.Holds(0)}, pending {worker.Pending.Count}, owner {store.Partitions[0].Owner ?? "none"}");
    }

    // Review of 4b: an answer that arrives during a long pass renews the guard at once, and its stand-down side waits for the
    // next pass. q forces 1 away while p's pass waits on its commit; the heartbeat sent at 5 s omits 1. At 12 s, past the
    // take's deadline, the pass ends and p still owns, still holding 1: nothing stood down beside the pass. Its next pass
    // stands 1 down and keeps 0 and 2 at their epochs.
    private static async Task AnswerDuringAPass(Action<bool, string> check)
    {
        var (store, clock, advance) = World(3, out _);
        var wire = new DelayedCommit(store);
        var worker = Owner(wire, [0, 1, 2], clock);
        await new DurableHost([worker]).ActivateAsync(0);
        var before = worker.Fences;
        var beat = new NodeHeartbeat("p", wire, [worker], Lease, Window);
        await store.AddAsync(new Message("m", InstanceRef.In(0, "x"), "add", Json.Write(new Add("m")), Start: Counter.Spec));
        await worker.WokenAsync(null);
        wire.DelayNext = true;          // the pass's commit lands and its answer waits
        var pass = worker.RunAsync().AsTask();
        await wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        (store.Partitions[1].Owner, store.Partitions[1].Epoch) = ("q", store.Partitions[1].Epoch + 1);     // a forced take by q
        advance(5_000);
        var answered = await beat.BeatAsync() is { } answer && answer.Owned.All(o => o.Partition != 1);
        var during = worker.Holds(1);
        advance(7_000);                 // 12 s
        wire.Reply.SetResult();
        var ended = await pass.WaitAsync(TimeSpan.FromSeconds(5));
        var after = worker.Holds(1);
        var next = await worker.RunAsync();
        check(answered && during && ended is not null && after && next is not null && worker.Owns && worker.Fences.Select(f => (f.Partition, f.Epoch)).SequenceEqual(before.Where(f => f.Partition != 1).Select(f => (f.Partition, f.Epoch))),
            $"an answer during a pass renews at once and stands down at the next pass: held 1 during {during}, after {after}, owns at 12 s {worker.Owns}, held [{string.Join(", ", worker.Fences)}]");
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
        var host = DurableHost.ForNode("p", wire, new WorkflowInstance([new Counter()]), 2, new() { Monotonic = clock, Partitions = 2, Flaws = Flaws });
        await host.ActivateAsync(0);
        var scope = (await host.ActivateAsync(1))!;
        var worker = host.NodeWorker!;
        var beat = new NodeHeartbeat("p", wire, [worker], Lease, Window);
        wire.FailRelease = true;
        await scope.DisposeAsync();     // the host gives 1 up
        var stranded = store.Partitions[1].Owner == "p" && !worker.Holds(1);
        await worker.RunAsync();
        await beat.BeatAsync();
        await worker.RunAsync();        // the answer's release side runs in the next pass
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
        worker.HeartbeatAnswered(during, middle);
        worker.HeartbeatAnswered(before, early);
        wire.RetakeReply.SetResult();
        await flush;
        worker.HeartbeatAnswered(before, early);
        await worker.RunAsync();        // the pass runs the recorded answer's stand-down side
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
        await w0.RunAsync(); await w1.RunAsync();
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
        await beat.BeatAsync();         // then the heartbeat
        await worker.RunAsync();        // and the next pass retakes
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
        var host = DurableHost.ForNode("p", wire, new WorkflowInstance([new Counter()]), 3, new() { Monotonic = clock, Partitions = 3, Flaws = Flaws });
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
        var store = new InMemoryStore(partitions, () => time, Flaws);
        return (store, () => time, by => time += by);
    }

    private static Worker Owner(IKernelStore store, int[] partitions, Func<long> clock) =>
        new("p", partitions, store, new WorkflowInstance([new Counter()]), new WorkerOptions { Monotonic = clock, Partitions = partitions.Length, Flaws = Flaws });

    /// <summary>K1b slice 6a: the mutation these witnesses run under (sim --node-lease-mutations); None otherwise.</summary>
    internal static Flaw Flaws { get; set; }

    private static async Task<Worker.HeartbeatSent> Beat(InMemoryStore store, Worker worker, Func<NodeBeat, NodeBeat>? alter = null)
    {
        var sent = worker.BeforeHeartbeat();
        var answer = await store.NodeHeartbeatAsync(worker.Node, Lease, Window);
        worker.HeartbeatAnswered(sent, alter?.Invoke(answer) ?? answer);
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
        worker.HeartbeatAnswered(sent, answer);
        if (!late) advance(7_000);      // 12 s: past the take's deadline, within the heartbeat's
        var pass = await worker.RunAsync();
        check(late ? pass is null && !worker.Owns : pass is not null && worker.Owns,
            late ? $"a heartbeat answered after its send time plus the lease restarts nothing: owns {worker.Owns}"
                 : $"a heartbeat answered in time renews the guard to its send time plus the lease: owns at 12 s {worker.Owns}");
    }

    // NL-3's Worker part: another process takes partition 1 while p renews; p's answer omits it. p stands 1 down at its
    // next pass and keeps 0 and 2 at their epochs, since nothing of 1 is in flight.
    private static async Task StandsDownWhatTheAnswerOmits(Action<bool, string> check)
    {
        var (store, clock, advance) = World(3, out _);
        var worker = Owner(store, [0, 1, 2], clock);
        await new DurableHost([worker]).ActivateAsync(0);
        var before = worker.Fences;
        (store.Partitions[1].Owner, store.Partitions[1].Epoch) = ("q", store.Partitions[1].Epoch + 1);     // a forced take by q
        advance(1_000);
        await Beat(store, worker);
        await worker.RunAsync();        // the stand-down side runs at the next pass
        var after = worker.Fences;
        check(after.SequenceEqual(before.Where(f => f.Partition != 1)) && !worker.Holds(1),
            $"a partition the answer omits is stood down and the rest keep their epochs: before [{string.Join(", ", before)}], after [{string.Join(", ", after)}]");
    }

    // An answer sent before a reset is discarded on both sides: the retaken partitions are neither stood down nor renewed by it.
    private static async Task DiscardsAnAnswerFromBeforeAReset(Action<bool, string> check)
    {
        var (store, clock, _) = World(3, out _);
        var worker = Owner(store, [0, 1], clock);
        await new DurableHost([worker]).ActivateAsync(0);
        // The old answer carries a partition whose take landed without the Worker installing it.
        var orphan = await store.TakeAsync(2, worker.Node, Lease);
        var sent = worker.BeforeHeartbeat();
        var answer = await store.NodeHeartbeatAsync(worker.Node, Lease, Window);
        worker.HeartbeatAnswered(sent, answer);
        var recorded = worker.AnswerWaiting;

        // A refused fence resets the Worker before the scheduled pass can judge the answer.
        store.Partitions[0].Position++;
        await store.AddAsync(new Message("m", InstanceRef.In(0, "x"), "add", Json.Write(new Add("m")), Start: Counter.Spec));
        await worker.PollAsync();
        await worker.FlushAsync();
        var retaken = worker.Fences;
        await worker.RunAsync();
        check(orphan is not null && recorded && !worker.AnswerWaiting && store.Partitions[2].Owner == worker.Node
            && retaken.Count == 2 && worker.Fences.SequenceEqual(retaken),
            "NL-8: a recorded heartbeat cannot release an unclaimed row after the Worker resets");

        worker.HeartbeatAnswered(sent, answer);
        check(sent.Generation != worker.BeforeHeartbeat().Generation && !worker.AnswerWaiting,
            "NL-8: a heartbeat answered after reset is discarded before it is recorded");
    }

    // NL-7's schedules: q's gain of partition 1 is sent at 0 and answered at 11 s. A heartbeat answered at 5 s set the guard
    // to 15 s, which stands; with no newer heartbeat the late reply restarts nothing and q stays expired.
    private static async Task LateTakeReply(Action<bool, string> check, bool heartbeat)
    {
        var (store, clock, advance) = World(2, out _);
        var wire = new DelayedCommit(store);
        var host = DurableHost.ForNode("q", wire, new WorkflowInstance([new Counter()]), 2, new() { Monotonic = clock, Partitions = 2, Flaws = Flaws });
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
