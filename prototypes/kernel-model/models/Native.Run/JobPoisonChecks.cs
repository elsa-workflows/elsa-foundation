namespace Kernel.Native.Run;

/// <summary>Job tickets, the parked checkpoint and the later termination of its child, through real workers.</summary>
internal static class JobPoisonChecks
{
    private sealed class Probe(bool throwOnResult, bool withChild = true, bool cancelOther = false) : Kernel.Sim.Model<List<string>>
    {
        public override string Key => "job-park-probe";
        public readonly List<string> Started = [];
        public readonly Dictionary<string, TaskCompletionSource<object?>> Pending = [];
        public readonly List<string> Results = new();

        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken)
        {
            Started.Add(job.Key);
            var result = new TaskCompletionSource<object?>();
            Pending[job.Key] = result;
            cancellationToken.Register(() => result.TrySetCanceled(cancellationToken));
            return new(result.Task);
        }

        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
            {
                if (turn.Instance.Definition == "child") continue;
                if (message.Kind == Kinds.Start)
                {
                    state.Add("saved");
                    if (withChild) turn.Start(new StartSpec(Key, "child"));
                    turn.Subscribe("approval", "waiting");
                    turn.StartJob("poison");
                    turn.StartJob("other");
                }
                if (message.Kind == Kinds.JobFinished)
                {
                    var key = message.Read<JobFinished>().Key;
                    Results.Add(key);
                    if (cancelOther && key == "poison") turn.CancelJob("other");
                    if (throwOnResult)
                    {
                        state.Add("uncommitted");
                        throw new InvalidOperationException("job result turn failed");
                    }
                }
            }
        }
    }

    private const string Id = "0/probe";
    private static readonly string Poison = Worker.TicketId(Id, "poison");
    private static readonly string Other = Worker.TicketId(Id, "other");

    private static void Check(bool value, string what)
    {
        if (!value) throw new InvalidOperationException(what);
    }

    internal static async Task CancellationAsync(bool throwOnResult, bool cancelOther, bool refuseCommit = false)
    {
        var store = new InMemoryStore(1, () => 100);
        var probe = new Probe(throwOnResult, withChild: false, cancelOther: cancelOther);
        var owner = new Worker("owner", 0, store, new WorkflowInstance([probe]), new WorkerOptions { Monotonic = () => 100 });
        async Task Drive(Func<bool> done)
        {
            for (var i = 0; i < 12 && !done(); i++) await owner.RunAsync();
        }
        await new DurableHost([owner]).ActivateAsync(0);
        await store.AddAsync(new Message("start", new InstanceRef(Id), Kinds.Start, Start: new StartSpec(probe.Key, "solo")));
        await owner.WokenAsync(null);
        await Drive(() => probe.Pending.ContainsKey("other"));
        var checkpoint = store.Instances[Id];
        Check(checkpoint.Children is null && probe.Pending.ContainsKey("other"), "start must leave two jobs and no children");
        var sibling = probe.Pending["other"].Task;
        if (refuseCommit)
        {
            store.Refuses = row =>
            {
                if (row.Status != InstanceStatus.Running || row.Jobs is not null) return false;
                // Complete the sibling with its cancellation reserved, before the failed write returns.
                probe.Pending["other"].TrySetResult("during refused commit");
                return true;
            };
        }
        probe.Pending["poison"].SetResult("first");
        await Drive(() => !store.Messages.ContainsKey(Poison));
        var after = store.Instances[Id];
        if (!throwOnResult && !refuseCommit)
        {
            Check(after.Status == InstanceStatus.Running && after.Jobs is null && sibling.IsCanceled && !store.Messages.ContainsKey(Other),
                "committed cancellation must remove the sibling ticket and cancel the running job");
            Check(probe.Results.SequenceEqual(["poison"]), "a committed cancellation must suppress the sibling result");
            return;
        }
        var fault = refuseCommit ? $"the row of {Id} was refused" : "job result turn failed";
        Check(after.Status == InstanceStatus.Faulted && after.Fault == fault && after.State == checkpoint.State
            && after.Children is null && Json.Read<string[]>(after.Jobs!).SequenceEqual(["other"]) && store.Messages.ContainsKey(Other)
            && (refuseCommit ? sibling.IsCompletedSuccessfully : !sibling.IsCompleted),
            "Park must retain the live sibling ticket, token and saved checkpoint");
        if (cancelOther)
        {
            if (!refuseCommit) probe.Pending["other"].SetResult("late");
            await Drive(() => !store.Messages.ContainsKey(Other));
            Check(!store.Messages.ContainsKey(Other) && store.Instances[Id] == (after with { Jobs = null, Revision = after.Revision + 1 }),
                "abandoned cancellation must allow the sibling result to retire the ticket on this same owner");
        }
        else
        {
            await store.AddAsync(new Message("stop", new InstanceRef(Id), Kinds.Terminate));
            await owner.WokenAsync(null);
            await Drive(() => !store.Messages.ContainsKey("stop"));
            Check(!store.Messages.ContainsKey("stop") && !store.Messages.ContainsKey(Other) && sibling.IsCanceled
                && store.Instances[Id] == (after with { Jobs = null, Revision = after.Revision + 1 }),
                "terminate of a childless parked row must retire its sibling ticket and cancel its running job");
        }
        Check(probe.Results.SequenceEqual(["poison"]) && owner.Owns && store.Ghost.Count == 0, "the parked model must not handle the sibling result or lose the fence");
    }

    internal static async Task RunAsync(bool resultThrows)
    {
        var time = 100L;
        var store = new InMemoryStore(1, () => time);
        var probe = new Probe(resultThrows);
        var ownerNumber = 0;
        async Task<Worker> Take()
        {
            time += 11_000;
            var owner = new Worker($"owner-{++ownerNumber}", 0, store, new WorkflowInstance([probe]),
                new WorkerOptions { MaxAttempts = 1, Monotonic = () => time });
            await new DurableHost([owner]).ActivateAsync(0);
            await owner.WokenAsync(null);
            Check(owner.Owns, "witness failed to take partition");
            return owner;
        }
        async Task Drive(Worker owner, Func<bool> done)
        {
            for (var i = 0; i < 50 && owner.Owns && !done(); i++) await owner.RunAsync();
        }

        var owner = await Take();
        await store.AddAsync(new Message("start", new InstanceRef(Id), Kinds.Start, Start: new StartSpec(probe.Key, "parent")));
        await Drive(owner, () => store.Instances.Count == 2 && probe.Started.Contains("other"));
        var checkpoint = store.Instances[Id];
        var child = Json.Read<string[]>(checkpoint.Children!).Single();
        Check(store.Instances.ContainsKey(child) && store.Messages.ContainsKey(Poison) && store.Messages.ContainsKey(Other), "jobs and child must be live initially");
        if (resultThrows) probe.Pending["poison"].SetResult("value");
        else
        {
            owner = await Take();
            await Drive(owner, () => store.Messages[Poison].Trial == owner.Epoch);
            Check(store.Messages[Poison].Trial == owner.Epoch && store.Messages[Other].Trial != owner.Epoch, "only the poison ticket must be on trial at the owner's loss");
            owner = await Take();
            Check(store.Messages[Poison].Attempts == 1 && store.Messages[Other].Attempts == 0, "the takeover must count exactly the lost job trial");
        }
        var poisonStarts = probe.Started.Count(s => s == "poison");
        await Drive(owner, () => store.Instances[Id].Status == InstanceStatus.Faulted);
        var parked = store.Instances[Id];
        Check(parked.Status == InstanceStatus.Faulted && parked.State == checkpoint.State && parked.Children == checkpoint.Children, "Park must retain the committed state and child");
        var fault = resultThrows ? "job result turn failed" : $"message {Poison} was on trial when 1 owners were lost";
        Check(parked.Fault == fault && probe.Started.Count(s => s == "poison") == poisonStarts, "Park must record its cause and never restart the poison ticket");
        Check(!store.Messages.ContainsKey(Poison) && store.Messages.ContainsKey(Other) && !store.Subscriptions.Any(s => s.Instance == Id), "Park must consume its ticket, preserve the other ticket and remove waits");

        if (resultThrows)
        {
            probe.Pending["other"].SetResult("late");
            await Drive(owner, () => !store.Messages.ContainsKey(Other));
            Check(!store.Messages.ContainsKey(Other), "the late result must actually be consumed before takeover");
        }

        var beforeTerminate = store.Instances[Id];
        if (resultThrows) owner = await Take();
        await store.AddAsync(new Message("terminate", new InstanceRef(Id), Kinds.Terminate));
        await owner.WokenAsync(null);
        for (var attempt = 0; attempt < 2 && store.Messages.ContainsKey("terminate"); attempt++)
        {
            await Drive(owner, () => !store.Messages.ContainsKey("terminate"));
            if (!owner.Owns && attempt == 0) owner = await Take();
        }
        Check(!store.Messages.ContainsKey("terminate"), $"terminate repeatedly reset after Park: Jobs={store.Instances[Id].Jobs}, poison ticket present={store.Messages.ContainsKey(Poison)}, ghost={store.Ghost.LastOrDefault()}");
        await Drive(owner, () => store.Instances[child].Status == InstanceStatus.Cancelled);
        var ended = store.Instances[Id];
        Check(parked.Jobs is { } remaining && Json.Read<string[]>(remaining).SequenceEqual(["other"]), "the Park commit must retain only the other job key");
        if (resultThrows)
        {
            Check(beforeTerminate == (parked with { Jobs = null, Revision = parked.Revision + 1 }), "the late result may only retire its own key and advance the revision");
            Check(store.Journal.Values.Count(j => j.Instance == Id && j.Revision == beforeTerminate.Revision) == 1, "history must record the late result exactly once");
        }
        Check(ended.Revision == beforeTerminate.Revision + 1 && ended.Status == InstanceStatus.Faulted && ended.State == checkpoint.State && ended.Fault == fault && ended.Jobs is null && ended.Children is null, "terminate must retain the faulted checkpoint and clear outstanding work");
        Check(!store.Messages.ContainsKey(Other) && store.Instances[child].Status == InstanceStatus.Cancelled, "the child must terminate and the remaining ticket disappear");
        Check(store.Ghost.Count == 0, "unexpected store count mismatch: " + store.Ghost.FirstOrDefault());
    }
}
