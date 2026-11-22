using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkKeptChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long initial = 1_701_400_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var actor = new Probe();
        var scope = new Contract.BoundScope("park");
        Worker MakeOwner(string node) => new(node, 0, store, new ActorAdapter(store, actor),
            new WorkerOptions { Monotonic = () => now - initial });
        var worker = MakeOwner("park-first");
        Contract.IIngress edge = new InMemoryIngress(store, new Requests("park-first"), "park-first");
        var start = await edge.SendAsync(scope, new Contract.Target.Start(new(actor.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(start.Actor!.Value);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump(owner);

        await edge.SendAsync(scope, target, "older", "update", options: new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1)));
        await Pump(owner);
        var row = store.Instances.Values.Single();
        var ordinary = store.Messages.Values.Single(message => message.Message.Id == "older");
        check(ordinary is { Done: true, KeptBy: null } && row.State == "saved", "ordinary done input has no Park disposition");

        var poison = new Message("twin", new(row.Id), "poison", "private payload", TargetCreation: row.Creation, KeepUntil: now + 1).AsIssued();
        var twin = new Message("twin", new(row.Id), "update", "ordinary payload", TargetCreation: row.Creation, KeepUntil: now + 2);
        var staged = new TurnCommit(row.Id, null, null, [], [], [poison, twin], [], [], null);
        var fence = worker.Fences.Single();
        var wrote = await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [staged], [], []));
        check(wrote.Landed, "the fixture inserts both full keys under the owner's fence");
        await Pump(owner);

        var parked = await new ActorList(store, 10).GetAsync(scope, target.Actor);
        check(parked is { Header.Status: Contract.ActorStatus.Faulted, Park: not null } && store.Instances[row.Id].State == "saved",
            "the real failed turn parks without changing the last saved state");
        check(Retained(poison, "private payload", ParkKeepReason.Consumed) && Retained(twin, "ordinary payload", ParkKeepReason.Consumed),
            "the Park keeps both consumed deliveries by full key with their original payloads");
        check(store.Journal.Values.Count(entry => entry.Entries.Contains("parked", StringComparison.Ordinal)) == 1,
            "the Park checkpoint and journal land once");

        var priorOffers = actor.Offers;
        var admission = await edge.SendAsync(scope, target, "twin", "update", "late payload",
            options: new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1)));
        await Pump(owner);
        var late = store.Messages.Values.Single(message => message.Message.Payload == "late payload").Message;
        check(admission.Outcome == Contract.AdmissionOutcome.Accepted && late.InboxKey != poison.InboxKey && late.InboxKey != twin.InboxKey &&
            Retained(late, "late payload", ParkKeepReason.Arrived) && actor.Offers == priorOffers &&
            (await new ActorList(store, 10).GetAsync(scope, target.Actor))?.Park == parked?.Park,
            "a late input with the same wire ID is kept as Arrived without entering the actor or replacing the Park");

        await edge.SendAsync(scope, target, "exhausted", "update", "exhausted payload",
            options: new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1)));
        var exhausted = store.Messages.Values.Single(message => message.Message.Id == "exhausted");
        exhausted.Attempts = 3;
        await Pump(owner);
        check(Retained(exhausted.Message, "exhausted payload", ParkKeepReason.Consumed) && actor.Offers == priorOffers &&
            (await new ActorList(store, 10).GetAsync(scope, target.Actor))?.Park == parked?.Park,
            "an exhausted late delivery is Consumed by the same Park on re-park");

        await edge.SendAsync(scope, target, "refused", "update", "refused payload",
            options: new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1)));
        var refused = store.Messages.Values.Single(message => message.Message.Id == "refused").Message;
        var refusals = 0;
        store.Refuses = candidate => candidate.Id == row.Id && ++refusals <= 2;
        await Pump(owner);
        store.Refuses = null;
        check(refusals == 3 && Retained(refused, "refused payload", ParkKeepReason.Consumed) && actor.Offers == priorOffers &&
            (await new ActorList(store, 10).GetAsync(scope, target.Actor))?.Park == parked?.Park,
            "a definite arrival commit refusal re-parks and keeps the input as Consumed");

        now += 3;
        await store.SweepAsync();
        check(!store.Messages.ContainsKey(ordinary.Message.InboxKey) && Retained(poison, "private payload", ParkKeepReason.Consumed) &&
            Retained(twin, "ordinary payload", ParkKeepReason.Consumed) && Retained(late, "late payload", ParkKeepReason.Arrived) &&
            Retained(exhausted.Message, "exhausted payload", ParkKeepReason.Consumed) && Retained(refused, "refused payload", ParkKeepReason.Consumed),
            "the sweep deletes expired ordinary markers and preserves expired kept rows");
        await owner.DisposeAsync();
        await using var successor = (await new DurableHost([MakeOwner("park-next")]).ActivateAsync(0))!;
        await Pump(successor);
        check(actor.PoisonOffers == 1 && actor.Offers == priorOffers && Retained(poison, "private payload", ParkKeepReason.Consumed) &&
            Retained(twin, "ordinary payload", ParkKeepReason.Consumed) && Retained(late, "late payload", ParkKeepReason.Arrived),
            "takeover does not offer done kept rows to the parked actor");

        bool Retained(Message message, string payload, ParkKeepReason reason) => store.Messages.GetValueOrDefault(message.InboxKey) is
            { Done: true, Held: false, Trial: 0, KeptBy: var actualReason, Message.Payload: var actual } && actual == payload && actualReason == reason;
    }

    private static async Task Pump(IScopeActivation owner)
    {
        for (var step = 0; step < 10; step++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "kept-check";
        public int PoisonOffers { get; private set; }
        public int Offers { get; private set; }
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            Offers++;
            if (turn.Inbox.Any(message => message.Kind == "poison"))
            {
                PoisonOffers++;
                throw new InvalidOperationException("turn fault");
            }
            turn.State = "saved";
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
