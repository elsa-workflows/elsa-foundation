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
        Contract.IIngress operators = new InMemoryIngress(store, requests, "operator-owner", (selected, _) => selected == scope);
        Contract.IActorReads reads = new ActorList(store, 10);
        var model = new Probe();
        var worker = new Worker("operator-owner", 0, store, new ActorAdapter(store, model, (requests, "operator-owner")),
            new WorkerOptions { Monotonic = () => 0, Transport = new Replies(requests) });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;

        var start = await ordinary.SendAsync(scope, new Contract.Target.Key("Invoice", new("operator-probe", "ready")),
            "start", Contract.Kinds.Start);
        var reference = start.Actor!.Value;
        var exact = new Contract.Target.Ref(reference);
        await Pump(owner);

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
        check(withheld.SequenceEqual(["start", Contract.Kinds.Suspend, Contract.Kinds.Migrate]),
            "trusted migrate passes withholding alone while later ordinary input stays queued");
        await operators.SendAsync(scope, exact, "resume", Contract.Kinds.Resume);
        await Pump(owner);
        check(ReadState().SequenceEqual(["start", Contract.Kinds.Suspend, Contract.Kinds.Migrate, Contract.Kinds.Resume, "ordinary"]),
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
        model.Protect = false;
        await operators.SendAsync(scope, exact, "allowed", Contract.Kinds.Delete, endedSelection);
        await Pump(owner);
        check(await reads.GetAsync(scope, reference) is null && store.Instances.Values.Single().Retired,
            "an authorized fresh selection retires the ended life through a real fenced delete");

        List<string> ReadState() => Json.Read<List<string>>(store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row.State);
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
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => Protect;
    }
}
