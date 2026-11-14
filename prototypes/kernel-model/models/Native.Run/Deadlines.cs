using Kernel.Native;

namespace Kernel.Native.Run;

public static class Deadlines
{
    public static async Task<int> RunAsync()
    {
        long clock = 0;
        var store = new InMemoryStore(1, () => clock);
        await using var worker = new Worker("deadline-owner", 0, store,
            new WorkflowInstance([new NativeModel(Reference.Registry())]),
            new WorkerOptions { Monotonic = () => clock, MaxInstances = 0 });
        if (await new DurableHost([worker]).ActivateAsync(0) is null) throw new InvalidOperationException("take refused");
        await worker.PollAsync();
        var id = InstanceRef.In(0, "deadline-wait");
        if (!worker.Post(new Message("deadline-start", id, Kinds.Start,
                Start: NativeModel.Spec("smoke-wait"), ReplyTo: ReplyTo.Of("caller", "request", 100)), fresh: true))
            throw new InvalidOperationException("start refused");
        for (var pass = 0; pass < 10 && (pass == 0 || worker.NeedsFlush || worker.Ready.Any()); pass++)
            await worker.RunAsync();
        var row = store.Instances[id.Id];
        var before = worker.Instances.SingleOrDefault(i => i.Id == id.Id);
        var pinned = before.Row is { Status: InstanceStatus.Running } && worker.Callers.Count() == 1 && worker.Evicted == 0;
        var firstWake = await worker.RunAsync();
        var scheduled = firstWake == TimeSpan.FromMilliseconds(100);
        clock = 99;
        var lastWake = await worker.RunAsync();
        pinned &= worker.Instances.Any(i => i.Id == id.Id);
        scheduled &= lastWake == TimeSpan.FromMilliseconds(1);
        clock += (long)Math.Ceiling(lastWake!.Value.TotalMilliseconds);
        await worker.RunAsync();
        var evicted = !worker.Instances.Any(i => i.Id == id.Id) && worker.Evicted == 1;
        var retained = store.Instances[id.Id] == row && store.Subscriptions.Count == 1;
        Console.WriteLine($"deadline idle eviction: pinned before={pinned}, deadline scheduled={scheduled}, evicted at deadline={evicted}, committed wait retained={retained}");
        return pinned && scheduled && evicted && retained && await QueuedHttp() ? 0 : 1;
    }

    private static async Task<bool> QueuedHttp()
    {
        long clock = 100;
        var store = new InMemoryStore(1, () => clock);
        var owner = new Owner("queued-http", 0, store, Reference.Registry(), new Network(),
            monotonic: () => clock, lease: TimeSpan.FromHours(1));
        await owner.StartAsync();
        var checkpointed = false;
        await owner.Http.SendAsync(new HttpRequest("POST", "slow"), owner.Worker, Modes.Ephemeral, async () =>
        {
            clock += 29_990; // the route's 30-second absolute deadline now has only 10 ms left
            await owner.Worker.RunAsync();
            checkpointed = store.Instances.Count == 1;
        });
        await owner.Worker.DisposeAsync();
        Console.WriteLine($"queued native HTTP: timer beyond original deadline checkpointed={checkpointed}");
        return checkpointed;
    }
}
