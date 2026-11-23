using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Workflows;

internal static partial class ShortWaitRaces
{
    private const string PrivateJob = "work\uD800";

    private sealed class AdmissionModel : Model<List<string>>
    {
        public override string Key => "local-job-admission";
        public readonly List<(string Actor, string Key)> Entries = [];
        public readonly TaskCompletionSource<object?> Result = new();
        public override async ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken token)
        {
            Entries.Add((job.Instance.Id, job.Key));
            return await Result.Task.WaitAsync(token).ConfigureAwait(false);
        }

        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var m in turn.Inbox)
            {
                if (m.Kind == Kinds.Start)
                {
                    turn.StartJob(PrivateJob);
                    turn.StartJob("other");
                }
                if (m.Kind == "park") throw new InvalidOperationException("authored fault");
                if (m.Kind == "end") turn.SetStatus(InstanceStatus.Finished);
                if (m.Kind == "cancel-one") turn.CancelJob(PrivateJob);
            }
        }
    }

    private sealed class ConcurrentJobs : Model<List<string>>
    {
        public override string Key => "concurrent-jobs";
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<object?>> Running = new();
        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken token)
        {
            var gate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Need(Running.TryAdd(job.Key, gate), "duplicate physical entry");
            return new(gate.Task);
        }
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
                if (message.Kind == Kinds.Start)
                    for (var i = 0; i < 32; i++) turn.StartJob(i.ToString());
                else if (message.Kind == Kinds.JobFinished) state.Add(message.Read<JobFinished>().Key);
        }
    }

    private static async Task ConcurrentReturns(string schedule)
    {
        var store = new InMemoryStore(1, () => 100);
        var wire = new DelayedCommit(store);
        var model = new ConcurrentJobs();
        var target = InstanceRef.In(0, "many");
        await using var worker = new Worker("owner", 0, wire, new WorkflowInstance([model]),
            new() { Monotonic = () => 100, JobScheduler = TaskScheduler.Default });
        await new DurableHost([worker]).ActivateAsync(0);
        await worker.PollAsync();
        worker.Post(new("start", target, Kinds.Start, Start: new(model.Key, "def")), fresh: true);
        await worker.RunAsync();
        Need(SpinWait.SpinUntil(() => model.Running.Count == 32, TimeSpan.FromSeconds(10)), "32 real entries required");
        var drivers = model.Running.Keys.Select(k => worker.JobCompletion(target.Id, k)!).ToArray();
        if (schedule == "reset")
        {
            await worker.DisposeAsync();
            await new DurableHost([worker]).ActivateAsync(0);
        }
        Task? flush = null;
        if (schedule == "held-ack")
        {
            worker.Post(new("ordinary", target, "edit"));
            worker.RunTurn(target.Id);
            wire.DelayNext = true;
            flush = worker.FlushAsync();
            await wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        await Task.WhenAll(model.Running.Values.Select(g => Task.Run(() => g.SetResult(42))));
        await Task.WhenAll(drivers).WaitAsync(TimeSpan.FromSeconds(10));
        if (flush is not null)
        {
            Need(store.Messages.Count == 32, "result acknowledged under a different commit");
            wire.Reply.SetResult(); await flush;
        }
        if (schedule == "reset")
        {
            Need(!worker.Ready.Any() && store.Messages.Count == 32 && !worker.Instances.Any(),
                "old generation published into a new activation");
        }
        else
        {
            for (var i = 0; i < 5; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            var seen = Json.Read<List<string>>(store.Instances[target.Id].State);
            Need(seen.Count == 32 && seen.Distinct().Count() == 32 && store.Messages.Count == 0,
                $"owner published {seen.Count} results with {seen.Distinct().Count()} keys, {store.Messages.Count} owed");
        }
        Console.WriteLine($"PASS concurrent publication 32 actual drivers {schedule}");
    }

    public static async Task JobEntry()
    {
        foreach (var schedule in new[] { "ordinary", "held-ack", "reset" }) await ConcurrentReturns(schedule);
        foreach (var action in new[] { Kinds.Suspend, "park", "end", "cancel-one" })
        foreach (var phase in new[] { "before-decision", "decided", "applied", "acknowledged" })
        {
            var store = new InMemoryStore(1, () => 100);
            var wire = new DelayedCommit(store);
            var model = new AdmissionModel();
            var scheduler = new HeldScheduler();
            var actor = InstanceRef.In(0, "target");
            var sibling = InstanceRef.In(0, "sibling");
            await using var worker = new Worker("owner", 0, wire, new WorkflowInstance([model]),
                new() { JobScheduler = scheduler, Monotonic = () => 100 });
            await new DurableHost([worker]).ActivateAsync(0);
            await worker.PollAsync();
            foreach (var id in new[] { actor, sibling })
                Need(worker.Post(new("start-" + id.Id, id, Kinds.Start, Start: new(model.Key, "def")), fresh: true), "start refused");
            await worker.RunAsync();
            Need(scheduler.Count == 4 && model.Entries.Count == 0, "setup did not queue four real bodies");
            var targetDrivers = new[] { PrivateJob, "other" }.Select(key => worker.JobCompletion(actor.Id, key)!).ToArray();
            if (phase == "before-decision") scheduler.RunAll();
            Need(worker.Post(new("restrict", actor, action)), "command refused");
            worker.RunTurn(actor.Id);
            if (phase == "decided") scheduler.RunAll();
            wire.DelayNext = true;
            var flush = worker.FlushAsync();
            await wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (phase == "applied") scheduler.RunAll();
            wire.Reply.SetResult();
            await flush;
            if (phase == "acknowledged") scheduler.RunAll();
            Need(model.Entries.Count(e => e.Actor == sibling.Id) == 2, "another actor lost entry");
            var expected = phase == "before-decision" ? 2 : action == "cancel-one" ? 1 : 0;
            Need(model.Entries.Count(e => e.Actor == actor.Id) == expected, "incorrect target entry count");
            if (phase != "before-decision" && action == "cancel-one")
                Need(model.Entries.Contains((actor.Id, "other")), "exact cancellation blocked another key");
            if (action == Kinds.Suspend && phase != "before-decision")
            {
                await Task.WhenAll(targetDrivers).WaitAsync(TimeSpan.FromSeconds(5));
                Need(store.Instances[actor.Id].Suspended, "suspend did not land");
                Need(worker.Post(new("resume", actor, Kinds.Resume)), "resume refused");
                for (var i = 0; i < 5; i++) { await worker.PollAsync(); await worker.RunAsync(); scheduler.RunAll(); }
                Need(!store.Instances[actor.Id].Suspended && model.Entries.Count(e => e.Actor == actor.Id) == 2,
                    "resume did not release exactly the two owed jobs");
            }
            Console.WriteLine($"PASS local job admission {action} {phase}");
        }

        foreach (var mode in new[] { "unknown-before", "unknown-after", "transient", "definite", "lost-fence" })
        {
            long clock = 100;
            var store = new InMemoryStore(1, () => clock);
            var fault = new CutFaultStore(store);
            var model = new AdmissionModel();
            var oldScheduler = new HeldScheduler();
            var freshScheduler = new HeldScheduler();
            var id = InstanceRef.In(0, "recovery");
            Worker Owner(string name, HeldScheduler scheduler) => new(name, 0, fault, new WorkflowInstance([model]),
                new() { JobScheduler = scheduler, Monotonic = () => clock, Lease = TimeSpan.FromSeconds(2) });
            await using var old = Owner("old", oldScheduler);
            await new DurableHost([old]).ActivateAsync(0);
            await old.PollAsync();
            old.Post(new("start", id, Kinds.Start, Start: new(model.Key, "def")), fresh: true);
            await old.RunAsync();
            Need(oldScheduler.Count == 2, "no queued jobs before failure");
            old.Post(new("suspend", id, Kinds.Suspend));
            old.RunTurn(id.Id);
            if (mode == "lost-fence") store.Partitions[0].Epoch++;
            else fault.Arm(mode);
            await old.FlushAsync();
            oldScheduler.RunAll();
            Need(model.Entries.Count == 0, "stale callback entered");
            if (mode is "transient" or "definite")
            {
                Need(old.Owns && (mode == "definite" ? store.Instances[id.Id].Status == InstanceStatus.Faulted :
                    store.Instances[id.Id].Suspended), "unexpected retry/park disposition");
            }
            else
            {
                Need(!old.Owns, "ambiguous or lost fence retained ownership");
                clock += 3000;
                await using var fresh = Owner("successor", freshScheduler);
                await new DurableHost([fresh]).ActivateAsync(0);
                for (var i = 0; i < 8; i++)
                {
                    await fresh.PollAsync();
                    await fresh.RunAsync();
                    var drivers = new[] { PrivateJob, "other" }.Select(key => fresh.JobCompletion(id.Id, key)).OfType<Task>().ToArray();
                    freshScheduler.RunAll();
                    model.Result.TrySetResult(42);
                    await Task.WhenAll(drivers).WaitAsync(TimeSpan.FromSeconds(5));
                }
                var expected = mode == "unknown-after" ? 0 : 2;
                Need(model.Entries.Count == expected, $"takeover {mode} entered {model.Entries.Count}, expected {expected}");
            }
            Console.WriteLine($"PASS local job admission recovery {mode}");
        }
    }
}
