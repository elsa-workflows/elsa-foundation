using System.Collections.Concurrent;
using Kernel.Foundation.InMemory;
using Kernel.Runtime.Hosting;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class HostDrainChecks
{
    public static async Task<int> RunAsync()
    {
        const int partitions = 2;
        const long startTime = 1_701_000_000_000;
        const long readyAt = startTime + 1_000;
        var lease = TimeSpan.FromSeconds(10);
        var now = startTime;
        var store = new InMemoryStore(partitions, () => Volatile.Read(ref now));
        var probe = new Probe();
        var requests = new Requests("drain");
        var ingress = new InMemoryIngress(store, requests, "owner");
        var reads = new ActorList(store, 10);
        var blue = new Contract.BoundScope("blue");
        var green = new Contract.BoundScope("green");

        await using var owner = Node("owner");
        await Start(owner);
        var a = await StartActor(blue, 0);
        var b = await StartActor(green, 1);
        await WaitFor(() => State(blue, a) is ["blue/start"] && State(green, b) is ["green/start"],
            "the first node committed both tenants' initial state");

        // Each caller can exercise a real operator grant in its own tenant.
        var operators = new InMemoryIngress(store, requests, owner.Node,
            (scope, kind) => (scope == blue || scope == green) && kind == Contract.Kinds.Resume);
        foreach (var (scope, actor) in new[] { (blue, a), (green, b) })
        {
            Check((await operators.SendAsync(scope, new Contract.Target.Ref(actor), "resume", Contract.Kinds.Resume))
                .Outcome == Contract.AdmissionOutcome.Accepted, "tenant operator command was admitted");
        }
        await WaitFor(() => Checkpoint(blue, a)?.Revision == 2 && Checkpoint(green, b)?.Revision == 2,
            "both tenant operator commands committed on the first node");

        foreach (var (scope, actor) in new[] { (blue, a), (green, b) })
        {
            Check((await ingress.SendAsync(scope, new Contract.Target.Ref(actor), "input", "input", scope.Tenant + "/input",
                new(readyAt))).Outcome == Contract.AdmissionOutcome.Accepted, "each tenant queued the same operation id");
        }

        // Set the in-flight trial cut with the store's fenced mark. The frozen clock keeps the rows out of the owner loop.
        var pending = store.Messages.Values.Where(row => row.Message.Kind == "input").ToArray();
        var fences = store.Partitions.Select((row, partition) => new PartitionFence(partition, row.Epoch, row.Position)).ToArray();
        Check(pending is [_, _] && pending.Select(row => row.Message.Target.Partition).Distinct().Count() == partitions &&
            store.Partitions.All(row => row.Owner == owner.Node), "each tenant has outstanding work in a different owned partition");
        var marked = await store.CommitAsync(CommitBatch.Owned(fences, lease, false, [], [], pending.Select(row => row.Message.InboxKey).ToArray()));
        Check(marked.Landed && pending.All(row => row.Trial == fences[row.Message.Target.Partition].Epoch && row.Trial > 0 && row.Attempts == 0),
            "both outstanding rows are on trial at their current fence");

        var messages = MessageImage();
        var ownership = OwnershipImage();
        var oldLease = store.Nodes[owner.Node].LeaseUntil;
        var originalA = Checkpoint(blue, a)!;
        var originalB = Checkpoint(green, b)!;
        var journal = store.Journal.Values.ToArray();

        var hostOperator = new object();
        object caller = blue;
        var command = new HostOwnershipAdministration(owner, () => ReferenceEquals(caller, hostOperator));
        foreach (var scope in new[] { blue, green })
        {
            caller = scope;
            Check(!await command.DrainAsync(), scope.Tenant + " operator was refused host drain");
        }
        var policyFailure = new InvalidOperationException("The fixture's host policy is unavailable.");
        var unavailable = new HostOwnershipAdministration(owner, () => throw policyFailure);
        var failed = false;
        try { await unavailable.DrainAsync(); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, policyFailure)) { failed = true; }
        Check(failed, "an authorization failure reached the caller");
        Check(ownership.SequenceEqual(OwnershipImage()) && store.Nodes[owner.Node].LeaseUntil == oldLease &&
            Enumerable.Range(0, partitions).All(owner.Runtime.Owns) && SameTenantData(),
            "authorization refusal and failure left ownership, leases and tenant rows intact");

        caller = hostOperator;
        Check(await command.DrainAsync() && owner.Errors == 0, "the host operator drained the real runtime");
        Check(store.Partitions.All(row => row.Owner is null) && Enumerable.Range(0, partitions).All(p => !owner.Runtime.Owns(p)) &&
            ownership.Select(row => (row.Epoch, row.Position)).SequenceEqual(OwnershipImage().Select(row => (row.Epoch, row.Position))) &&
            SameTenantData(), "orderly drain freed both fences and preserved tenant rows, trial marks and attempts");

        // The old node has aged out of membership, but its lease still protects any unreleased fence.
        Check(readyAt < oldLease, "the successor has to take the released fences before the old lease expires");
        Volatile.Write(ref now, readyAt);
        var observed = new ConcurrentQueue<Stored>();
        await using var successor = Node("successor");
        await Start(successor, observed.Enqueue);
        await WaitFor(() => State(blue, a) is ["blue/start", "blue/input"] && State(green, b) is ["green/start", "green/input"],
            "the successor applied each tenant's own input once");
        Check(Enumerable.Range(0, partitions).All(successor.Runtime.Owns) && successor.Errors == 0,
            "the successor owns both partitions");
        await successor.StopAsync();

        var replayed = observed.Where(row => row.Message.Kind == "input").ToArray();
        Check(replayed.Length == pending.Length && messages.Where(row => row.Message.Kind == "input").All(row =>
            replayed.Count(item => item.Message == row.Message && item.Seq == row.Seq && item.Seen == row.Seen &&
                item.Trial == row.Trial && item.Attempts == 0) == 1) && pending.All(row => row.Done && row.Attempts == 0),
            "the successor observed the same trial rows without any lease-death attempt");
        Check(await reads.GetAsync(green, a) is null && await reads.GetAsync(blue, b) is null &&
            Checkpoint(blue, a) is { } finalA && Checkpoint(green, b) is { } finalB &&
            (finalA.Id, finalA.Creation, finalA.Revision) == (originalA.Id, originalA.Creation, originalA.Revision + 1) &&
            (finalB.Id, finalB.Creation, finalB.Revision) == (originalB.Id, originalB.Creation, originalB.Revision + 1),
            "each tenant kept its exact actor life and only its own new revision");
        Check((await reads.TailAsync(blue, a)).Items.Count(entry => entry.Entry.Contains("blue/input", StringComparison.Ordinal)) == 1 &&
            (await reads.TailAsync(green, b)).Items.Count(entry => entry.Entry.Contains("green/input", StringComparison.Ordinal)) == 1 &&
            (await reads.TailAsync(green, a)).Items.Count == 0 && (await reads.TailAsync(blue, b)).Items.Count == 0,
            "the successor committed one tenant-bound journal entry per input");
        return 0;

        RuntimeNode Node(string name) => new(name, store, partitions, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(20)
        });

        ValueTask Start(RuntimeNode node, Action<Stored>? onObserved = null) =>
            node.StartAsync(DurableHost.ForNode(node.Node, store, new ActorAdapter(store, probe), partitions,
                new WorkerOptions { Partitions = partitions, Lease = lease, Horizon = TimeSpan.Zero,
                    Monotonic = () => Volatile.Read(ref now) - startTime, OnObservedDelivery = onObserved }));

        async Task<Contract.ActorRef> StartActor(Contract.BoundScope scope, int partition)
        {
            var key = Enumerable.Range(0, partitions).Select(i => "root-" + i)
                .First(candidate => InstanceRef.Spread(scope.Tenant + "_" + candidate, partitions) == partition);
            var spec = new Contract.StartSpec(probe.Type, "drain", scope.Tenant + "/start", History: Contract.History.Full);
            var receipt = await ingress.SendAsync(scope, new Contract.Target.Key(key, spec), "start", Contract.Kinds.Start);
            Check(receipt.Outcome == Contract.AdmissionOutcome.Accepted, "tenant root was admitted");
            return receipt.Actor!.Value;
        }

        InstanceRow? Checkpoint(Contract.BoundScope scope, Contract.ActorRef actor) => store.ReadFoundationActor(scope.Tenant, actor.Id).Actor?.Row;
        List<string>? State(Contract.BoundScope scope, Contract.ActorRef actor) =>
            Checkpoint(scope, actor) is { } row ? Json.Read<List<string>>(row.State) : null;
        (string? Owner, long Epoch, long Position)[] OwnershipImage() => store.Partitions.Select(row => (row.Owner, row.Epoch, row.Position)).ToArray();
        (InboxKey Key, string? Tenant, Message Message, long Seq, long Seen, long Trial, int Attempts, bool Done, bool Held)[] MessageImage() =>
            store.Messages.Select(pair => (pair.Key, pair.Value.Tenant, pair.Value.Message, pair.Value.Seq, pair.Value.Seen,
                pair.Value.Trial, pair.Value.Attempts, pair.Value.Done, pair.Value.Held)).ToArray();
        bool SameTenantData() => messages.SequenceEqual(MessageImage()) && Checkpoint(blue, a) == originalA && Checkpoint(green, b) == originalB &&
            journal.SequenceEqual(store.Journal.Values);
    }

    private static async Task WaitFor(Func<bool> predicate, string description)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!predicate() && timeout.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
        Check(predicate(), description);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "host-drain-probe";
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
