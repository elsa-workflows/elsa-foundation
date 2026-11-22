using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Workflows;

internal static partial class ShortWaitRaces
{
    private sealed class ToggleScheduler : TaskScheduler
    {
        public bool Reject = true;
        public int Attempts;
        protected override void QueueTask(Task task)
        {
            Attempts++;
            if (Reject) throw new InvalidOperationException("scheduler unavailable");
            TryExecuteTask(task);
        }
        protected override bool TryExecuteTaskInline(Task task, bool previouslyQueued) => false;
        protected override IEnumerable<Task> GetScheduledTasks() => [];
    }

    private sealed class RetriedJob : Model<List<string>>
    {
        public override string Key => "scheduler-retry";
        public readonly List<string> Entries = [];
        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken token)
        {
            Entries.Add(job.Instance.Id);
            return ValueTask.FromResult<object?>(job.Key);
        }
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
                if (message.Kind == Kinds.Start) turn.StartJob("work");
                else if (message.Kind == "cancel-work") turn.CancelJob("work");
                else if (message.Kind == Kinds.JobFinished) state.Add(message.Read<JobFinished>().Key);
        }
    }

    private static async Task SchedulerRetry(bool cancel)
    {
        var store = new InMemoryStore(1, () => 100);
        var scheduler = new ToggleScheduler();
        var model = new RetriedJob();
        var actor = InstanceRef.In(0, "main");
        var sibling = InstanceRef.In(0, "sibling");
        await using var owner = new Worker("owner", 0, store, new WorkflowInstance([model]),
            new() { Monotonic = () => 100, JobScheduler = scheduler });
        await new DurableHost([owner]).ActivateAsync(0);
        await owner.PollAsync();
        owner.Post(new("begin", actor, Kinds.Start, Start: new(model.Key, "def")), fresh: true);
        await owner.RunAsync();
        var key = InboxKey.Ticket(actor.Id, "work");
        var position = store.Partitions[0].Position;
        for (var i = 0; i < 4; i++)
        {
            var previous = scheduler.Attempts;
            var delay = await owner.RunAsync();
            Need(scheduler.Attempts <= previous + 1 && delay > TimeSpan.Zero, "scheduler refusal spun in an idle pass");
            Need(store.Messages.ContainsKey(key) && store.Partitions[0].Position == position && model.Entries.Count == 0,
                "scheduler refusal invented a logical result, entry or commit");
        }
        scheduler.Reject = false;
        var beforeCancelledPass = scheduler.Attempts;
        await owner.RunAsync(new CancellationToken(canceled: true));
        Need(scheduler.Attempts == beforeCancelledPass && model.Entries.Count == 0 && store.Messages.ContainsKey(key),
            "a pass cancelled before invocation resubmitted the owed ticket");
        scheduler.Reject = true;
        if (cancel)
        {
            owner.Post(new("cancel", actor, "cancel-work"));
            await owner.RunAsync();
            Need(!store.Messages.ContainsKey(key), "exact cancellation left a rejected ticket owed");
        }
        owner.Post(new("begin-sibling", sibling, Kinds.Start, Start: new(model.Key, "def")), fresh: true);
        scheduler.Reject = false;
        for (var i = 0; i < 4; i++) await owner.RunAsync();
        Need(model.Entries.Count(id => id == actor.Id) == (cancel ? 0 : 1) &&
            model.Entries.Count(id => id == sibling.Id) == 1 && store.Messages.Count == 0, "retry retargeted, duplicated or stranded work");
        Need(Json.Read<List<string>>(store.Instances[actor.Id].State).Count == (cancel ? 0 : 1), "unexpected authored outcome");
        Console.WriteLine($"PASS repeated scheduler refusal remains bounded; owed job and neighboring actor progress cancel={cancel}");
    }

    private static async Task CancelledPassKeepsPublicationAndWake()
    {
        var store = new InMemoryStore(1, () => 100);
        var held = new HeldScheduler();
        var model = new RetriedJob();
        var first = InstanceRef.In(0, "first");
        var next = InstanceRef.In(0, "next");
        var observations = new List<string>();
        await using var owner = new Worker("owner", 0, store, new WorkflowInstance([model]),
            new() { Monotonic = () => 100, JobScheduler = held, OnObservedDelivery = row => observations.Add(row.Message.Id) });
        await new DurableHost([owner]).ActivateAsync(0);
        await owner.PollAsync();
        owner.Post(new("start-first", first, Kinds.Start, Start: new(model.Key, "def")), fresh: true);
        await owner.RunAsync();
        held.RunAll();
        Need(model.Entries.SequenceEqual([first.Id]), "original admitted job did not publish");
        await store.AddAsync(new("start-next", next, Kinds.Start, Start: new(model.Key, "def")));
        await owner.WokenAsync(next);
        var position = store.Partitions[0].Position;
        var advice = await owner.RunAsync(new CancellationToken(canceled: true));
        Need(advice == TimeSpan.Zero && owner.Owns && store.Partitions[0].Position == position &&
            !observations.Contains("start-next"), "cancelled pass consumed a wake or changed the owner");
        for (var i = 0; i < 5; i++) { await owner.RunAsync(); held.RunAll(); }
        Need(observations.Contains("start-next") && model.Entries.SequenceEqual([first.Id, next.Id]) &&
            store.Messages.Count == 0 && Json.Read<List<string>>(store.Instances[first.Id].State) is ["work"] &&
            Json.Read<List<string>>(store.Instances[next.Id].State) is ["work"], "healthy resume lost a publication or a wake");
        Console.WriteLine("PASS cancelled pass preserves the accepted result and the pending wake for ordinary resume");
    }

    private static async Task PromotionFailure(string mode)
    {
        long now = 100;
        var store = new InMemoryStore(1, () => now);
        var fault = new CutFaultStore(store);
        var held = new HeldScheduler();
        var actor = InstanceRef.In(0, "promotion-failure");
        var key = InboxKey.Ticket(actor.Id, "work");
        var seen = new List<long>();
        var model = new TrialJob(() => seen.Add(store.Messages.GetValueOrDefault(key)?.Seen ?? -1));
        Worker Owner(string name) => new(name, 0, fault, new WorkflowInstance([model]),
            new() { Monotonic = () => now, JobScheduler = held, PollLimit = 1 });
        await using var old = Owner("old");
        await new DurableHost([old]).ActivateAsync(0);
        old.Post(new("begin", actor, Kinds.Start, Start: new(model.Key, "def", Ephemeral: true,
            ShortWait: TimeSpan.FromMilliseconds(100))), fresh: true);
        old.RunTurn(actor.Id);
        var queued = old.JobCompletion(actor.Id, "work")!;
        now += 101;
        if (mode == "lost-fence") store.Partitions[0].Epoch++;
        else fault.Arm(mode);
        await old.FlushAsync();
        held.RunAll();
        await queued.WaitAsync(TimeSpan.FromSeconds(5));
        Need(seen.Count == 0, "a queued pre-promotion body entered after a failure");
        if (mode == "definite")
            Need(store.Instances[actor.Id].Status == InstanceStatus.Faulted && !store.Messages.ContainsKey(key), "refused promotion did not park and retire volatile work");
        else if (mode == "transient" || mode == "unknown-after")
        {
            await using var replacement = mode == "unknown-after" ? Owner("replacement") : null;
            var current = replacement ?? old;
            if (replacement is not null)
            {
                now += 20_000;
                await new DurableHost([replacement]).ActivateAsync(0);
            }
            for (var i = 0; i < 8; i++) { await current.PollAsync(); await current.RunAsync(); held.RunAll(); }
            Need(seen.SequenceEqual([current.Epoch]) && !store.Messages.ContainsKey(key) &&
                Json.Read<List<string>>(store.Instances[actor.Id].State) is ["work"], "a landed promotion lost its marked retry");
        }
        else Need(!old.Owns && !store.Instances.ContainsKey(actor.Id) && !store.Messages.ContainsKey(key), "unlanded promotion survived loss of the volatile owner");
        Console.WriteLine($"PASS queued promotion failure preserves mark and ownership mode={mode}");
    }

    public static async Task JobRetries()
    {
        foreach (var cancel in new[] { false, true }) await SchedulerRetry(cancel);
        await CancelledPassKeepsPublicationAndWake();
        foreach (var mode in new[] { "transient", "definite", "unknown-before", "unknown-after", "lost-fence" })
            await PromotionFailure(mode);
    }
}
