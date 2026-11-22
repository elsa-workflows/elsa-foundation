using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkDeleteChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await Delete(protectedAtOpen: false, protectedAtEnd: false, check);
        await Delete(protectedAtOpen: true, protectedAtEnd: true, check);
        await Delete(protectedAtOpen: false, protectedAtEnd: true, check);
    }

    private static async Task Delete(bool protectedAtOpen, bool protectedAtEnd, Action<bool, string> check)
    {
        const long initial = 1_702_310_070_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now) { ParkedRetentionWindow = TimeSpan.FromMinutes(10) };
        var scope = new Contract.BoundScope("park-delete");
        var probe = new Probe();
        var requests = new Requests("park-delete");
        var edge = new InMemoryIngress(store, requests, "park-delete", (_, kind) => kind == Contract.Kinds.Delete);
        var commits = new List<TurnCommit>();
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed) commits.AddRange(batch.Turns);
            else if (batch.Turns.FirstOrDefault(turn => turn.Delete && turn.Row is { HeldCascade: not null }) is { } complete)
                CheckRetirementGuards(store, batch, complete, check);
        });
        Worker Owner(string name) => new(name, 0, wire, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - initial, Transport = new Replies(requests) });
        var worker = Owner("park-delete-first");
        await using var first = (await new DurableHost([worker]).ActivateAsync(0))!;
        var parent = (await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "root")), "parent", Contract.Kinds.Start)).Actor!.Value;
        await Step(worker, parent);
        var child = await Spawn(parent);
        var descendant = await Spawn(child);
        var id = Row(child).Id;

        await Send(child, "park", "park");
        await Step(worker, child);
        var reads = new ActorList(store, 10);
        var selected = (await reads.GetAsync(scope, child))!.Selection!;
        var previous = Checkpoint(child);
        var expiry = store.FoundationExpiry(previous.Row)!.Armed!.Value;
        const int count = 2 * InMemoryStore.ParkPageLimit + 1;
        for (var index = 0; index < count - 1; index++)
            await Send(child, $"wait-{index}", "pending");
        await Step(worker, child);

        probe.Protect = protectedAtOpen;
        var beforeOpen = Row(child);
        var payload = Json.Write(new Contract.OperatorCommand(selected));
        var request = await Send(child, "delete", Contract.Kinds.Delete, payload);
        var repeated = protectedAtOpen ? null : edge.RequestAsync(scope, new Contract.Target.Ref(child),
            "different-operation", Contract.Kinds.Delete, payload, null, new Contract.RequestOptions(null, now + 1_000)).AsTask();
        await Step(worker, child);
        if (protectedAtOpen)
        {
            check(request.Outcome == Contract.AdmissionOutcome.Accepted && Row(child) == beforeOpen &&
                Kept() == count && store.Messages.ContainsKey(expiry),
                "a protected parked instance refuses Delete at Open and keeps its Park, expiry and retained inputs");
            return;
        }

        var started = Row(child);
        var opened = started is { Status: InstanceStatus.Faulted, ParkDiscard.Kind: ParkClearKind.Delete, Children: not null } &&
            started.ParkResolutionGeneration == beforeOpen.ParkResolutionGeneration + 1 && Kept() == count &&
            Checkpoint(child).Terminal == previous.Terminal && !store.Messages.ContainsKey(expiry) && Notices() == 0;
        check(opened, "qualified Delete opens the drain without replacing the current Park mark or issuing its final notice");
        if (!opened) return;
        await Step(worker, child);
        var reply = await repeated!.WaitAsync(TimeSpan.FromSeconds(5));
        check(reply is { Error: "in progress", Item: "delete" } &&
              store.Instances[id].ParkDiscard is { Operation: "delete", Cleared: 0 },
            "another qualified Delete reports the operation already owning this drain");
        await Step(worker, child);
        check(Row(child).ParkDiscard?.Cleared == InMemoryStore.ParkPageLimit && Kept() == count - InMemoryStore.ParkPageLimit,
            "Delete clears one bounded page and leaves a durable cursor for its successor");
        await first.DisposeAsync();

        now++;
        var successor = Owner("park-delete-later");
        await using var current = (await new DurableHost([successor]).ActivateAsync(0))!;
        for (var pass = 0; pass < 15 && Row(child).ParkDiscard?.Cleared < count; pass++)
        {
            await Step(successor, parent);
            await Step(successor, child);
        }
        check(Row(child).ParkDiscard?.Cleared == count && Kept() == 0 && Notices() == 0,
            "the successor clears the suffix before the final disposition");
        probe.Protect = protectedAtEnd;
        now++;
        await Step(successor, child);
        await Step(successor, parent);
        var after = store.Instances[id];
        var journal = store.Journal.Values.Where(row => row.Instance == id).ToArray();
        var matched = Notices() == 1 && Kept() == 0 && Row(descendant).Status == InstanceStatus.Running &&
            (protectedAtEnd
                ? after is { Retired: false, Status: InstanceStatus.Faulted, ParkDiscarded: true } &&
                    Checkpoint(child) is { Park: null, Terminal: { } mark } && mark.Revision == after.Revision && mark.At == now &&
                    journal.Any(row => row.Entries.Contains("\"protected\"", StringComparison.Ordinal))
                : after is { Retired: true, HeldCascade: not null } && await reads.GetAsync(scope, child) is null &&
                    journal is [{ Revision: -1 } deleted] && deleted.Entries.Contains("\"deleted\"", StringComparison.Ordinal));
        var name = protectedAtEnd ? "protection at Complete keeps a new Faulted mark after clearing the Park and delivering a single notice" :
            "unprotected Complete retires the row with one deletion marker, one final notice and the descendant obligation";
        check(matched, name + (matched ? "" : $" (retired {after.Retired}, held cascade {after.HeldCascade?.Count}, " +
            $"notices {Notices()}, kept {Kept()}, journal {string.Join(", ", journal.Select(row => row.Revision))}, worker owns {successor.Owns})"));
        if (matched && protectedAtEnd)
        {
            var laterSelection = (await reads.GetAsync(scope, child))!.Selection!;
            var again = await Send(child, "later-delete", Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(laterSelection)));
            await Step(successor, child);
            check(again.Outcome == Contract.AdmissionOutcome.Accepted && laterSelection != selected &&
                  Row(child) == after && Notices() == 1 && Kept() == 0,
                "the new Faulted selection can name the row and protection still refuses a later Delete");
        }

        int Kept() => store.Messages.Values.Count(row => row.Message.Target.Id == id && row.KeptBy is not null);
        int Notices() => commits.SelectMany(turn => turn.Sent).Count(message => message.Sender == id && message.Kind == Kinds.ChildFinished &&
            message.Read<FoundationChildNotice>() is { Final: true, Status: InstanceStatus.Faulted });

        async Task<Contract.ActorRef> Spawn(Contract.ActorRef actor)
        {
            await Send(actor, "spawn", "spawn");
            await Step(worker, actor);
            var spawned = new Contract.ActorRef(Row(actor).State);
            await Step(worker, spawned);
            return spawned;
        }

        InMemoryStore.ActorCheckpoint Checkpoint(Contract.ActorRef actor) => store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!;
        InstanceRow Row(Contract.ActorRef actor) => Checkpoint(actor).Row;
        ValueTask<Contract.Admission> Send(Contract.ActorRef actor, string operation, string kind, string? payload = null) =>
            edge.SendAsync(scope, new Contract.Target.Ref(actor), operation, kind, payload);

        async Task Step(Worker owner, Contract.ActorRef actor)
        {
            await owner.PollAsync();
            await owner.LoadAsync();
            await owner.FlushAsync();
            owner.RunTurn(store.FoundationTarget(scope.Tenant, actor.Id)!.Id);
            await owner.FlushAsync();
        }
    }

    private static void CheckRetirementGuards(InMemoryStore store, CommitBatch batch, TurnCommit complete, Action<bool, string> check)
    {
        var before = store.Instances[complete.Instance];
        var control = before.ParkDiscard!.Control;
        var position = store.Partitions[batch.Partition].Position;
        var obligation = complete.Row!.HeldCascade!;
        var oldChild = obligation.Children[0];
        var wrongChild = obligation with { Children = obligation.Children.SetItem(0, oldChild with { Creation = oldChild.Creation + 1 }) };
        var changes = new (TurnCommit Turn, string Missing)[]
        {
            (complete with { Consumed = [], Done = [] }, "control"),
            (complete with { Sent = [] }, "notice"),
            (complete with { Row = complete.Row! with { HeldCascade = null, Parent = null, ParentCreation = 0 } }, "obligation"),
            (complete with { Row = complete.Row! with { HeldCascade = wrongChild } }, "exact child creation")
        };
        foreach (var change in changes)
        {
            var turns = batch.Turns.Select(turn => ReferenceEquals(turn, complete) ? change.Turn : turn).ToArray();
            var refused = false;
            try { _ = store.CommitAsync(batch with { Turns = turns }).GetAwaiter().GetResult(); }
            catch (StoreException error) when (error.Failure == Failure.Definite) { refused = true; }
            check(refused && store.Instances[complete.Instance] == before && store.Messages.ContainsKey(control) &&
                  store.Partitions[batch.Partition].Position == position,
                $"the store refuses a retirement without its {change.Missing} before writing anything");
        }
    }

    private sealed class Replies(Requests requests) : IReplyTransport
    {
        public void Send(string node, Reply reply) => requests.Receive(reply);
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "park-delete";
        public bool Protect { get; set; }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == "spawn") turn.State = turn.Start(new(Type, "child")).Id;
                if (message.Kind == "park") throw new InvalidOperationException("Park the child.");
            }
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => Protect;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
    }
}
