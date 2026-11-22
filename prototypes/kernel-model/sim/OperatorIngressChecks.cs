using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class OperatorIngressChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long now = 1_701_000_000_000;
        var store = new InMemoryStore(1, () => now);
        var requests = new Requests("operators");
        var scope = new Contract.BoundScope("acme");
        var foreign = new Contract.BoundScope("other");
        Contract.IIngress ordinary = new InMemoryIngress(store, requests, "operator-owner");
        Contract.IIngress operators = new InMemoryIngress(store, requests, "operator-owner", (selected, _) => selected == scope || selected == foreign);
        Contract.IActorReads reads = new ActorList(store, 10);
        var model = new Probe();
        var worker = new Worker("operator-owner", 0, store, new ActorAdapter(store, model, (requests, "operator-owner")),
            new WorkerOptions { Monotonic = () => 0, Transport = new Replies(requests) });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;

        var start = await ordinary.SendAsync(scope, new Contract.Target.Key("Invoice", new("operator-probe", "ready")),
            "start", Contract.Kinds.Start);
        var reference = start.Actor!.Value;
        var exact = new Contract.Target.Ref(reference);
        var beforeBirth = await operators.SendAsync(scope, exact, "early-migrate", Contract.Kinds.Migrate);
        await Pump(owner);
        check(beforeBirth is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: var accepted } && accepted == reference &&
            ReadState().SequenceEqual(["start", Contract.Kinds.Migrate]),
            "a control without a selection can address the tenant's admitted creator before its first turn");

        var unbound = await ordinary.SendAsync(scope, exact, "no-operator", Contract.Kinds.Suspend);
        var cross = await operators.SendAsync(foreign, exact, "foreign-operator", Contract.Kinds.Suspend);
        check(unbound.Outcome == Contract.AdmissionOutcome.Refused && cross.Outcome == Contract.AdmissionOutcome.Refused,
            "control admission requires the trusted host's authority in the exact tenant");
        check((await operators.SendAsync(scope, new Contract.Target.Key("INVOICE"), "suspend", Contract.Kinds.Suspend)).Actor == reference,
            "authorized key-based control admission resolves the exact life");
        await ordinary.SendAsync(scope, exact, "ordinary", "ordinary");
        await operators.SendAsync(scope, exact, "migrate", Contract.Kinds.Migrate);
        await Pump(owner);
        var withheld = ReadState();
        check(withheld.SequenceEqual(["start", Contract.Kinds.Migrate, Contract.Kinds.Suspend, Contract.Kinds.Migrate]),
            "trusted migrate passes withholding alone while later ordinary input stays queued");
        await operators.SendAsync(scope, exact, "resume", Contract.Kinds.Resume);
        await Pump(owner);
        check(ReadState().SequenceEqual(["start", Contract.Kinds.Migrate, Contract.Kinds.Suspend, Contract.Kinds.Migrate, Contract.Kinds.Resume, "ordinary"]),
            "resume is delivered alone and the withheld input then progresses");

        var liveView = await reads.GetAsync(scope, reference);
        var liveSelection = Json.Write(new Contract.OperatorCommand(liveView!.Selection!));
        await ordinary.SendAsync(scope, exact, "finish", "finish");
        var lateTerminate = operators.RequestAsync(scope, exact, "late-terminate", Contract.Kinds.Terminate, liveSelection, null,
            new(null, now + 100)).AsTask();
        await Pump(owner);
        check(await lateTerminate.WaitAsync(TimeSpan.FromSeconds(2)) is { Error: "stale selection" } &&
            (await reads.GetAsync(scope, reference))?.Header.Status == Contract.ActorStatus.Finished &&
            !ReadState().Contains(Contract.Kinds.Terminate),
            "a terminate accepted while running is fenced off when an earlier input lands the end mark");

        var invalidDelete = await operators.SendAsync(scope, exact, "delete-old-selection", Contract.Kinds.Delete, liveSelection);
        check(invalidDelete is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection },
            "an already stale selection is refused at admission");

        var ended = await reads.GetAsync(scope, reference);
        var endedSelection = Json.Write(new Contract.OperatorCommand(ended!.Selection!));
        model.Protect = true;
        var protectedDelete = operators.RequestAsync(scope, exact, "protected", Contract.Kinds.Delete, endedSelection, null,
            new(null, now + 100)).AsTask();
        await Pump(owner);
        check(await protectedDelete.WaitAsync(TimeSpan.FromSeconds(2)) is { Error: "protected" } &&
            (await reads.GetAsync(scope, reference))?.Header.Status == Contract.ActorStatus.Finished,
            "the committed ended actor retains its exact life when its type protects deletion");
        model.ThrowProtection = true;
        var throwingDelete = operators.RequestAsync(scope, exact, "throwing-hook", Contract.Kinds.Delete, endedSelection, null,
            new(null, now + 100)).AsTask();
        await Pump(owner);
        check(await throwingDelete.WaitAsync(TimeSpan.FromSeconds(2)) is { Error: "protection unavailable" } &&
            (await reads.GetAsync(scope, reference))?.Header.Status == Contract.ActorStatus.Finished,
            "a throwing deletion hook refuses without stranding the owner or changing the ended checkpoint");
        model.ThrowProtection = false;
        model.Protect = false;
        await operators.SendAsync(scope, exact, "allowed", Contract.Kinds.Delete, endedSelection);
        await Pump(owner);
        check(await reads.GetAsync(scope, reference) is null && store.Instances.Values.Single().Retired,
            "an authorized fresh selection retires the ended life through a real fenced delete");

        await CheckTerminalMark(check);

        List<string> ReadState() => Json.Read<List<string>>(store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row.State);
    }

    private static async Task CheckTerminalMark(Action<bool, string> check)
    {
        const long initial = 1_701_000_010_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("terminal-mark");
        var edge = new InMemoryIngress(store, new Requests("terminal-mark"), "terminal-mark");
        var worker = new Worker("terminal-mark", 0, store, new ActorAdapter(store, new Probe()), new WorkerOptions { Monotonic = () => now - initial });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var started = await edge.SendAsync(scope, new Contract.Target.Start(new("operator-probe", "ready")), "start", Contract.Kinds.Start);
        var actor = started.Actor!.Value;
        var reads = new ActorList(store, 10);
        await Pump(owner);
        var live = (await reads.GetAsync(scope, actor))!.Selection;
        await edge.SendAsync(scope, new Contract.Target.Ref(actor), "end", "finish");
        await Pump(owner);
        var terminal = store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!;
        var selection = (await reads.GetAsync(scope, actor))!.Selection;
        var mark = terminal.Terminal;
        check(mark == new InMemoryStore.FoundationTerminalMark(terminal.Row.Revision, now) &&
            store.MatchesFoundationSelection(scope.Tenant, actor.Id, selection) && !store.MatchesFoundationSelection(scope.Tenant, actor.Id, live),
            "the end commit stamps its revision and clock, and the previous running selection becomes stale");
        if (mark is null) return;

        var sameTimeWrongRevision = store.SealFoundationSelection(scope.Tenant, terminal with { Terminal = mark with { Revision = mark.Revision + 1 } });
        var sameRevisionWrongTime = store.SealFoundationSelection(scope.Tenant, terminal with { Terminal = mark with { At = mark.At + 1 } });
        check(!store.MatchesFoundationSelection(scope.Tenant, actor.Id, sameTimeWrongRevision) &&
            !store.MatchesFoundationSelection(scope.Tenant, actor.Id, sameRevisionWrongTime),
            "the selection rejects both a different revision at the same timestamp and a different timestamp at the same revision");

        now++;
        var fence = worker.Fences.Single();
        var row = terminal.Row;
        var metadata = new TurnCommit(row.Id, row with { Revision = row.Revision + 1 }, row.Revision, [], [], [], [], [], null);
        var saved = await store.CommitAsync(new(0, fence.Epoch, fence.Position, Defaults.Lease, false, [metadata], [], []));
        var after = store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!;
        check(saved.Landed && after.Row.Revision == row.Revision + 1 && after.Terminal == mark &&
            store.MatchesFoundationSelection(scope.Tenant, actor.Id, selection),
            "a later fenced bookkeeping commit keeps the original terminal mark and its selection");
    }

    public static async Task Fallback(Action<bool, string> check)
    {
        const long now = 1_701_100_000_000;
        var store = new InMemoryStore(1, () => now);
        var requests = new Requests("operator-fallback");
        var scope = new Contract.BoundScope("acme");
        Contract.IIngress edge = new InMemoryIngress(store, requests, "fallback", (selected, _) => selected == scope);
        Contract.IActorReads reads = new ActorList(store, 10);
        var legacy = new WorkflowInstance([new LegacyProbe()]);
        var router = new ActorTypeRouter(legacy, new Dictionary<string, IActorType> { ["operator-probe"] = new ActorAdapter(store, new Probe()) });
        var worker = new Worker("fallback", 0, store, router, new WorkerOptions { Monotonic = () => 0, Transport = new Replies(requests) });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;

        var start = await edge.SendAsync(scope, new Contract.Target.Start(new("legacy-operator", "parent")), "start", Contract.Kinds.Start);
        var reference = start.Actor!.Value;
        var target = new Contract.Target.Ref(reference);
        await Pump(owner);
        var running = await reads.GetAsync(scope, reference);
        await edge.SendAsync(scope, target, "finish", "finish");
        var deleted = edge.RequestAsync(scope, target, "running-delete", Contract.Kinds.Delete,
            Json.Write(new Contract.OperatorCommand(running!.Selection!)), null, new(null, now + 100)).AsTask();
        await Pump(owner);
        check(await deleted.WaitAsync(TimeSpan.FromSeconds(2)) is { Error: "operator unavailable" } &&
            store.ReadFoundationActor(scope.Tenant, reference.Id).Actor is { Row: { Status: InstanceStatus.Finished, Children: not null } },
            "a mixed router fails closed before a legacy fallback can retire a live child with a stale selection");

        var ended = await reads.GetAsync(scope, reference);
        var current = edge.RequestAsync(scope, target, "ended-delete", Contract.Kinds.Delete,
            Json.Write(new Contract.OperatorCommand(ended!.Selection!)), null, new(null, now + 100)).AsTask();
        await Pump(owner);
        check(await current.WaitAsync(TimeSpan.FromSeconds(2)) is { Error: "operator unavailable" } &&
            store.ReadFoundationActor(scope.Tenant, reference.Id).Actor is { Row.Children: not null },
            "a still-current operator selection cannot bypass the guard through the legacy fallback");
    }

    private sealed class LegacyProbe : Model<List<string>>
    {
        public override string Key => "legacy-operator";

        protected override void Turn(ITurn turn, List<string> state)
        {
            if (turn.Inbox.Any(message => message.Kind == Kinds.Start) && turn.Instance.Definition == "parent")
                turn.Start(new StartSpec(Key, "child"));
            if (turn.Inbox.Any(message => message.Kind == "finish")) turn.SetStatus(InstanceStatus.Finished);
        }
    }

    private static async Task Pump(IScopeActivation owner)
    {
        for (var step = 0; step < 20; step++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class Replies(Requests requests) : IReplyTransport
    {
        public void Send(string node, Reply reply) => requests.Receive(reply);
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "operator-probe";
        public bool Protect { get; set; }
        public bool ThrowProtection { get; set; }
        public bool Withholds(Contract.ActorHeader header) => header.TypeData == "suspended";

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var history = turn.State is null ? [] : Json.Read<List<string>>(turn.State);
            foreach (var message in turn.Inbox)
            {
                if (Contract.Kinds.IsCommand(message.Kind) && turn.Inbox.Count != 1)
                    throw new InvalidOperationException("A command was batched with other input.");
                if (message.Kind is Contract.Kinds.Suspend or Contract.Kinds.Resume)
                    turn.SetHeader(message.Kind == Contract.Kinds.Suspend ? "suspended" : "ready", turn.Header.Labels);
                if (message.Kind == "finish") turn.End(Contract.ActorStatus.Finished);
                history.Add(message.Kind);
            }

            turn.State = Json.Write(history);
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) =>
            ThrowProtection ? throw new InvalidOperationException("Unsupported stored state.") : Protect;
    }
}
