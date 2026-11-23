namespace Kernel.Native.Run;

internal static class ParkR15IsolationChecks
{
    // Existing layer-2 Command policy consumes an ordinary input without ending its actor.
    // This exercises fence resumption, not a change to WorkflowInstance's default Park policy.
    private sealed class Plain : IActorType
    {
        public int Entries;
        public TaskCompletionSource<object?>? Pending;
        public bool Withholds(InstanceRow row, ITurnServices services) => false;
        public TurnOutcome? Command(TurnInput input) => input.Taken[0].Message.Kind == "timer" ? TurnAsync(input).Result : null;
        public ValueTask<TurnOutcome> TurnAsync(TurnInput input)
        {
            var turn = new DurableTurn(input); var state = input.Row is { } row ? Json.Read<List<string>>(row.State) : [];
            foreach (var m in turn.Inbox) { if (m.Kind == "spawn") turn.StartJob("work"); else state.Add(m.Kind); }
            var next = new InstanceRow(input.Actor, "plain", "definition", InstanceStatus.Running, Json.Write(state), turn.Revision, null, Jobs: turn.Jobs());
            return ValueTask.FromResult(turn.Seal(turn.Outcome(next.Status), next, state, "[]"));
        }
        public TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error) => throw new InvalidOperationException("Command owns this input: " + error);
        public ValueTask<object?> ExecuteJobAsync(string model, IJob job, CancellationToken token)
        {
            var pending = new TaskCompletionSource<object?>(); if (++Entries > 1) Pending = pending;
            token.Register(() => pending.TrySetCanceled(token)); return new(pending.Task);
        }
    }
    private static void Require(bool ok, string why) => ParkR15Checks.Require(ok, why);
    public static IEnumerable<(string Label, Func<Task> Run)> Cases(Func<IActorType, IActorType>? wrap = null)
    {
        foreach (var q in new[] { false, true })
        {
            foreach (var routed in new[] { false, true }) foreach (var callbackFirst in new[] { false, true })
                yield return ($"R15 reacquire fence without IO {q}/{routed}/{callbackFirst}", () => Reacquire(q, routed, callbackFirst, wrap));
            yield return ($"R15 mixed entered/deferred cohort {q}", () => Cohort(q, wrap));
        }
    }
    private static async Task Reacquire(bool q, bool routed, bool callbackFirst, Func<IActorType, IActorType>? wrap)
    {
        const string id = "0/canonical"; long time = 100; var creation = q ? 1 : 0;
        var store = new InMemoryStore(1, () => time); var wire = new ParkR15Checks.AliasStore(store); var actor = new Plain(); var queue = new ParkR15Checks.Queue();
        IActorType Type() { IActorType a = routed ? new ActorTypeRouter(new WorkflowInstance([]), new Dictionary<string, IActorType> { ["plain"] = actor }) : actor; return wrap is null ? a : wrap(a); }
        async Task<Worker> Take(string name)
        {
            var w = new Worker(name, 0, wire, Type(), new() { Monotonic = () => time, Horizon = TimeSpan.FromSeconds(40), MaxAttempts = 1, TrialGroup = 1, JobScheduler = name == "c" ? queue : null });
            await new DurableHost([w]).ActivateAsync(0); return w;
        }
        async Task Drive(Worker w, int n) { for (var i = 0; i < n; i++) { await w.WokenAsync(null); await w.RunAsync(); } }
        await using var a = await Take("a");
        await store.AddAsync(new("start", new(id), Kinds.Start, Start: new("plain", "definition", Creation: q ? new(1, 0, "plain") : null))); await Drive(a, 5);
        await store.AddAsync(new("timer", new(id.ToUpperInvariant()), "timer", Due: 5_000, TargetCreation: q ? 1 : null)); await Drive(a, 2);
        Require(a.Post(new("spawn", new(id), "spawn", TargetCreation: q ? 1 : null)), "spawn failed"); await Drive(a, 4);
        time = 12_000; await using var b = await Take("b"); await Drive(b, 1);
        Require(store.Messages["timer"].Trial == b.Epoch, "no genuine timer trial");
        time = 24_000; await using var c = await Take("c");
        Require(c.Post(new("future", new(id), "future", Due: 60_000, TargetCreation: q ? 1 : null)), "preload failed");
        await c.LoadAsync(); await c.PollAsync(); await c.FlushAsync(); Require(queue.Pending.Count == 1, "no queued canonical trial");
        await c.LoadAsync(); var blocked = c.JobCompletion(id, "work", creation)!;
        if (callbackFirst) { queue.Drain(); await blocked; }
        Require(c.Post(new("bystander", new("0/other"), Kinds.Start, Start: new("plain", "definition", Creation: q ? new(1, 0, "other") : null))), "bystander failed");
        _ = c.Ready.ToArray(); Require(!c.Trial.Any() && actor.Entries == 1, "never-entered trial kept runtime isolation");
        var writes = wire.TrialWrites; var trial = store.Messages[InboxKey.Ticket(id, "work", creation)].Trial;
        c.RunTurn(id); await c.FlushAsync();
        if (!callbackFirst)
        {
            queue.Drain(); Require(actor.Entries == 1, "old queued callback entered after poison cleared without reacquiring isolation");
            await blocked; _ = c.Ready.ToArray();
        }
        queue.Drain();
        Require(actor.Entries == 2 && actor.Pending is not null && c.Trial.Any() && wire.TrialWrites == writes &&
            store.Messages[InboxKey.Ticket(id, "work", creation)].Trial == trial, "resumption lost isolation or rewrote durable trial authority");
        await Drive(c, 8); Require(!store.Instances.ContainsKey("0/other"), "bystander crossed the resumed genuine trial");
        var driver = c.JobCompletion(id, "work", creation)!; actor.Pending!.SetResult("done"); await driver; await Drive(c, 30); await c.BeatAsync();
        Require(c.Owns && !c.Trial.Any() && store.Messages.Count == 0 && store.Instances[id].Jobs is null &&
            store.Instances.ContainsKey("0/other") && store.Ghost.Count == 0 && actor.Entries == 2, "resumed trial/result/registry/bystander failed to close");
    }
    private sealed class CohortProbe : Kernel.Sim.Model<List<string>>
    {
        public override string Key => "cohort";
        public readonly Dictionary<string, int> Entries = [];
        public TaskCompletionSource<object?>? Pending;
        protected override void Turn(ITurn turn, List<string> state) { foreach (var m in turn.Inbox) { if (m.Kind == "spawn") turn.StartJob("work"); else state.Add(m.Kind); } }
        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken token)
        {
            Entries[job.Instance.Id] = Entries.GetValueOrDefault(job.Instance.Id) + 1;
            var pending = new TaskCompletionSource<object?>(); if (job.Instance.Id == "0/healthy" && Entries[job.Instance.Id] == 2) Pending = pending;
            token.Register(() => pending.TrySetCanceled(token)); return new(pending.Task);
        }
    }
    private static async Task Cohort(bool q, Func<IActorType, IActorType>? wrap)
    {
        const string id = "0/canonical", healthy = "0/healthy"; long time = 100; var creation = q ? 1 : 0;
        var store = new InMemoryStore(1, () => time); var wire = new ParkR15Checks.AliasStore(store); var model = new CohortProbe(); var queue = new ParkR15Checks.Queue();
        async Task<Worker> Take(string name)
        {
            IActorType actor = new WorkflowInstance([model]);
            var w = new Worker(name, 0, wire, wrap is null ? actor : wrap(actor), new() { Monotonic = () => time, Horizon = TimeSpan.FromSeconds(40), MaxAttempts = 1,
                TrialGroup = name == "c" ? 2 : 1, PollLimit = name == "b" ? 1 : name == "c" ? 3 : 500, JobScheduler = name == "c" ? queue : null });
            await new DurableHost([w]).ActivateAsync(0); return w;
        }
        async Task Drive(Worker w, int n) { for (var i = 0; i < n; i++) { await w.WokenAsync(null); await w.RunAsync(); queue.Drain(); } }
        await using var a = await Take("a");
        await store.AddAsync(new("start", new(id), Kinds.Start, Start: new(model.Key, "definition", Creation: q ? new(1, 0, "a") : null))); await Drive(a, 5);
        await store.AddAsync(new("timer", new(id.ToUpperInvariant()), "timer", Due: 5_000, TargetCreation: q ? 1 : null)); await Drive(a, 2);
        Require(a.Post(new("spawn-a", new(id), "spawn", TargetCreation: q ? 1 : null)), "first cohort spawn failed"); await Drive(a, 4);
        Require(a.Post(new("healthy-start", new(healthy), Kinds.Start, Start: new(model.Key, "definition", Creation: q ? new(1, 0, "b") : null))), "healthy actor failed"); await Drive(a, 4);
        Require(a.Post(new("spawn-b", new(healthy), "spawn", TargetCreation: q ? 1 : null)), "second cohort spawn failed"); await Drive(a, 4);
        time = 200; await store.AddAsync(new("stop", new(id), Kinds.Terminate, Due: 4_000, TargetCreation: q ? 1 : null));
        time = 12_000; await using var b = await Take("b"); await Drive(b, 1);
        Require(store.Messages["timer"].Trial == b.Epoch, "no failed ordinary cohort prefix");
        time = 24_000; await using var c = await Take("c");
        Require(c.Post(new("future-a", new(id), "future", Due: 60_000, TargetCreation: q ? 1 : null)) &&
            c.Post(new("future-b", new(healthy), "future", Due: 60_000, TargetCreation: q ? 1 : null)), "cohort preloads failed");
        await c.LoadAsync(); await c.PollAsync(); await c.FlushAsync();
        Require(c.Trial.Count() == 2 && queue.Pending.Count == 2, "mixed real cohort was not fenced together");
        await c.PollAsync(); await c.FlushAsync(); await c.LoadAsync(); queue.Drain();
        Require(model.Pending is not null && model.Entries[id] == 1 && model.Entries[healthy] == 2, "cohort did not deny only the poisoned body");
        Require(c.Post(new("other-start", new("0/other"), Kinds.Start, Start: new(model.Key, "bystander", Creation: q ? new(1, 0, "other") : null))), "cohort bystander failed");
        await Drive(c, 8);
        Require(c.Trial.Count() == 1 && c.Trial.Single().Contains(healthy) && store.Instances[id].Status == InstanceStatus.Running &&
            !store.Instances.ContainsKey("0/other"), "yielding one ticket released a genuinely entered cohort member");
        var driver = c.JobCompletion(healthy, "work", creation)!; model.Pending!.SetResult("genuine"); await driver; await Drive(c, 40); await c.BeatAsync();
        Require(c.Owns && !c.Trial.Any() && store.Messages.Count == 0 && store.Ghost.Count == 0 && store.Instances[id].Status == InstanceStatus.Cancelled &&
            store.Instances[id].Jobs is null && store.Instances[healthy].Jobs is null && store.Instances.ContainsKey("0/other") && model.Entries[id] == 1 && model.Entries[healthy] == 2,
            "mixed cohort/terminal/registry/partition did not close after genuine completion");
    }
}
