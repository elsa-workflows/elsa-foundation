namespace Kernel.Native.Run;

internal static class ParkR14ControlChecks
{
    private sealed class Probe : Kernel.Sim.Model<List<string>>
    {
        public override string Key => "r14-cut";
        protected override void Turn(ITurn turn, List<string> state) { foreach (var message in turn.Inbox) state.Add(message.Kind); }
    }
    private static void Require(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    public static IEnumerable<(string Label, Func<Task> Run)> Cases(Func<IActorType, IActorType>? wrap = null)
    {
        foreach (var qualified in new[] { false, true })
        foreach (var command in new[] { "none", Kinds.Terminate, Kinds.Suspend, Kinds.Resume })
        foreach (var exhausted in new[] { false, true })
            yield return ($"R14 control projection {qualified}/{command}/{exhausted}", () => Run(qualified, command, exhausted, wrap));
    }
    private static async Task Run(bool qualified, string command, bool exhausted, Func<IActorType, IActorType>? wrap)
    {
        const string id = "0/cut";
        long time = 100;
        var store = new InMemoryStore(1, () => time);
        var model = new Probe();
        async Task<Worker> Take(string name)
        {
            IActorType actor = new WorkflowInstance([model]);
            var owner = new Worker(name, 0, store, wrap is null ? actor : wrap(actor), new()
            { Monotonic = () => time, Horizon = TimeSpan.FromSeconds(40), TrialGroup = 1, MaxAttempts = exhausted ? 1 : 3, PollLimit = 2 });
            await new DurableHost([owner]).ActivateAsync(0); return owner;
        }
        async Task Drive(Worker owner, int count) { for (var i = 0; i < count; i++) { await owner.WokenAsync(null); await owner.RunAsync(); } }
        async Task Add(string key, string kind, long? due = null, StartSpec? start = null) =>
            await store.AddAsync(new(key, new(id), kind, Start: start, Due: due, TargetCreation: qualified ? 1 : null));
        await using var first = await Take("first");
        await Add("start", Kinds.Start, start: new(model.Key, "definition", Creation: qualified ? new(1, 0, "cut") : null));
        await Drive(first, 5);
        await Add("t", "later-maturity", 9_000); await Add("s", "earlier-maturity", 8_000);
        await Drive(first, 3);
        Require(store.Messages["s"].Seen == first.Epoch && store.Messages["t"].Seen == first.Epoch, "future timers were not genuinely marked");
        time = 8_000;
        if (command != "none") await Add("command", command, 8_500);
        time = 12_000;
        await using var second = await Take("second"); await Drive(second, 1);
        Require(store.Messages["t"].Trial == second.Epoch && store.Messages["s"].Trial != second.Epoch,
            "later-maturity timer was not genuinely selected before the command was polled");
        time = 24_000;
        await using var third = await Take("third"); await Drive(third, 30);
        time += 4_000; await third.BeatAsync();
        if (command == Kinds.Suspend)
        {
            Require(store.Instances[id].Suspended && store.Instances[id].Status == InstanceStatus.Running &&
                Json.Read<List<string>>(store.Instances[id].State).Count(s => s == "earlier-maturity") == 1,
                "suspend did not follow its mature ordinary predecessor");
            await Add("resume", Kinds.Resume); await Drive(third, 20);
        }
        var expected = command == Kinds.Terminate ? InstanceStatus.Cancelled : exhausted ? InstanceStatus.Faulted : InstanceStatus.Running;
        var row = store.Instances[id]; var state = Json.Read<List<string>>(row.State);
        Require(row.Status == expected && third.Owns && store.Ghost.Count == 0 && store.Messages.Count == 0 && !third.Ready.Any() && !third.Trial.Any(),
            "control projection stranded ordinary work/control or lost the fence");
        if (command != "none") Require(state.Count(s => s == "earlier-maturity") == 1, "the before-control timer was lost or duplicated");
        Require(state.Count(s => s == "later-maturity") == (!exhausted ? 1 : 0),
            "an exhausted timer executed or an already-admitted healthy trial was lost");
    }
}
