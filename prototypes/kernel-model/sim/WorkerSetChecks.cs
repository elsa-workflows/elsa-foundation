namespace Kernel.Sim;

/// <summary>
/// K1 slice 2a: one worker owns several virtual partitions on the in-memory store. Turns in several partitions flush as
/// one owned batch. A refused fence rolls the whole flush back, and the worker then takes back, one at a time and at a
/// new epoch, each partition it still holds; a lost answer does the same. Slice 2b: suspects, trials and child ids per
/// virtual-partition epoch.
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
            await store.AddAsync(Add(x, one));
            await store.AddAsync(Add(y, two));
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

        // The marks land at each partition's epoch. The store then sees the owner's node lease expire, and another node takes 1.
        ready = await Deliver("three", "four");
        check(store.Messages["three"].Seen == e0 && store.Messages["four"].Seen == e1, "each mark records its own partition's epoch");
        store.Nodes[store.Partitions[1].Owner!].LeaseUntil = 0;
        var taken = await store.TakeAsync(1, "b", Defaults.Lease);
        before = commits;
        await worker.FlushAsync();      // the turns in 0 and 1: the fence of 1 is stale
        var rolledBack = commits == before + 1 && store.Instances[x.Id].Revision == 1 && store.Instances[y.Id].Revision == 1
            && store.Partitions[0].Position == p0 && store.Partitions[1].Position == p1 && store.Messages.ContainsKey("three") && store.Messages.ContainsKey("four");
        check(taken is not null && ready.Count == 2 && rolledBack, "one stale fence rolls the whole flush back");
        check(worker.Fences.SequenceEqual([new PartitionFence(0, e0 + 1, p0)]) && store.Partitions[0].Owner == "a" && Unserved(store, worker, time) == "",
            $"after a refused fence the worker retakes 0 at a new epoch and drops 1: held [{string.Join(", ", worker.Fences)}]{Unserved(store, worker, time)}");

        // Memory went with the reset; the new epoch's poll reads what the worker had marked in 0.
        for (var i = 0; i < 20 && store.Instances[x.Id].Revision < 2; i++) await worker.RunAsync();
        var journal0 = store.Journal.Keys.Where(k => k.Partition == 0).Select(k => k.Position).Order().ToList();
        check(store.Instances[x.Id].Revision == 2 && !store.Messages.ContainsKey("three") && worker.PositionOf(0) == store.Partitions[0].Position
              && journal0.SequenceEqual(Enumerable.Range(1, (int)store.Partitions[0].Position).Select(p => (long)p)),
            $"the retaken partition continues from the store with no journal hole: x at revision {store.Instances[x.Id].Revision}, journal [{string.Join(" ", journal0)}]");
        check(store.Partitions[1].Position == p1 && store.Partitions[1].Owner == "b" && store.Instances[y.Id].Revision == 1 && !store.Messages["four"].Done,
            "the moved partition is left to its new owner");

        await LostAnswer(check);
        await MovedRows(check);
        await TurnAndMark(check);
        await TimerAcrossReset(check);
        await PollLimitAcrossReset(check);
        await WithoutOwnedSets(check);
        await SuspectsAndTrials(check);
        await ChildIds(check);
        await StandDownDuringRetake(check);
        await StandDownMeetsRefusedFlush(check);
        await NodeSet(check);
        await SharedPassDeadline(check);
        await LossLeavesNothingBehind(check);
        await LossNotQuiet(check);
        await NodeWithoutOwnedSets(check);
        await ProjectorRetry(check);
        await CountsAgree(check);
    }

    // Review of #4817 (reviewer A): a projector round that throws is tried again, and the tail is projected. Five journal
    // rows in partition 0, pages of two, and the first read of the journal throws: one failed round, then three.
    private static async Task ProjectorRetry(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(1, () => time);
        var host = DurableHost.ForNode("a", store, new WorkflowInstance([new Counter()]), 1, new() { Monotonic = () => time, Partitions = 1 });
        var scope = (await host.ActivateAsync(0))!;
        for (var i = 0; i < 5; i++) await store.AddAsync(Add(InstanceRef.In(0, "c" + i), "t" + i));
        await scope.WokenAsync(null);
        for (var i = 0; i < 5; i++) await scope.RunAsync();
        var flaky = new DelayedCommit(store) { FailJournalReads = 1 };
        var projection = new NodeProjection(flaky, take: 2);
        projection.Nudge([0]);
        var caughtUp = false;
        for (var i = 0; i < 250 && !caughtUp; i++)
        {
            await Task.Delay(20);
            caughtUp = (await store.ProjectedAsync(0)) is { Written: 5 } p && p.Position == 5;
        }
        await projection.Idle;
        check(caughtUp && flaky.JournalReadsFailed == 1 && projection.RoundsOf(0) == 4,
            $"a projector round that throws is tried again and the tail is projected: caught up {caughtUp}, failed reads {flaky.JournalReadsFailed}, rounds {projection.RoundsOf(0)}");
    }

    // Review of #4816 (reviewer B): on a store without owned sets (commit=ef) the node's host gives each scope its own
    // worker. Activate, give up and activate again all work, and only positional one-partition batches are sent.
    private static async Task NodeWithoutOwnedSets(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(2, () => time);
        var wire = new DelayedCommit(store) { WithoutOwnedSets = true };
        var host = DurableHost.ForNode("e", wire, new WorkflowInstance([new Counter()]), 2, new() { Monotonic = () => time, Partitions = 2 });
        var (zero, one) = ((await host.ActivateAsync(0))!, (await host.ActivateAsync(1))!);
        var epoch = store.Partitions[1].Epoch;
        await one.DisposeAsync();
        var given = store.Partitions[1].Owner;
        var again = await host.ActivateAsync(1);
        foreach (var p in new[] { 0, 1 }) await store.AddAsync(Add(InstanceRef.In(p, "x"), "x" + p));
        foreach (var scope in new[] { zero, again! })
        {
            await scope.WokenAsync(null);
            for (var i = 0; i < 5; i++) await scope.RunAsync();
        }
        check(host.NodeWorker is null && zero is Worker { Partition: 0 } && again is Worker { Partition: 1, Active: true } && ReferenceEquals(again, one)
              && given is null && store.Partitions[1].Epoch == epoch + 1 && store.Instances.ContainsKey("0/x") && store.Instances.ContainsKey("1/x")
              && wire.Batches.Count > 0 && wire.Batches.All(b => b.Fences is null),
            $"a node on a store without owned sets runs a worker per scope: given up to {given ?? "nobody"}, epoch {epoch} then {store.Partitions[1].Epoch}, {wire.Batches.Count} batches, owned {wire.Batches.Count(b => b.Fences is not null)}");
    }

    // Review of #4816 (reviewer A): a row sent to a partition that is then handed over is never read back, so it must not
    // keep the worker behind (F1). With PollLimit 1, x in 0 forwards to 2/z while the worker is behind, then 2 is handed
    // over; a later forward to 3/w is handed over in memory again, its row seen at 3's epoch.
    private static async Task LossLeavesNothingBehind(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(4, () => time);
        var host = DurableHost.ForNode("a", store, new WorkflowInstance([new Counter()]), 4, new() { Monotonic = () => time, Partitions = 4, PollLimit = 1 });
        var worker = host.NodeWorker!;
        var scopes = new IScopeActivation?[4];
        foreach (var p in new[] { 0, 2, 3 }) scopes[p] = await host.ActivateAsync(p);
        var x = InstanceRef.In(0, "x");
        async Task Pass() { await scopes[0]!.WokenAsync(null); await scopes[0]!.RunAsync(); }
        await store.AddAsync(new("one", x, "add", Json.Write(new Add("one", ForwardTo: "2/z")), Start: Counter.Spec));
        await store.AddAsync(new("two", x, "add", Json.Write(new Add("two")), Start: Counter.Spec));
        for (var i = 0; i < 10 && !store.Messages.Values.Any(m => m.Message.Target.Id == "2/z"); i++) await Pass();
        await scopes[2]!.DisposeAsync();
        for (var i = 0; i < 4; i++) await Pass();
        await store.AddAsync(new("three", x, "add", Json.Write(new Add("three", ForwardTo: "3/w")), Start: Counter.Spec));
        long? seen = null;
        for (var i = 0; i < 10 && seen is null; i++)
        {
            await Pass();
            seen = store.Messages.Values.FirstOrDefault(m => m.Message.Target.Id == "3/w")?.Seen;
        }
        check(store.Partitions[2].Owner is null && seen == worker.EpochOf(3),
            $"a hand-over leaves nothing behind: the forward to 3/w was written seen at {seen?.ToString() ?? "never"}, partition 3's epoch is {worker.EpochOf(3)}");
    }

    // Review of #4816 (reviewer A): a loss whose partition has a turn in flight, or a trial, is not quiet. The worker
    // writes what every partition holds in memory only, resets, releases it and retakes the rest at new epochs.
    private static async Task LossNotQuiet(Action<bool, string> check)
    {
        const long time = 100;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(InMemoryStore, DurableHost, IScopeActivation[])> Node(Action<InMemoryStore>? before = null)
        {
            var store = new InMemoryStore(3, () => time);
            before?.Invoke(store);
            var host = DurableHost.ForNode("a", store, new WorkflowInstance([new Counter(), new Ticker(TimeSpan.FromSeconds(1)), new Suspending(gate.Task)]), 3, new() { Monotonic = () => time, Partitions = 3 });
            return (store, host, [(await host.ActivateAsync(0))!, (await host.ActivateAsync(1))!, (await host.ActivateAsync(2))!]);
        }
        void Fallback(string what, InMemoryStore store, Worker worker, IReadOnlyList<PartitionFence> held, string detail) =>
            check(store.Partitions[1].Owner is null && worker.Fences.Select(f => (f.Partition, f.Epoch)).SequenceEqual(held.Where(f => f.Partition != 1).Select(f => (f.Partition, f.Epoch + 1)))
                  && Unserved(store, worker, time) == "", $"{what}: held [{string.Join(", ", held)}], now [{string.Join(", ", worker.Fences)}]{Unserved(store, worker, time)}{detail}");

        // A turn suspended in 1, and an instance held in memory only in kept 0: it is written before the reset.
        var (store, host, scopes) = await Node();
        var worker = host.NodeWorker!;
        worker.Post(new("arm-0", InstanceRef.In(0, "t"), "arm", Start: Ticker.Spec with { Ephemeral = true }), fresh: true);
        await store.AddAsync(new("s", InstanceRef.In(1, "s"), "go", Start: Suspending.Spec));
        await scopes[0].WokenAsync(null);
        for (var i = 0; i < 10 && !worker.Instances.Any(s => s.Id == "1/s" && s.Busy); i++) await scopes[0].RunAsync();
        var unwritten = worker.Unwritten.ToList();
        var held = worker.Fences;
        await scopes[1].DisposeAsync();
        Fallback("a loss with a turn in flight in its partition resets and retakes the rest", store, worker, held,
            $"; held in memory only before [{string.Join(" ", unwritten)}], 0/t written {store.Instances.ContainsKey("0/t")}");
        check(unwritten.SequenceEqual(["0/t"]) && store.Instances.ContainsKey("0/t"), "the fallback writes the kept partitions' instances held in memory only before it resets");
        gate.SetResult();

        // A trial in 1: what "b" marked there at epoch 2 before it was lost.
        (store, host, scopes) = await Node(s => (s.Partitions[1].Owner, s.Partitions[1].Epoch) = ("b", 2));
        worker = host.NodeWorker!;
        await store.AddAsync(new("m", InstanceRef.In(1, "z"), "add", Json.Write(new Add("m")), Start: Counter.Spec));
        store.Messages["m"].Seen = 2;
        await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
        var trial = worker.Trial.ToList();
        held = worker.Fences;
        await scopes[1].DisposeAsync();
        Fallback("a loss with a trial in its partition resets and retakes the rest", store, worker, held, $"; on trial [{string.Join(" ", trial)}]");
    }

    /// <summary>A turn that waits for a gate: an async turn in flight.</summary>
    private sealed class Suspending(Task gate) : IExecutionModel
    {
        public static readonly StartSpec Spec = new("suspending", "s");
        public string Key => "suspending";
        public Type StateType => typeof(CounterState);
        public int StateVersion => 1;
        public object CreateState(StartSpec start) => new CounterState();
        public object UpgradeState(string state, int fromVersion) => Json.Read<CounterState>(state);
        public async ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken) => await gate;
        public ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken) => new((object?)null);
    }

    // The node's one pass serves every scope, and a scope that reuses its answer gets what is left of it. Review schedule
    // (CodeRabbit, #4816): B reads its 30 s timer; A runs a pass that answers 30 s; A is given up at t = 1 s; B calls at
    // t = 30 s, not having seen A's pass. The timer must run then, not 30 s later.
    private static async Task SharedPassDeadline(Action<bool, string> check)
    {
        long time = 0;
        var store = new InMemoryStore(2, () => time);
        var host = DurableHost.ForNode("a", store, new WorkflowInstance([new Counter()]),
            2, new() { Monotonic = () => time, Partitions = 2, Lease = TimeSpan.FromSeconds(120), Horizon = TimeSpan.FromSeconds(60) });
        var (a, b) = ((await host.ActivateAsync(0))!, (await host.ActivateAsync(1))!);
        var timer = InstanceRef.In(1, "t");
        await store.AddAsync(Add(timer, "due", due: 30_000));
        await b.WokenAsync(null);
        var first = await b.RunAsync();          // B's pass reads the timer
        await a.WokenAsync(null);
        var shared = await a.RunAsync();         // A's pass, which B has not seen: 30 s to the timer
        time = 1_000;
        await a.DisposeAsync();
        time = 30_000;
        var next = await b.RunAsync();
        for (var i = 0; i < 5 && next == TimeSpan.Zero; i++) next = await b.RunAsync();
        check(shared == TimeSpan.FromSeconds(30) && !a.Active && store.Instances.TryGetValue(timer.Id, out var row) && row.Revision == 1,
            $"a scope that reuses a shared pass answer gets what is left of it: B's first answer {first}, A's {shared}, the timer's row {(store.Instances.TryGetValue(timer.Id, out var r) ? r.Revision : 0)} at 30 s");
    }

    // Slice 5 (D1): the node's one worker gains and loses partitions while it runs, through its host. A gain takes that
    // row alone. A quiet loss releases that row only: the rest keep their epochs, positions and memory. A loss with a job
    // still running in the partition resets, releases it, and takes the rest back at new epochs.
    private static async Task NodeSet(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(4, () => time);
        var job = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = DurableHost.ForNode("a", store, new WorkflowInstance([new Counter(), new Runner((_, _) => new ValueTask<object?>(job.Task))]),
            4, new() { Monotonic = () => time, Partitions = 4 });
        var worker = host.NodeWorker!;
        var scopes = new IScopeActivation?[4];
        foreach (var p in new[] { 0, 2, 3 }) scopes[p] = await host.ActivateAsync(p);
        var (x, y, r) = (InstanceRef.In(0, "x"), InstanceRef.In(2, "y"), InstanceRef.In(3, "r"));
        await store.AddAsync(Add(x, "one"));
        await store.AddAsync(Add(y, "two"));
        await scopes[0]!.WokenAsync(null);
        for (var i = 0; i < 10 && !(store.Instances.ContainsKey(x.Id) && store.Instances.ContainsKey(y.Id)); i++) await scopes[0]!.RunAsync();
        var before = worker.Fences;
        check(string.Join(" ", before.Select(f => f.Partition)) == "0 2 3" && before.Select(f => f.Epoch).All(e => e > 0) && store.Instances.ContainsKey(y.Id),
            $"one worker serves the scattered set {{0, 2, 3}}: [{string.Join(", ", before)}]");
        scopes[1] = await host.ActivateAsync(1);
        check(scopes[1] is { Active: true } && worker.Fences.Where(f => f.Partition != 1).SequenceEqual(before), $"a gain takes that row alone: [{string.Join(", ", worker.Fences)}]");
        await scopes[2]!.DisposeAsync();
        var memory = worker.Instances.Select(i => i.Id).ToList();
        check(store.Partitions[2].Owner is null && !scopes[2]!.Active && worker.Fences.Where(f => f.Partition != 1).SequenceEqual(before.Where(f => f.Partition != 2))
              && memory.Contains(x.Id) && !memory.Contains(y.Id), $"a quiet loss releases that row only and keeps the rest: [{string.Join(", ", worker.Fences)}], memory [{string.Join(" ", memory)}]");
        await store.AddAsync(new Message("go", r, "go", Json.Write("work"), Start: Runner.Spec));
        await scopes[0]!.WokenAsync(null);
        for (var i = 0; i < 10 && worker.JobCompletion(r.Id, "work") is null; i++) await scopes[0]!.RunAsync();
        var held = worker.Fences;
        await scopes[3]!.DisposeAsync();
        var expected = held.Where(f => f.Partition != 3).Select(f => f with { Epoch = f.Epoch + 1 });
        check(worker.JobCompletion(r.Id, "work") is null && store.Partitions[3].Owner is null && worker.Fences.SequenceEqual(expected) && Unserved(store, worker, time) == "",
            $"a loss with a job running resets and takes the rest back at new epochs: held [{string.Join(", ", held)}], now [{string.Join(", ", worker.Fences)}]{Unserved(store, worker, time)}");
        job.SetResult(null);
    }

    // A stand-down while a retake is in flight: the retake installs nothing, gives up what it took, and gives up what it
    // had not reached yet (partition 2) at the epoch it held.
    private static async Task StandDownDuringRetake(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(3, () => time);
        var wire = new DelayedCommit(store);
        var worker = new Worker("a", [0, 1, 2], wire, new WorkflowInstance([new Counter()]), new() { Monotonic = () => time, Partitions = 3 });
        await new DurableHost([worker]).ActivateAsync(0);
        var (e0, e2) = (worker.EpochOf(0), worker.EpochOf(2));
        await store.AddAsync(Add(InstanceRef.In(1, "y"), "two"));
        await Step(worker);
        store.Nodes[store.Partitions[1].Owner!].LeaseUntil = 0;
        await store.TakeAsync(1, "b", Defaults.Lease);
        wire.DelayRetake = true;
        var flush = worker.FlushAsync();        // refused: the retake of 0 executes, and its answer waits
        await wire.Retaken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.DisposeAsync();
        wire.RetakeReply.SetResult();
        await flush.WaitAsync(TimeSpan.FromSeconds(5));
        check(!worker.Owns && store.Partitions[0].Epoch == e0 + 1 && store.Partitions[0].Owner is null,
            $"a stand-down during a retake installs nothing and releases what the retake took: owns {worker.Owns}, partition 0 owner {store.Partitions[0].Owner ?? "none"} at {store.Partitions[0].Epoch}");
        check(store.Partitions[2].Epoch == e2 && store.Partitions[2].Owner is null && store.Partitions[1].Owner == "b",
            $"and it releases the partition the retake had not reached, at its epoch: partition 2 owner {store.Partitions[2].Owner ?? "none"} at {store.Partitions[2].Epoch}");
    }

    // A stand-down whose own flush (writing what it holds in memory only) is refused takes nothing back.
    private static async Task StandDownMeetsRefusedFlush(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(2, () => time);
        var worker = new Worker("a", [0, 1], store, new WorkflowInstance([new Ticker(TimeSpan.FromSeconds(1))]), new() { Monotonic = () => time, Partitions = 2 });
        await new DurableHost([worker]).ActivateAsync(0);
        var e1 = worker.EpochOf(1);
        foreach (var t in new[] { InstanceRef.In(0, "t"), InstanceRef.In(1, "u") })
        {
            worker.Post(new($"arm-{t.Partition}", t, "arm", Start: Ticker.Spec with { Ephemeral = true }), fresh: true);
            worker.RunTurn(t.Id);
        }
        await worker.FlushAsync();
        var unwritten = worker.Unwritten.Order(StringComparer.Ordinal).ToList();
        store.Nodes[store.Partitions[0].Owner!].LeaseUntil = 0;
        await store.TakeAsync(0, "b", Defaults.Lease);
        await worker.DisposeAsync();            // writes 0/t and 1/u first: the fence of 0 is stale
        check(unwritten.SequenceEqual(["0/t", "1/u"]) && !worker.Owns && store.Partitions[1].Epoch == e1 && !store.Instances.ContainsKey("1/u"),
            $"a stand-down that meets a refused flush takes nothing back: unwritten [{string.Join(", ", unwritten)}], owns {worker.Owns}, partition 1 at epoch {store.Partitions[1].Epoch}");
        check(store.Partitions[1].Owner is null && store.Partitions[0].Owner == "b",
            $"and it gives up the partition still leased to it: partition 1 owner {store.Partitions[1].Owner ?? "none"}, partition 0 owner {store.Partitions[0].Owner}");
    }

    /// <summary>The partitions the store shows live under this worker's node at an epoch the worker does not hold.</summary>
    private static string Unserved(InMemoryStore store, Worker worker, long now)
    {
        var unserved = Enumerable.Range(0, store.Partitions.Length).Where(p => store.Partitions[p] is var row && row.Owner == worker.Node
            && store.Nodes.TryGetValue(worker.Node, out var node) && node.LeaseUntil >= now && worker.EpochOf(p) != row.Epoch).ToList();
        return unserved.Count == 0 ? "" : $"; unserved: {string.Join(" ", unserved)}";
    }

    private static Message Add(InstanceRef to, string id, long? due = null) => new(id, to, "add", Json.Write(new Add(id)), Start: Counter.Spec, Due: due);

    /// <summary>Poll, load and flush (the marks), then run every ready turn.</summary>
    private static async Task Step(Worker worker)
    {
        await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
        foreach (var id in worker.Ready.ToList()) worker.RunTurn(id);
    }

    // An answer lost after the commit landed: the worker takes back each partition still its own, at a new epoch, and
    // drops the one another node holds now; one take that failed as a whole would strand the rest.
    private static async Task LostAnswer(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(3, () => time);
        var wire = new DelayedCommit(store);
        var worker = new Worker("c", [0, 1, 2], wire, new WorkflowInstance([new Counter()]), new() { Monotonic = () => time, Partitions = 3 });
        await new DurableHost([worker]).ActivateAsync(0);
        var epochs = worker.Fences.ToDictionary(f => f.Partition, f => f.Epoch);
        var (x, y) = (InstanceRef.In(0, "x"), InstanceRef.In(1, "y"));
        await store.AddAsync(Add(x, "one"));
        await store.AddAsync(Add(y, "two"));
        await Step(worker);
        store.Nodes[store.Partitions[2].Owner!].LeaseUntil = 0;
        var moved = await store.TakeAsync(2, "b", Defaults.Lease);
        wire.LoseNext = true;
        await worker.FlushAsync();      // fences 0 and 1, lands, and the answer is lost
        check(moved is not null && store.Instances.ContainsKey(x.Id) && store.Instances.ContainsKey(y.Id) && wire.Batches[^1].Fenced.Select(f => f.Partition).SequenceEqual([0, 1]),
            "the flush whose answer is lost landed over partitions 0 and 1");
        check(worker.Fences.Select(f => (f.Partition, f.Epoch)).SequenceEqual([(0, epochs[0] + 1), (1, epochs[1] + 1)])
              && worker.PositionOf(0) == store.Partitions[0].Position && worker.PositionOf(1) == store.Partitions[1].Position
              && store.Partitions[2].Owner == "b" && Unserved(store, worker, time) == "",
            $"after a lost answer the worker retakes 0 and 1 at new epochs and drops 2: held [{string.Join(", ", worker.Fences)}]{Unserved(store, worker, time)}");
    }

    // After a refused fence over {0, 1, 2, 3}: 1 was released at the worker's epoch, 2 was taken by another node, and 3
    // was taken at a new epoch under the same node name by another worker. Only 0 is taken back.
    private static async Task MovedRows(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(4, () => time);
        var worker = new Worker("a", [0, 1, 2, 3], store, new WorkflowInstance([new Counter()]), new() { Monotonic = () => time, Partitions = 4 });
        await new DurableHost([worker]).ActivateAsync(0);
        var epochs = worker.Fences.ToDictionary(f => f.Partition, f => f.Epoch);
        await store.AddAsync(Add(InstanceRef.In(2, "z"), "two"));
        await Step(worker);
        await store.ReleaseAsync(1, epochs[1]);
        store.Nodes[store.Partitions[2].Owner!].LeaseUntil = 0;
        await store.TakeAsync(2, "b", Defaults.Lease);
        var elsewhere = await store.TakeAsync(3, "a", Defaults.Lease);
        await worker.FlushAsync();      // the turn in 2: its fence is stale
        check(store.Partitions[1].Owner is null && !worker.Fences.Any(f => f.Partition == 1),
            $"a partition released at the worker's epoch is not taken back: held [{string.Join(", ", worker.Fences)}]");
        check(store.Partitions[3].Epoch == elsewhere!.Epoch && !worker.Fences.Any(f => f.Partition == 3),
            $"a partition the same node name took at another epoch is not adopted: held [{string.Join(", ", worker.Fences)}]");
        check(worker.Fences.SequenceEqual([new PartitionFence(0, epochs[0] + 1, 0)]) && Unserved(store, worker, time) is "" or "; unserved: 3",
            $"only partition 0 is taken back, at a new epoch{Unserved(store, worker, time)}");
    }

    // A batch with a turn in 0 and a mark in 1 fences both and lands.
    private static async Task TurnAndMark(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(2, () => time);
        var wire = new DelayedCommit(store);
        var worker = new Worker("a", [0, 1], wire, new WorkflowInstance([new Counter()]), new() { Monotonic = () => time, Partitions = 2 });
        await new DurableHost([worker]).ActivateAsync(0);
        await store.AddAsync(Add(InstanceRef.In(0, "x"), "one"));
        await Step(worker);
        await store.AddAsync(Add(InstanceRef.In(1, "y"), "two"));
        await worker.PollAsync();
        await worker.FlushAsync();
        var batch = wire.Batches[^1];
        check(batch.Turns.Count == 1 && batch.Seen.SequenceEqual(["two"]) && batch.Fenced.Select(f => f.Partition).SequenceEqual([0, 1])
              && store.Instances.ContainsKey("0/x") && store.Messages["two"].Seen == worker.EpochOf(1),
            $"a turn in 0 and a mark in 1 land in one batch fencing both: fenced [{string.Join(" ", batch.Fenced.Select(f => f.Partition))}], landed {store.Instances.ContainsKey("0/x")}");
    }

    // A timer beyond the horizon, handed over in memory (written seen at the old epoch), survives a refused fence: the
    // new epoch's poll reads it once it comes within the horizon, and it fires.
    private static async Task TimerAcrossReset(Action<bool, string> check)
    {
        long time = 100;
        var store = new InMemoryStore(2, () => time);
        var worker = new Worker("a", [0, 1], store, new WorkflowInstance([new Counter(), new Ticker()]), new() { Monotonic = () => time, Partitions = 2 });
        await new DurableHost([worker]).ActivateAsync(0);
        var e0 = worker.EpochOf(0);
        await store.AddAsync(new("arm", InstanceRef.In(0, "t"), "arm", Start: Ticker.Spec));
        await Step(worker);
        await worker.FlushAsync();      // the timer, 60 s out, handed over
        var handedOver = store.Messages.Values.SingleOrDefault(m => m.Message.Kind == "timer")?.Seen;
        await store.AddAsync(Add(InstanceRef.In(1, "y"), "two"));
        await Step(worker);
        store.Nodes[store.Partitions[1].Owner!].LeaseUntil = 0;
        await store.TakeAsync(1, "b", Defaults.Lease);
        await worker.FlushAsync();      // refused: 1 is stale
        var beat = new NodeHeartbeat(worker.Node, store, [worker], Defaults.Lease, Defaults.Lease);
        for (var i = 0; i < 40 && Fired(store) == 0; i++)
        {
            time += 2_000;
            await beat.BeatAsync();
            await worker.WokenAsync(null);
            await worker.RunAsync();
        }
        check(handedOver == e0 && worker.Owns && Fired(store) == 1,
            $"a handed-over timer beyond the horizon fires after a refused fence: written seen at {handedOver} (epoch {e0}), fired {Fired(store)}");
        static int Fired(InMemoryStore store) => store.Instances.TryGetValue("0/t", out var row) ? Json.Read<TickerState>(row.State).Fired : 0;
    }

    // With a poll limit of 2 and two rows held in memory (not yet due), a new message to that partition after a refused
    // fence is still served.
    private static async Task PollLimitAcrossReset(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(2, () => time);
        var worker = new Worker("a", [0, 1], store, new WorkflowInstance([new Counter()]), new() { Monotonic = () => time, Partitions = 2, PollLimit = 2 });
        await new DurableHost([worker]).ActivateAsync(0);
        var x = InstanceRef.In(0, "x");
        var y = InstanceRef.In(1, "y");
        await store.AddAsync(Add(y, "two"));
        await store.AddAsync(Add(x, "later1", due: time + 5_000));
        await store.AddAsync(Add(x, "later2", due: time + 5_000));
        await Step(worker);
        await Step(worker);
        await worker.FlushAsync();
        // A turn in 1 queued after the last flush; then 1 moves, and the flush that carries it is refused.
        await store.AddAsync(Add(y, "three"));
        await Step(worker);
        var (revision, p0) = (store.Instances[y.Id].Revision, store.Partitions[0].Position);
        var queued = worker.Queued;
        store.Nodes[store.Partitions[1].Owner!].LeaseUntil = 0;
        await store.TakeAsync(1, "b", Defaults.Lease);
        await worker.FlushAsync();
        check(queued > 0 && store.Instances[y.Id].Revision == revision && store.Messages.ContainsKey("three") && store.Partitions[0].Position == p0
              && store.Messages["later1"].Seen > 0 && store.Messages["later2"].Seen > 0,
            $"with a poll limit of 2 and two rows held in 0, a flush with a turn in 1 is refused (queued {queued})");
        await store.AddAsync(Add(x, "fresh"));
        for (var i = 0; i < 20 && !Served(); i++)
        {
            await worker.WokenAsync(null);
            await worker.RunAsync();
        }
        check(worker.Owns && Served(), "with a poll limit of 2 and rows held, a new message after a refused fence is served");
        bool Served() => store.Instances.TryGetValue(x.Id, out var row) && Json.Read<CounterState>(row.State).Ids.Contains("fresh");
    }

    // A store without owned sets gets only today's positional batches; a worker of several partitions cannot take on it.
    private static async Task WithoutOwnedSets(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(2, () => time);
        var wire = new DelayedCommit(store) { WithoutOwnedSets = true };
        var one = new Worker("d", 0, wire, new WorkflowInstance([new Counter()]), new() { Monotonic = () => time, Partitions = 2 });
        await new DurableHost([one]).ActivateAsync(0);
        await store.AddAsync(Add(InstanceRef.In(0, "x"), "one"));
        await Step(one);
        await one.FlushAsync(); await one.BeatAsync();
        check(wire.Batches.Count >= 3 && wire.Batches.All(b => b.Fences is null && b.Partition == 0) && store.Instances.ContainsKey("0/x"),
            $"a store without owned sets gets only positional one-partition batches ({wire.Batches.Count} batches)");
        var several = new Worker("e", [0, 1], wire, new WorkflowInstance([new Counter()]), new() { Monotonic = () => time, Partitions = 2 });
        var refused = false;
        try { await new DurableHost([several]).ActivateAsync(0); } catch (NotSupportedException) { refused = true; }
        check(refused && !several.Owns && store.Partitions[1].Owner is null, "a worker of several partitions does not take on a store without owned sets");
    }

    // Epochs count per partition, so the same number can be this worker's in one partition and another owner's in the
    // next: the suspect test compares a row only with the epochs this process held in the row's own partition.
    private static async Task SuspectsAndTrials(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(2, () => time);
        store.Partitions[0].Epoch = 1;      // "a" takes 0 at epoch 2
        (store.Partitions[1].Owner, store.Partitions[1].Epoch) = ("b", 2);    // "b" held 1 at epoch 2 and was lost
        var (x, z) = (InstanceRef.In(0, "x"), InstanceRef.In(1, "z"));
        await store.AddAsync(new("m", z, "add", Json.Write(new Add("m")), Start: Counter.Spec));
        store.Messages["m"].Seen = 2;       // what "b" marked before it was lost
        await store.AddAsync(new("n", x, "add", Json.Write(new Add("n")), Start: Counter.Spec));
        var worker = new Worker("a", [0, 1], store, new WorkflowInstance([new Counter()]), new() { Monotonic = () => time, Partitions = 2 });
        await new DurableHost([worker]).ActivateAsync(0);
        var (e0, e1) = (worker.EpochOf(0), worker.EpochOf(1));
        await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
        check(e0 == 2 && e1 == 3 && worker.Trial.SequenceEqual(["m"]) && store.Messages["m"].Trial == e1 && store.Messages["n"] is { Seen: 2, Trial: 0 },
            $"a row another owner marked at epoch 2 of partition 1 is a suspect while this worker holds partition 0 at epoch 2: on trial [{string.Join(", ", worker.Trial)}]");
        check(worker.Ready.SequenceEqual([z.Id]),
            $"a trial in partition 1 holds back partition 0 of the same worker, as it would any partition of its process: ready [{string.Join(", ", worker.Ready)}]");

        // Partition 1 moves while its trial is in flight. The beat's fence is refused, and the reset retakes 0 at a new epoch.
        store.Nodes[store.Partitions[1].Owner!].LeaseUntil = 0;
        var moved = await store.TakeAsync(1, "c", Defaults.Lease);
        await worker.BeatAsync();
        check(moved is not null && !worker.Trial.Any() && worker.Fences.Select(f => (f.Partition, f.Epoch)).SequenceEqual([(0, e0 + 1)]) && store.Messages["n"].Attempts == 0,
            $"the reset retakes partition 0 at a new epoch and drops 1 with its trial: held [{string.Join(", ", worker.Fences)}], on trial [{string.Join(", ", worker.Trial)}]");
        await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
        check(!worker.Trial.Any() && store.Messages["n"].Trial == 0,
            $"what this process marked in 0 at its earlier epoch is no suspect after the retake: on trial [{string.Join(", ", worker.Trial)}], trial mark {store.Messages["n"].Trial}");
        var ready = worker.Ready.ToList();
        foreach (var id in ready) worker.RunTurn(id);
        await worker.FlushAsync();
        check(ready.SequenceEqual([x.Id]) && !worker.Trial.Any() && store.Instances.GetValueOrDefault(x.Id)?.Revision == 1 && !store.Messages.ContainsKey("n"),
            $"the trial in the dropped partition gates nothing retaken: ready [{string.Join(", ", ready)}]");
    }

    // A child id names the (partition, epoch) of its parent's fenced commit. Each held partition has its own issuer, and
    // every reset, after a lost answer or a refused fence, retakes at a new epoch with a new issuer.
    private static async Task ChildIds(Action<bool, string> check)
    {
        const long time = 100;
        var store = new InMemoryStore(2, () => time);
        var wire = new DelayedCommit(store);
        await store.ReleaseAsync(1, (await store.TakeAsync(1, "earlier", Defaults.Lease))!.Epoch);    // so that the two epochs differ
        var worker = new Worker("a", [0, 1], wire, new WorkflowInstance([new Parent(), new Child()]), new() { Monotonic = () => time, Partitions = 2 });
        await new DurableHost([worker]).ActivateAsync(0);
        var (e0, e1) = (worker.EpochOf(0), worker.EpochOf(1));
        var (children, starts) = (new List<string>(), new HashSet<InboxKey>());

        async Task Start(params string[] parents)
        {
            foreach (var parent in parents)
                await store.AddAsync(new($"start-{parent}", new InstanceRef(parent), Kinds.Start, Start: new StartSpec("parent", "p", Json.Write(new ParentInput(1, -1)))));
            await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
            foreach (var parent in parents) worker.RunTurn(parent);
        }
        void Collect()      // every child start, once per message: two starts to one id would show it twice
        {
            foreach (var row in store.Messages.Values.Where(m => m.Message.Target.ChildAddress).OrderBy(m => m.Seq))
                if (starts.Add(row.Message.InboxKey)) children.Add(row.Message.Target.Id);
        }

        await Start("0/p", "1/q");
        await worker.FlushAsync();
        Collect();
        check(children.SequenceEqual([$"g0/child/0.{e0}.1", $"g1/child/1.{e1}.1"]),
            $"each partition issues its children's ids at its own epoch: [{string.Join(", ", children)}]");

        // The answer of a flush with a child in it is lost after it landed: the retake starts new epochs and new issuers.
        await Start("0/s");
        wire.LoseNext = true;
        await worker.FlushAsync();
        Collect();
        await Start("0/t");
        await worker.FlushAsync();
        Collect();
        var retaken = worker.EpochOf(0);

        // Partition 1 moves; the beat is refused, and the reset retakes partition 0 at a new epoch again.
        store.Nodes[store.Partitions[1].Owner!].LeaseUntil = 0;
        await store.TakeAsync(1, "b", Defaults.Lease);
        await worker.BeatAsync();
        var again = worker.EpochOf(0);
        var held = worker.Fences.Select(f => (f.Partition, f.Epoch)).SequenceEqual([(0, again)]);
        await Start("0/r");
        await worker.FlushAsync();
        Collect();
        check(retaken == e0 + 1 && again == retaken + 1 && held && children.Distinct().Count() == children.Count
              && children.SequenceEqual([$"g0/child/0.{e0}.1", $"g1/child/1.{e1}.1", $"g0/child/0.{e0}.2", $"g0/child/0.{retaken}.1", $"g0/child/0.{again}.1"]),
            $"child ids stay unique across a lost-answer retake and a refused-fence reset: [{string.Join(", ", children)}]");
    }
    // K1 slice 7: the runtime's scopes, the workers' placement count and the store's partitions are checked together when
    // the runtime starts a node's host; a store created at 64 with a runtime and workers at 1,024 is refused.
    private static async Task CountsAgree(Action<bool, string> check)
    {
        async Task<string?> Start(int stored, int placed, int scopes, bool part = false, int? waiting = null)
        {
            var store = new InMemoryStore(stored, () => 100);
            if (waiting is { } p) await store.AddAsync(Add(InstanceRef.In(p, "late"), "t"));
            var host = DurableHost.ForNode("a", store, new WorkflowInstance([new Counter()]), placed, new() { Monotonic = () => 100, Partitions = placed }, placesPart: part);
            try { await new PartitionRuntime("a", store, scopes, TimeSpan.FromSeconds(3)).StartAsync(host); return null; }
            catch (InvalidOperationException e) { return e.Message; }
        }
        var refused = await Start(64, 1024, 1024);
        check(refused is not null && refused.Contains("1024 scopes") && refused.Contains("over 1024 partitions") && refused.Contains("has 64 partitions"),
            $"a store at 64 with the runtime and workers at 1,024 is refused, naming the three counts ({refused ?? "started"})");
        check(await Start(1024, 64, 1024) is not null, "workers at 64 under a runtime of 1,024 are refused");
        check(await Start(1024, 1024, 1024) is null, "the runtime, the workers and the store at 1,024 start");
        var unserved = await Start(1024, 64, 64, waiting: 900);
        check(unserved is not null && unserved.Contains("64 scopes") && unserved.Contains("has 1024 partitions"),
            $"a store at 1,024 with a message in partition 900, and the runtime and workers at 64, is refused ({unserved ?? "started"})");
        check(await Start(1024, 4, 4) is not null, "a runtime and workers that place 4 of a store's 1,024 are refused");
        check(await Start(1024, 4, 4, part: true) is null, "with placesPart, the simulation's only, 4 of a store's 1,024 start");
    }
}

public sealed class TickerState
{
    public bool Armed { get; set; }
    public int Fired { get; set; }
}

/// <summary>Arms one timer (60 s out by default, beyond the poll horizon) and counts it when it fires.</summary>
public sealed class Ticker(TimeSpan? delay = null) : Model<TickerState>
{
    public static readonly StartSpec Spec = new("ticker", "t");
    public override string Key => "ticker";

    protected override void Turn(ITurn turn, TickerState state)
    {
        foreach (var message in turn.Inbox)
        {
            if (message.Kind == "arm" && !state.Armed)
            {
                state.Armed = true;
                turn.Send(turn.Instance.Ref, "timer", null, new SendOptions(Delay: delay ?? TimeSpan.FromSeconds(60)));
            }
            else if (message.Kind == "timer") state.Fired++;
        }
    }
}
