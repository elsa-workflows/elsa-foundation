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
        await TerminatePark(parentTerminate: false, check);
        await TerminatePark(parentTerminate: true, check);
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

    private static async Task TerminatePark(bool parentTerminate, Action<bool, string> check)
    {
        const long initial = 1_702_310_030_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now) { ParkedRetentionWindow = TimeSpan.FromMinutes(10) };
        var scope = new Contract.BoundScope("park-terminate");
        var probe = new Probe();
        var edge = new InMemoryIngress(store, new Requests("park-terminate"), "park-terminate", (_, kind) => kind == Contract.Kinds.Terminate);
        var writes = new List<TurnCommit>();
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed) writes.AddRange(batch.Turns);
        });
        Worker Owner(string node) => new(node, 0, wire, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - initial });
        var worker = Owner("park-terminate");
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var parent = (await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "parent")),
            "parent", Contract.Kinds.Start)).Actor!.Value;
        await Step(worker, parent);
        var child = await Spawn(parent);
        var descendant = await Spawn(child);
        var parentId = Row(parent).Id;
        var id = Row(child).Id;
        await Send(child, "park", "park");
        await Step(worker, child);
        var parked = Checkpoint(child);
        var expiry = store.FoundationExpiry(parked.Row)!.Armed!.Value;
        const int count = 2 * InMemoryStore.ParkPageLimit + 1;
        for (var index = 0; index < count - 1; index++)
            await Send(child, $"kept-{index}", "unexpected");
        await Step(worker, child);

        var reads = new ActorList(store, 10);
        var selection = (await reads.GetAsync(scope, child))!.Selection!;
        var payload = Json.Write(new Contract.OperatorCommand(selection));
        if (parentTerminate)
        {
            await Send(parent, "end", "cancel");
            for (var step = 0; step < 3 && Row(parent).Status == InstanceStatus.Running; step++)
                await Step(worker, parent);
        }
        else
            await Send(child, "operator", Contract.Kinds.Terminate, payload);
        await Step(worker, child);

        var open = Row(child);
        var opened = await reads.GetAsync(scope, child);
        var openedCorrectly = open is
            { Status: InstanceStatus.Faulted, ParkDiscard: { Kind: ParkClearKind.Terminate, Cursor: null, Cleared: 0 }, Children: null } &&
            opened?.Park?.OpenResolution == "terminating" && Checkpoint(child).Park == parked.Park &&
            store.MatchesFoundationSelection(scope.Tenant, child.Id, selection) &&
            Kept() == count && Notices() == 0 && !store.Messages.ContainsKey(expiry) &&
            writes.Count(turn => turn.Instance == id && turn.FoundationCascade is not null && turn.Unsubscribe) == 1;
        var source = parentTerminate ? "parent" : "operator";
        check(openedCorrectly, $"{source}: Terminate Open preserves the Park mark, recalls expiry and hands off the subtree without an end notice" +
            (openedCorrectly ? "" : $" (drain {open.ParkDiscard}, revision {open.Revision}, children {open.Children}, kept {Kept()}, notices {Notices()}, " +
                $"expiry still queued {store.Messages.ContainsKey(expiry)}, same Park {Checkpoint(child).Park == parked.Park})"));
        if (!openedCorrectly) return;

        await Send(child, "another", Contract.Kinds.Terminate, payload);
        await Send(child, "late", "unexpected");
        await Step(worker, child);
        var boundary = Row(child);
        check(boundary.ParkDiscard is { Cleared: InMemoryStore.ParkPageLimit } && Kept() == count - InMemoryStore.ParkPageLimit &&
            store.Messages.Values.Any(row => !row.Done && row.Message.Target.Id == id && row.Message.Id == "late"),
            $"{source}: the first page clears only its bounded kept prefix and withholds later arrivals");

        await owner.DisposeAsync();
        now += 20;
        var successor = Owner("park-terminate-successor");
        await using var next = (await new DurableHost([successor]).ActivateAsync(0))!;
        for (var step = 0; step < 12; step++)
        {
            await next.WokenAsync(null);
            await next.RunAsync();
        }

        var complete = Checkpoint(child);
        var ending = writes.Where(turn => turn.Instance == id && turn.Journal?.Contains("\"terminated\"", StringComparison.Ordinal) == true).ToArray();
        var completedCorrectly = complete is { Park: null, Row: { Status: InstanceStatus.Cancelled, ParkDiscard: null } } &&
            complete.EndedAt == now && complete.Row.State == parked.Row.State && Kept() == 0 &&
            Row(descendant).Status == InstanceStatus.Cancelled && Notices() == 1 &&
            ending is [{ Sent: [var notice] }] && notice.Read<System.Text.Json.JsonElement>().GetProperty("Status").GetInt32() == (int)InstanceStatus.Cancelled &&
            store.Messages.Values.All(row => row.Message.Target.Id != id || row.Message.Kind != Kinds.Drain);
        check(completedCorrectly, $"{source}: takeover completes once as Cancelled, clears every kept key and emits the sole Cancelled notice" +
            (completedCorrectly ? "" : $" (status {complete.Row.Status}, {complete.Row.ParkDiscard}, {Kept()} kept, " +
                $"descendant {Row(descendant).Status}, {Notices()} final notices, {ending.Length} terminal writes, " +
                $"pending {string.Join(", ", store.Messages.Values.Where(row => !row.Done && row.Message.Target.Id == id).Select(row => $"{row.Message.Kind}/{row.Message.Id}:{row.Seen}:{row.Trial}"))})"));
        var late = store.Messages.Values.SingleOrDefault(row => row.Message.Target.Id == id && row.Message.Id == "late");
        var deliveryCommit = late is null ? -1 : writes.FindIndex(turn => turn.Consumed.Concat(turn.Done).Contains(late.Message.InboxKey));
        check(probe.UnexpectedOffers == 0 && late is { Done: true } && ending is [var finalTurn] &&
            deliveryCommit > writes.IndexOf(finalTurn),
            $"{source}: after Complete the later input is consumed without any model turn");

        int Kept() => store.Messages.Values.Count(row => row.KeptBy is not null && row.Message.Target.Id == id);
        int Notices() => writes.SelectMany(turn => turn.Sent).Count(message =>
            message.Kind == Kinds.ChildFinished && message.Sender == id && message.Target.Id == parentId &&
            message.Read<System.Text.Json.JsonElement>().GetProperty("Final").GetBoolean());

        async Task<Contract.ActorRef> Spawn(Contract.ActorRef from)
        {
            await Send(from, "spawn-" + from.Id, "spawn");
            await Step(worker, from);
            var result = new Contract.ActorRef(Row(from).State);
            await Step(worker, result);
            return result;
        }

        ValueTask<Contract.Admission> Send(Contract.ActorRef actor, string operation, string kind, string? body = null) =>
            edge.SendAsync(scope, new Contract.Target.Ref(actor), operation, kind, body);
        InMemoryStore.ActorCheckpoint Checkpoint(Contract.ActorRef actor) => store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!;
        InstanceRow Row(Contract.ActorRef actor) => Checkpoint(actor).Row;

        async Task Step(Worker node, Contract.ActorRef actor)
        {
            await node.PollAsync();
            await node.LoadAsync();
            await node.FlushAsync();
            node.RunTurn(store.FoundationTarget(scope.Tenant, actor.Id)!.Id);
            await node.FlushAsync();
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "park-cascade";
        public int UnexpectedOffers { get; private set; }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
                switch (message.Kind)
                {
                    case "spawn": turn.State = turn.Start(new(Type, "child")).Id; break;
                    case "park": throw new InvalidOperationException("The fixture parks the parent.");
                    case "unexpected": UnexpectedOffers++; break;
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
