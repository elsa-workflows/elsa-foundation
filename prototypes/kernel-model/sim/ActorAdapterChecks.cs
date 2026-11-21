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

        Contract.ActorRef Admit(string name, string scenario, History history = History.Minimal)
        {
            var spec = new StartSpec(actor.Type, "seed", scenario, History: history, Creation: new(1, 0, name));
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
        var parked = Admit("typed-parked", "recall");
        var noState = Admit("typed-null", "unset", History.State);
        var holdingParent = Admit("typed-hold-term", "hold-parent");
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
            !store.ReadFoundationActors("acme").Actors.Any(item => item.Row.State.Contains("must-rollback")) &&
            actor.AttemptedChild is { } rolledBack &&
            store.FoundationTarget("acme", rolledBack.Id) is null &&
            store.AddFoundationMessage("acme", rolledBack.Id, "orphan", "inspect") is null,
            "a refused public actor turn parks without its staged checkpoint, child or message");

        var parentCheckpoint = store.ReadFoundationActor("acme", holdingParent.Id).Actor!.Row;
        var pendingChild = new Contract.ActorRef(Json.Read<ProbeState>(parentCheckpoint.State).Child!);
        check(store.AddFoundationMessage("acme", holdingParent.Id, "hold-terminate", Kinds.Terminate, "hold") == true,
            "the fixture submits a terminate to a parent with a live attached child");
        await Pump(first);
        check(store.Messages.Values.Any(row => row.Message.Id == "hold-terminate" && !row.Done) &&
            (await reader.GetAsync(scope, pendingChild))?.Header.Status == Contract.ActorStatus.Running &&
            store.ReadFoundationActor("acme", holdingParent.Id).Actor!.Row.Children == parentCheckpoint.Children,
            "a held terminate stays pending and cannot cancel or discharge the live child");

        check(store.AddFoundationMessage("acme", holdingParent.Id, "wake-held-term", "late-update") == true,
            "fresh input can stimulate the held command");
        await Pump(first);
        check((await reader.GetAsync(scope, pendingChild))?.Header.Status == Contract.ActorStatus.Cancelled &&
            store.ReadFoundationActor("acme", holdingParent.Id).Actor is { Header.TypeData: "seed", Row: var terminated } &&
            Json.Read<ProbeState>(terminated.State).Count == 1 &&
            Json.Read<ProbeState>(terminated.State).TerminateCalls == 2,
            "the isolated terminate reaches the actor again, cascades on consumption and lets later input proceed");

        var outsideSameId = false;
        try { store.AddFoundationMessage("acme", root.Id, rootState.Handle!, "firing"); }
        catch (ArgumentException exception) when (exception.ParamName == "id") { outsideSameId = true; }
        check(outsideSameId && store.AddFoundationMessage("acme", recalled.Id, "recall", "recall") == true,
            "outside sends cannot use an issued timer id");
        var noStateView = await reader.GetAsync(scope, noState);
        check(noStateView is { Header: { TypeData: "seed", Labels.Count: 0 } },
            "the initial typed header commits unchanged when the actor omits SetHeader");
        var nullAddress = store.ReadFoundationActor("acme", noState.Id).Actor!.Row.Id;
        int Changes() => store.Journal.Values.Count(entry => entry.Instance == nullAddress && entry.Entries == "[]");
        foreach (var (kind, changes) in new[] { ("null-unchanged", 0), ("null-text", 1), ("null-unset", 2) })
        {
            store.AddFoundationMessage("acme", noState.Id, kind, kind);
            await Pump(first);
            check(Changes() == changes, $"State history records the logical nullable transition: {kind}");
        }

        var parkedBefore = store.ReadFoundationActor("acme", parked.Id).Actor!.Row.State;
        store.AddFoundationMessage("acme", parked.Id, "reject-update", "reject-update");
        await Pump(first);
        store.AddFoundationMessage("acme", parked.Id, "late-update", "late-update");
        await first.DisposeAsync();

        now = due - 1;
        await using var next = await Owner("typed-successor");
        await Pump(next);
        check(Json.Read<ProbeState>(store.ReadFoundationActor("acme", root.Id).Actor!.Row.State).Fires == 0,
            "the replacement owner withholds a public actor's timer before its absolute deadline");
        now = due;
        store.AddFoundationMessage("acme", noState.Id, "verify-null", "verify-null");
        await Pump(next);
        check((await reader.GetAsync(scope, noState))?.Header.TypeData == "still-null",
            "an unset actor state survives a reload without becoming the literal text null");
        check((await reader.GetAsync(scope, parked)) is { Park: not null, Header.Status: Contract.ActorStatus.Faulted } &&
            store.ReadFoundationActor("acme", parked.Id).Actor!.Row.State == parkedBefore,
            "an unresolved Park holds fresh input and an earlier due timer while preserving the previous state");
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
        var bound = new EventIngress(store, requests, "receiving-node");
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
        var isolated = Read("acme", root).Count == 0 && Read("acme", remote).Count == 0 && Read("other", foreign).Count == 0;
        check(isolated, "a legacy publication with the same event key cannot reach a scoped actor");
        if (!isolated) return;

        var wait = bound.RequestAsync(scope, new("signal"), "event-1", "request", null,
            new(ambient, now + 10_000)).AsTask();
        var addresses = store.Messages.Values.Where(row => row.Message.ReplyTo is not null).ToArray();
        check(addresses.Length == 2 && !wait.IsCompleted, "one bound event reaches both local and remote live waits");
        await Pump(elsewhere);
        await Pump(local);
        transport.Deliver();
        check(await wait.WaitAsync(TimeSpan.FromSeconds(2)) is { Channel: "signal", Item: "request" } &&
            Read("acme", root) is { Count: 1, WithAmbient: 1 } && Read("acme", remote) is { Count: 1, WithAmbient: 0 } &&
            Read("other", foreign).Count == 0,
            "only the addressed node sees the event request Ambient, and both matching actors consume the event");

        check(store.AddFoundationMessage("acme", remote.Id, "turn-publish", "publish", "actor output") == true,
            "a typed actor can receive a request to publish an event");
        await Pump(elsewhere);
        await Pump(local);
        check(Read("acme", root) is { Count: 2, WithAmbient: 1, LastEvent: { } id } && id.StartsWith('~') &&
            Read("acme", remote) is { Count: 2, LastEvent: var received, Published: var issued } && id == received && id == issued &&
            Read("other", foreign).Count == 0,
            "the turn's durable publish ticket preserves tenant scope and returns the same opaque event id as delivery");

        var a = bound.RequestAsync(scope, new("signal"), "separate-a", "emit-first", null, new("first-object", now + 10_000)).AsTask();
        var b = bound.RequestAsync(scope, new("signal"), "separate-b", "reply-first", null, new(null, now + 10_000)).AsTask();
        await Pump(local);
        transport.Deliver();
        check(await a.WaitAsync(TimeSpan.FromSeconds(2)) is { Channel: "first-emit" } &&
            await b.WaitAsync(TimeSpan.FromSeconds(2)) is { Channel: "first-reply" } &&
            Read("acme", root).Observations.SequenceEqual(["separate-a:first-object:1", "separate-b:none:1"]),
            "two local requests use separate ambient values and the first authored response wins in either order");

        using var cancellation = new CancellationTokenSource();
        var held = bound.RequestAsync(scope, new("signal"), "held-ambient", "hold", null,
            new("held-object", now + 10_000), cancellation.Token).AsTask();
        await Pump(local);
        check(!held.IsCompleted && store.AddFoundationMessage("acme", root.Id, "drive-held", "drive-held") == true,
            "holding the requested event leaves the live request unanswered");
        await Pump(local);
        transport.Deliver();
        check(await held.WaitAsync(TimeSpan.FromSeconds(2)) is { Channel: "held-answered" } &&
            Read("acme", root).Observations.TakeLast(2).SequenceEqual(["held-ambient:held-object:1", "held-ambient:none:2"]),
            "a held event is handed back in order on ordinary input without reusing its first-turn Ambient");

        foreach (var (birthId, mode, early) in new[]
        {
            ("birth-event", "reply-emit", false), ("birth-end", "reply-end", false),
            ("birth-event-early", "reply-emit", true), ("birth-end-early", "reply-end", true)
        })
        {
            await bound.SetStartAsync(scope, "spawn", "starts", new Contract.StartSpec(probe.Type, birthId, "configured-input"));
            var spawned = bound.RequestAsync(scope, new("spawn"), birthId, mode, null,
                new("initial-ambient", now + 10_000, ReleaseBeforeCommit: early)).AsTask();
            var partition = InstanceRef.Spread(birthId + ":starts", 2);
            await Pump(partition == 0 ? local : elsewhere);
            transport.Deliver();
            var born = store.ReadFoundationActors("acme").Actors.Single(item => item.Header.TypeData == birthId);
            var bornState = Json.Read<ProbeState>(born.Row.State);
            var expectedAmbient = partition == 0 ? "initial-ambient" : "none";
            check(await spawned.WaitAsync(TimeSpan.FromSeconds(2)) is { Channel: "spawned", Item: var payload } && payload as string == mode &&
                bornState.Observations.SequenceEqual([$"start:configured-input:{expectedAmbient}", $"event:{mode}:none"]),
                $"a silent start keeps its request and the later event's explicit reply wins: {mode}, early {early}");
        }

        transport.Throwing = true;
        using var lostCaller = new CancellationTokenSource();
        var lostReply = bound.RequestAsync(scope, new("signal"), "disconnected", "reply-first", null,
            new(null, now + 10_000, ReleaseBeforeCommit: true), lostCaller.Token).AsTask();
        await Pump(local);
        lostCaller.Cancel();
        try { await lostReply; }
        catch (OperationCanceledException) when (lostCaller.IsCancellationRequested) { }
        transport.Throwing = false;
        store.AddFoundationMessage("acme", root.Id, "after-live-failure", "late-update");
        await Pump(local);
        var afterLoss = Read("acme", root);
        var attemptsAfterLoss = afterLoss.Observations.Count(o => o.StartsWith("disconnected:", StringComparison.Ordinal));
        check(transport.Failures == 1 && attemptsAfterLoss == 1 && afterLoss.Count == 3,
            $"throwing early transport cannot strand the turn, and later input runs after the single commit ({transport.Failures} failed sends, {attemptsAfterLoss} committed turns, count {afterLoss.Count})");

        store.Refuses = row => row.State.Contains("refused-output", StringComparison.Ordinal);
        var refused = bound.RequestAsync(scope, new("signal"), "refused-output", "refused", null,
            new(null, now + 10_000)).AsTask();
        await Pump(local);
        transport.Deliver();
        check(await refused.WaitAsync(TimeSpan.FromSeconds(2)) is { Channel: null, Status: Contract.ActorStatus.Faulted },
            "a refused actor commit releases the Park status without releasing the authored reply");

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
        private readonly List<Reply> _sent = [];
        public bool Throwing { get; set; }
        public int Failures { get; private set; }

        public void Send(string node, Reply reply)
        {
            if (Throwing && reply.Channel == "first-reply")
            {
                Failures++;
                throw new IOException("caller disappeared");
            }
            if (node == "receiving-node") _sent.Add(reply);
        }

        public void Deliver()
        {
            foreach (var reply in _sent) requests.Receive(reply);
            _sent.Clear();
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
        public string? Published { get; set; }
        public List<string> Observations { get; set; } = [];
        public bool HeldOnce { get; set; }
        public int TerminateCalls { get; set; }
        public long? FiredAt { get; set; }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "typed-actor";
        public Contract.ActorRef? AttemptedChild { get; private set; }
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Inbox is [{ Kind: "null-unchanged" or "null-text" or "null-unset" } change])
            {
                if (change.Kind == "null-text") turn.State = "null";
                else if (change.Kind == "null-unset") turn.State = null;
                return default;
            }
            var state = turn.State is { } saved ? Json.Read<ProbeState>(saved) : new();
            if (turn.Inbox is [{ Kind: Contract.Kinds.Start, Payload: "unset" }]) return default;
            foreach (var message in turn.Inbox)
            {
                switch (message.Kind)
                {
                    case Contract.Kinds.Start when message.Payload == "configured-input":
                        state.Observations.Add($"start:{message.Payload}:{turn.Ambient ?? "none"}");
                        break;
                    case Contract.Kinds.Event when message.Event is { Key: "spawn", Wait: "starts" }:
                        state.Observations.Add($"event:{message.Payload}:{turn.Ambient ?? "none"}");
                        turn.Reply(message, "spawned", message.Payload);
                        if (message.Payload == "reply-end") turn.End(Contract.ActorStatus.Finished);
                        else turn.Emit("too-late", "second");
                        break;
                    case Contract.Kinds.Start when message.Payload == "hold-parent":
                        state.Child = turn.Start(new(Type, "child", "child")).Id;
                        break;
                    case Contract.Kinds.Terminate:
                        if (message.Payload == "hold" && state.TerminateCalls++ == 0) turn.Hold(message);
                        else turn.End(Contract.ActorStatus.Cancelled);
                        break;
                    case Contract.Kinds.Start when message.Payload == "wait":
                        turn.Subscribe("signal", "active");
                        break;
                    case "publish":
                        state.Published = turn.Publish("signal", message.Payload, TimeSpan.FromSeconds(10));
                        break;
                    case Contract.Kinds.Event when message.Event is { Key: "signal", Wait: "active" } delivered &&
                        message.Payload is "emit-first" or "reply-first" or "hold" or "refused":
                        state.Observations.Add($"{delivered.Id}:{turn.Ambient ?? "none"}:{turn.Inbox.Count}");
                        if (message.Payload == "emit-first")
                        {
                            turn.Emit("first-emit", "first");
                            turn.Reply(message, "second-reply", "second");
                        }
                        else if (message.Payload == "reply-first")
                        {
                            turn.Reply(message, "first-reply", "first");
                            turn.Emit("second-emit", "second");
                        }
                        else if (message.Payload == "refused")
                            turn.Reply(message, "must-not-release", "refused");
                        else if (!state.HeldOnce)
                        {
                            turn.Hold(message);
                            state.HeldOnce = true;
                        }
                        else
                            turn.Emit("held-answered", "released");
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
                            if (message.Payload == "fail") AttemptedChild = child;
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
                    case "reject-update":
                        state.Child = "must-rollback";
                        break;
                    case "late-update":
                        state.Count++;
                        break;
                    case "verify-null":
                        turn.SetHeader(turn.State is null ? "still-null" : "changed", turn.Header.Labels);
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
