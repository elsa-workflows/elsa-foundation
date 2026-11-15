namespace Kernel.Native.Run;

// Boundary modes seed only retry-envelope Attempts on real issued rows. They are internal robustness
// checks, not supported public or legacy ingress. Entry modes use actual queued callbacks and model barriers.
internal static class ParkAdmissionChecks
{
    private sealed class Queue : TaskScheduler
    {
        public readonly List<Task> Pending = [];
        protected override void QueueTask(Task task) => Pending.Add(task);
        protected override bool TryExecuteTaskInline(Task task, bool queued) => false;
        protected override IEnumerable<Task> GetScheduledTasks() => Pending;
        public void Drain()
        {
            foreach (var task in Pending.ToArray()) { Pending.Remove(task); TryExecuteTask(task); }
        }
    }
    private sealed class Probe : Kernel.Sim.Model<List<string>>
    {
        public override string Key => "park-entry";
        public readonly Dictionary<(string, long), TaskCompletionSource<object?>> Entered = [];
        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken token)
        {
            var result = new TaskCompletionSource<object?>();
            Entered[(job.Instance.Id, job.Creation)] = result;
            token.Register(() => result.TrySetCanceled(token));
            return new(result.Task);
        }
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
                if (message.Kind == Kinds.Start) turn.StartJob("work");
                else if (message.Kind == "explode") throw new InvalidOperationException("entry fault");
                else state.Add(message.Kind);
        }
    }
    private static void Require(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }

    public static async Task RunAsync(string mode)
    {
        const string id = "0/entry";
        var store = new InMemoryStore(1, () => 100);
        var scheduler = new Queue();
        var model = new Probe();
        await using var owner = new Worker("entry", 0, store, new WorkflowInstance([model]),
            new() { Monotonic = () => 100, JobScheduler = scheduler, MaxAttempts = 1 });
        await new DurableHost([owner]).ActivateAsync(0);
        async Task Pump(Func<bool> done)
        {
            for (var i = 0; i < 50 && !done(); i++) await owner.RunAsync();
            Require(done(), "entry witness did not settle");
        }
        async Task Send(string wire, string kind, long? creation = 1, StartSpec? start = null, long? due = null)
        {
            await store.AddAsync(new(wire, new(id), kind, Start: start, TargetCreation: creation, Due: due));
            await owner.WokenAsync(null);
        }
        await Send("start", Kinds.Start, start: new(model.Key, "d", Creation: new(1, 0, "entry")));
        await Pump(() => scheduler.Pending.Count == 1);
        var ticket = InboxKey.Ticket(id, "work", 1);
        if (mode == "entered") scheduler.Drain();
        if (mode is "entered" or "queued" or "recreated")
        {
            await Send("fault", "explode");
            await Pump(() => store.Instances[id].Status == InstanceStatus.Faulted);
            if (mode != "recreated") scheduler.Drain();
            await owner.RunAsync();
            Require(model.Entered.ContainsKey((id, 1)) == (mode == "entered"), "queued terminal body entered, or real entry was lost");
            if (mode == "entered")
            {
                var pending = model.Entered[(id, 1)];
                Require(!pending.Task.IsCompleted && store.Messages.ContainsKey(ticket), "Park cancelled an already-entered unrelated job");
                var published = owner.JobCompletion(id, "work", 1)!;
                pending.SetResult("late");
                await published;
                await Pump(() => !store.Messages.ContainsKey(ticket));
                Require(store.Instances[id].Jobs is null, "genuine completion failed to retire its exact ticket");
            }
            else await Pump(() => !store.Messages.ContainsKey(ticket));
            if (mode == "recreated")
            {
                await Send("delete", Kinds.Delete);
                await Pump(() => store.Instances[id].Retired);
                await Send("restart", Kinds.Start, 2, new(model.Key, "d", Creation: new(2, 1, "new")));
                await Pump(() => store.Instances[id].Creation == 2 && scheduler.Pending.Count >= 2);
                scheduler.Drain();
                Require(model.Entered.ContainsKey((id, 2)) && !model.Entered.ContainsKey((id, 1)), "old callback entered a recreated target");
            }
            Require(owner.Owns && store.Ghost.Count == 0, "entry checks lost their fence");
            return;
        }
        if (mode == "suspended")
        {
            await Send("suspend", Kinds.Suspend);
            await Pump(() => store.Instances[id].Suspended);
            scheduler.Drain();
            await owner.RunAsync();
            Require(model.Entered.Count == 0, "queued callback entered a withheld row");
            await Send("resume", Kinds.Resume);
            await Pump(() => scheduler.Pending.Count > 0);
            scheduler.Drain();
            Require(model.Entered.ContainsKey((id, 1)), "resume stranded its still-owed current job");
            return;
        }
        var kind = mode == "discarded" ? Kinds.JobFinished : "explode";
        await store.AddAsync(new("boundary", new(id), kind,
            kind == Kinds.JobFinished ? Json.Write(new JobFinished("work", "forged", null)) : null,
            Due: mode == "future" ? 10_000 : null, TargetCreation: mode == "wrong" ? 2 : 1));
        store.Messages["boundary"].Attempts = 1; // explicit retry-envelope boundary, actor/ticket remain worker-created
        await owner.PollAsync();
        await owner.LoadAsync();
        await owner.FlushAsync();
        if (mode == "unrelated")
        {
            await store.AddAsync(new("other", new("0/unrelated"), Kinds.Start,
                Start: new(model.Key, "d", Creation: new(1, 0, "other"))));
            await owner.WokenAsync(null);
            await Pump(() => scheduler.Pending.Count >= 2);
        }
        scheduler.Drain();
        if (mode == "unrelated") Require(model.Entered.ContainsKey(("0/unrelated", 1)), "actor-local poison blocked an unrelated actor");
        Require(model.Entered.ContainsKey((id, 1)) == (mode is not ("poison" or "unrelated")), "poison boundary blocked eligible work or admitted a known doomed body");
        if (mode is "poison" or "unrelated")
        {
            await Pump(() => store.Instances[id].Status == InstanceStatus.Faulted);
            Require(store.Instances[id].Fault!.Contains("on trial"), "exhausted input did not Park");
        }
        else Require(store.Instances[id].Status == InstanceStatus.Running, "negative boundary changed current status");
        Require(owner.Owns && store.Ghost.Count == 0, "boundary check lost its fence");
    }
}
