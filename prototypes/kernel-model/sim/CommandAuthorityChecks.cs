using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

/// <summary>FC-C3 commits on the built-in store with read-back of every relevant table.</summary>
internal static class CommandAuthorityChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var store = new InMemoryStore(2, () => 100);
        var parent = InstanceRef.In(0, "parent");
        var child = InstanceRef.In(1, "child");
        var foreign = InstanceRef.In(1, "foreign");
        var leaseFor = TimeSpan.FromSeconds(20);
        var lease = (await store.TakeAsync(0, "parent-owner", leaseFor))!;
        var row = new InstanceRow(parent.Id, "probe", "definition", InstanceStatus.Running, "{}", 1, null,
            Children: ChildRegistry.Write([new(child.Id, 7)]));
        var birth = new TurnCommit(parent.Id, row, null, [], [], [], [], [], "[]");
        check((await store.CommitAsync(new CommitBatch(0, lease.Epoch, 0, leaseFor, false, [birth], [], []))).Landed,
            "the owner's commit records an exact child in the parent's registry");

        var inputs = new[] { ("suspend", Kinds.Suspend, child, 7L), ("foreign", Kinds.Terminate, foreign, 7L),
            ("stale", Kinds.Terminate, child, 6L), ("private", "kernel.cascade", child, 7L),
            ("migrate", "kernel.migrate", child, 7L) };

        var cases = inputs.Select(input => (input.Item1, input.Item2, input.Item3, input.Item4, (string?)null)).Append(
            ("then-delete", Kinds.Terminate, child, 7L, """{"thenDelete":true}"""));
        foreach (var (id, kind, target, creation, payload) in cases)
        {
            var source = new Message(id, parent, "trigger");
            await store.AddAsync(source);
            var next = row with { Revision = 2, State = "{\"changed\":true}" };
            var authored = new Message("authored-" + id, target, kind, payload, Sender: parent.Id, TargetCreation: creation) { ActorAuthored = true };
            var batch = new CommitBatch(0, lease.Epoch, 1, leaseFor, true,
                [new TurnCommit(parent.Id, next, 1, [source.InboxKey], [], [authored], [new("key", parent.Id, id)], [], "[2]")], [], []);
            var refused = false;
            try { await store.CommitAsync(batch); }
            catch (StoreException exception) when (exception.Failure == Failure.Definite) { refused = true; }
            check(refused && store.Instances[parent.Id] == row && store.Messages.ContainsKey(source.InboxKey) &&
                !store.Messages.ContainsKey(authored.InboxKey) && store.Journal.Count == 1 && store.Subscriptions.Count == 0 &&
                store.Partitions[0].Position == 1,
                $"the commit refuses actor-authored {id} and rolls back every effect");
        }

        var terminate = new Message("child-terminate", child, Kinds.Terminate, Sender: parent.Id, TargetCreation: 7) { ActorAuthored = true };
        var valid = new TurnCommit(parent.Id, row with { Revision = 2 }, 1, [], [], [terminate], [], [], "[]");
        check((await store.CommitAsync(new CommitBatch(0, lease.Epoch, 1, leaseFor, false, [valid], [], []))).Landed &&
            store.Messages.GetValueOrDefault(terminate.InboxKey)?.Message.TargetCreation == 7,
            "the parent can send Terminate to the exact registered child without looking up the child row");
    }

    public static async Task RunActor(Action<bool, string> check)
    {
        const long now = 1_701_400_000_000;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        var actor = new Probe();
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests("command-authority"), "command-authority");
        var worker = new Worker("command-authority", 0, store, new ActorAdapter(store, actor), new WorkerOptions { Monotonic = () => 0 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var parent = (await ingress.SendAsync(scope, new Contract.Target.Start(new(actor.Type, "parent")),
            "parent", Contract.Kinds.Start)).Actor!.Value;
        await Pump();

        var children = Json.Read<Children>(Row(parent).State);
        await ingress.SendAsync(scope, new Contract.Target.Ref(children.Ended), "finish", "finish");
        await Pump();
        var ended = Row(children.Ended);
        var history = History(ended.Id);
        check(ended.Status == InstanceStatus.Finished && Row(children.Running).Status == InstanceStatus.Running &&
            ChildRegistry.Read(Row(parent).Children).Contains(new(ended.Id, ended.Creation)),
            "FC-C3 has a finished child whose final notice is still held by its parent");

        await ingress.SendAsync(scope, new Contract.Target.Ref(parent), "cancel-children", "cancel-children");
        await Pump();
        var outputs = Json.Read<string[]>(Row(parent).State);
        check(outputs.Length == 2 && outputs.All(id => !store.Messages.Values.Any(row => row.Message.Id == id)) &&
            Row(children.Running).Status == InstanceStatus.Cancelled,
            "the parent's two registered-child commands commit and the running child is cancelled");
        check(Row(children.Ended) == ended && History(ended.Id).SequenceEqual(history) && actor.EndedCalls == 0 &&
            !store.Messages.Values.Any(row => row.Message.Kind == "unexpected-termination"),
            "an actor-authored terminate to a finished child is consumed without a turn, state change or new effects");

        InstanceRow Row(Contract.ActorRef reference) => store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row;
        JournalRow[] History(string id) => store.Journal.Values.Where(row => row.Instance == id).OrderBy(row => row.Position).ToArray();
        async Task Pump()
        {
            for (var pass = 0; pass < 20; pass++)
            {
                await owner.WokenAsync(null);
                await owner.RunAsync();
                await Task.Yield();
            }
        }
    }

    private sealed record Children(Contract.ActorRef Ended, Contract.ActorRef Running);

    internal static async Task RunCascade(Action<bool, string> check)
    {
        const long now = 1_701_400_010_000;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("cascade-origin");
        var actor = new Probe();
        var emitted = new List<Message>();
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed) emitted.AddRange(batch.Turns.SelectMany(turn => turn.Sent));
        });
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests("cascade"), "cascade");
        var worker = new Worker("cascade", 0, wire, new ActorAdapter(store, actor), new WorkerOptions { Monotonic = () => 0 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var parentRef = await Start("parent");
        var foreignRef = await Start("foreign");
        await Pump();
        var parent = Row(parentRef);
        var foreign = Row(foreignRef);
        var children = Json.Read<Children>(parent.State);
        var child = Row(children.Running);
        var target = new InstanceRef(child.Id);
        var wrongIssuer = new Message("wrong-parent", target, Kinds.Terminate, Sender: foreign.Id,
            TargetCreation: child.Creation, SenderCreation: foreign.Creation).AsIssued().AsCascadeTerminate();
        var invalidSend = wrongIssuer with { Id = "copied-mark" };
        var wrongGeneration = new Message("wrong-issuer-generation", target, Kinds.Terminate, Sender: foreign.Id,
            TargetCreation: child.Creation, SenderCreation: foreign.Creation + 1).AsIssued().AsCascadeTerminate();
        var fence = worker.Fences.Single();
        foreach (var malformed in new[] { invalidSend, wrongGeneration })
        {
            var refused = false;
            try { await SendRaw(malformed); }
            catch (StoreException error) when (error.Failure == Failure.Definite) { refused = true; }
            check(refused && !store.Messages.ContainsKey(malformed.InboxKey),
                "the store refuses a cascade without its original issuer or exact sender creation");
        }
        check((await SendRaw(wrongIssuer)).Landed, "a valid kernel envelope can reach a child whose recorded parent differs");
        await Pump();
        check(Row(children.Running) == child && !store.Messages.ContainsKey(wrongIssuer.InboxKey),
            "the real Worker consumes a cascade from an unrelated creation without entering or ending the child");

        var stale = new Message("stale-parent", target, Kinds.Terminate, Sender: parent.Id,
            TargetCreation: child.Creation, SenderCreation: parent.Creation - 1).AsIssued().AsCascadeTerminate();
        // An older issuer's durable send is already present when the child owner reads the inbox.
        store.Messages.Add(stale.InboxKey, new(stale, long.MaxValue - 1, 0));
        await Pump();
        check(Row(children.Running) == child && !store.Messages.ContainsKey(stale.InboxKey),
            "a matching parent address with a stale creation cannot terminate the child");

        var second = Row(children.Ended);
        var outside = new Message("outside", new(second.Id), Kinds.Terminate, Sender: foreign.Id,
            TargetCreation: second.Creation, SenderCreation: foreign.Creation).AsIssued().AsCascadeTerminate();
        await store.AddAsync(outside);
        check(store.Messages[outside.AsOrdinary().InboxKey].Message is { CascadeOrigin: false, InboxScope: 0 },
            "ordinary ingress resets a cascade mark and its issued scope");
        await Pump();
        check(Row(children.Ended).Status == InstanceStatus.Cancelled, "the ordinary terminate keeps its existing authority");
        var settledChild = Row(children.Ended);

        await ingress.SendAsync(scope, new Contract.Target.Ref(parentRef), "cancel-tree", "cancel-tree");
        await Pump();
        check(Row(children.Running).Status == InstanceStatus.Cancelled && emitted.Any(message => message is
            { Kind: Kinds.Terminate, CascadeOrigin: true } && message.Target == target && message.Sender == parent.Id &&
            message.SenderCreation == parent.Creation && message.TargetCreation == child.Creation),
            "the real terminal turn marks its descendant send and the recorded child obeys it");
        check(Row(children.Ended) == settledChild && actor.EndedCalls == 0 &&
            emitted.All(message => message.Kind != "unexpected-termination"),
            "a marked cascade to an already ended leaf settles without re-entering the model");

        ValueTask<Committed> SendRaw(Message message) => store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position,
            Defaults.Lease, false, [new(foreign.Id, null, null, [], [], [message], [], [], null)], [], []));
        InstanceRow Row(Contract.ActorRef reference) => store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row;
        async Task<Contract.ActorRef> Start(string typeData) => (await ingress.SendAsync(scope,
            new Contract.Target.Start(new(actor.Type, typeData)), typeData, Contract.Kinds.Start)).Actor!.Value;
        async Task Pump()
        {
            for (var i = 0; i < 20; i++)
            {
                await owner.WokenAsync(null);
                await owner.RunAsync();
            }
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "command-authority";
        public int EndedCalls { get; private set; }
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
                switch (message.Kind)
                {
                    case Contract.Kinds.Start when turn.Header.TypeData == "parent":
                        turn.State = Json.Write(new Children(turn.Start(new(Type, "child")), turn.Start(new(Type, "child"))));
                        break;
                    case Contract.Kinds.ChildFinished:
                        turn.Hold(message);
                        break;
                    case "finish":
                        turn.End(Contract.ActorStatus.Finished);
                        break;
                    case "cancel-tree":
                        turn.End(Contract.ActorStatus.Cancelled);
                        break;
                    case "cancel-children":
                        var children = Json.Read<Children>(turn.State!);
                        turn.State = Json.Write(new[] { turn.Send(children.Ended, Contract.Kinds.Terminate),
                            turn.Send(children.Running, Contract.Kinds.Terminate) });
                        break;
                    case Contract.Kinds.Terminate when turn.Header.Status == Contract.ActorStatus.Running:
                        turn.End(Contract.ActorStatus.Cancelled);
                        break;
                    case Contract.Kinds.Terminate:
                        EndedCalls++;
                        turn.State = Json.Write("unexpected termination");
                        turn.Record("unexpected termination");
                        turn.Send(turn.Header.Ref, "unexpected-termination", dueAt: turn.Now + 1);
                        break;
                }
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
