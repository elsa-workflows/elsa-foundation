namespace Kernel.Sim;

internal static class StarterContextChecks
{
    internal static async Task Run(Action<bool, string> check, Func<InMemoryStore, IKernelStore> aliases)
    {
        foreach (var alias in new[] { false, true })
        foreach (var localSuccessor in new[] { false, true })
        {
            long time = 100; var store = new InMemoryStore(1, () => time); var seen = new List<Observation>();
            await using var owner = Owner("first", aliases(store), seen, () => time);
            await new DurableHost([owner]).ActivateAsync(0);
            var id = InstanceRef.In(0, "ACTOR"); var route = alias ? InstanceRef.In(0, "actor") : id;
            var first = new Marker("first-secret"); var next = new Marker("next-secret");
            owner.Post(new("start", id, Kinds.Start, Start: Spec(false) with { Creation = new(1, 0, "first") }, TargetCreation: 1), fresh: true, ambient: first);
            await Pump(owner);
            var future = new Message("future", route, Kinds.Start, Start: Spec(true) with { Creation = new(2, 1, "next") }, TargetCreation: 2, Due: time + 2000);
            if (localSuccessor) owner.Post(future, ambient: next);
            else await store.AddAsync(future);
            owner.Post(new("resume", route, "keepalive", TargetCreation: 1), ambient: new Marker("foreign-resume"));
            await Pump(owner);
            owner.Post(new("finish", route, "finish", TargetCreation: 1)); await Pump(owner);
            var finished = store.Instances[id.Id];
            owner.Post(new("erase", id, Kinds.Delete, TargetCreation: 1)); await Pump(owner);
            time += 2100; await Pump(owner);
            var successor = store.Instances[id.Id]; var old = seen.Where(x => x.Creation == 1).ToArray();
            var born = seen.Where(x => x.Creation == 2).ToArray();
            check(finished.Status == InstanceStatus.Finished && successor is { Creation: 2, Status: InstanceStatus.Finished } &&
                old.Length == 3 && old.All(x => ReferenceEquals(x.Ambient, first)) && born.Length == 1 &&
                ReferenceEquals(born[0].Ambient, localSuccessor ? next : null) && !successor.State.Contains("secret", StringComparison.Ordinal),
                $"only the selected creator supplies context across resume and future birth, alias={alias}, local successor={localSuccessor}");
        }
        foreach (var ephemeral in new[] { false, true })
        {
            var store = new InMemoryStore(1, () => 100); var seen = new List<Observation>();
            await using var owner = Owner("legacy", store, seen, () => 100);
            await new DurableHost([owner]).ActivateAsync(0); var id = InstanceRef.In(0, "reused");
            var first = new Marker("old-secret");
            owner.Post(new("local", id, Kinds.Start, Start: Spec(true) with { Ephemeral = ephemeral }), fresh: true, ambient: first);
            await Pump(owner);
            if (store.Instances.ContainsKey(id.Id))
            {
                await store.AddAsync(new("erase", id, Kinds.Delete)); await Pump(owner);
            }
            await store.AddAsync(new("remote", id, Kinds.Start, Start: Spec(true))); await Pump(owner);
            check(seen.Count == 2 && ReferenceEquals(seen[0].Ambient, first) && seen[1].Ambient is null &&
                store.Instances[id.Id].Status == InstanceStatus.Finished && !store.Instances[id.Id].State.Contains("secret", StringComparison.Ordinal),
                $"a remote birth cannot inherit a finished local caller's context, ephemeral predecessor={ephemeral}");
        }
        {
            var store = new InMemoryStore(1, () => 100); var seen = new List<Observation>();
            var id = InstanceRef.In(0, "takeover"); var first = new Marker("activation-secret");
            await using (var owner = Owner("old", store, seen, () => 100))
            {
                await new DurableHost([owner]).ActivateAsync(0);
                owner.Post(new("start", id, Kinds.Start, Start: Spec(false) with { Creation = new(1, 0, "one") }, TargetCreation: 1), fresh: true, ambient: first);
                await Pump(owner);
            }
            await using var replacement = Owner("new", store, seen, () => 100);
            await new DurableHost([replacement]).ActivateAsync(0);
            await store.AddAsync(new("resume", id, "keepalive", TargetCreation: 1)); await Pump(replacement);
            check(seen.Count == 2 && ReferenceEquals(seen[0].Ambient, first) && seen[1].Ambient is null &&
                store.Instances[id.Id].Revision == 2 && !store.Instances[id.Id].State.Contains("secret", StringComparison.Ordinal),
                "a replacement owner hydrates the checkpoint without another activation's local context");
        }
    }

    private static StartSpec Spec(bool finish) => new("context-probe", "probe", Json.Write(finish), History: History.Minimal);
    private static Worker Owner(string name, IKernelStore store, List<Observation> seen, Func<long> time) =>
        new(name, 0, store, new WorkflowInstance([new Probe(seen)]), new() { Monotonic = time });

    private static async Task Pump(Worker worker)
    {
        for (var pass = 0; pass < 12; pass++) { await worker.PollAsync(); await worker.RunAsync(); }
    }

    private sealed record Marker(string Name);
    private sealed record Observation(long Creation, object? Ambient);
    private sealed class ProbeState
    {
        public ProbeState() { }
        public int Turns { get; set; }
        public bool Finish { get; set; }
    }
    private sealed class Probe(List<Observation> seen) : Model<ProbeState>
    {
        public override string Key => "context-probe";
        public override object CreateState(StartSpec start) => new ProbeState { Finish = Json.Read<bool>(start.Input!) };
        protected override void Turn(ITurn turn, ProbeState state)
        {
            state.Turns++; seen.Add(new(turn.Instance.Creation, turn.Ambient));
            if (state.Finish || turn.Inbox.Any(m => m.Kind == "finish")) turn.SetStatus(InstanceStatus.Finished);
        }
    }
}
