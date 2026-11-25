using System.Collections.Concurrent;
using System.Diagnostics;
using Kernel.Foundation.InMemory;
using Kernel.Runtime.Hosting;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

/// <summary>
/// Tenancy case T12 (docs/architecture/tenancy.md): while two tenants have work and messages on trial, host administration
/// releases a partition and drains a node; then a node holding both tenants' partitions is killed and a third takes them
/// once the lease expires, on the injected clock.
/// </summary>
internal static class HostT12Checks
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly AsyncLocal<string?> AdminStep = new();

    public static async Task<int> RunAsync(Flaw flaws = Flaw.None)
    {
        await PassingReleaseAsync();
        const int partitions = 2;
        const long startTime = 1_701_000_000_000;
        const long readyAt = startTime + 60_000;     // past every clock step before the last, so no input runs early
        var lease = TimeSpan.FromSeconds(10);
        var now = startTime;
        var store = new InMemoryStore(partitions, () => Volatile.Read(ref now), flaws);
        // The spy sees the reads and releases made inside a host-admin call's own flow, never the node's loops.
        var adminReads = new ConcurrentQueue<string>();
        var adminReleases = new ConcurrentQueue<string>();
        (string? Owner, long Epoch, long Position)[]? atRelease = null, atSet = null;
        var heldRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var spy = new CountingStore(store, static () => { },
            beforeRelease: () => { if (AdminStep.Value is { } step) adminReleases.Enqueue(step); },
            afterRelease: async () =>
            {
                if (AdminStep.Value == "release") atRelease = OwnershipImage();
                if (AdminStep.Value == "release-set") atSet = OwnershipImage();
                if (AdminStep.Value != "release-held") return;
                heldRelease.TrySetResult();
                await finishRelease.Task;
            },
            beforeRead: read => { if (AdminStep.Value is { } step) adminReads.Enqueue(step + ":" + read); });
        var landed = new ConcurrentQueue<CommitBatch>();
        var observed = new CountingStore(store, static () => { }, observeCommit: (batch, done) => { if (done) landed.Enqueue(batch); });
        // The actor code is watched too: no host-admin step may reach a tenant's actor.
        var probe = new Probe(call => { if (AdminStep.Value is { } step) adminReads.Enqueue(step + ":actor." + call); });
        var drainEntered = false;
        var ingress = new InMemoryIngress(store, new Requests("t12"), "first");
        var reads = new ActorList(store, 10);
        var blue = ContractScopes.Bind("blue");
        var green = ContractScopes.Bind("green");

        await using var first = Node("first", _ => { drainEntered = true; return Task.CompletedTask; });
        await Start(first, spy);
        var a = await StartActor(blue, 0);
        var b = await StartActor(green, 1);
        await WaitFor(() => State(blue, a) is ["blue/start"] && State(green, b) is ["green/start"], "the first node committed both tenants' start");
        var tenantOf = new Dictionary<string, string> { [Checkpoint(blue, a)!.Id] = "blue", [Checkpoint(green, b)!.Id] = "green" };

        // Both tenants queue the same operation ids; the frozen clock keeps every input from running until the end.
        foreach (var (scope, actor) in new[] { (blue, a), (green, b) })
            foreach (var id in new[] { "trial", "plain" })
                Check((await ingress.SendAsync(scope, new Contract.Target.Ref(actor), id, "input", scope.Tenant + "/" + id, new(readyAt)))
                    .Outcome == Contract.AdmissionOutcome.Accepted, scope.Tenant + " queued input " + id);
        var inputs = store.Messages.Where(pair => pair.Value.Message.Kind == "input").ToArray();
        var onTrial = inputs.Where(pair => pair.Value.Message.Id.Contains("trial", StringComparison.Ordinal)).Select(pair => pair.Key).ToHashSet();
        Check(inputs.Length == 4 && onTrial.Count == 2 && inputs.Select(pair => pair.Value.Tenant).Distinct().Count() == 2 &&
            inputs.Select(pair => pair.Value.Message.Target.Partition).Distinct().Count() == partitions,
            "each tenant has two inputs in its own partition");
        Check(await MarkTrial(inputs.Select(pair => pair.Key)), "all four inputs are on trial at the first node's fences");

        var messages = MessageImage();
        var instances = InstanceImage();
        var journal = store.Journal.Values.ToArray();
        Check(!Kept(messages, Tampered(messages, payload: true)) && !Kept(messages, Tampered(messages, payload: false)),
            "flaw control: the row check rejects a changed payload and a changed tenant");
        Check(await AsAdmin("control", async () => (await spy.LoadAsync([.. tenantOf.Keys])).Count + (probe.Withholds(null!) ? 1 : 0)) == 2 &&
            adminReads.SequenceEqual(new[] { "control:LoadAsync", "control:actor.Withholds" }),
            "negative control: a store read and an actor call inside a host-admin step are caught");
        adminReads.Clear();

        var hostOperator = new object();
        object caller = blue;
        var release = new HostOwnershipAdministration(first, () => ReferenceEquals(caller, hostOperator));
        var ownership = OwnershipImage();
        foreach (var scope in new[] { blue, green })
        {
            caller = scope;
            Check(!(await release.ReleaseAsync([0])).Authorized && !(await release.ReleaseAsync([partitions])).Authorized &&
                !await release.DrainAsync(), scope.Tenant + " operator was refused release, an out-of-range release and drain");
        }
        caller = hostOperator;
        var outside = false;
        try { await release.ReleaseAsync([partitions]); }
        catch (ArgumentOutOfRangeException) { outside = true; }
        Check(outside && ownership.SequenceEqual(OwnershipImage()) && Enumerable.Range(0, partitions).All(first.Runtime.Owns),
            "refusals and an out-of-range partition left ownership as it was");

        // Host administration releases partition 0 alone; the next take of it, here by the same node, counts no death.
        Check(await AsAdmin("release", () => release.ReleaseAsync([0]).AsTask()) is { Authorized: true, Released: [0] },
            "host administration released partition 0");
        Check(atRelease is [(null, var epoch0, _), ("first", _, _)] && epoch0 == ownership[0].Epoch && adminReleases.SequenceEqual(new[] { "release" }),
            "the release freed partition 0 alone, at its epoch, through the store's release");
        await WaitFor(() => store.Partitions[0] is { Owner: "first" } row && row.Epoch == ownership[0].Epoch + 1, "partition 0 was taken again");
        Check(Kept(messages, MessageImage()) && Attempted(messages, MessageImage(), None) && SameTenantData(),
            "after the release every tenant row is intact, with no attempt added");
        var beforeSet = OwnershipImage();
        Check(await AsAdmin("release-set", () => release.ReleaseAsync([1, 0, 1]).AsTask()) is { Released: [0, 1] } &&
            adminReleases.Count(step => step == "release-set") == 1 && atSet is { } freed &&
            freed.SequenceEqual(beforeSet.Select(row => (Owner: (string?)null, row.Epoch, row.Position))),
            "host administration released both partitions, each at its epoch, in one store release");
        await WaitFor(() => store.Partitions.Select(row => (row.Owner, row.Epoch)).SequenceEqual(beforeSet.Select(row => ((string?)"first", row.Epoch + 1))),
            "both partitions were taken again, each one epoch on");

        // A stop joins a release under way before its own turn drain.
        var inFlight = AsAdmin("release-held", () => release.ReleaseAsync([0]).AsTask());
        await heldRelease.Task.WaitAsync(Timeout);
        var draining = AsAdmin("drain", () => release.DrainAsync().AsTask());
        await Task.Delay(200);
        Check(!draining.IsCompleted && !drainEntered, "the drain waited for the release under way before its turn drain");
        finishRelease.TrySetResult();
        Check((await inFlight.WaitAsync(Timeout)).Released is [0] && await draining.WaitAsync(Timeout) && drainEntered && first.Errors == 0,
            "host administration drained the first node after the release, with no fault");
        Check(store.Partitions.All(row => row.Owner is null) && adminReleases.Count(step => step == "drain") > 0,
            "the drain freed both partitions through the store's release");
        Check(adminReads.IsEmpty, "no host-admin step read a tenant payload");
        Check(Kept(messages, MessageImage()) && Attempted(messages, MessageImage(), None) && SameTenantData(),
            "after the drain every tenant row is intact, with no attempt added");

        // A second node takes the free partitions once the first has aged out of membership.
        Volatile.Write(ref now, startTime + 1_000);
        await using var second = Node("second");
        await Start(second, store);
        await WaitFor(() => store.Partitions.All(row => row.Owner == "second"), "the second node took both tenants' partitions");
        Check(Kept(messages, MessageImage()) && Attempted(messages, MessageImage(), None) && SameTenantData(),
            "the take after the drain added no attempt");

        // One input per tenant goes on trial at the second node's fences; the other stays on trial at an older epoch.
        Check(await MarkTrial(onTrial) && inputs.All(pair => (pair.Value.Trial == store.Partitions[pair.Value.Message.Target.Partition].Epoch) ==
            onTrial.Contains(pair.Key)), "one input per tenant is on trial at the second node's fences");
        await second.KillAsync();
        Check(store.Partitions.All(row => row.Owner == "second"), "the kill gave nothing up");
        var atCrash = MessageImage();
        var crashEpochs = OwnershipImage();

        // Before the dead node's lease expires, neither a running node nor a direct take gets its partitions.
        await using var third = Node("third");
        await Start(third, observed);
        await Task.Delay(300);
        Check(store.Partitions.All(row => row.Owner == "second") && (await store.TakeAsync([0, 1], "early", lease)).Taken.Count == 0 &&
            OwnershipImage().SequenceEqual(crashEpochs), "nothing was taken while the dead node's lease ran");
        Volatile.Write(ref now, store.Nodes["second"].LeaseUntil + 1);
        Check(Volatile.Read(ref now) < readyAt, "the takeover comes before any input is due");
        await WaitFor(() => store.Partitions.All(row => row.Owner == "third"), "the third node took both partitions after the lease expired");
        var taken = MessageImage();
        Check(store.Partitions.Select(row => row.Epoch).SequenceEqual(crashEpochs.Select(row => row.Epoch + 1)),
            "each partition moved one epoch to the third node");
        Check(Kept(messages, taken) && SameTenantData(), "after the takeover every tenant row keeps its tenant, ids and payload");
        Check(Attempted(atCrash, taken, onTrial), "exactly the inputs on trial at the crash got one more attempt");
        Check(!Attempted(atCrash, taken, None) && !Attempted(atCrash, taken, inputs.Select(pair => pair.Key).ToHashSet()),
            "flaw control: the attempt check rejects no attempt and an attempt on every input");

        // The work goes on under the third node, each commit in its own tenant.
        Volatile.Write(ref now, readyAt);
        await WaitFor(() => inputs.All(pair => store.Messages[pair.Key].Done), "the third node applied every input");
        Check(State(blue, a) is { } sa && sa.Order().SequenceEqual(["blue/plain", "blue/start", "blue/trial"]) &&
            State(green, b) is { } sb && sb.Order().SequenceEqual(["green/plain", "green/start", "green/trial"]) && third.Errors == 0,
            "both tenants' work continued under the new owner, once each");
        var turns = landed.SelectMany(batch => batch.Turns).ToArray();
        Check(inputs.All(pair => turns.Any(turn => turn.Consumed.Concat(turn.Done).Contains(pair.Key))) && turns.All(OwnTenant),
            "every later commit landed in its own tenant, and together they took every input");
        var crossed = turns.First(turn => turn.Consumed.Count + turn.Done.Count > 0);
        Check(!OwnTenant(crossed with { Instance = tenantOf.Keys.Single(id => id != crossed.Instance) }),
            "flaw control: the tenant check rejects a turn that consumes another tenant's input");
        Check(await reads.GetAsync(green, a) is null && await reads.GetAsync(blue, b) is null &&
            (await reads.TailAsync(green, a)).Items.Count == 0 && (await reads.TailAsync(blue, b)).Items.Count == 0,
            "neither tenant reads the other's actor");
        return 0;

        bool OwnTenant(TurnCommit turn) =>
            tenantOf.TryGetValue(turn.Instance, out var tenant) &&
            turn.Consumed.Concat(turn.Done).All(key => store.Messages.TryGetValue(key, out var row) && row.Tenant == tenant) &&
            (turn.Journal is null || !turn.Journal.Contains(tenant == "blue" ? "green/" : "blue/", StringComparison.Ordinal)) &&
            (turn.Row is null || !turn.Row.State.Contains(tenant == "blue" ? "green/" : "blue/", StringComparison.Ordinal));

        async Task<bool> MarkTrial(IEnumerable<InboxKey> keys)
        {
            var fences = store.Partitions.Select((row, partition) => new PartitionFence(partition, row.Epoch, row.Position)).ToArray();
            return (await store.CommitAsync(CommitBatch.Owned(fences, lease, false, [], [], [.. keys]))).Landed;
        }

        RuntimeNode Node(string name, Func<CancellationToken, Task>? beforeRelease = null) => new(name, store, partitions, Loop, beforeRelease: beforeRelease);

        ValueTask Start(RuntimeNode node, IKernelStore backing) =>
            node.StartAsync(DurableHost.ForNode(node.Node, backing, new ActorAdapter(store, probe), partitions,
                new WorkerOptions { Partitions = partitions, Lease = lease, Horizon = TimeSpan.Zero, Monotonic = () => Volatile.Read(ref now) - startTime }));

        async Task<Contract.ActorRef> StartActor(Contract.BoundScope scope, int partition)
        {
            var key = Enumerable.Range(0, 16).Select(i => "root-" + i)
                .First(candidate => InstanceRef.Spread(scope.Tenant + "_" + candidate, partitions) == partition);
            var spec = new Contract.StartSpec(probe.Type, "t12", scope.Tenant + "/start", History: Contract.History.Full);
            var receipt = await ingress.SendAsync(scope, new Contract.Target.Key(key, spec), "start", Contract.Kinds.Start);
            Check(receipt.Outcome == Contract.AdmissionOutcome.Accepted, scope.Tenant + " root was admitted");
            return receipt.Actor!.Value;
        }

        InstanceRow? Checkpoint(Contract.BoundScope scope, Contract.ActorRef actor) => store.ReadFoundationActor(scope.Tenant, actor.Id).Actor?.Row;
        List<string>? State(Contract.BoundScope scope, Contract.ActorRef actor) =>
            Checkpoint(scope, actor) is { } row ? Json.Read<List<string>>(row.State) : null;
        (string? Owner, long Epoch, long Position)[] OwnershipImage() => store.Partitions.Select(row => (row.Owner, row.Epoch, row.Position)).ToArray();
        InstanceRow?[] InstanceImage() => [Checkpoint(blue, a), Checkpoint(green, b), Checkpoint(green, a), Checkpoint(blue, b)];
        bool SameTenantData() => InstanceImage().SequenceEqual(instances) && journal.SequenceEqual(store.Journal.Values);
        Row[] MessageImage() => [.. store.Messages.OrderBy(pair => pair.Value.Seq).Select(pair => new Row(pair.Key, pair.Value.Tenant,
            pair.Value.Message, pair.Value.Seq, pair.Value.Done, pair.Value.Held, pair.Value.Seen, pair.Value.Trial, pair.Value.Attempts))];
    }

    private static readonly LoopOptions Loop = new()
    {
        BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(20)
    };

    // A worker-per-scope host (a store without owned sets): a release skips a scope whose pass is under way, also one
    // whose pass began while an earlier scope of the same release awaited the store.
    private static async Task PassingReleaseAsync()
    {
        const int partitions = 2;
        var store = new InMemoryStore(partitions, static () => 1_701_000_000_000);
        Hold? pass = null, release = null;
        var host = new CountingStore(store, static () => { },
            beforeCommit: async batch =>
            {
                if (Volatile.Read(ref pass) is { } held && batch.Turns.Any(turn => turn.Journal?.Contains(held.Marker, StringComparison.Ordinal) == true))
                    await held.EnterAsync();
            },
            afterRelease: async () => { if (Interlocked.Exchange(ref release, null) is { } held) await held.EnterAsync(); })
            { OwnedSets = false };
        var probe = new Probe(static _ => { });
        var ingress = new InMemoryIngress(store, new Requests("t12-pass"), "solo");
        await using var node = new RuntimeNode("solo", store, partitions, Loop);
        await node.StartAsync(DurableHost.ForNode("solo", host, new ActorAdapter(store, probe), partitions,
            new WorkerOptions { Partitions = partitions, Lease = TimeSpan.FromSeconds(10), Horizon = TimeSpan.Zero, Monotonic = static () => 0 }));
        var admin = new HostOwnershipAdministration(node, static () => true);
        var blue = await Root(ContractScopes.Bind("blue"), 0);
        var green = await Root(ContractScopes.Bind("green"), 1);
        await WaitFor(() => State(blue) is ["blue/start"] && State(green) is ["green/start"], "the worker-per-scope node committed both starts");

        // One partition: a release during its held pass skips it.
        pass = new("blue/held");
        await Send(blue, "blue/held");
        await pass.Entered.Task.WaitAsync(Timeout);
        Check(await Within(admin.ReleaseAsync([0]).AsTask()) is { Authorized: true, Released: [] } && node.Runtime.Owns(0),
            "a release during the held pass skipped the passing partition");
        pass.Finish.TrySetResult();
        await WaitFor(() => Done(blue) && State(blue) is ["blue/start", "blue/held"], "the held pass committed its input once");
        Check((await admin.ReleaseAsync([0])).Released is [0], "the next release gave the partition up");
        await WaitFor(() => node.Runtime.Owns(0), "the node took partition 0 again");

        // Two partitions: while partition 0's give-up awaits the store, a pass starts on partition 1, which is then skipped.
        var storeCall = new Hold("");
        release = storeCall;     // the hook takes it once: partition 0's give-up
        pass = new("green/held");
        var releasing = admin.ReleaseAsync([0, 1]).AsTask();
        await storeCall.Entered.Task.WaitAsync(Timeout);
        await Send(green, "green/held");
        await pass.Entered.Task.WaitAsync(Timeout);
        storeCall.Finish.TrySetResult();
        Check(await Within(releasing) is { Released: [0] } && node.Runtime.Owns(1),
            "the release gave up partition 0 and skipped partition 1, whose pass began meanwhile");
        pass.Finish.TrySetResult();
        await WaitFor(() => Done(green) && State(green) is ["green/start", "green/held"], "partition 1's pass committed its input once");
        Check(node.Errors == 0, "the worker-per-scope node recorded no fault");

        async Task<Contract.ActorRef> Root(Contract.BoundScope scope, int partition)
        {
            var key = Enumerable.Range(0, 16).Select(i => "root-" + i)
                .First(candidate => InstanceRef.Spread(scope.Tenant + "_" + candidate, partitions) == partition);
            var spec = new Contract.StartSpec(probe.Type, "t12", scope.Tenant + "/start", History: Contract.History.Full);
            return (await ingress.SendAsync(scope, new Contract.Target.Key(key, spec), "start", Contract.Kinds.Start)).Actor!.Value;
        }
        async Task Send(Contract.ActorRef actor, string payload) =>
            Check((await ingress.SendAsync(Tenant(actor), new Contract.Target.Ref(actor), "held", "input", payload)).Outcome ==
                Contract.AdmissionOutcome.Accepted, "the worker-per-scope node received " + payload);
        Contract.BoundScope Tenant(Contract.ActorRef actor) => ContractScopes.Bind(actor == blue ? "blue" : "green");
        bool Done(Contract.ActorRef actor) => store.Messages.Values.Single(row => row.Message.Kind == "input" && row.Tenant == Tenant(actor).Tenant)
            is { Done: true, Attempts: 0 };
        List<string>? State(Contract.ActorRef actor) =>
            store.ReadFoundationActor(Tenant(actor).Tenant, actor.Id).Actor?.Row is { } row ? Json.Read<List<string>>(row.State) : null;
    }

    // A call that must return while the test still holds a store call or a commit; a hang counts as a failure.
    private static async Task<T?> Within<T>(Task<T> call) where T : class =>
        await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(2))) == call ? await call : null;

    private sealed class Hold(string marker)
    {
        public string Marker => marker;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task EnterAsync()
        {
            Entered.TrySetResult();
            return Finish.Task;
        }
    }

    private sealed record Row(InboxKey Key, string? Tenant, Message Message, long Seq, bool Done, bool Held, long Seen, long Trial, int Attempts);

    // Every row keeps its tenant, ids, payload, order and state; only Seen, Trial and Attempts may differ.
    private static bool Kept(Row[] before, Row[] after) =>
        before.Select(row => row with { Seen = 0, Trial = 0, Attempts = 0 }).SequenceEqual(after.Select(row => row with { Seen = 0, Trial = 0, Attempts = 0 }));

    private static bool Attempted(Row[] before, Row[] after, IReadOnlySet<InboxKey> once) =>
        before.Length == after.Length && before.Zip(after).All(pair =>
            pair.First.Key == pair.Second.Key && pair.Second.Attempts == pair.First.Attempts + (once.Contains(pair.First.Key) ? 1 : 0));

    private static readonly HashSet<InboxKey> None = [];

    private static Row[] Tampered(Row[] rows, bool payload)
    {
        var copy = rows.ToArray();
        var last = copy[^1];
        copy[^1] = payload ? last with { Message = last.Message with { Payload = last.Message.Payload + "!" } } : last with { Tenant = "other" };
        return copy;
    }

    private static async Task<T> AsAdmin<T>(string step, Func<Task<T>> work)
    {
        AdminStep.Value = step;     // flows into everything the call awaits, and back out when this method returns
        try { return await work(); }
        finally { AdminStep.Value = null; }
    }

    private static async Task WaitFor(Func<bool> predicate, string description)
    {
        var elapsed = Stopwatch.StartNew();
        while (!predicate() && elapsed.Elapsed < Timeout) await Task.Delay(10);
        Check(predicate(), description);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }

    private sealed class Probe(Action<string> touched) : Contract.IActorType
    {
        public string Type => "host-t12-probe";
        public bool Withholds(Contract.ActorHeader header) { touched(nameof(Withholds)); return false; }
        public long TerminalGrowth(Contract.ActorHeader header, string state) { touched(nameof(TerminalGrowth)); return 0; }
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) { touched(nameof(ProtectsDeletion)); return false; }
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) { touched(nameof(RestoreWaits)); return new([], [], []); }
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            touched(nameof(TurnAsync));
            var history = turn.State is null ? [] : Json.Read<List<string>>(turn.State);
            foreach (var message in turn.Inbox)
                if (message.Payload is { } payload)
                {
                    history.Add(payload);
                    turn.Record(payload);
                }
            turn.State = Json.Write(history);
            return default;
        }
    }
}
