using System.Collections.Concurrent;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class FoundationTenantChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long start = 1_701_700_000_000;
        var now = start;
        var store = new InMemoryStore(1, () => now);
        var probe = new Probe();
        var first = new Contract.BoundScope("acme");
        var second = new Contract.BoundScope("beta");
        var ingress = new InMemoryIngress(store, new Requests("tenant-isolation"), "tenant-isolation");
        var reads = new ActorList(store, 10);
        var spec = new Contract.StartSpec(probe.Type, "ordinary");
        var keyed = new Contract.Target.Key("invoice-9", spec with { TypeData = "keyed" });
        var eventStart = spec with { TypeData = "event-start" };
        var waitEvent = new Contract.Target.Event("order-17");
        var createEvent = new Contract.Target.Event("create-order");
        var keep = new Contract.AddOptions(KeepFor: TimeSpan.FromMinutes(1));

        var left = await ingress.SendAsync(first, new Contract.Target.Start(spec), "birth", Contract.Kinds.Start);
        var right = await ingress.SendAsync(second, new Contract.Target.Start(spec), "birth", Contract.Kinds.Start);
        var repeatedBirth = await ingress.SendAsync(first, new Contract.Target.Start(spec), "birth", Contract.Kinds.Start);
        var separateBirths = IsDistinct(left, right) && repeatedBirth is { Outcome: Contract.AdmissionOutcome.Duplicate } &&
            repeatedBirth.Actor == left.Actor;
        check(separateBirths, "T13 tenant-scoped outside operation receipts admit both roots and deduplicate only the original tenant");
        if (!separateBirths) return;

        var leftKey = await ingress.SendAsync(first, keyed, "keyed-birth", Contract.Kinds.Start);
        var rightKey = await ingress.SendAsync(second, keyed, "keyed-birth", Contract.Kinds.Start);
        var separateKeys = IsDistinct(leftKey, rightKey);
        check(separateKeys, "T13 identical caller keys and operation ids bind two tenant-owned actors");
        if (!separateKeys) return;

        var leftActor = leftKey.Actor!.Value;
        var rightActor = rightKey.Actor!.Value;
        var leftRef = new Contract.Target.Ref(leftActor);
        var rightRef = new Contract.Target.Ref(rightActor);
        var foreign = await ingress.SendAsync(second, leftRef, "op-1", "note", "foreign");
        var leftSend = await ingress.SendAsync(first, leftRef, "op-1", "note", "acme/direct");
        var rightSend = await ingress.SendAsync(second, rightRef, "op-1", "note", "beta/direct");
        var repeatedSend = await ingress.SendAsync(first, leftRef, "op-1", "note", "acme/duplicate");
        check(foreign.Outcome == Contract.AdmissionOutcome.Refused && IsDistinct(leftSend, rightSend) &&
              repeatedSend is { Outcome: Contract.AdmissionOutcome.Duplicate } && repeatedSend.Actor == leftActor,
            "T2 T13 both direct sends keep their own occurrence and the foreign target is refused");

        var firstEarly = await ingress.SendAsync(first, waitEvent, "early", "publish", "acme/early", keep);
        var secondEarly = await ingress.SendAsync(second, waitEvent, "early", "publish", "beta/early", keep);
        await using var owner = await Owner("first-owner");
        await Pump(owner);
        await ingress.SendAsync(first, leftRef, "register", "keep");
        await ingress.SendAsync(second, rightRef, "register", "keep");
        await ingress.SetStartAsync(first, createEvent.CorrelationKey, "creator", eventStart);
        await ingress.SetStartAsync(second, createEvent.CorrelationKey, "creator", eventStart);
        await Pump(owner);
        await owner.DisposeAsync();

        now++;
        await using var successor = await Owner("successor");
        var firstLive = await ingress.SendAsync(first, waitEvent, "live", "publish", "acme/live");
        var secondLive = await ingress.SendAsync(second, waitEvent, "live", "publish", "beta/live");
        var firstCreate = await ingress.SendAsync(first, createEvent, "create", "publish", "acme/created");
        var secondCreate = await ingress.SendAsync(second, createEvent, "create", "publish", "beta/created");
        await Pump(successor);
        var admissions = new[] { firstEarly, secondEarly, firstLive, secondLive, firstCreate, secondCreate };
        check(admissions.All(receipt => receipt.Outcome == Contract.AdmissionOutcome.Accepted) &&
            admissions.Select(receipt => receipt.Publication?.Incarnation).Distinct().Count() == admissions.Length &&
            History(first, leftActor) is ["acme/direct", "acme/early", "acme/live"] &&
            History(second, rightActor) is ["beta/direct", "beta/early", "beta/live"],
            "T3 T13 live and kept events with colliding ids reach only the bound tenant after takeover");

        var leftList = await reads.ListAsync(first, new Contract.ActorQuery());
        var rightList = await reads.ListAsync(second, new Contract.ActorQuery());
        check(leftList.Items.Count == 3 && rightList.Items.Count == 3 &&
            leftList.Items.All(view => view.Header.Tenant == first.Tenant) &&
            rightList.Items.All(view => view.Header.Tenant == second.Tenant) &&
            leftList.Items.Count(view => view.Header.TypeData == "event-start" &&
                History(first, view.Header.Ref) is ["acme/created"]) == 1 &&
            rightList.Items.Count(view => view.Header.TypeData == "event-start" &&
                History(second, view.Header.Ref) is ["beta/created"]) == 1,
            "T4 T13 an exact event-created life and read listing stay bound to each tenant");
        check((await reads.GetAsync(first, rightActor)) is null && (await reads.GetAsync(second, leftActor)) is null &&
            await reads.GetAsync(first, leftActor) is { Header.Tenant: "acme" } &&
            await reads.GetAsync(second, rightActor) is { Header.Tenant: "beta" },
            "T13 the point read refuses a foreign exact reference and returns the bound checkpoint");

        await ingress.SendAsync(first, leftRef, "job", "job", "acme/effect");
        await ingress.SendAsync(second, rightRef, "job", "job", "beta/effect");
        await Pump(successor);
        check(probe.Effects.Count == 2 && probe.Effects.Contains((leftActor, "acme/effect")) &&
            probe.Effects.Contains((rightActor, "beta/effect")) &&
            History(first, leftActor) is ["acme/direct", "acme/early", "acme/live", "acme/effect"] &&
            History(second, rightActor) is ["beta/direct", "beta/early", "beta/live", "beta/effect"],
            "T13 each Worker job receives only its actor's arguments and settles into that exact actor");

        async Task<IScopeActivation> Owner(string node)
        {
            var worker = new Worker(node, 0, store, new ActorAdapter(store, probe),
                new WorkerOptions { Monotonic = () => now - start });
            return (await new DurableHost([worker]).ActivateAsync(0))!;
        }

        List<string>? History(Contract.BoundScope scope, Contract.ActorRef actor) =>
            store.ReadFoundationActor(scope.Tenant, actor.Id).Actor is { } checkpoint
                ? Json.Read<List<string>>(checkpoint.Row.State) : null;
    }

    private static bool IsDistinct(Contract.Admission first, Contract.Admission second) =>
        first is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } reference } &&
        second is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } other } && reference != other;

    private static async Task Pump(IScopeActivation owner)
    {
        for (var pass = 0; pass < 40; pass++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "tenant-check";
        internal ConcurrentQueue<(Contract.ActorRef Actor, string? Argument)> Effects { get; } = new();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var history = turn.State is null ? [] : Json.Read<List<string>>(turn.State);
            foreach (var message in turn.Inbox)
            {
                switch (message)
                {
                    case { Kind: "keep" }:
                        turn.Subscribe("order-17", "slot", keep: true);
                        break;
                    case { Kind: "job" }:
                        turn.StartJob("effect", message.Payload);
                        break;
                    case { Kind: Contract.Kinds.JobFinished, Payload: { } result }:
                        if (Json.Read<Contract.JobFinished>(result) is { Error: null, Result: { } value })
                            history.Add(Json.Read<string>(value));
                        break;
                    case { Payload: { } content }:
                        history.Add(content);
                        break;
                }
            }
            turn.State = Json.Write(history);
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            Effects.Enqueue((job.Actor, job.Arguments));
            return new(job.Arguments);
        }

        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
