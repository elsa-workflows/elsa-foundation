using Kernel.Agent;
using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Workflows;

return await Scenarios.Run(args);

static class Scenarios
{
    private static int failures;
    private static void Check(bool ok, string label)
    {
        Console.WriteLine($"{(ok ? "ok" : "FAIL")} {label}");
        if (!ok) failures++;
    }

    public static async Task<int> Run(string[] args)
    {
        long time = 100;
        var store = new InMemoryStore(1, () => time);
        var firstCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int effects = 0;
        async ValueTask<object?> Tool(IJob job, CancellationToken token)
        {
            var call = Interlocked.Increment(ref effects); // a non-idempotent outside effect
            if (call == 1) return await firstCompletion.Task; // crash after effect, before ticket becomes a result
            return "facts";
        }
        var live = new Capture();
        Worker Owner(string node) => new(node, 0, store, new WorkflowInstance([new AgentModel(Tool, duplicateResult: args.Contains("--mutate-result"), persistLive: args.Contains("--mutate-live"))]),
            new WorkerOptions { Monotonic = () => time, Transport = live });
        async Task Start(Worker worker) { await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); }
        async Task Pump(Worker worker, Func<bool> done, bool poll = false)
        {
            if (poll) await worker.WokenAsync(null);
            for (var i = 0; i < 3000 && !done(); i++)
            {
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
            Start: new StartSpec(AgentModel.ModelKey, "coordinator"), ReplyTo: ReplyTo.Of("caller", "stream")), fresh: true);
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
        Check(store.Subscriptions.Count == 1 && live.Items.Count(r => r.Channel == "tokens") == liveBefore, "fresh ownership retains approval without replaying live tokens");
        await Events.PublishAsync(store, new EventRow(AgentModel.Approval, "approval-1", "true", time + 60_000));
        await Pump(third, () => store.Instances["0/lead"].Status == InstanceStatus.Finished, poll: true);
        Check(State("0/lead").Approved && store.Subscriptions.Count == 0, "approval resumes and finishes after takeover");
        firstCompletion.SetResult("late");
        await Task.Delay(10);
        await first.RunAsync();
        Check(State("0/lead").Results == 1, "late original tool result cannot overwrite selected state");

        // Independent cancellation journey: cancel while the tool is running.
        var held = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledStore = new InMemoryStore(1, () => time);
        var cancelWorker = new Worker("cancel", 0, cancelledStore,
            new WorkflowInstance([new AgentModel(async (_, _) => await held.Task)]), new WorkerOptions { Monotonic = () => time });
        await new DurableHost([cancelWorker]).ActivateAsync(0);
        await cancelWorker.PollAsync();
        cancelWorker.Post(new Message("s", InstanceRef.In(0, "cancelled"), Kinds.Start, Start: new StartSpec(AgentModel.ModelKey, "coordinator")), fresh: true);
        await Pump(cancelWorker, () => cancelledStore.Instances.TryGetValue("0/cancelled", out var r) && Json.Read<Conversation>(r.State).Stage == "tool");
        await cancelledStore.AddAsync(new Message("cancel", InstanceRef.In(0, "cancelled"), "cancel", KeepUntil: time + 60_000));
        await Pump(cancelWorker, () => cancelledStore.Instances["0/cancelled"].Status == InstanceStatus.Cancelled, poll: true);
        held.SetResult("late");
        await Task.Delay(10);
        for (var i = 0; i < 5; i++) await cancelWorker.RunAsync();
        Check(Json.Read<Conversation>(cancelledStore.Instances["0/cancelled"].State).Results == 0, "cancelled tool cannot later change the conversation");
        Console.WriteLine(failures == 0 ? "agent spike: all passed" : $"agent spike: {failures} failed");
        return failures == 0 ? 0 : 1;
    }
}

sealed class Capture : IReplyTransport
{
    public List<Reply> Items { get; } = [];
    public void Send(string node, Reply reply) => Items.Add(reply);
}
