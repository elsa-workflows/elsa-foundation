using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class FoundationMigrationDispatchChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long initial = 1_702_500_000_000;
        const string key = "invoice-9";
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("migration");
        var probe = new Probe();
        var edge = new InMemoryIngress(store, new Requests("migration-dispatch"), "migration-dispatch",
            (_, kind) => kind is Contract.Kinds.Migrate or Contract.Kinds.Delete);
        var reads = new ActorList(store, 10);
        var source = new Contract.StartSpec(probe.Type, "source");
        var targetKey = new Contract.Target.Key(key, source);
        var shortReuse = new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(2));
        var original = (await edge.SendAsync(scope, targetKey, "first", Contract.Kinds.Start, options: shortReuse)).Actor!.Value;
        var peer = (await edge.SendAsync(scope, new Contract.Target.Key("peer", source), "peer", Contract.Kinds.Start)).Actor!.Value;
        var plan = (await edge.SendAsync(scope, new Contract.Target.Start(source with { TypeData = "plan" }), "plan", Contract.Kinds.Start)).Actor!.Value;
        await using var first = await Owner("migration-first");
        await Pump(first);

        var page = await reads.ListAsync(scope, new Contract.ActorQuery(Status: Contract.ActorStatus.Running));
        var batch = page.Items.Where(view => view.Header.TypeData == "source").Select(view => view.Header.Ref).ToArray();
        await edge.SendAsync(scope, new Contract.Target.Ref(plan), "selection", "record-selection", Json.Write(batch));
        await Pump(first);
        var selectedLives = ReadPlan().Selected;
        var recorded = selectedLives.Count == 2 && selectedLives.Contains(original) && selectedLives.Contains(peer);
        check(recorded, "MP1 the plan commits the exact source lives from the read model");
        if (!recorded) return;

        var latePayload = Json.Write(new Migration(plan, "source", "late-target"));
        var late = await edge.SendAsync(scope, new Contract.Target.Ref(original), "delayed", Contract.Kinds.Migrate, latePayload,
            new Contract.AddOptions(DueAt: initial + 20));
        var physical = store.FoundationTarget(scope.Tenant, original.Id)!;
        var delayed = store.Messages.Values.SingleOrDefault(row => row.Message.Id == "delayed");
        var admitted = late is { Outcome: Contract.AdmissionOutcome.Accepted } && late.Actor == original &&
            delayed is { Done: false, Message.TargetCreation: 1 } && delayed.Message.Target.Id == physical.Id;
        check(admitted, "MP2 the delayed admission is pinned to creation one before the key is retired");
        if (!admitted) return;
        var delayedKey = delayed!.Message.InboxKey;
        var delayedSeq = delayed.Seq;

        await edge.SendAsync(scope, new Contract.Target.Ref(original), "finish", "finish");
        await Pump(first);
        var selection = (await reads.GetAsync(scope, original))!.Selection!;
        await edge.SendAsync(scope, new Contract.Target.Ref(original), "delete", Contract.Kinds.Delete,
            Json.Write(new Contract.OperatorCommand(selection)));
        await Pump(first);
        now += 3;
        var replacement = (await edge.SendAsync(scope, targetKey, "second", Contract.Kinds.Start)).Actor!.Value;
        await Pump(first);
        var successorAddress = store.FoundationTarget(scope.Tenant, replacement.Id);
        check(replacement != original && successorAddress == new CreationAddress(physical.Id, 2),
            "MP2 the same caller key returns a different exact ref on the same physical actor");
        await first.DisposeAsync();

        await using var successor = await Owner("migration-second");
        var payload = Json.Write(new Migration(plan, "source", "target"));
        var admissions = new Dictionary<Contract.ActorRef, Contract.Admission>();
        foreach (var selected in ReadPlan().Selected)
        {
            var request = new Contract.Target.Ref(selected);
            admissions.Add(selected, await edge.SendAsync(scope, request, "batch/" + selected.Id, Contract.Kinds.Migrate, payload));
        }
        check(admissions[original] is { Outcome: Contract.AdmissionOutcome.Refused, Actor: null } &&
            admissions[peer] is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } exact } && exact == peer,
            "MP1 MP2 the outside dispatcher uses each recorded ref and refuses the already retired life");
        check(store.Messages.TryGetValue(delayedKey, out var beforeDue) &&
            beforeDue is { Done: false, Message.TargetCreation: 1 } && beforeDue.Message.Due == initial + 20 &&
            beforeDue.Seq == delayedSeq && beforeDue.Message.Target.Id == physical.Id,
            "MP2 the exact admitted inbox row is still pending after recreation and takeover, before it is due");
        now = initial + 20;
        await Pump(successor);

        check(ReadPlan().Outcomes is [var migrated] && migrated == peer &&
            (await reads.GetAsync(scope, peer)) is { Header.TypeData: "target" } &&
            (await reads.GetAsync(scope, replacement)) is { Header.TypeData: "source" } &&
            store.Messages.TryGetValue(delayedKey, out var settled) &&
            settled is { Done: true, Message.TargetCreation: 1 } && settled.Seq == delayedSeq,
            "MP1 MP2 only the selected peer migrates and records an outcome after takeover and delayed predecessor delivery");

        PlanState ReadPlan() => Json.Read<PlanState>(store.ReadFoundationActor(scope.Tenant, plan.Id).Actor!.Row.State);

        async Task<IScopeActivation> Owner(string node)
        {
            var worker = new Worker(node, 0, store, new ActorAdapter(store, probe),
                new WorkerOptions { Monotonic = () => now - initial });
            return (await new DurableHost([worker]).ActivateAsync(0))!;
        }
    }

    private static async Task Pump(IScopeActivation owner)
    {
        for (var pass = 0; pass < 30; pass++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
            await Task.Yield();
        }
    }

    private sealed record Migration(Contract.ActorRef Plan, string From, string To);
    private sealed record PlanState(List<Contract.ActorRef> Selected, List<Contract.ActorRef> Outcomes);

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "migration-dispatch";

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var state = turn.State is null ? new PlanState([], []) : Json.Read<PlanState>(turn.State);
            foreach (var message in turn.Inbox)
            {
                switch (message)
                {
                    case { Kind: "finish" }:
                        turn.End(Contract.ActorStatus.Finished);
                        break;
                    case { Kind: Contract.Kinds.Migrate, Payload: { } payload }:
                        var migration = Json.Read<Migration>(payload);
                        if (turn.Header.TypeData != migration.From) break;
                        turn.SetHeader(migration.To, turn.Header.Labels);
                        turn.Send(migration.Plan, "outcome", Json.Write(turn.Header.Ref));
                        break;
                    case { Kind: "record-selection", Payload: { } selection }:
                        state = state with { Selected = Json.Read<List<Contract.ActorRef>>(selection) };
                        break;
                    case { Kind: "outcome", Payload: { } outcome }:
                        state.Outcomes.Add(Json.Read<Contract.ActorRef>(outcome));
                        break;
                }
            }
            turn.State = Json.Write(state);
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
