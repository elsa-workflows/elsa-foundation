namespace Kernel.Store.Conformance;

internal static class OpaqueTickets
{
    private static readonly string[] Keys = ["x\ud800", "x\ufffd", "x\ud801", "x\0"];

    internal static async Task ThroughWorker(Context context)
    {
        var actor = new InstanceRef("0/opaque");
        var firstModel = new KeyModel();
        var remaining = Keys.Where(key => key != Keys[2]).ToArray();
        await using (var old = new Worker("old", 0, context.S, new WorkflowInstance([firstModel])))
        {
            await new DurableHost([old]).ActivateAsync(0);
            Context.That(old.Post(new("start", actor, Kinds.Start, Start: new(firstModel.Key, "keys"))), "volatile legacy start refused");
            await Drain(old);
            var row = (await context.S.LoadAsync([actor.Id])).Single();
            Context.That(row.Status == InstanceStatus.Running && row.Jobs is not null && DurableKeyJson.ReadKeys(row.Jobs).ToHashSet().SetEquals(Keys),
                "one workflow turn did not store every distinct issued ticket");
            Context.That(firstModel.Executed.SetEquals(Keys), "the original owner failed to drive the distinct handlers");
        }
        var store = context.Provided.NewClient();
        var model = new KeyModel();
        await using var owner = new Worker("replacement", 0, store, new WorkflowInstance([model]));
        await new DurableHost([owner]).ActivateAsync(0);
        // Address the reloaded checkpoint before polling its suspects. No live ticket or driver on this
        // owner can provide the cancellation key. The exact original D801 must come from durable state.
        owner.Post(new("cancel", actor, "cancel-third"));
        await owner.LoadAsync(); owner.RunTurn(actor.Id); await owner.FlushAsync();
        var saved = (await store.LoadAsync([actor.Id])).Single();
        var owed = await store.PollAsync(0, long.MaxValue, TimeSpan.Zero, 100);
        Context.That(owner.Owns && saved.Jobs is not null && DurableKeyJson.ReadKeys(saved.Jobs).ToHashSet().SetEquals(remaining) &&
            owed.Messages.Select(m => m.Message.InboxKey).ToHashSet().SetEquals(remaining.Select(k => InboxKey.Ticket(actor.Id, k))),
            "cold cancel removed a different UTF-16 key or failed to retire its original ticket");
        await Drain(owner);
        // Suspect tickets on a fresh owner can run serially under trial. Finish each genuine active driver
        // before asking the worker to select the next one; the order of distinct issued tickets is immaterial.
        for (var i = 0; i < remaining.Length; i++)
        {
            var key = remaining.FirstOrDefault(k => model.Executed.Contains(k) && !model.Completed.Contains(k))
                ?? throw new ConformanceException("No active exact ticket on the replacement owner.");
            var driver = owner.JobCompletion(actor.Id, key) ?? throw new ConformanceException("Recovered driver lost its exact handle.");
            model.Bodies[key].SetResult("ok");
            await driver.WaitAsync(TimeSpan.FromSeconds(10));
            await Drain(owner);
        }
        var end = (await store.LoadAsync([actor.Id])).Single();
        Context.That(end is { Status: InstanceStatus.Finished, Jobs: null } && model.Completed.Count == remaining.Length && model.Completed.ToHashSet().SetEquals(remaining),
            "the workflow did not consume each genuine completion once with its original key");
        Context.That((await store.PollAsync(0, long.MaxValue, TimeSpan.Zero, 100)).Messages.Count == 0, "an issued ticket remains owed");
    }

    internal static async Task ColdTerminate(Context context)
    {
        var target = new InstanceRef("0/terminate");
        await using (var first = new Worker("original", 0, context.S, new WorkflowInstance([new KeyModel()])))
        {
            await new DurableHost([first]).ActivateAsync(0);
            first.Post(new("start", target, Kinds.Start, Start: new("opaque-keys", "keys")));
            await Drain(first);
            var before = (await context.S.LoadAsync([target.Id])).Single();
            Context.That(before.Jobs is not null && DurableKeyJson.ReadKeys(before.Jobs).ToHashSet().SetEquals(Keys), "initial registry lost an exact key");
        }
        var store = context.Provided.NewClient();
        await using var cold = new Worker("successor", 0, store, new WorkflowInstance([new KeyModel()]));
        await new DurableHost([cold]).ActivateAsync(0);
        cold.Post(new("terminate", target, Kinds.Terminate));
        await cold.LoadAsync(); cold.RunTurn(target.Id); await cold.FlushAsync();
        var after = (await store.LoadAsync([target.Id])).Single();
        Context.That(cold.Owns && after is { Status: InstanceStatus.Cancelled, Jobs: null } &&
            (await store.PollAsync(0, long.MaxValue, TimeSpan.Zero, 100)).Messages.Count == 0,
            "cold terminate failed to remove all four original ticket rows and their registry together");
    }

    private static async Task Drain(Worker worker)
    {
        for (int i = 0; i < 4; i++) { await worker.PollAsync(); await worker.RunAsync(); }
        Context.That(worker.Owns, "the job's worker reset");
    }

    private sealed class KeyModel : IExecutionModel
    {
        internal readonly Dictionary<string, TaskCompletionSource<object?>> Bodies = Keys.ToDictionary(key => key,
            _ => new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously));
        internal readonly HashSet<string> Executed = [];
        internal readonly List<string> Completed = [];
        public string Key => "opaque-keys";
        public Type StateType => typeof(int);
        public int StateVersion => 1;
        public object CreateState(StartSpec spec) => 0;
        public object UpgradeState(string state, int version) => 0;
        public ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken token)
        {
            Executed.Add(job.Key);
            return new(Bodies[job.Key].Task.WaitAsync(token));
        }
        public ValueTask TurnAsync(ITurn turn, CancellationToken token)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Kinds.Start) foreach (var key in Keys) turn.StartJob(key);
                if (message.Kind == "cancel-third") turn.CancelJob(Keys[2]);
                if (message.Kind == Kinds.JobFinished)
                {
                    Completed.Add(message.Read<JobFinished>().Key);
                    if (Completed.Count == Keys.Length - 1) turn.SetStatus(InstanceStatus.Finished);
                }
            }
            return ValueTask.CompletedTask;
        }
    }
}
