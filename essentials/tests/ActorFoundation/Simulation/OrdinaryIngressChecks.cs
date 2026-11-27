using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class OrdinaryIngressChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long startAt = 1_700_500_000_000;
        var now = startAt;
        var store = new InMemoryStore(1, () => now);
        var edge = new OrdinaryIngress(store);
        var scope = ContractScopes.Bind("acme");
        var other = ContractScopes.Bind("other");
        var read = new ActorList(store, 10);
        var spec = new Contract.Target.Start(new("ordinary-ingress", "startup", "configured"));
        var deduplicate = new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(20));
        var race = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            await edge.SendAsync(scope, spec, "root-request", deduplicate))));
        var first = race.First().Actor!.Value;
        var startElsewhere = await edge.SendAsync(other, spec, "root-request", deduplicate);
        check(race.Count(receipt => receipt.Outcome == Contract.AdmissionOutcome.Accepted) == 1 &&
            race.All(receipt => receipt.Actor == first) &&
            startElsewhere is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } remoteRef } && remoteRef != first &&
            store.Messages.Count == 2,
            "concurrent generated starts with the same outside id create one exact life per tenant");

        var exact = new Contract.Target.Ref(first);
        check(await edge.SendAsync(other, exact, "foreign", "ordinary") is { Outcome: Contract.AdmissionOutcome.Refused, Actor: null } &&
            await edge.SendAsync(scope, exact, "control", Contract.Kinds.Terminate) is { Outcome: Contract.AdmissionOutcome.Refused, Actor: null },
            "ordinary exact ingress hides foreign refs and rejects operator kinds");
        var firstInput = await edge.SendAsync(scope, exact, "exact-send", "ordinary", "once", deduplicate);
        var duplicate = await edge.SendAsync(scope, exact, "exact-send", "ordinary", "different", deduplicate);
        check(firstInput is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: var addressed } && addressed == first &&
            duplicate.Outcome == Contract.AdmissionOutcome.Duplicate,
            "an exact send can follow the admission before the actor's first turn");

        async Task<IScopeActivation> Owner(string node)
        {
            var worker = new Worker(node, 0, store, new ActorAdapter(store, new Probe()), new WorkerOptions { Monotonic = () => now - startAt });
            return (await new DurableHost([worker]).ActivateAsync(0))!;
        }
        var owner = await Owner("ordinary-owner");
        await Pump(owner);
        check((await read.GetAsync(scope, first)) is { Header.TypeData: "startup" } &&
            Read(scope, first).SequenceEqual(["configured", "once"]) &&
            Read(other, startElsewhere.Actor!.Value).SequenceEqual(["configured"]),
            "the real contract adapter receives the configured start input and the exact ordinary send");
        await owner.DisposeAsync();

        now += 20;
        await using var next = await Owner("ordinary-successor");
        var onBoundary = await edge.SendAsync(scope, spec, "root-request", deduplicate);
        check(onBoundary is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: var replayed } && replayed == first &&
            (await edge.SendAsync(scope, exact, "exact-send", "ordinary", "different", deduplicate)).Outcome == Contract.AdmissionOutcome.Duplicate,
            "both duplicate receipts remain valid at the inclusive clock bound after takeover");

        now++;
        await store.SweepAsync();
        var repeated = await edge.SendAsync(scope, spec, "root-request", deduplicate);
        await Pump(next);
        check(repeated is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } later } && later != first &&
            Read(scope, first).SequenceEqual(["configured", "once"]) &&
            Read(scope, later).SequenceEqual(["configured"]),
            "a new generated start after the receipt expires has its own exact ref and leaves the old life intact");

        Contract.IIngress ingress = new InMemoryIngress(store, new Requests("ordinary-successor"), "ordinary-successor");
        var due = now + 200;
        var delayed = new Contract.AddOptions(DueAt: due, KeepFor: TimeSpan.FromMilliseconds(7));
        var delayedFirst = await ingress.SendAsync(scope, exact, "scheduled", "ordinary", "first occurrence", delayed);
        var occurrence = store.Messages.Keys.Single(key => store.Messages[key].Message.Id == "scheduled");
        var legacy = new Message(occurrence.Id, new InstanceRef("0/legacy"), "ordinary", Due: due + 100);
        check(await store.AddAsync(legacy) && store.Messages.ContainsKey(occurrence) && store.Messages.ContainsKey(legacy.InboxKey),
            "the private occurrence and the same legitimate legacy wire id coexist in separate scopes");
        now += 7;
        var delayedDuplicate = await ingress.SendAsync(scope, exact, "scheduled", "ordinary", "ignore", delayed);
        now++;
        var delayedSecond = await ingress.SendAsync(scope, exact, "scheduled", "ordinary", "second occurrence", delayed);
        var immediateRetry = await ingress.SendAsync(scope, exact, "scheduled", "ordinary", "ignore again", delayed);
        await store.SweepAsync();
        var pending = await read.InboxAsync(scope, first);
        check(delayedFirst.Outcome == Contract.AdmissionOutcome.Accepted && delayedDuplicate.Outcome == Contract.AdmissionOutcome.Duplicate &&
            delayedSecond.Outcome == Contract.AdmissionOutcome.Accepted && immediateRetry.Outcome == Contract.AdmissionOutcome.Duplicate &&
            pending.Items is [{ Id: "scheduled", Due: var firstDue, Handle: var firstHandle, State: Contract.DeliveryState.Pending },
                { Id: "scheduled", Due: var secondDue, Handle: var secondHandle, State: Contract.DeliveryState.Pending }] &&
            firstDue == due && secondDue == due && firstHandle != secondHandle,
            "an expired pending send admits a second occurrence while preserving the first and deduplicating the second");
        now = due - 1;
        await Pump(next);
        check(Read(scope, first).SequenceEqual(["configured", "once"]),
            "the two pending occurrences remain ineligible before their shared due time");
        now++;
        await Pump(next);
        var committed = await read.TailAsync(scope, first);
        var effects = committed.Items.Where(entry => entry.Entry == Json.Write("first occurrence") || entry.Entry == Json.Write("second occurrence"));
        check(effects.Select(entry => entry.Entry).SequenceEqual([Json.Write("first occurrence"), Json.Write("second occurrence")]) &&
            (await read.InboxAsync(scope, first)).Items.Count == 0,
            "the public journal shows both occurrences applied exactly once in insertion order");

        List<string> Read(Contract.BoundScope tenant, Contract.ActorRef reference) =>
            Json.Read<List<string>>(store.ReadFoundationActor(tenant.Tenant, reference.Id).Actor!.Row.State);
    }

    private static async Task Pump(IScopeActivation activation)
    {
        for (var step = 0; step < 20; step++)
        {
            await activation.WokenAsync(null);
            await activation.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "ordinary-ingress";
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var state = turn.State is null ? [] : Json.Read<List<string>>(turn.State);
            foreach (var input in turn.Inbox.Select(message => message.Payload).OfType<string>())
            {
                state.Add(input);
                turn.Record(input);
            }
            turn.State = Json.Write(state);
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
