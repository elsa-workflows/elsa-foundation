using System.Collections.Concurrent;
using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Workflows;

internal static class JobCreationRace
{
    internal static async Task Run()
    {
        var owner = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, maxConcurrencyLevel: 1);
        try { await Task.Factory.StartNew(RunOwner, CancellationToken.None, TaskCreationOptions.DenyChildAttach, owner.ExclusiveScheduler).Unwrap(); }
        finally { owner.Complete(); await owner.Completion; }
    }

    private static async Task RunOwner()
    {
        var bodies = new ConcurrentQueue<long>();
        var oldReturn = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newReturn = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<object?> Tool(IJob job, CancellationToken token)
        {
            Require(TaskScheduler.Current == TaskScheduler.Default, "tool did not execute outside the owner scheduler");
            bodies.Enqueue(job.Creation);
            // Deliberately non-cooperative like an already running external operation. Driver completion is awaited below.
            return job.Creation == 1 ? await oldReturn.Task : await newReturn.Task;
        }
        var target = InstanceRef.In(0, "job-generation");
        var store = new InMemoryStore(1, () => 100);
        await using var worker = new Worker("job-generation", 0, store, new WorkflowInstance([new Http(0, Tool)]),
            new WorkerOptions { Monotonic = () => 100, MaxInstances = 0, JobScheduler = TaskScheduler.Default });
        await new DurableHost([worker]).ActivateAsync(0);
        StartSpec Start(long generation) => Http.Spec(Http.Work) with { Creation = new(generation, generation - 1, "job-" + generation) };
        async Task Pump(Func<bool> done, int limit = 1000)
        {
            for (var i = 0; i < limit && !done(); i++)
            {
                await worker.PollAsync(); if (worker.NeedsLoad) await worker.LoadAsync();
                foreach (var ready in worker.Ready.ToArray()) worker.RunTurn(ready);
                if (worker.NeedsFlush) await worker.FlushAsync();
                await Task.Delay(1); // give the independent default scheduler a pass; retain the bounded pump
            }
            Require(done(), "owned worker did not reach the required job boundary");
        }
        async Task Add(string id, string kind, long generation, StartSpec? start = null)
        { await store.AddAsync(new Message(id, target, kind, Start: start, TargetCreation: generation)); }

        var drivers = new List<Task>();
        try
        {
            await Add("job-start-1", Kinds.Start, 1, Start(1));
            await Pump(() => bodies.Count == 1 && store.Instances.ContainsKey(target.Id));
            var oldTicket = store.Messages.Values.Single(m => m.Message.Kind == Kinds.Job).Message;
            var oldDriver = worker.JobCompletion(target.Id, "work", 1) ?? throw new InvalidOperationException("J1: no old driver task");
            drivers.Add(oldDriver);
            await Add("job-cancel-1", Kinds.Terminate, 1); await Pump(() => store.Instances[target.Id].Status == InstanceStatus.Cancelled);
            Require(!oldDriver.IsCompleted, "old operation did not remain in flight after cancellation");
            await Add("job-delete-1", Kinds.Delete, 1); await Pump(() => store.Instances[target.Id].Retired);
            await Add("job-start-2", Kinds.Start, 2, Start(2)); await Pump(() => bodies.Count == 2 && store.Instances[target.Id].Creation == 2);
            var newDriver = worker.JobCompletion(target.Id, "work", 2) ?? throw new InvalidOperationException("J1: no new driver task");
            drivers.Add(newDriver);
            var replacement = store.Instances[target.Id];
            oldReturn.TrySetResult("old"); await oldDriver.WaitAsync(TimeSpan.FromSeconds(5));
            Require(store.Instances[target.Id] == replacement && !newDriver.IsCompleted, "old completed driver changed or completed the replacement");

            foreach (var pinned in new[] { true, false })
            {
                var replay = oldTicket with { TargetCreation = pinned ? 1 : null };
                Require(await store.AddAsync(replay), "old ticket marker still exists; dispatch would not be exercised");
                await Pump(() => bodies.Count > 2 || !store.Messages.ContainsKey(replay.Id));
                Require(bodies.Count == 2 && store.Instances[target.Id] == replacement, "stale/missing-identity job executed against the new creation");
            }
            Require(Worker.TicketId("0/a@1!b", "x", 2) != Worker.TicketId("0/a", "b@2!x", 1), "qualified job IDs alias actor/key punctuation");
            newReturn.TrySetResult("new"); await newDriver.WaitAsync(TimeSpan.FromSeconds(5));
            await Pump(() => store.Instances[target.Id].Status == InstanceStatus.Finished);
            Require(bodies.ToArray().SequenceEqual([1L, 2L]), "job bodies were retargeted or repeated across creations");
            Console.WriteLine("PASS creation:job-generation-race");
        }
        finally
        {
            foreach (var generation in new[] { 0L, 1L, 2L })
                if (worker.JobCompletion(target.Id, "work", generation) is { } driver) drivers.Add(driver);
            oldReturn.TrySetResult("cleanup"); newReturn.TrySetResult("cleanup");
            await Task.WhenAll(drivers.Distinct()).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static void Require(bool ok, string detail)
    { if (!ok) throw new InvalidOperationException("J1: " + detail); }
}
