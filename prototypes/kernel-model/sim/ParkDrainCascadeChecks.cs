using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkDrainCascadeChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await Run(parentTerminate: true, check);
        await Run(parentTerminate: false, check);
        await Run(parentTerminate: true, check, expiry: true);
    }

    private static async Task Run(bool parentTerminate, Action<bool, string> check, bool expiry = false)
    {
        const long initial = 1_702_310_020_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now) { ParkedRetentionWindow = expiry ? TimeSpan.FromMilliseconds(1) : null };
        var scope = new Contract.BoundScope("park-cascade");
        var probe = new Probe();
        var edge = new InMemoryIngress(store, new Requests("park-cascade"), "park-cascade",
            (_, kind) => kind is Contract.Kinds.Discard or Contract.Kinds.Terminate);
        var pages = new List<TurnCommit>();
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed) pages.AddRange(batch.Turns.Where(turn => turn.FoundationCascade is not null));
        });
        var worker = new Worker("park-cascade", 0, wire, new ActorAdapter(store, probe), new WorkerOptions { Monotonic = () => now - initial });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var parent = (await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "parent")),
            "parent", Contract.Kinds.Start)).Actor!.Value;
        await Step(parent);
        var child = await Spawn(parent);
        var grandchild = await Spawn(child);
        var reads = new ActorList(store, 10);
        Contract.Admission? stale = null;
        if (!parentTerminate)
        {
            var running = (await reads.GetAsync(scope, child))!.Selection!;
            stale = await Send(child, "old-operator", Contract.Kinds.Terminate, Json.Write(new Contract.OperatorCommand(running)),
                new(DueAt: now + 1));
        }
        await Send(child, "park", "park");
        await Step(child);
        var selection = (await reads.GetAsync(scope, child))!.Selection!;
        var payload = Json.Write(new Contract.OperatorCommand(selection));

        var discard = expiry ? null : await Send(child, "discard", Contract.Kinds.Discard, payload);
        if (parentTerminate)
        {
            await Send(parent, "end", "cancel");
            for (var step = 0; step < 3 && Row(parent).Status == InstanceStatus.Running; step++) await Step(parent);
        }
        else await Send(child, "authorized-operator", Contract.Kinds.Terminate, payload);
        if (expiry) now++;
        await Step(child);
        var opened = Row(child);
        var id = opened.Id;
        var source = expiry ? "parent during expiry" : parentTerminate ? "parent during discard" : "selected operator";
        check((expiry || discard?.Outcome == Contract.AdmissionOutcome.Accepted) && opened.ParkDiscard?.Expired == expiry &&
            opened.Children is not null && Row(grandchild).Status == InstanceStatus.Running,
            $"{source}: the drain opens before the waiting terminate and keeps its original subtree");
        if (stale is not null)
        {
            now++;
            await Step(child);
            check(stale.Outcome == Contract.AdmissionOutcome.Accepted && Row(child) == opened &&
                pages.All(turn => turn.Instance != id), "an operator selected before the Park gets no cascade authority at the open drain");
        }
        await Step(child);

        var refused = Row(child);
        check(refused == opened with { Revision = opened.Revision + 1, Children = null } &&
            pages.Count(turn => turn.Instance == id) == 1 &&
            pages.Where(turn => turn.Instance == id).SelectMany(turn => turn.Sent)
                .Any(message => message.CascadeOrigin && message.Target.Id == Row(grandchild).Id),
            $"{source}: the refusing commit discharges the child and preserves the open drain");
        await Step(grandchild);
        for (var step = 0; step < 4; step++) await Step(child);
        check(Row(child) is { Status: InstanceStatus.Faulted, ParkDiscarded: true, ParkDiscard: null } &&
            Row(grandchild).Status == InstanceStatus.Cancelled && pages.Count(turn => turn.Instance == id) == 1,
            $"{source}: the subtree ends and the original drain completes as Faulted");

        async Task<Contract.ActorRef> Spawn(Contract.ActorRef from)
        {
            await Send(from, "spawn-" + from.Id, "spawn");
            await Step(from);
            var result = new Contract.ActorRef(Row(from).State);
            await Step(result);
            return result;
        }

        ValueTask<Contract.Admission> Send(Contract.ActorRef to, string id, string kind, string? body = null,
            Contract.AddOptions? options = null) => edge.SendAsync(scope, new Contract.Target.Ref(to), id, kind, body, options);

        InstanceRow Row(Contract.ActorRef actor) => store.Instances[store.FoundationTarget(scope.Tenant, actor.Id)!.Id];

        async Task Step(Contract.ActorRef actor)
        {
            var id = store.FoundationTarget(scope.Tenant, actor.Id)!.Id;
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(id);
            await worker.FlushAsync();
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "park-cascade";

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
                switch (message.Kind)
                {
                    case "spawn": turn.State = turn.Start(new(Type, "child")).Id; break;
                    case "park": throw new InvalidOperationException("The fixture parks the parent.");
                    case "cancel":
                    case Contract.Kinds.Terminate: turn.End(Contract.ActorStatus.Cancelled); break;
                }
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
    }
}
