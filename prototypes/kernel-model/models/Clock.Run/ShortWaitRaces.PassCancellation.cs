using Kernel.Durable;
using Kernel.Sim;
using Kernel.Runtime;
using Kernel.Workflows;

internal static partial class ShortWaitRaces
{
    public static async Task PassCancellation(string boundary)
    {
        if (boundary == "turn")
        {
            await CancelledPassTurn();
            return;
        }
        var store = new InMemoryStore(1, () => 100);
        var wire = new DelayedCommit(store);
        var scheduler = new ToggleScheduler { Reject = false };
        var model = new RetriedJob();
        var actor = InstanceRef.In(0, "passes");
        var key = InboxKey.Ticket(actor.Id, "work");
        var start = new Message("start", actor, Kinds.Start, Start: new(model.Key, "def"));
        Worker New(string name) => new(name, 0, wire, new WorkflowInstance([model]),
            new() { Monotonic = () => 100, JobScheduler = scheduler });
        if (boundary == "load")
        {
            // A behind-poll owner persisted a job but never saw its row. Its successor can mark before loading.
            await using var old = New("previous");
            await new DurableHost([old]).ActivateAsync(0);
            old.Post(start, fresh: true);
            await old.RunAsync();
            Need(store.Messages[key].Seen == 0 && scheduler.Attempts == 0, "original ticket should be unmarked");
        }
        await using var owner = New("current");
        await new DurableHost([owner]).ActivateAsync(0);
        await owner.PollAsync();
        using var stop = new CancellationTokenSource();
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Read(CancellationToken actual)
        {
            Need(actual == stop.Token, "store did not receive the pass token");
            reading.SetResult();
            if (boundary == "poll-throws") await reply.Task.WaitAsync(actual);
            else await reply.Task;
        }
        switch (boundary)
        {
            case "poll-throws":
            case "poll-returns":
                await store.AddAsync(start);
                await owner.WokenAsync(actor);
                wire.BeforePoll = Read;
                break;
            case "load":
                await owner.FlushAsync();
                Need(owner.NeedsLoad && store.Messages[key].Seen == owner.Epoch, "cold marked ticket required");
                wire.AfterLoad = Read;
                break;
            case "commit":
                owner.Post(start, fresh: true);
                wire.DelayNext = true;
                break;
            default:
                throw new ArgumentException("Expected poll-throws, poll-returns, load or commit", nameof(boundary));
        }
        var pass = owner.RunAsync(stop.Token).AsTask();
        await (boundary == "commit" ? wire.Landed.Task : reading.Task).WaitAsync(TimeSpan.FromSeconds(5));
        Need(scheduler.Attempts == 0 && model.Entries.Count == 0, "admission preceded controlled owner operation");
        stop.Cancel();
        reply.TrySetResult();
        wire.Reply.TrySetResult();
        await pass.WaitAsync(TimeSpan.FromSeconds(5));
        Need(owner.Owns && scheduler.Attempts == 0 && model.Entries.Count == 0,
            $"fresh job submitted after cancellation during {boundary}");
        if (boundary is "load" or "commit")
            Need(!owner.NeedsLoad && owner.Position == store.Partitions[0].Position && store.Messages.ContainsKey(key),
                "accepted load or commit was discarded");
        wire.BeforePoll = wire.AfterLoad = null;
        var polls = wire.PollCalls;
        var loads = wire.LoadCalls;
        for (var i = 0; i < 6; i++) await owner.RunAsync();
        Need(model.Entries.SequenceEqual([actor.Id]) && store.Messages.Count == 0 &&
            Json.Read<List<string>>(store.Instances[actor.Id].State) is ["work"], "ordinary resume lost the wake or the job");
        Need(wire.PollCalls == polls + (boundary == "poll-throws" ? 1 : 0) &&
            (boundary != "load" || wire.LoadCalls == loads), "resume reread a successful operation or missed the interrupted poll");
        Console.WriteLine($"PASS caller cancellation at {boundary} defers fresh entry and preserves ordinary resume");
    }

    private sealed class DelayedStartJob : IExecutionModel
    {
        public string Key => "delayed-job-start";
        public Type StateType => typeof(List<string>);
        public int StateVersion => 1;
        public object CreateState(StartSpec start) => new List<string>();
        public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException();
        public readonly TaskCompletionSource Entered = new();
        public readonly TaskCompletionSource Reply = new();
        public int Entries, Results;

        public async ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken)
        {
            foreach (var item in turn.Inbox)
                if (item.Kind == Kinds.Start)
                {
                    Entered.SetResult();
                    await Reply.Task;
                    turn.StartJob("work");
                }
                else if (item.Kind == Kinds.JobFinished)
                {
                    Results++;
                    turn.SetStatus(InstanceStatus.Finished);
                }
        }

        public ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken)
        {
            Entries++;
            return ValueTask.FromResult<object?>(job.Key);
        }
    }

    private static async Task CancelledPassTurn()
    {
        var store = new InMemoryStore(1, () => 100);
        var scheduler = new ToggleScheduler { Reject = false };
        var model = new DelayedStartJob();
        var id = InstanceRef.In(0, "late-turn");
        var ended = 0;
        await using var owner = new Worker("owner", 0, store, new WorkflowInstance([model]), new()
        {
            Monotonic = () => 100, JobScheduler = scheduler, OnLeftMemory = (_, _) => ended++
        });
        await new DurableHost([owner]).ActivateAsync(0);
        await owner.PollAsync();
        owner.Post(new("start", id, Kinds.Start, Start: new(model.Key, "def", Ephemeral: true)), fresh: true);
        using var stop = new CancellationTokenSource();
        await owner.RunAsync(stop.Token);
        Need(model.Entered.Task.IsCompleted && scheduler.Attempts == 0, "expected an admitted, suspended model turn");
        stop.Cancel();
        model.Reply.SetResult();
        Need(owner.Messages.Any(m => m.Id == InboxKey.Ticket(id.Id, "work")) && scheduler.Attempts == 0,
            "the late model continuation did not retain its volatile ticket or submitted a fresh job");
        for (var i = 0; i < 4; i++) await owner.RunAsync();
        Need(model.Entries == 1 && model.Results == 1 && ended == 1 && !owner.Messages.Any() &&
            store.Partitions[0].Position == 0 && store.Messages.Count == 0 && store.Instances.Count == 0,
            "resumed volatile work did not complete exactly once without persistence");
        Console.WriteLine("PASS cancelled pass leaves late Ephemeral turn job for a healthy pass with zero writes");
    }
}
