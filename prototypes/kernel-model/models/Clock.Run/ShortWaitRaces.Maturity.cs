using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Workflows;

internal static partial class ShortWaitRaces
{
    private static async Task Maturity(Func<string, Func<Task>, Task> Check)
    {
        foreach (var post in new[] { false, true })
            foreach (var explicitDue in new[] { false, true })
                foreach (var wake in new[] { 100L, 200L })
                    await Check($"immediate cut: Post={post}, explicit due={explicitDue}, wake={wake}", async () =>
                    {
                        long clock = 100;
                        var store = new InMemoryStore(1, () => clock);
                        store.Partitions[0].Epoch = 1;
                        var id = InstanceRef.In(0, "mature-immediate");
                        store.Instances[id.Id] = new(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
                        await store.AddAsync(new("suspect", id, "work"));
                        store.Messages["suspect"].Seen = 1;
                        await store.AddAsync(new("timer", id, "timer", Due: 175));
                        await using var owner = new Worker("owner", 0, store, new WorkflowInstance([new TrialCut()]),
                            new() { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
                        await new DurableHost([owner]).ActivateAsync(0);
                        await owner.PollAsync();
                        await owner.LoadAsync();
                        var command = new Message("command", id, Kinds.Terminate, Due: explicitDue ? 100 : null);
                        if (post) owner.Post(command);
                        else { await store.AddAsync(command); await owner.PollAsync(); }
                        await owner.FlushAsync();
                        Need(owner.Trial.Contains("suspect"), "the suspect was not selected");
                        clock = wake;
                        for (var i = 0; i < 8; i++) { await owner.PollAsync(); await owner.RunAsync(); }
                        var row = store.Instances[id.Id];
                        Need(row.Status == InstanceStatus.Cancelled && Json.Read<List<string>>(row.State).SequenceEqual(["work"]),
                            "an already observed immediate command was overtaken: " + row.State);
                    });
        foreach (var firstPoll in new[] { 100L, 200L })
            foreach (var timerDue in new[] { 125L, 175L })
                await Check($"late first poll: at={firstPoll}, timer due={timerDue}", async () =>
                {
                    long clock = 100;
                    var store = new InMemoryStore(1, () => clock);
                    store.Partitions[0].Epoch = 1;
                    var id = InstanceRef.In(0, "mature-first-poll");
                    store.Instances[id.Id] = new(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
                    await store.AddAsync(new("suspect", id, "work"));
                    store.Messages["suspect"].Seen = 1;
                    await store.AddAsync(new("timer", id, "timer", Due: timerDue));
                    await store.AddAsync(new("command", id, Kinds.Terminate, Due: 150));
                    clock = firstPoll;
                    await using var owner = new Worker("owner", 0, store, new WorkflowInstance([new TrialCut()]),
                        new() { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
                    await new DurableHost([owner]).ActivateAsync(0);
                    await owner.PollAsync();
                    await owner.LoadAsync();
                    await owner.FlushAsync();
                    Need(owner.Trial.Contains("suspect"), "the late-poll suspect was not selected");
                    clock = 200;
                    for (var i = 0; i < 8; i++) { await owner.PollAsync(); await owner.RunAsync(); }
                    var row = store.Instances[id.Id];
                    var expected = timerDue < 150 ? new[] { "work", "timer" } : new[] { "work" };
                    Need(row.Status == InstanceStatus.Cancelled && Json.Read<List<string>>(row.State).SequenceEqual(expected),
                        "poll time changed durable ordering: " + row.State);
                });
        foreach (var postCommand in new[] { false, true })
            foreach (var postLate in new[] { false, true })
                await Check($"cut matures during trial ACK: command Post={postCommand}, late Post={postLate}", async () =>
                {
                    long clock = 100;
                    var store = new InMemoryStore(1, () => clock);
                    store.Partitions[0].Epoch = 1;
                    var id = InstanceRef.In(0, "mature-during-ack");
                    store.Instances[id.Id] = new(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
                    var command = new Message("command", id, Kinds.Terminate, Due: 150);
                    if (!postCommand) await store.AddAsync(command);
                    await store.AddAsync(new("suspect", id, "work"));
                    store.Messages["suspect"].Seen = 1;
                    var wire = new DelayedCommit(store);
                    await using var owner = new Worker("owner", 0, wire, new WorkflowInstance([new TrialCut()]),
                        new() { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
                    await new DurableHost([owner]).ActivateAsync(0);
                    await owner.PollAsync();
                    await owner.LoadAsync();
                    if (postCommand) owner.Post(command);
                    await owner.FlushAsync();
                    Need(owner.Trial.Contains("suspect"), "the delayed-ACK suspect was not selected");
                    clock = 149;
                    wire.DelayNext = true;
                    var flight = owner.RunAsync().AsTask();
                    try
                    {
                        await wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(30));
                        Need(!flight.IsCompleted && store.Instances[id.Id].State == "[\"work\"]", "commit did not land before held ACK");
                        clock = 151;
                        var late = new Message("late", id, "late");
                        if (postLate) owner.Post(late);
                        else { await store.AddAsync(late); await owner.WokenAsync(id); }
                    }
                    finally { wire.Reply.TrySetResult(); }
                    await flight;
                    for (var i = 0; i < 8; i++) { await owner.PollAsync(); await owner.RunAsync(); }
                    var row = store.Instances[id.Id];
                    Need(row.Status == InstanceStatus.Cancelled && Json.Read<List<string>>(row.State).SequenceEqual(["work"]),
                        "late input overtook the command after its trial ACK: " + row.State);
                });
    }
}
