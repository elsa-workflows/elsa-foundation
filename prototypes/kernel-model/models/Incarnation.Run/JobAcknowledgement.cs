using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Workflows;

internal static class JobAcknowledgement
{
    internal static async Task Run(bool qualified, bool caughtUp = false)
    {
        var calls = 0;
        var store = new InMemoryStore(1, () => 100);
        var wire = new DelayedCommit(store);
        var http = new Http(0, (_, _) => { calls++; return new ValueTask<object?>("done"); });
        await using var worker = new Worker("job-ack", 0, wire, new WorkflowInstance([http]), new WorkerOptions { Monotonic = () => 100 });
        await new DurableHost([worker]).ActivateAsync(0);
        if (caughtUp) await worker.PollAsync();
        // No initial poll: until caught up, this first job goes through the table and can be polled before its commit ack.
        var target = InstanceRef.In(0, "job-ack");
        var start = Http.Spec(Http.Work) with { Creation = qualified ? new(1, 0, "job-ack") : null };
        worker.Post(new Message("job-ack-start", target, Kinds.Start, Start: start, TargetCreation: qualified ? 1 : null), fresh: true);
        worker.RunTurn(target.Id); wire.DelayNext = true;
        var flush = worker.FlushAsync(); await wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Require(store.Instances.ContainsKey(target.Id) && worker.Instances.Single().Row is null && worker.Instances.Single().Busy,
            "fixture did not hold the actual first commit acknowledgement");
        await worker.PollAsync();
        Require(calls == 0 && (caughtUp || worker.Messages.Any(m => m.Id == Worker.TicketId(target.Id, "work", qualified ? 1 : 0))),
            "job did not wait behind the staged owner checkpoint");
        wire.Reply.TrySetResult(); await flush;
        if (!caughtUp) await worker.PollAsync();
        var completion = worker.JobCompletion(target.Id, "work", qualified ? 1 : 0);
        Require(completion is not null, "actual job driver was not available for observation");
        await completion!.WaitAsync(TimeSpan.FromSeconds(5));
        Require(worker.JobResultReady(target.Id, "work"), "completed job result was invisible to its observer");
        for (var i = 0; i < 100 && store.Instances[target.Id].Status == InstanceStatus.Running; i++)
        {
            await worker.PollAsync(); if (worker.NeedsLoad) await worker.LoadAsync();
            foreach (var ready in worker.Ready.ToArray()) worker.RunTurn(ready);
            if (worker.NeedsFlush) await worker.FlushAsync();
        }
        Require(calls == 1 && store.Instances[target.Id].Status == InstanceStatus.Finished, caughtUp
            ? "handed-over job was not started after acknowledgement" : "polled-before-ack job was lost after acknowledgement");
        Require(!worker.JobResultReady(target.Id, "work"), "consumed job result remained ready");
        Console.WriteLine($"PASS creation:job-ack,qualified={qualified},caughtUp={caughtUp}");
    }

    private static void Require(bool ok, string detail)
    { if (!ok) throw new InvalidOperationException("J0: " + detail); }
}
