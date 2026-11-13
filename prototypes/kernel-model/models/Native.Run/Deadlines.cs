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
        clock = 99;
        await worker.RunAsync();
        pinned &= worker.Instances.Any(i => i.Id == id.Id);
        clock = 100;
        await worker.RunAsync();
        var evicted = !worker.Instances.Any(i => i.Id == id.Id) && worker.Evicted == 1;
        var retained = store.Instances[id.Id] == row && store.Subscriptions.Count == 1;
        Console.WriteLine($"deadline idle eviction: pinned before={pinned}, evicted at deadline={evicted}, committed wait retained={retained}");
        return pinned && evicted && retained ? 0 : 1;
    }
}
