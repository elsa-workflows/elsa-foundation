namespace Kernel.Native.Run;

// Supported worker-created schedules; no seeded actor, envelope, retry counter or completion.
internal static class ParkR9Checks
{
    private sealed class Probe(int timer, string? control = null, bool sibling = false, bool repeat = false, bool hold = false, bool timerAfter = false)
        : Kernel.Sim.Model<List<string>>
    {
        public override string Key => "r9-probe";
        public readonly Dictionary<string, TaskCompletionSource<object?>> Pending = [];
        public readonly List<(string Key, long Creation)> Entries = [];
        public int Handled;
        protected override void Turn(ITurn turn, List<string> state)
        {
            var release = turn.Inbox.Any(m => m.Kind == "release");
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Kinds.Start)
                {
                    if (control is not null) turn.Send(turn.Instance.Ref, control,
                        options: new(Delay: TimeSpan.FromMilliseconds(14_000)));
                    if (timer > 0 && !timerAfter) turn.Send(turn.Instance.Ref, "timer", options: new(Delay: TimeSpan.FromMilliseconds(timer)));
                    turn.StartJob("key");
                    if (timer > 0 && timerAfter) turn.Send(turn.Instance.Ref, "timer", options: new(Delay: TimeSpan.FromMilliseconds(timer)));
                    if (sibling) turn.StartJob("other");
                }
                else if (message.Kind == Kinds.JobFinished)
                {
                    if (hold && !release) { turn.Hold(message); continue; }
                    Handled++;
                    state.Add("handled");
                    if (repeat && Handled == 1) turn.StartJob("key");
                }
                else if (message.Kind == "restart") turn.StartJob("key");
                else state.Add(message.Kind);
            }
        }
        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken token)
        {
            Entries.Add((job.Key, job.Creation));
            var result = new TaskCompletionSource<object?>();
            Pending[job.Key] = result;
            token.Register(() => result.TrySetCanceled(token));
            return new(result.Task);
        }
    }
    private static void Require(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
    private static async Task Drive(Worker worker, Func<bool> done)
    {
        for (var i = 0; i < 80 && worker.Owns && !done(); i++) { await worker.WokenAsync(null); await worker.RunAsync(); }
        Require(done(), "healthy worker did not settle");
    }

    public static IEnumerable<(string Label, Func<Task> Run)> Cases()
    {
        foreach (var qualified in new[] { false, true })
        foreach (var timer in new[] { 5_000, 15_000, 30_000 })
        foreach (var sibling in new[] { false, true })
        foreach (var group in new[] { 1, 8 })
        foreach (var timerAfter in new[] { false, true })
            yield return ($"R9 issued timer/poison {qualified}/{timer}/{sibling}/{group}/{timerAfter}",
                () => Ordered(timer, qualified, sibling, group, null, timerAfter));
        foreach (var qualified in new[] { false, true })
        foreach (var control in new[] { Kinds.Terminate, Kinds.Suspend, "volatile-stop" })
            yield return ($"R9 earlier control {qualified}/{control}", () => Ordered(15_000, qualified, true, 1, control));
        foreach (var mode in new[] { "volatile", "durable", "qualified" })
        foreach (var repeat in new[] { false, true })
        foreach (var hold in new[] { false, true })
        foreach (var promoteFirst in new[] { false, true })
            yield return ($"R9 issued result {mode}/{repeat}/{hold}/{promoteFirst}",
                () => Result(mode, repeat, hold, promoteFirst));
    }

    private static async Task Ordered(int timer, bool qualified, bool sibling, int group, string? control, bool timerAfter = false)
    {
        const string id = "0/r9-ordered";
        long time = 100;
        var store = new InMemoryStore(1, () => time);
        var model = new Probe(timer, control is "volatile-stop" ? null : control, sibling, timerAfter: timerAfter);
        var owners = new List<Worker>();
        async Task<Worker> Take(string name)
        {
            var owner = new Worker(name, 0, store, new WorkflowInstance([model]),
                new() { Monotonic = () => time, MaxAttempts = 1, TrialGroup = group });
            owners.Add(owner);
            await new DurableHost([owner]).ActivateAsync(0);
            await owner.PollAsync();
            return owner;
        }
        var generation = qualified ? 1 : 0;
        var ticket = InboxKey.Ticket(id, "key", generation);
        try
        {
            var owner = await Take("a");
            Require(owner.Post(new("start", new(id), Kinds.Start, Start: new(model.Key, "d",
                Creation: qualified ? new(1, 0, "r9-order") : null)), fresh: true), "start rejected");
            await Drive(owner, () => model.Pending.ContainsKey("key"));
            time += 11_000;
            owner = await Take("b");
            await Drive(owner, () => store.Messages[ticket].Trial == owner.Epoch);
            var checkpoint = store.Instances[id];
            var priorEntries = model.Entries.Count;
            time += 11_000;
            owner = await Take("c");
            Require(store.Messages[ticket].Attempts == 1, "real lost trial did not exhaust the job");
            if (control == "volatile-stop") Require(owner.Post(new("stop", new(id), Kinds.Terminate,
                Due: time - 1_000, TargetCreation: qualified ? 1 : null)), "control rejected");
            await owner.LoadAsync();
            await owner.FlushAsync();
            if (control is null)
            {
                var timerOnTrial = store.Messages.Values.Any(m => m.Message.Kind == "timer" && m.Trial == owner.Epoch);
                Require(timerOnTrial == (timer == 15_000 && !timerAfter), "only an earlier due timer may precede the exhausted job's Park");
            }
            if (control == Kinds.Suspend)
            {
                await Drive(owner, () => store.Instances[id].Suspended);
                Require(store.Instances[id].Status == InstanceStatus.Running && model.Entries.Count == priorEntries,
                    "earlier suspension was overtaken by Park or fresh job admission");
                Require(owner.Post(new("resume", new(id), Kinds.Resume, TargetCreation: qualified ? 1 : null)), "resume rejected");
            }
            var expected = control is Kinds.Terminate or "volatile-stop" ? InstanceStatus.Cancelled : InstanceStatus.Faulted;
            await Drive(owner, () => store.Instances[id].Status == expected);
            Require(store.Instances[id].State == checkpoint.State && model.Handled == 0,
                "Park/control must preserve the committed checkpoint without synthesizing an authored job result");
            Require(model.Entries.Count == priorEntries, "known exhausted input admitted a fresh sibling job");
            time = Math.Max(time + 4_000, timer + 101);
            await Drive(owner, () => store.Messages.Count == 0);
            await owner.BeatAsync();
            Require(owner.Owns && store.Instances[id].Jobs is null && store.Ghost.Count == 0,
                "Park/control left a job registry, stranded trial or invalid fence");
        }
        finally { foreach (var owner in owners.AsEnumerable().Reverse()) await owner.DisposeAsync(); }
    }

    private static async Task Result(string mode, bool repeat, bool hold, bool promoteFirst)
    {
        const string id = "0/r9-result";
        long time = 100;
        var store = new InMemoryStore(1, () => time);
        // The unchanged main in-memory store rejects a persisted key reissued in its consume batch.
        // Volatile before-deadline restart remains same-turn; durable counterparts restart after retirement commits.
        var sameTurn = mode == "volatile" && !promoteFirst;
        var model = new Probe(0, repeat: repeat && sameTurn, hold: hold);
        var generation = mode == "qualified" ? 1 : 0;
        var ephemeral = mode == "volatile";
        await using var owner = new Worker("result", 0, store, new WorkflowInstance([model]), new() { Monotonic = () => time });
        await new DurableHost([owner]).ActivateAsync(0);
        await owner.PollAsync();
        Require(owner.Post(new("start", new(id), Kinds.Start, Start: new(model.Key, "d", Ephemeral: ephemeral,
            Creation: generation > 0 ? new(1, 0, "r9-result") : null)), fresh: true), "result start rejected");
        await Drive(owner, () => model.Entries.Count == 1);
        if (ephemeral) Require(store.Instances.Count == 0 && store.Messages.Count == 0, "short wait was not genuinely volatile");
        if (promoteFirst)
        {
            time += 5_500;
            await Drive(owner, () => store.Instances.ContainsKey(id));
        }
        var driver = owner.JobCompletion(id, "key", generation)!;
        model.Pending["key"].SetResult("first");
        await driver;
        if (hold)
        {
            for (var i = 0; i < 4; i++) await owner.RunAsync();
            var held = owner.Instances.Single(x => x.Id == id).Row!;
            Require(held.Jobs == Json.Write(new[] { "key" }) && model.Handled == 0, "held real completion lost or duplicated its key");
            Require(owner.Post(new("release", new(id), "release", TargetCreation: generation > 0 ? generation : null)), "release rejected");
        }
        // Run without Poll: genuine volatile completion/restart must not introduce persistence I/O.
        for (var i = 0; i < 12 && (model.Handled == 0 || repeat && sameTurn && model.Entries.Count != 2); i++) await owner.RunAsync();
        Require(model.Handled == 1, "genuine result was not consumed exactly once");
        if (repeat && !sameTurn)
        {
            await Drive(owner, () => store.Instances[id].Jobs is null && !store.Messages.ContainsKey(InboxKey.Ticket(id, "key", generation)));
            Require(owner.Post(new("restart", new(id), "restart", TargetCreation: generation > 0 ? generation : null)), "restart stimulus rejected");
            for (var i = 0; i < 12 && model.Entries.Count != 2; i++) await owner.RunAsync();
        }
        Require(model.Entries.Count == (repeat ? 2 : 1), "same-key restart did not enter exactly once after legal retirement");
        var row = owner.Instances.Single(x => x.Id == id).Row!;
        Require(row.Status == InstanceStatus.Running && row.Jobs == (repeat ? Json.Write(new[] { "key" }) : null),
            "real result left an orphan or wrong job registry before promotion");
        if (ephemeral && !promoteFirst) Require(store.Instances.Count == 0 && store.Messages.Count == 0 && store.Partitions[0].Position == 0,
            "consuming/restarting an in-memory job spent an extra persistence budget");
        time = 5_600;
        await Drive(owner, () => store.Instances.ContainsKey(id));
        var ticket = InboxKey.Ticket(id, "key", generation);
        Require(store.Instances[id].Status == InstanceStatus.Running && store.Instances[id].Jobs == row.Jobs &&
            store.Messages.ContainsKey(ticket) == repeat, "deadline promotion persisted an orphan key or missing restarted ticket");
        if (repeat)
        {
            var second = owner.JobCompletion(id, "key", generation)!;
            model.Pending["key"].SetResult("second");
            await second;
            if (hold) Require(owner.Post(new("release-second", new(id), "release", TargetCreation: generation > 0 ? generation : null)), "second release rejected");
            await Drive(owner, () => model.Handled == 2 && store.Instances[id].Jobs is null);
        }
        Require(owner.Owns && !store.Messages.ContainsKey(ticket) && store.Instances[id].Jobs is null && store.Ghost.Count == 0,
            "real completion failed exact final retirement");
    }
}
