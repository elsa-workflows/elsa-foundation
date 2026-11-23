using System.Globalization;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class SequentialCascadeChecks
{
    private const int Count = 10_001;

    internal static async Task Run(Action<bool, string> check)
    {
        const long now = 1_704_070_000_000;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("sequential-cascade");
        var edge = new InMemoryIngress(store, new Requests("cascade-tree"), "cascade-tree", (_, kind) => kind == Contract.Kinds.Terminate);
        var model = new Tree();
        var rootRef = await edge.SendAsync(scope, new Contract.Target.Start(new(model.Type, "parent", History: Contract.History.Minimal)),
            "root", Contract.Kinds.Start);
        var root = rootRef.Actor!.Value;
        var id = store.FoundationTarget(scope.Tenant, root.Id)!.Id;
        var starts = 0;
        var oneStartPerTurn = true;
        var targets = new HashSet<CreationAddress>();
        var notices = new Dictionary<InboxKey, Message>();
        var settled = new HashSet<CreationAddress>();
        var pages = 0;
        var issuedStops = 0;
        var pagesInBounds = true;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed) return;
            foreach (var turn in batch.Turns)
            {
                if (turn.Instance == id)
                {
                    var births = turn.Sent.Count(m => m.Start is not null);
                    if (births > 0) { starts += births; oneStartPerTurn &= births == 1; }
                    if (turn.FoundationCascade is not null)
                    {
                        pages++;
                        var issued = turn.Sent.Where(m => m.CascadeOrigin).ToArray();
                        issuedStops += issued.Length;
                        pagesInBounds &= issued.Length is > 0 and <= CascadeProtocol.PageLimit;
                        foreach (var stop in issued) targets.Add(new(stop.Target.Id, stop.TargetCreation!.Value));
                    }
                    foreach (var key in turn.Consumed.Concat(turn.Done))
                        if (notices.TryGetValue(key, out var child) && turn.Row is null)
                            settled.Add(new(child.Sender!, child.SenderCreation));
                }
                else
                    foreach (var message in turn.Sent)
                        if (message.Kind == Kinds.ChildFinished && message.Target.Id == id && message.Sender is not null)
                            notices.Add(message.InboxKey, message);
            }
        });
        var adapter = new ActorAdapter(store, model);
        var worker = new Worker("cascade-tree", 0, wire, adapter,
            new WorkerOptions { Monotonic = () => 0 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        for (var i = 0; i < Count * 3 && model.Waiting < Count; i++) await Pass();
        var parent = store.Instances[id];
        var children = ChildRegistry.Read(parent.Children).ToHashSet();
        var waiting = store.Messages.Values.Where(r => !r.Done && r.Message.Kind == "wait-timer" && r.Message.Due > now)
            .Select(r => new CreationAddress(r.Message.Target.Id, r.Message.TargetCreation!.Value)).ToHashSet();
        var bound = starts == Count && oneStartPerTurn && waiting.SetEquals(children) && children.Count == Count &&
            children.All(child => store.Instances.GetValueOrDefault(child.Id) is
                { Status: InstanceStatus.Running, Creation: var generation, Parent: var p, ParentCreation: var pc } &&
                p == id && pc == parent.Creation && generation == child.Creation);
        check(bound, $"PC-4 the model creates one bound child per turn: {starts} starts, {children.Count} registered, {waiting.Count} waiting on timers");
        if (!bound) return;
        var selection = (await new ActorList(store, 1).GetAsync(scope, root))!.Selection;
        var accepted = await edge.SendAsync(scope, new Contract.Target.Ref(root), "end", Contract.Kinds.Terminate,
            Json.Write(new Contract.OperatorCommand(selection)));
        var idlePasses = 0;
        for (var i = 0; i < Count * 2 && settled.Count < Count && idlePasses < 32; i++)
        {
            var progress = (pages, notices.Count, settled.Count);
            await Pass();
            idlePasses = progress == (pages, notices.Count, settled.Count) ? idlePasses + 1 : 0;
        }
        var end = store.Instances[id];
        check(accepted.Outcome == Contract.AdmissionOutcome.Accepted && end is { Status: InstanceStatus.Cancelled, Cascade: null, Children: null } &&
            pages == (Count + CascadeProtocol.PageLimit - 1) / CascadeProtocol.PageLimit && pagesInBounds && issuedStops == Count && targets.SetEquals(children) &&
            children.All(child => store.Instances[child.Id].Status == InstanceStatus.Cancelled),
            $"PC-4 {pages} bounded pages issue {issuedStops} stops to {targets.Count} children, parent {end.Status}, {children.Count(child => store.Instances[child.Id].Status == InstanceStatus.Cancelled)} cancelled");
        check(settled.SetEquals(children) && notices.Count == Count && model.ParentNotices == 0,
            $"PC-4 the ended parent settles {settled.Count} of {notices.Count} final notices without re-entering the model or restoring a child");

        // Final notices normally carry no reply route. Inspect the real dispatch result with an
        // observation route after the same envelopes have settled against the deregistered parent.
        var slot = new Slot { Row = end, Loaded = true };
        var refused = notices.Values.Count(message => Refusal(message) == "UnknownChildCreation");
        check(refused == Count, $"PC-4 all {refused} genuine late notices receive UnknownChildCreation");
        var sample = notices.Values.FirstOrDefault();
        check(sample is not null && Refusal(sample with { Payload = null }) == "InvalidChildCompletion",
            "PC-4 an incomplete child notice retains its distinct refusal");

        string? Refusal(Message message)
        {
            var delivery = new Delivery(message, 0) { RequestAddress = "observe-result" };
            var outcome = adapter.Command(new TurnInput(worker, slot, id, [delivery], 0, null, now));
            return outcome is { CreationRefused: true, Replied: [{ Item: CreationResult result }] } ? result.Result : null;
        }

        async Task Pass()
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
    }

    private sealed class Tree : Contract.IActorType
    {
        public string Type => "sequential-tree";
        public int Waiting { get; private set; }
        public int ParentNotices { get; private set; }
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancel) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancel)
        {
            foreach (var item in turn.Inbox)
            {
                if (item.Kind == Contract.Kinds.Terminate)
                {
                    turn.End(Contract.ActorStatus.Cancelled);
                    continue;
                }
                if (turn.Header.TypeData == "leaf")
                {
                    if (item.Kind == Contract.Kinds.Start)
                    {
                        turn.Send(turn.Header.Ref, "wait-timer", dueAt: turn.Now + 60_000);
                        Waiting++;
                    }
                    continue;
                }
                if (item.Kind == Contract.Kinds.ChildFinished) { ParentNotices++; continue; }
                if (item.Kind is not (Contract.Kinds.Start or "next-child")) continue;
                var n = item.Kind == Contract.Kinds.Start ? 1 : int.Parse(turn.State!, CultureInfo.InvariantCulture) + 1;
                turn.Start(new(Type, "leaf", History: Contract.History.Minimal));
                turn.State = n.ToString(CultureInfo.InvariantCulture);
                if (n < Count) turn.Send(turn.Header.Ref, "next-child");
            }
            return default;
        }
    }
}
