using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ActorAdapterChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long start = 1_700_200_000_000;
        const long due = start + 100;
        var now = start;
        var store = new InMemoryStore(1, () => now);
        var actor = new Probe();
        var adapter = new ActorAdapter(store, actor);
        var router = new ActorTypeRouter(new WorkflowInstance([]), new Dictionary<string, IActorType> { [actor.Type] = adapter });

        Contract.ActorRef Admit(string name, string scenario)
        {
            var spec = new StartSpec(actor.Type, "seed", scenario, History: History.Minimal, Creation: new(1, 0, name));
            var creator = new Message(name, InstanceRef.In(0, name), Kinds.Start, Start: spec, TargetCreation: 1);
            return new(store.AdmitFoundationStart("acme", creator)!);
        }

        async Task<IScopeActivation> Owner(string node)
        {
            var worker = new Worker(node, 0, store, router, new WorkerOptions { Monotonic = () => now - start });
            return (await new DurableHost([worker]).ActivateAsync(0))!;
        }

        var root = Admit("typed-root", "keep");
        var recalled = Admit("typed-recalled", "recall");
        var rejected = Admit("typed-rejected", "fail");
        store.Refuses = row => row.State.Contains("must-rollback", StringComparison.Ordinal);
        var first = await Owner("typed-owner");
        await Pump(first);
        var reader = new ActorList(store, 10);
        var scope = new Contract.BoundScope("acme");
        var rootView = await reader.GetAsync(scope, root);
        check(rootView is { Header: { TypeData: "typed", Labels.Count: 1, Status: Contract.ActorStatus.Running } } &&
            rootView.Header.Ref == root && rootView.Header.Labels["seed"] == "seed",
            "public actor start and SetHeader reach the real fenced checkpoint");
        var persisted = store.ReadFoundationActor("acme", root.Id).Actor!.Row;
        var rootState = Json.Read<ProbeState>(persisted.State);
        var child = new Contract.ActorRef(rootState.Child!);
        check(rootState.Handle is { Length: 65 } && rootState.Handle.StartsWith('~') &&
            store.Messages.Values.Any(row => row.Message.Id == rootState.Handle && row.Message.Due == due) &&
            (await reader.GetAsync(scope, child)) is { Header: { Status: Contract.ActorStatus.Finished, Parent: var parent } } && parent == root &&
            rootState.ChildOutput == "from-parent",
            "public child creation, same-turn send and final notice expose only exact refs and opaque handles");
        check((await reader.GetAsync(scope, rejected)) is { Park: not null, Header.Status: Contract.ActorStatus.Faulted } &&
            store.Messages.Values.All(row => row.Message.Payload != "must-rollback") &&
            !store.ReadFoundationActors("acme").Actors.Any(item => item.Row.State.Contains("must-rollback")),
            "a refused public actor turn parks without its staged checkpoint, child or message");

        var outsideSameId = false;
        try { store.AddFoundationMessage("acme", root.Id, rootState.Handle!, "firing"); }
        catch (ArgumentException exception) when (exception.ParamName == "id") { outsideSameId = true; }
        check(outsideSameId && store.AddFoundationMessage("acme", recalled.Id, "recall", "recall") == true,
            "outside sends cannot use an issued timer id");
        await Pump(first);
        await first.DisposeAsync();

        now = due - 1;
        await using var next = await Owner("typed-successor");
        await Pump(next);
        check(Json.Read<ProbeState>(store.ReadFoundationActor("acme", root.Id).Actor!.Row.State).Fires == 0,
            "the replacement owner withholds a public actor's timer before its absolute deadline");
        now = due;
        await Pump(next);
        rootState = Json.Read<ProbeState>(store.ReadFoundationActor("acme", root.Id).Actor!.Row.State);
        var recalledState = Json.Read<ProbeState>(store.ReadFoundationActor("acme", recalled.Id).Actor!.Row.State);
        check(rootState.Fires == 1 && rootState.FiredAt == due && recalledState.Fires == 0 &&
            (await reader.GetAsync(scope, root)) is { Header: { Status: Contract.ActorStatus.Finished, EndedAt: due } },
            "the replacement gives the actor its exact timer once, and a recalled timer stays gone");
        check(store.Journal.Values.Any(row => row.Instance == persisted.Id && row.Entries.Contains("start")) &&
            store.Journal.Values.Any(row => row.Instance == persisted.Id && row.Entries.Contains("end")),
            "public actor lifecycle entries survive the Minimal history level");
    }

    public static async Task EventAmbient(Action<bool, string> check)
    {
        const long now = 1_700_200_000_000;
        var store = new InMemoryStore(2, () => now);
        var requests = new Requests("public-event");
        var transport = new LocalReplies(requests);
        var probe = new Probe();
        var scope = new Contract.BoundScope("acme");
        var bound = new EventRequests(store, requests, "receiving-node");
        var ambient = new object();
        var root = Arm(0, "local", "acme");
        var remote = Arm(1, "remote", "acme");
        var foreign = Arm(1, "foreign", "other");

        async Task<IScopeActivation> Owner(string name, int partition)
        {
            var adapter = new ActorAdapter(store, probe, (requests, name));
            var worker = new Worker(name, partition, store, adapter,
                new WorkerOptions { Transport = transport, Monotonic = () => 0, Partitions = 2 });
            return (await new DurableHost([worker]).ActivateAsync(partition))!;
        }

        await using var local = await Owner("receiving-node", 0);
        await using var elsewhere = await Owner("remote-node", 1);
        await Pump(local);
        await Pump(elsewhere);

        // Publish a legacy event with the same spelling and id. It cannot answer the scoped request or consume its wait.
        await Events.PublishAsync(store, new EventRow("acme_signal", "event-1", "unbound", now + 10_000), 2);
        await Pump(local);
        await Pump(elsewhere);
        check(Read("acme", root).Count == 0 && Read("acme", remote).Count == 0 && Read("other", foreign).Count == 0,
            "a legacy publication with the same event key cannot reach a scoped actor");

        var wait = bound.RequestAsync(scope, new("signal"), "event-1", "request", null,
            new(ambient, now + 10_000)).AsTask();
        var addresses = store.Messages.Values.Where(row => row.Message.ReplyTo is not null).ToArray();
        check(addresses.Length == 2 && !wait.IsCompleted, "one bound event reaches both local and remote live waits");
        await Pump(local);
        await Pump(elsewhere);
        check(await wait.WaitAsync(TimeSpan.FromSeconds(2)) is { Channel: "signal", Item: "request" } &&
            Read("acme", root) is { Count: 1, WithAmbient: 1 } && Read("acme", remote) is { Count: 1, WithAmbient: 0 } &&
            Read("other", foreign).Count == 0,
            "only the addressed node sees the event request Ambient, and both matching actors consume the event");

        check(store.AddFoundationMessage("acme", remote.Id, "turn-publish", "publish", "actor output") == true,
            "a typed actor can receive a request to publish an event");
        await Pump(elsewhere);
        await Pump(local);
        check(Read("acme", root) is { Count: 2, WithAmbient: 1, LastEvent: { } id } && id.StartsWith('~') &&
            Read("acme", remote).Count == 2 && Read("other", foreign).Count == 0,
            "the turn's durable publish ticket preserves tenant scope and returns the same opaque event id as delivery");

        Contract.ActorRef Arm(int partition, string name, string tenant)
        {
            var spec = new StartSpec(probe.Type, name, "wait", Creation: new(1, 0, name));
            return new(store.AdmitFoundationStart(tenant, new Message(name, InstanceRef.In(partition, name), Kinds.Start,
                Start: spec, TargetCreation: 1))!);
        }

        ProbeState Read(string tenant, Contract.ActorRef reference) => Json.Read<ProbeState>(store.ReadFoundationActor(tenant, reference.Id).Actor!.Row.State);
    }

    private sealed class LocalReplies(Requests requests) : IReplyTransport
    {
        public void Send(string node, Reply reply)
        {
            if (node == "receiving-node") requests.Receive(reply);
        }
    }

    private static async Task Pump(IScopeActivation activation)
    {
        for (var step = 0; step < 24; step++)
        {
            await activation.WokenAsync(null);
            await activation.RunAsync();
            await Task.Yield();
        }
    }

    public sealed class ProbeState
    {
        public string? Handle { get; set; }
        public string? Child { get; set; }
        public string? ChildOutput { get; set; }
        public int Fires { get; set; }
        public int Count { get; set; }
        public int WithAmbient { get; set; }
        public string? LastEvent { get; set; }
        public long? FiredAt { get; set; }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "typed-actor";
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var state = turn.State is { } saved ? Json.Read<ProbeState>(saved) : new();
            foreach (var message in turn.Inbox)
            {
                switch (message.Kind)
                {
                    case Contract.Kinds.Start when message.Payload == "wait":
                        turn.Subscribe("signal", "active");
                        break;
                    case "publish":
                        turn.Publish("signal", message.Payload, TimeSpan.FromSeconds(10));
                        break;
                    case Contract.Kinds.Event when message.Event is { Key: "signal", Wait: "active" } delivered:
                        state.Count++;
                        if (turn.Ambient is not null) state.WithAmbient++;
                        state.LastEvent = delivered.Id;
                        turn.Emit("signal", message.Payload);
                        break;
                    case Contract.Kinds.Start when message.Payload == "child":
                        break;
                    case Contract.Kinds.Start:
                        turn.SetHeader("typed", new Dictionary<string, string> { ["seed"] = turn.Header.TypeData });
                        state.Handle = turn.Send(turn.Header.Ref, "firing", "unquoted", turn.Now + 100);
                        if (message.Payload is "keep" or "fail")
                        {
                            var child = turn.Start(new(Type, "child", "child"));
                            state.Child = child.Id;
                            turn.Send(child, "signal", message.Payload == "fail" ? "must-rollback" : "from-parent");
                        }
                        if (message.Payload == "fail") state.Child = "must-rollback";
                        break;
                    case "signal" when message.Sender == turn.Header.Parent && turn.Header.Parent is not null:
                        turn.End(Contract.ActorStatus.Finished, message.Payload);
                        break;
                    case Contract.Kinds.ChildFinished:
                        var notice = Json.Read<Contract.ChildFinished>(message.Payload!);
                        if (notice is { Final: true, Status: Contract.ActorStatus.Finished } && notice.Child.Id == state.Child)
                            state.ChildOutput = Json.Read<string>(notice.Output!);
                        break;
                    case "recall":
                        turn.Recall(state.Handle!);
                        break;
                    case "firing" when message.Sender == turn.Header.Ref && message.Id == state.Handle && message.Payload == "unquoted":
                        state.Fires++;
                        state.FiredAt = turn.Now;
                        turn.End(Contract.ActorStatus.Finished);
                        break;
                }
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
