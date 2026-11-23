namespace Kernel.Native.Run;

// Natural parent/child notices from real starts; no fabricated completion or seeded lifecycle row.
internal static class ParkR10Checks
{
    private sealed class Probe(bool hold, InstanceStatus childStatus, int children) : Kernel.Sim.Model<List<string>>
    {
        public override string Key => "r10-child-notice";
        public int ChildEntries, NoticeTurns, Accepted, Releases;
        public readonly HashSet<InboxKey> NoticeKeys = [];
        public readonly List<(int Notices, bool Release)> Batches = [];
        protected override void Turn(ITurn turn, List<string> state)
        {
            if (turn.Instance.Definition == "child")
            {
                ChildEntries++;
                if (childStatus == InstanceStatus.Faulted) throw new InvalidOperationException("real child failure");
                turn.SetStatus(childStatus);
                return;
            }
            var release = turn.Inbox.Any(m => m.Kind == "release");
            var notices = turn.Inbox.Count(m => m.Kind == WorkflowKinds.ChildFinished);
            if (notices > 0) Batches.Add((notices, release));
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Kinds.Start)
                    for (var i = 0; i < children; i++) turn.Start(new(Key, "child"));
                if (message.Kind == WorkflowKinds.ChildFinished)
                {
                    NoticeTurns++;
                    NoticeKeys.Add(message.InboxKey);
                    if (hold && !release) turn.Hold(message);
                    else { Accepted++; state.Add("accepted"); }
                }
                if (message.Kind == "release") { Releases++; state.Add("released"); }
            }
        }
    }
    private static void Require(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
    public static IEnumerable<(string Label, Func<Task> Run)> Cases()
    {
        foreach (var qualified in new[] { false, true })
        foreach (var hold in qualified ? new[] { false } : new[] { false, true })
        foreach (var status in new[] { InstanceStatus.Finished, InstanceStatus.Faulted, InstanceStatus.Cancelled })
        foreach (var children in new[] { 1, 2 })
        foreach (var post in new[] { false, true })
            yield return ($"R10 genuine child notice {qualified}/{hold}/{status}/{children}/{post}",
                () => Run(qualified, hold, status, children, post, null));
        foreach (var control in new[] { Kinds.Terminate, Kinds.Suspend })
        foreach (var post in new[] { false, true })
            yield return ($"R10 held legacy notice control {control}/{post}",
                () => Run(false, true, InstanceStatus.Finished, 1, post, control));
    }

    private static async Task Run(bool qualified, bool hold, InstanceStatus childStatus, int children, bool post, string? control)
    {
        const string id = "0/r10-parent";
        long time = 100;
        var store = new InMemoryStore(1, () => time);
        var model = new Probe(hold, childStatus, children);
        await using var owner = new Worker("notice", 0, store, new WorkflowInstance([model]), new() { Monotonic = () => time });
        await new DurableHost([owner]).ActivateAsync(0);
        async Task Pulse() { await owner.WokenAsync(null); await owner.RunAsync(); }
        async Task Until(Func<bool> done)
        {
            for (var i = 0; i < 80 && owner.Owns && !done(); i++) await Pulse();
            Require(done(), "healthy child-notice sequence did not settle");
        }
        async Task Send(string kind)
        {
            var message = new Message(kind, new(id), kind, TargetCreation: qualified ? 1 : null);
            if (post) Require(owner.Post(message), "ordinary stimulus was rejected");
            else await store.AddAsync(message);
        }
        await store.AddAsync(new("start", new(id), Kinds.Start, Start: new(model.Key, "parent",
            Creation: qualified ? new(1, 0, "r10-notice") : null)));
        await Until(() => store.Instances.Values.Count(r => r.Definition == "child" && r.Status != InstanceStatus.Running) == children);
        if (hold)
        {
            await Until(() => model.NoticeTurns == children);
            for (var i = 0; i < 3; i++) await Pulse();
            Require(model.NoticeTurns == children && model.Accepted == 0, "a held notice spun without a new stimulus");
        }
        if (control is not null)
        {
            await Send(control);
            if (control == Kinds.Suspend)
            {
                await Until(() => store.Instances[id].Suspended);
                Require(store.Instances[id].Status == InstanceStatus.Running, "suspension refaulted the parent");
                await Send(Kinds.Resume);
                await Until(() => !store.Instances[id].Suspended);
            }
            else await Until(() => store.Instances[id].Status == InstanceStatus.Cancelled);
        }
        await Send("release");
        for (var i = 0; i < 30; i++) await Pulse();
        time += 4_000;
        await owner.BeatAsync();
        var row = store.Instances[id];
        if (control == Kinds.Terminate)
        {
            Require(row.Status == InstanceStatus.Cancelled && model.Accepted == 0 && model.Releases == 0,
                "terminal parent must drop held notices and later ordinary input without authored execution");
        }
        else
        {
            Require(row.Status == InstanceStatus.Running && model.Accepted == children && model.Releases == 1 &&
                Json.Read<List<string>>(row.State).Count(x => x == "accepted") == children && row.State.Contains("released"),
                "held child notice did not progress with the genuine ordinary release");
            if (qualified) Require(model.Batches.All(b => b.Notices == 1 && !b.Release), "qualified child lifecycle notice lost singleton isolation");
            if (hold) Require(model.Batches.Last().Release, "legacy held notice did not retain next-input batching");
        }
        Require(model.ChildEntries == children && model.NoticeKeys.Count == children && store.Messages.Count == 0 &&
            owner.Owns && store.Ghost.Count == 0 && !owner.Ready.Any() && !owner.Trial.Any(),
            "notice closure duplicated child work, stranded input or lost a healthy fence");
    }
}
