namespace Kernel.Native.Run;

internal static class ParkR11Checks
{
    private sealed class Probe(InstanceStatus childStatus = InstanceStatus.Finished) : Kernel.Sim.Model<List<string>>
    {
        public const string ModelKey = "r11-deferred";
        public override string Key => ModelKey;
        public int DeferredCalls, Delivered;
        public readonly List<string[]> Batches = [];
        protected override void Turn(ITurn turn, List<string> state)
        {
            if (turn.Instance.Definition == "child")
            {
                if (turn.Inbox.Any(m => m.Kind == "finish"))
                {
                    if (childStatus == InstanceStatus.Faulted) throw new InvalidOperationException("real child fault");
                    turn.SetStatus(childStatus);
                }
                return;
            }
            Batches.Add(turn.Inbox.Select(m => m.Kind).ToArray());
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Kinds.Start) turn.Start(new(Key, "child"));
                if (message.Kind == "deferred")
                {
                    DeferredCalls++;
                    if (!state.Contains("child-noticed")) turn.Hold(message);
                    else { state.Add("delivered"); Delivered++; }
                }
                if (message.Kind == WorkflowKinds.ChildFinished) state.Add("child-noticed");
            }
        }
    }
    // This application actor owns its ordinary wire vocabulary, including a child-finished-shaped kind.
    // It is not a workflow and must not acquire the workflow fallback's committed wake semantics.
    private sealed class Plain : IActorType
    {
        public int Calls;
        public bool Withholds(InstanceRow row, ITurnServices services) => false;
        public TurnOutcome? Command(TurnInput input) => null;
        public ValueTask<TurnOutcome> TurnAsync(TurnInput input)
        {
            var turn = new DurableTurn(input);
            var state = input.Row is { } row ? Json.Read<List<string>>(row.State) : [];
            var release = turn.Inbox.Any(m => m.Kind == "release");
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == "deferred") { Calls++; if (!release) turn.Hold(message); else state.Add("delivered"); }
                if (message.Kind == WorkflowKinds.ChildFinished) state.Add("plain-kind");
                if (message.Kind == "release") state.Add("released");
            }
            var next = new InstanceRow(input.Actor, "plain", "plain", InstanceStatus.Running, Json.Write(state), turn.Revision, null);
            return ValueTask.FromResult(turn.Seal(turn.Outcome(next.Status), next, state, "[]"));
        }
        public TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error) =>
            throw new InvalidOperationException("unexpected plain actor Park: " + error);
        public ValueTask<object?> ExecuteJobAsync(string model, IJob job, CancellationToken token) => throw new NotSupportedException();
    }
    private static void Require(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
    private static IActorType Route(IActorType workflow) => new ActorTypeRouter(new Plain(), new Dictionary<string, IActorType> { [Probe.ModelKey] = workflow });
    public static IEnumerable<(string Label, Func<Task> Run)> Cases()
    {
        foreach (var qualified in new[] { false, true })
        foreach (var routed in new[] { false, true })
        foreach (var status in new[] { InstanceStatus.Finished, InstanceStatus.Faulted, InstanceStatus.Cancelled })
        foreach (var count in new[] { 1, 2 })
            yield return ($"R11 committed child wakes deferred {qualified}/{routed}/{status}/{count}",
                () => Run(qualified, status, count, routed ? Route : null));
        foreach (var qualified in new[] { false, true })
        {
            yield return ($"R11 refused notice checkpoint {qualified}", () => Run(qualified, InstanceStatus.Finished, 1, null, refused: true));
            yield return ($"R11 lost owner before notice commit {qualified}", () => Run(qualified, InstanceStatus.Finished, 1, null, loseOwner: true));
        }
        yield return ("R11 other model has no workflow wake", OtherModel);
    }
    public static Task ThroughAsync(bool qualified, InstanceStatus status, int count, Func<IActorType, IActorType> wrap) => Run(qualified, status, count, wrap);
    private static async Task Run(bool qualified, InstanceStatus status, int count, Func<IActorType, IActorType>? wrap,
        bool refused = false, bool loseOwner = false)
    {
        const string id = "0/r11-parent";
        long time = 100;
        var store = new InMemoryStore(1, () => time);
        var model = new Probe(status);
        var actor = new WorkflowInstance([model]);
        await using var owner = new Worker("first", 0, store, wrap is null ? actor : wrap(actor), new() { Monotonic = () => time });
        await new DurableHost([owner]).ActivateAsync(0);
        async Task Pulse(Worker worker) { await worker.WokenAsync(null); await worker.RunAsync(); }
        async Task Until(Worker worker, Func<bool> done)
        {
            for (var i = 0; i < 80 && worker.Owns && !done(); i++) await Pulse(worker);
            Require(done(), "deferred-before-child sequence did not settle");
        }
        await store.AddAsync(new("start", new(id), Kinds.Start, Start: new(model.Key, "parent", Creation: qualified ? new(1, 0, "r11") : null)));
        await Until(owner, () => store.Instances.Count == 2);
        var child = store.Instances.Values.Single(r => r.Definition == "child");
        for (var i = 0; i < count; i++) await store.AddAsync(new("deferred-" + i, new(id), "deferred", TargetCreation: qualified ? 1 : null));
        await Until(owner, () => model.DeferredCalls == count);
        for (var i = 0; i < 3; i++) await Pulse(owner);
        Require(model.DeferredCalls == count && store.Instances[id].State == "[]", "held input spun or observed uncommitted child state");
        if (refused) store.Refuses = row => row.Id == id && row.Status == InstanceStatus.Running && row.State.Contains("child-noticed");
        await store.AddAsync(new("finish", new(child.Id), "finish", TargetCreation: qualified ? 1 : null));
        if (loseOwner)
        {
            await Until(owner, () => store.Instances[child.Id].Status != InstanceStatus.Running);
            await owner.PollAsync(); await owner.LoadAsync(); await owner.FlushAsync();
            owner.RunTurn(id);
            Require(owner.Queued > 0 && store.Instances[id].State == "[]", "notice checkpoint was not actually pending");
            time += 11_000;
            var nextModel = new Probe(status);
            await using var next = new Worker("replacement", 0, store, new WorkflowInstance([nextModel]), new() { Monotonic = () => time });
            await new DurableHost([next]).ActivateAsync(0);
            await owner.FlushAsync();
            Require(!owner.Owns && store.Instances[id].State == "[]" && model.Delivered == 0, "a lost owner applied or woke its uncommitted notice");
            await Until(next, () => store.Messages.Count == 0);
            Require(next.Owns && nextModel.Delivered == count && store.Ghost.Count == 0, "replacement did not replay the committed-state continuation");
            return;
        }
        for (var i = 0; i < 30; i++) await Pulse(owner);
        time += 4_000; await owner.BeatAsync();
        if (refused)
        {
            Require(store.Instances[id].Status == InstanceStatus.Faulted && store.Instances[id].State == "[]" && model.Delivered == 0,
                "a refused notice checkpoint activated authored deferred input");
        }
        else
        {
            Require(store.Instances[id].Status == InstanceStatus.Running && model.Delivered == count &&
                Json.Read<List<string>>(store.Instances[id].State).SequenceEqual(new[] { "child-noticed" }.Concat(Enumerable.Repeat("delivered", count))),
                "a committed child notice did not revisit earlier held input");
            if (!qualified) Require(model.Batches.Any(b => b.Contains("deferred") && b.Contains(WorkflowKinds.ChildFinished)),
                "legacy batching was replaced by singleton selection");
            else Require(model.Batches.Where(b => b.Contains(WorkflowKinds.ChildFinished)).All(b => b.Length == 1),
                "qualified child selection lost isolation");
        }
        Require(owner.Owns && store.Messages.Count == 0 && store.Ghost.Count == 0 && !owner.Ready.Any() && !owner.Trial.Any(),
            "deferred closure left work stranded or lost a healthy fence");
    }
    private static async Task OtherModel()
    {
        var store = new InMemoryStore(1, () => 100);
        var plain = new Plain();
        var router = new ActorTypeRouter(new WorkflowInstance([new Probe()]), new Dictionary<string, IActorType> { ["plain"] = plain });
        await using var owner = new Worker("plain", 0, store, router, new() { Monotonic = () => 100 });
        await new DurableHost([owner]).ActivateAsync(0);
        async Task Send(string kind)
        {
            await store.AddAsync(new(kind, new("0/plain"), kind, Start: kind == Kinds.Start ? new("plain", "plain") : null));
            for (var i = 0; i < 12; i++) { await owner.WokenAsync(null); await owner.RunAsync(); }
        }
        await Send(Kinds.Start); await Send("deferred"); await Send(WorkflowKinds.ChildFinished);
        Require(plain.Calls == 2 && store.Messages.Count == 1, "workflow fallback woke another model's held input");
        await Send("release");
        Require(owner.Owns && store.Messages.Count == 0 && store.Ghost.Count == 0 && store.Instances["0/plain"].State.Contains("delivered"),
            "ordinary application release was lost");
    }
}
