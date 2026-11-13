using Kernel.Agent;
using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Workflows;

var owner = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, maxConcurrencyLevel: 1);
try
{
    return await Task.Factory.StartNew(() => Scenarios.Run(args, owner.ExclusiveScheduler),
        CancellationToken.None, TaskCreationOptions.DenyChildAttach, owner.ExclusiveScheduler).Unwrap();
}
finally
{
    owner.Complete();
    await owner.Completion;
}

static class Scenarios
{
    private static int failures;
    private static void Check(bool ok, string label)
    {
        Console.WriteLine($"{(ok ? "ok" : "FAIL")} {label}");
        if (!ok) failures++;
    }

    public static async Task<int> Run(string[] args, TaskScheduler ownerScheduler)
    {
        void RequireOwner()
        {
            if (TaskScheduler.Current != ownerScheduler) throw new InvalidOperationException("worker phases must execute on the exclusive owner scheduler");
        }
        static void RequireJob()
        {
            if (TaskScheduler.Current != TaskScheduler.Default) throw new InvalidOperationException("tool bodies must execute on the default job scheduler");
        }
        long time = 100;
        var store = new InMemoryStore(1, () => time);
        var firstCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int effects = 0, oldBodyReturned = 0;
        var lateGuardObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<object?> Tool(IJob job, CancellationToken token)
        {
            RequireJob();
            var call = Interlocked.Increment(ref effects); // a non-idempotent outside effect
            if (call == 1)
            {
                var answer = await firstCompletion.Task; // crash after effect, before ticket becomes a result
                RequireJob();
                Volatile.Write(ref oldBodyReturned, 1);
                return answer;
            }
            return "facts";
        }
        var live = new Capture();
        int staleResultTurns = 0;
        Worker Owner(string node) => new(node, 0, store, new WorkflowInstance([new AgentModel(Tool, duplicateResult: args.Contains("--mutate-result"), persistLive: args.Contains("--mutate-live"))]),
            new WorkerOptions { JobScheduler = TaskScheduler.Default, Monotonic = () =>
            {
                if (node == "first" && Volatile.Read(ref oldBodyReturned) != 0)
                {
                    RequireOwner();
                    lateGuardObserved.TrySetResult(); // the completed old body reached the worker's post-job lease check
                }
                return time;
            }, Transport = live, OnTurn = (_, inbox, _) => { if (node == "first" && inbox.Any(m => m.Kind == Kinds.JobFinished)) staleResultTurns++; } });
        async Task Start(Worker worker) { await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); }
        async Task Pump(Worker worker, Func<bool> done, bool poll = false)
        {
            RequireOwner();
            if (poll) await worker.WokenAsync(null);
            for (var i = 0; i < 3000 && !done(); i++)
            {
                RequireOwner();
                await worker.RunAsync();
                if (i % 10 == 0) await worker.WokenAsync(null);
                await Task.Delay(1);
            }
            if (!done()) throw new Exception("pump did not reach its condition");
        }
        Conversation State(string id) => Json.Read<Conversation>(store.Instances[id].State);
        var first = Owner("first");
        await Start(first);
        first.Post(new Message("start", InstanceRef.In(0, "lead"), Kinds.Start,
            Start: new StartSpec(AgentModel.ModelKey, "coordinator"), ReplyTo: ReplyTo.Of("caller", "stream", time + 60_000)), fresh: true);
        await Pump(first, () => effects == 1 && store.Instances.TryGetValue("0/lead", out var r) && Json.Read<Conversation>(r.State).Stage == "tool");
        Check(State("0/lead").Transcript.SequenceEqual(["user: prepare a proposal"]), "checkpoint persisted before the tool result");
        Check(live.Items.Any(r => r.Channel == "tokens" && Equals(r.Item, "Preparing proposal")), "tokens observed live");
        Check(!store.Instances["0/lead"].State.Contains("Preparing proposal") && !store.Journal.Values.Any(j => j.ToString()!.Contains("Preparing proposal")), "emitted token is absent from state and journal");
        time += 11_000; // old process vanishes; lease expires, no graceful release
        var second = Owner("second");
        await Start(second);
        await Pump(second, () => store.Instances.TryGetValue("0/lead", out var r) && Json.Read<Conversation>(r.State).Replied && store.Subscriptions.Count == 1, poll: true);
        Check(effects == 2 && State("0/lead").Results == 1 && State("0/lead").Transcript.Count(s => s == "tool: facts") == 1, "tool effect repeated; one result changed the checkpoint");
        Check(store.Instances.TryGetValue("0/peer", out var peer) && peer.Status == InstanceStatus.Finished && State("0/lead").Replied, "second agent received a request and replied by message");
        Check(store.Subscriptions.Count == 1 && State("0/lead").Stage == "approval", "human approval is a durable subscription");
        var liveBefore = live.Items.Count(r => r.Channel == "tokens");
        time += 11_000;
        var third = Owner("third");
        await Start(third);
        await third.WokenAsync(null);
        Check(store.Subscriptions.Count == 1, "fresh ownership retains approval");
        await Events.PublishAsync(store, new EventRow(AgentModel.Approval, "approval-1", "true", time + 60_000));
        await Pump(third, () => store.Instances["0/lead"].Status == InstanceStatus.Finished, poll: true);
        Check(State("0/lead").Approved && store.Subscriptions.Count == 0, "approval resumes and finishes after takeover");
        Check(live.Items.Count(r => r.Channel == "tokens") == liveBefore, "processing the recovered instance and approval did not replay live tokens");
        var selected = store.Instances["0/lead"];
        var selectedJournal = store.Journal.Values.ToArray();
        var selectedPosition = store.Partitions[0].Position;
        firstCompletion.SetResult("late");
        await lateGuardObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Yield();
        Check(!first.Ready.Contains("0/lead"), "completed old tool reached the expired owner check and was not queued for a model turn");
        await first.RunAsync();
        Check(staleResultTurns == 0 && !first.Owns && store.Instances["0/lead"] == selected
            && store.Partitions[0].Position == selectedPosition && store.Journal.Values.SequenceEqual(selectedJournal),
            "expired old owner stood down before the late result turn and preserved row, journal and position");

        // Both decisions arrive before the peer's reply; the first decision is final.
        var decisionStore = new InMemoryStore(1, () => time);
        var decisionWorker = new Worker("decision", 0, decisionStore,
            new WorkflowInstance([new AgentModel((_, _) => { RequireJob(); return new ValueTask<object?>("facts"); })]), new WorkerOptions { JobScheduler = TaskScheduler.Default, Monotonic = () => time });
        await new DurableHost([decisionWorker]).ActivateAsync(0);
        await decisionWorker.PollAsync();
        decisionWorker.Post(new Message("decision-start", InstanceRef.In(0, "decision"), Kinds.Start,
            Start: new StartSpec(AgentModel.ModelKey, "coordinator")), fresh: true);
        await Pump(decisionWorker, () => decisionStore.Subscriptions.Count == 1);
        Check(!Json.Read<Conversation>(decisionStore.Instances["0/decision"].State).Replied, "both decisions are queued before the peer reply");
        await Events.PublishAsync(decisionStore, new EventRow(AgentModel.Approval, "yes", "true", time + 60_000));
        await Events.PublishAsync(decisionStore, new EventRow(AgentModel.Approval, "no", "false", time + 60_000));
        await Pump(decisionWorker, () => decisionStore.Instances["0/decision"].Status != InstanceStatus.Running, poll: true);
        var decision = Json.Read<Conversation>(decisionStore.Instances["0/decision"].State);
        Check(decisionStore.Instances["0/decision"].Status == InstanceStatus.Finished && decision.Approved &&
              decision.Transcript.Count(x => x.StartsWith("human:")) == 1, "first approval wins over a later denial before peer reply");

        // Independent cancellation journey: cancel while the tool is running.
        var held = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var signalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<object?>? completedBody = null;
        var cancelledStore = new InMemoryStore(1, () => time);
        ValueTask<object?> HeldTool(IJob job, CancellationToken token)
        {
            RequireJob();
            async Task<object?> Body()
            {
                using var registration = token.Register(() => signalled.TrySetResult());
                started.TrySetResult();
                var answer = await held.Task; // deliberately ignores cancellation like a non-cooperative external effect
                RequireJob();
                return answer;
            }
            return new ValueTask<object?>(completedBody = Body());
        }
        var cancelWorker = new Worker("cancel", 0, cancelledStore,
            new WorkflowInstance([new AgentModel(HeldTool)]), new WorkerOptions { JobScheduler = TaskScheduler.Default, Monotonic = () => time });
        await new DurableHost([cancelWorker]).ActivateAsync(0);
        await cancelWorker.PollAsync();
        cancelWorker.Post(new Message("s", InstanceRef.In(0, "cancelled"), Kinds.Start, Start: new StartSpec(AgentModel.ModelKey, "coordinator")), fresh: true);
        await Pump(cancelWorker, () => started.Task.IsCompleted && cancelledStore.Instances.TryGetValue("0/cancelled", out var r) && Json.Read<Conversation>(r.State).Stage == "tool");
        Check(started.Task.IsCompleted, "the tool actually started before cancellation");
        await cancelledStore.AddAsync(new Message("cancel", InstanceRef.In(0, "cancelled"), "cancel", KeepUntil: time + 60_000));
        await Pump(cancelWorker, () => cancelledStore.Instances["0/cancelled"].Status == InstanceStatus.Cancelled, poll: true);
        await signalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cancelled = cancelledStore.Instances["0/cancelled"];
        Check(cancelled.Status == InstanceStatus.Cancelled && !cancelledStore.Messages.ContainsKey(Worker.TicketId("0/cancelled", "lookup")),
            "cancellation signalled the running tool and removed its ticket");
        held.SetResult("late");
        await completedBody!.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Yield(); // run the captured owner continuation that discards the cancelled ticket's result
        for (var i = 0; i < 5; i++) { await cancelWorker.WokenAsync(null); await cancelWorker.RunAsync(); }
        Check(cancelledStore.Instances["0/cancelled"] == cancelled && !cancelledStore.Messages.ContainsKey(Worker.TicketId("0/cancelled", "lookup")),
            "non-cooperative tool completed late without changing the terminal row or recreating its ticket");
        Console.WriteLine(failures == 0 ? "agent spike: all passed" : $"agent spike: {failures} failed");
        return failures == 0 ? 0 : 1;
    }
}

sealed class Capture : IReplyTransport
{
    public List<Reply> Items { get; } = [];
    public void Send(string node, Reply reply) => Items.Add(reply);
}
