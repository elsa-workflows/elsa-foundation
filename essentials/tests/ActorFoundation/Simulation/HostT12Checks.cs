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
        const int partitions = 2;
        const long startTime = 1_701_000_000_000;
        const long readyAt = startTime + 60_000;     // past every clock step before the last, so no input runs early
        var lease = TimeSpan.FromSeconds(10);
        var now = startTime;
        var store = new InMemoryStore(partitions, () => Volatile.Read(ref now), flaws);
        // The spy sees the reads and releases made inside a host-admin call's own flow, never the node's loops.
        var adminReads = new ConcurrentQueue<string>();
        var adminReleases = new ConcurrentQueue<string>();
        (string? Owner, long Epoch, long Position)[]? atRelease = null;
        var spy = new CountingStore(store, static () => { },
            beforeRelease: () => { if (AdminStep.Value is { } step) adminReleases.Enqueue(step); },
            afterRelease: () => { if (AdminStep.Value == "release") atRelease = OwnershipImage(); return default; },
            beforeRead: read => { if (AdminStep.Value is { } step) adminReads.Enqueue(step + ":" + read); });
        var landed = new ConcurrentQueue<CommitBatch>();
        var observed = new CountingStore(store, static () => { }, observeCommit: (batch, done) => { if (done) landed.Enqueue(batch); });
        var probe = new Probe();
        var ingress = new InMemoryIngress(store, new Requests("t12"), "first");
        var reads = new ActorList(store, 10);
        var blue = ContractScopes.Bind("blue");
        var green = ContractScopes.Bind("green");

        await using var first = Node("first");
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
        Check(await AsAdmin("control", async () => (await spy.LoadAsync([.. tenantOf.Keys])).Count) == 2 &&
            adminReads.SequenceEqual(new[] { "control:LoadAsync" }), "negative control: a payload read inside a host-admin step is caught");
        adminReads.Clear();

        var hostOperator = new object();
        object caller = blue;
        var release = new HostOwnershipAdministration(first, () => ReferenceEquals(caller, hostOperator));
        var ownership = OwnershipImage();
        foreach (var scope in new[] { blue, green })
        {
            caller = scope;
            Check(!await release.ReleaseAsync([0]) && !await release.DrainAsync(), scope.Tenant + " operator was refused release and drain");
        }
        var outside = false;
        try { await release.ReleaseAsync([partitions]); }
        catch (ArgumentOutOfRangeException) { outside = true; }
        Check(outside && ownership.SequenceEqual(OwnershipImage()) && Enumerable.Range(0, partitions).All(first.Runtime.Owns),
            "refusals and an out-of-range partition left ownership as it was");

        // Host administration releases partition 0 alone; the next take of it, here by the same node, counts no death.
        caller = hostOperator;
        Check(await AsAdmin("release", () => release.ReleaseAsync([0]).AsTask()), "host administration released partition 0");
        Check(atRelease is [(null, var epoch0, _), ("first", _, _)] && epoch0 == ownership[0].Epoch && adminReleases.SequenceEqual(new[] { "release" }),
            "the release freed partition 0 alone, at its epoch, through the store's release");
        await WaitFor(() => store.Partitions[0] is { Owner: "first" } row && row.Epoch == ownership[0].Epoch + 1, "partition 0 was taken again");
        Check(Kept(messages, MessageImage()) && Attempted(messages, MessageImage(), None) && SameTenantData(),
            "after the release every tenant row is intact, with no attempt added");
        Check(await AsAdmin("release-set", () => release.ReleaseAsync([1, 0, 1]).AsTask()) && adminReleases.Count(step => step == "release-set") == 1,
            "host administration released both partitions in one store release");
        await WaitFor(() => store.Partitions.All(row => row.Owner == "first") && store.Partitions[1].Epoch == ownership[1].Epoch + 1,
            "both partitions were taken again");

        Check(await AsAdmin("drain", () => release.DrainAsync().AsTask()) && first.Errors == 0, "host administration drained the first node");
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
        Volatile.Write(ref now, store.Nodes["second"].LeaseUntil + 1);
        Check(Volatile.Read(ref now) < readyAt, "the takeover comes before any input is due");

        await using var third = Node("third");
        await Start(third, observed);
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

        RuntimeNode Node(string name) => new(name, store, partitions, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(20)
        });

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

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "host-t12-probe";
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
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
