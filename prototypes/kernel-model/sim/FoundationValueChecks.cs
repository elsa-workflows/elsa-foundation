using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class FoundationValueChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long initial = 1_702_725_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now) { IngressDeduplicationWindow = TimeSpan.FromMilliseconds(100) };
        var scope = ContractScopes.Bind("values");
        var probe = new Probe();
        var edge = new InMemoryIngress(store, new Requests("values"), "values", (_, kind) => kind == Contract.Kinds.Delete);
        var first = Owner("values-first");
        await using var owner = await Activate(first);
        var a = await Start(first, "first", "alpha");
        var b = await Start(first, "second", "beta");
        var addressA = Address(a);
        var addressB = Address(b);
        await owner.DisposeAsync();
        var lease = (await store.TakeAsync(0, "values-fence", Defaults.Lease))!;

        var bytes = Enumerable.Repeat((byte)0x61, 4_096).ToArray();
        var value = new FoundationValue(bytes);
        var original = bytes.ToArray();
        bytes[0] = 0;
        var stale = new FoundationValue([0x62]);
        var staged = State(addressA, value);
        var staleWrite = await store.CommitAsync(Batch([State(addressA, stale)], lease.Epoch - 1));
        check(!staleWrite.Landed && store.ReadFoundationValue(addressA, stale.Reference) is null &&
              store.Instances[addressA.Id].Revision == staged.Expected,
            "value and checkpoint writes both stay absent under a stale ownership fence");

        var unsupported = false;
        try { StoreBatchRules.Validate(Batch([staged], lease.Epoch)); }
        catch (StoreException error) when (error.Failure == Failure.Definite) { unsupported = true; }
        var noCheckpoint = await Refused(Batch([staged with { Row = null, Expected = null }], lease.Epoch));
        check(unsupported && noCheckpoint && store.FoundationValueCount(addressA) == 0,
            "an unsupported provider or a store batch without a checkpoint cannot accept value bytes");

        var wrongRevision = await Refused(Batch([staged with { Expected = staged.Expected - 1 }], lease.Epoch));
        check(wrongRevision && store.FoundationValueCount(addressA) == 0,
            "a rejected checkpoint revision leaves no unreferenced value");

        var firstCommit = await store.CommitAsync(Batch([staged], lease.Epoch));
        var exposed = store.ReadFoundationValue(addressA, value.Reference);
        if (exposed is not null) exposed[0] = 0;
        check(firstCommit.Landed && store.Instances[addressA.Id].State == Json.Write(value.Reference) &&
              store.ReadFoundationValue(addressA, value.Reference)?.SequenceEqual(original) == true &&
              store.ReadFoundationValue(addressA, default) is null &&
              store.ReadFoundationValue(addressA, value.Reference with { Length = 0 }) is null &&
              store.ReadFoundationValue(addressB, value.Reference) is null,
            "the landed checkpoint exposes immutable value bytes only to its own creation");

        var repeat = await store.CommitAsync(Batch([State(addressA, new(original)), State(addressB, new(original))], lease.Epoch));
        check(repeat.Landed && store.FoundationValueCount(addressA) == 1 && store.FoundationValueCount(addressB) == 1,
            "the same digest is idempotent within a creation and has a distinct row for another actor");

        foreach (var landedBeforeLoss in new[] { false, true })
        {
            var addition = new FoundationValue([landedBeforeLoss ? (byte)0x63 : (byte)0x64]);
            var prior = store.Instances[addressA.Id].State;
            var attempt = State(addressA, addition);
            var cut = new CountingStore(store, () => { }, (_, landed) =>
            {
                if (landed == landedBeforeLoss) throw new StoreException(Failure.Unknown, "Lose the value checkpoint's answer.");
            });
            var lost = false;
            try { await cut.CommitAsync(Batch([attempt], lease.Epoch)); }
            catch (StoreException error) when (error.Failure == Failure.Unknown) { lost = true; }
            var paired = (store.ReadFoundationValue(addressA, addition.Reference) is not null) == landedBeforeLoss &&
                store.Instances[addressA.Id].State == (landedBeforeLoss ? attempt.Row!.State : prior);
            lease = (await store.TakeAsync(0, "values-fence", Defaults.Lease))!;
            var retry = await store.CommitAsync(Batch([State(addressA, addition)], lease.Epoch));
            check(lost && paired && retry.Landed && store.ReadFoundationValue(addressA, addition.Reference) is not null &&
                store.Instances[addressA.Id].State == attempt.Row!.State,
                $"the reset sees the value and reference together when the first write landed={landedBeforeLoss}");
        }

        await store.ReleaseAsync(0, lease.Epoch);
        var later = Owner("values-delete");
        await using var active = await Activate(later);
        await edge.SendAsync(scope, new Contract.Target.Ref(a), "finish", "finish");
        await Step(later, addressA.Id);
        var selection = (await new ActorList(store, 5).GetAsync(scope, a))!.Selection!;
        await edge.SendAsync(scope, new Contract.Target.Ref(a), "delete", Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(selection)));
        await Step(later, addressA.Id);
        check(store.Instances[addressA.Id].Retired && store.FoundationValueCount(addressA) == 0 &&
            store.ReadFoundationValue(addressA, value.Reference) is null &&
            store.ReadFoundationValue(addressB, value.Reference)?.SequenceEqual(original) == true,
            "qualified Delete purges physical values with the retired row and preserves the other actor's same digest");

        now += 101;
        var successor = await Start(later, "replacement", "alpha");
        var next = Address(successor);
        check(next.Id == addressA.Id && next.Creation == addressA.Creation + 1 &&
            store.ReadFoundationValue(next, value.Reference) is null,
            "a new creation under the caller key cannot read the previous run's digest");

        CreationAddress Address(Contract.ActorRef actor)
        {
            var birth = store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row;
            return new(birth.Id, birth.Creation);
        }
        TurnCommit State(CreationAddress actor, FoundationValue addition)
        {
            var previous = store.Instances[actor.Id];
            return new(actor.Id, previous.WithFoundationState(Json.Write(addition.Reference)) with { Revision = previous.Revision + 1 },
                previous.Revision, [], [], [], [], [], "value reference checkpoint") { FoundationValues = [addition] };
        }
        CommitBatch Batch(IReadOnlyList<TurnCommit> turns, long epoch) =>
            new(0, epoch, store.Partitions[0].Position, Defaults.Lease, false, turns, [], []);
        async Task<bool> Refused(CommitBatch batch)
        {
            try { return !(await store.CommitAsync(batch)).Landed; }
            catch (StoreException) { return true; }
        }
        Worker Owner(string node) => new(node, 0, store, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - initial });
        async Task<Contract.ActorRef> Start(Worker worker, string operation, string callerKey)
        {
            var started = await edge.SendAsync(scope, new Contract.Target.Key(callerKey, new(probe.Type, "values")), operation, Contract.Kinds.Start);
            var actor = started.Actor!.Value;
            await Step(worker, store.FoundationTarget(scope.Tenant, actor.Id)!.Id);
            return actor;
        }
    }

    private static async Task<IScopeActivation> Activate(Worker worker) => (await new DurableHost([worker]).ActivateAsync(0))!;

    private static async Task Step(Worker worker, string actor)
    {
        await worker.PollAsync();
        await worker.LoadAsync();
        await worker.FlushAsync();
        worker.RunTurn(actor);
        await worker.FlushAsync();
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "value-store-witness";
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Inbox.Any(message => message.Kind == "finish")) turn.End(Contract.ActorStatus.Finished);
            return default;
        }
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
    }
}
