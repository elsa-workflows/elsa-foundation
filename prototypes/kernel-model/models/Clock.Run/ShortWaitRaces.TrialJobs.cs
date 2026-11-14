using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Workflows;

internal static partial class ShortWaitRaces
{
    private sealed class TrialJob(Action entered) : Model<List<string>>
    {
        public override string Key => "job-on-trial";
        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken token)
        {
            entered();
            return ValueTask.FromResult<object?>(job.Key);
        }
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
                if (message.Kind == Kinds.Start) turn.StartJob("work");
                else if (message.Kind == Kinds.JobFinished) state.Add(message.Read<JobFinished>().Key);
        }
    }

    public static async Task TrialJobAdmission()
    {
        foreach (var partition in new[] { 0, 1 }) await ParkQueuedJobDuringTrial(partition);

        await ParkQueuedJobDuringTrial(1, handoff: true);

        var store = new InMemoryStore(1, () => 100);
        var actor = InstanceRef.In(0, "suspect-job");
        var key = InboxKey.Ticket(actor.Id, "work");
        var physicalTrials = new List<long>();
        var model = new TrialJob(() => physicalTrials.Add(store.Messages[key].Trial));
        var firstScheduler = new HeldScheduler();
        var resumedScheduler = new HeldScheduler();
        Worker Owner(string name, HeldScheduler scheduler) => new(name, 0, store, new WorkflowInstance([model]),
            new() { Monotonic = () => 100, JobScheduler = scheduler });
        await using (var old = Owner("old", firstScheduler))
        {
            await new DurableHost([old]).ActivateAsync(0);
            await old.PollAsync();
            old.Post(new("start", actor, Kinds.Start, Start: new(model.Key, "def")), fresh: true);
            await old.RunAsync();
            Need(store.Messages[key].Seen == old.Epoch && firstScheduler.Count == 1, "former owner did not mark its ticket");
        }
        await using var next = Owner("next", resumedScheduler);
        await new DurableHost([next]).ActivateAsync(0);
        await next.PollAsync();
        await next.LoadAsync();
        Need(resumedScheduler.Count == 0, "inherited ticket ran before fenced selection");
        await next.FlushAsync();
        Need(next.Trial.SequenceEqual([key.Id]) && store.Messages[key].Trial == next.Epoch, "wrong selected trial");
        var driver = next.JobCompletion(actor.Id, "work")!;
        resumedScheduler.RunAll();
        await driver.WaitAsync(TimeSpan.FromSeconds(5));
        Need(physicalTrials.SequenceEqual([next.Epoch]), "selected job could not enter its own fenced trial");
        await next.RunAsync();
        Need(!next.Trial.Any() && !store.Messages.ContainsKey(key) &&
            Json.Read<List<string>>(store.Instances[actor.Id].State) is ["work"], "selected trial never cleared");
        Console.WriteLine("PASS selected physical job ran under its own fenced trial and cleared it");

        foreach (bool behind in new[] { true, false })
            await PromotedQueuedJob(behind);
    }

    private static async Task ParkQueuedJobDuringTrial(int suspectPartition, bool handoff = false)
    {
        long now = 100;
        var store = new InMemoryStore(2, () => now);
        var wire = new DelayedCommit(store);
        var gate = new TrialGate();
        var held = new HeldScheduler();
        var suspect = InstanceRef.In(suspectPartition, "suspect");
        var jobs = InstanceRef.In(0, "jobs");
        var work = new List<long>();
        var model = new TrialJob(() => work.Add(store.Messages.GetValueOrDefault("recover")?.Trial ?? 0));
        // The reader model has no jobs. A previous owner creates it and marks its "recover" input.
        var readerModel = new TrialReader();
        Worker Owner(string node, int partition, TrialGate process, int horizon, HeldScheduler? scheduler = null) =>
            new(node, partition, wire, new WorkflowInstance([model, readerModel]), new()
            {
                Partitions = 2, Monotonic = () => now, Horizon = TimeSpan.FromMilliseconds(horizon),
                Gate = process, JobScheduler = scheduler
            });
        await using (var predecessor = Owner("old", suspectPartition, new(), 300))
        {
            await new DurableHost([predecessor]).ActivateAsync(suspectPartition);
            await predecessor.PollAsync();
            predecessor.Post(new("create-reader", suspect, Kinds.Start, Start: new(readerModel.Key, "def")), fresh: true);
            await predecessor.RunAsync();
            await store.AddAsync(new("recover", suspect, "recover", Due: 200));
            await predecessor.PollAsync();
            await predecessor.LoadAsync();
            await predecessor.FlushAsync();
            Need(store.Messages["recover"].Seen == predecessor.Epoch, "missing earlier fenced Seen mark");
        }
        await using var owner = Owner("current", 0, gate, 0, held);
        await using var neighbor = suspectPartition == 0 ? null : Owner("current", suspectPartition, gate, 0);
        await new DurableHost([owner]).ActivateAsync(0);
        await owner.PollAsync();
        owner.Post(new("start-job", jobs, Kinds.Start, Start: new(model.Key, "def")), fresh: true);
        await owner.RunAsync();
        var original = owner.JobCompletion(jobs.Id, "work")!;
        Need(held.Count == 1 && work.Count == 0, "expected one queued ordinary job");
        var trialOwner = neighbor ?? owner;
        if (neighbor is not null) await new DurableHost([neighbor]).ActivateAsync(suspectPartition);
        now = 200;
        await trialOwner.PollAsync();
        await trialOwner.LoadAsync();
        wire.DelayNext = true;
        var flush = trialOwner.FlushAsync();
        await wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Need(store.Messages["recover"].Trial == trialOwner.Epoch && trialOwner.Trial.SequenceEqual(["recover"]),
                "unrelated recovery input did not get its fenced mark");
            held.RunAll();
            await original.WaitAsync(TimeSpan.FromSeconds(5));
            Need(work.Count == 0, "ordinary physical job entered while unrelated trial ACK was held");
        }
        finally { wire.Reply.TrySetResult(); await flush; }
        if (!handoff) await owner.RunAsync();
        if (trialOwner.Trial.Any()) { trialOwner.RunTurn(suspect.Id); await trialOwner.FlushAsync(); }
        await using var successor = handoff ? Owner("successor", 0, new(), 0, held) : null;
        if (successor is not null)
        {
            await owner.DisposeAsync();
            Need(held.Count == 0, "stand-down submitted a job from a deferred entry notification");
            await new DurableHost([successor]).ActivateAsync(0);
            await successor.PollAsync();
        }
        for (var i = 0; i < 4; i++) { await (successor ?? owner).RunAsync(); held.RunAll(); }
        Need(work.SequenceEqual([0L]) && Json.Read<List<string>>(store.Instances[jobs.Id].State) is ["work"],
            "owed genuine job did not run once after the trial");
        Console.WriteLine($"PASS held-ACK unrelated trial blocks queued entry, then releases it partition={suspectPartition} handoff={handoff}");
    }

    private sealed class TrialReader : Model<List<string>>
    {
        public override string Key => "trial-input";
        protected override void Turn(ITurn turn, List<string> state) => state.AddRange(turn.Inbox.Select(message => message.Kind));
    }

    private static async Task PromotedQueuedJob(bool behind)
    {
        long now = 100;
        var store = new InMemoryStore(1, () => now);
        var scheduler = new HeldScheduler();
        var actor = InstanceRef.In(0, "ephemeral-job");
        var key = InboxKey.Ticket(actor.Id, "work");
        var seen = new List<long>();
        var model = new TrialJob(() => seen.Add(store.Messages[key].Seen));
        await using var worker = new Worker("owner", 0, store, new WorkflowInstance([model]),
            new() { Monotonic = () => now, JobScheduler = scheduler, PollLimit = 1 });
        await new DurableHost([worker]).ActivateAsync(0);
        if (!behind) await worker.PollAsync();
        worker.Post(new("start", actor, Kinds.Start, Start: new(model.Key, "def", Ephemeral: true,
            ShortWait: TimeSpan.FromMilliseconds(100))), fresh: true);
        worker.RunTurn(actor.Id);
        Need(scheduler.Count == 1 && store.Messages.Count == 0, "job did not start as a volatile queued ticket");
        var original = worker.JobCompletion(actor.Id, "work")!;
        now += 101;
        await worker.FlushAsync();
        Need(store.Messages[key].Seen == (behind ? 0 : worker.Epoch), "promotion did not establish the intended Seen mark");
        scheduler.RunAll();
        await original.WaitAsync(TimeSpan.FromSeconds(5));
        Need(seen.Count == (behind ? 0 : 1), "a queued body bypassed the newly required Seen mark");
        for (var i = 0; i < 8; i++)
        {
            await worker.PollAsync();
            await worker.RunAsync();
            scheduler.RunAll();
        }
        Need(seen.SequenceEqual([worker.Epoch]) && !store.Messages.ContainsKey(key) &&
            Json.Read<List<string>>(store.Instances[actor.Id].State) is ["work"], "marked job did not complete once");
        Console.WriteLine($"PASS queued promoted ticket enters under its fenced Seen mark behind={behind}");
    }
}
