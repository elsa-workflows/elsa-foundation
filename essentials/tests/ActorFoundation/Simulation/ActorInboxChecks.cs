using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ActorInboxChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long start = 1_700_400_000_000;
        const long due = start + 100;
        var now = start;
        var store = new InMemoryStore(1, () => now);
        Contract.IActorReads reader = new ActorList(store, 1);
        var scope = ContractScopes.Bind("acme");
        var other = ContractScopes.Bind("other");
        var address = InstanceRef.In(0, "actor-inbox");
        var initial = Admit(1);
        var owner = await Owner("inbox-first");
        await Pump(owner);
        check(store.AddFoundationMessage(scope.Tenant, initial.Id, "work", "hold") == true &&
            store.AddFoundationMessage(scope.Tenant, initial.Id, "later", "next", dueAt: due) == true,
            "the exact inbox fixture inserts its hold and future timer");

        var pending = await reader.InboxAsync(scope, initial);
        await Pump(owner);
        now++;
        var first = await reader.InboxAsync(scope, initial);
        check(first is { Items: [{ Id: "work", State: Contract.DeliveryState.Held, Sender: null }], Next: not null } &&
            pending.Items is [{ State: Contract.DeliveryState.Pending }] && pending.Items[0].Handle == first.Items[0].Handle &&
            first.Items[0].Handle.Length == 65 && first.Items[0].Size > 0 && first.AsOf == now,
            "the inbox exposes pending and fenced held states with one opaque handle and its AsOf reading");
        await owner.DisposeAsync();
        await using var successor = await Owner("inbox-successor");
        var second = await reader.InboxAsync(scope, initial, first.Next);
        check(second is { Items: [{ Id: "later", Due: due, State: Contract.DeliveryState.Pending }], Next: null } &&
            second.Items[0].Handle != first.Items[0].Handle &&
            (await reader.InboxAsync(other, initial)).Items.Count == 0,
            "an inbox continuation reaches the next committed row across owner change without crossing tenants");

        var foreignCursor = false;
        try { await reader.InboxAsync(other, initial, first.Next); }
        catch (ArgumentException error) when (error.ParamName == "from") { foreignCursor = true; }
        check(foreignCursor, "an inbox continuation cannot be replayed in another tenant");

        check(store.AddFoundationMessage(scope.Tenant, initial.Id, "finish", "finish") == true, "the held work can be released by an outside input");
        await Pump(successor);
        check((await reader.InboxAsync(scope, initial)) is { Items: [{ Id: "later" }] },
            "consumed deduplication markers are absent from the operational inbox");

        check(store.AddFoundationMessage(scope.Tenant, initial.Id, "delete", Kinds.Delete) == true, "the ended inbox fixture admitted its retirement");
        await Pump(successor);
        var replacement = Admit(2);
        await Pump(successor);
        check(store.Messages.Values.Any(row => row.Message.Target.Id == address.Id && row.Message.TargetCreation == 1 && row.Message.Id == "later") &&
            (await reader.InboxAsync(scope, initial)).Items.Count == 0 &&
            (await reader.InboxAsync(scope, replacement)).Items.Count == 0,
            "a pending earlier-life timer remains invisible in either the retired actor or its successor's inbox");

        Contract.ActorRef Admit(long generation)
        {
            var id = generation.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var spec = new StartSpec("inbox", "probe", Creation: new(generation, generation - 1, id));
            var reference = store.AdmitFoundationStart(scope.Tenant,
                new Message(id, address, Kinds.Start, Start: spec, TargetCreation: generation));
            return reference is { } found ? new(found) : throw new InvalidOperationException("The fixture start was refused.");
        }

        async Task<IScopeActivation> Owner(string node)
        {
            var worker = new Worker(node, 0, store, new WorkflowInstance([new Probe()]), new WorkerOptions { Monotonic = () => now - start });
            return (await new DurableHost([worker]).ActivateAsync(0))!;
        }
    }

    public static async Task PublicHold(Action<bool, string> check)
    {
        const long now = 1_701_600_000_000;
        var store = new InMemoryStore(1, () => now);
        var scope = ContractScopes.Bind("acme");
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests("hold-order"), "hold-order");
        Contract.IActorReads reads = new ActorList(store, 16);
        var probe = new HeldProbe();
        var start = new Contract.Target.Start(new(probe.Type, "probe"));
        var subject = (await ingress.SendAsync(scope, start, "begin", Contract.Kinds.Start)).Actor!.Value;
        var target = new Contract.Target.Ref(subject);
        var owner = await Activate("hold-first");
        await Pump(owner);
        await ingress.SendAsync(scope, target, "a", "fc-hold");
        await Pump(owner);
        await ingress.SendAsync(scope, target, "b", "fc-hold");
        await Pump(owner);
        var duplicate = await ingress.SendAsync(scope, target, "a", "fc-hold");
        check(duplicate.Outcome == Contract.AdmissionOutcome.Duplicate && (await reads.InboxAsync(scope, subject)).Items is
            [{ Id: "a", State: Contract.DeliveryState.Held }, { Id: "b", State: Contract.DeliveryState.Held }] &&
            Applied().Count == 0, "Hold keeps multiple public messages unconsumed in their original order");

        await ingress.SendAsync(scope, target, "c", "release");
        await Pump(owner);
        check(Applied().SequenceEqual(["a", "b", "c"]) && (await reads.InboxAsync(scope, subject)).Items.Count == 0,
            "the next public turn sees held inputs before fresh input and consumes each exactly once");

        await ingress.SendAsync(scope, target, "d", "fc-hold");
        await Pump(owner);
        await owner.DisposeAsync();
        await ingress.SendAsync(scope, target, "e", "release");
        await using var successor = await Activate("hold-next");
        await Pump(successor);
        check(Applied().SequenceEqual(["a", "b", "c", "d", "e"]) && (await reads.InboxAsync(scope, subject)).Items.Count == 0,
            "an owner change preserves the held input before the next fresh input");

        async Task<IScopeActivation> Activate(string node)
        {
            var worker = new Worker(node, 0, store, new ActorAdapter(store, probe), new WorkerOptions { Monotonic = () => 0 });
            return (await new DurableHost([worker]).ActivateAsync(0))!;
        }

        List<string> Applied() => Json.Read<List<string>>(store.ReadFoundationActor(scope.Tenant, subject.Id).Actor!.Row.State);
    }

    private sealed class HeldProbe : Contract.IActorType
    {
        public string Type => "hold-order";
        public bool Withholds(Contract.ActorHeader header) => false;
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var result = turn.State is null ? [] : Json.Read<List<string>>(turn.State);
            if (turn.Inbox.Any(message => message.Kind == "release"))
                result.AddRange(turn.Inbox.Select(message => message.Id));
            else
                foreach (var message in turn.Inbox.Where(message => message.Kind == "fc-hold")) turn.Hold(message);
            turn.State = Json.Write(result);
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
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

    private sealed class Probe : Model<List<int>>
    {
        public override string Key => "inbox";

        protected override void Turn(ITurn turn, List<int> state)
        {
            if (turn.Inbox.Any(message => message.Kind == "finish")) turn.SetStatus(InstanceStatus.Finished);
            else
                foreach (var message in turn.Inbox.Where(message => message.Kind == "hold")) turn.Hold(message);
        }
    }
}
