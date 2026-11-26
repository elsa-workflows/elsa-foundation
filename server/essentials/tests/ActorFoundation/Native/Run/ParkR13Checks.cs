namespace Kernel.Native.Run;

internal static class ParkR13Checks
{
    private sealed class Probe(string ending) : Kernel.Sim.Model<List<string>>
    {
        public override string Key => "end-order";
        public readonly List<string> Entries = [];
        public TaskCompletionSource<object?>? LastFirst;
        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken token)
        {
            Entries.Add(job.Key);
            var completion = new TaskCompletionSource<object?>();
            if (job.Key == "first") LastFirst = completion;
            token.Register(() => completion.TrySetCanceled(token));
            return new(completion.Task);
        }
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
            {
                if (turn.Instance.Definition == "child")
                {
                    if (message.Kind == "finish") turn.SetStatus(InstanceStatus.Finished);
                    continue;
                }
                if (message.Kind == Kinds.Start) { turn.StartJob("first"); turn.StartJob("second"); turn.Start(new(Key, "child")); }
                else if (message.Kind == Kinds.JobFinished) turn.Hold(message);
                else if (message.Kind == WorkflowKinds.ChildFinished)
                {
                    if (ending == "throw") throw new InvalidOperationException("original parent fault");
                    turn.SetStatus(ending == "cancel" ? InstanceStatus.Cancelled : InstanceStatus.Finished);
                }
            }
        }
    }
    private static void Require(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    public static IEnumerable<(string Label, Func<Task> Run)> Cases(Func<IActorType, IActorType>? wrap = null)
    {
        foreach (var qualified in new[] { false, true })
        foreach (var ending in new[] { "finish", "cancel", "throw" })
        foreach (var late in new[] { false, true })
        foreach (var healthy in new[] { false, true })
        foreach (var routed in new[] { false, true })
            yield return ($"R13 fenced ended predecessor {qualified}/{ending}/{late}/{healthy}/{routed}",
                () => Run(qualified, ending, late, healthy, routed, wrap));
    }
    private static async Task Run(bool qualified, string ending, bool late, bool healthy, bool routed, Func<IActorType, IActorType>? wrap)
    {
        const string id = "0/parent";
        long time = 100;
        var creation = qualified ? 1 : 0;
        var store = new InMemoryStore(1, () => time);
        var model = new Probe(ending);
        async Task<Worker> Take(string name)
        {
            IActorType actor = new WorkflowInstance([model]);
            if (routed) actor = new ActorTypeRouter(new WorkflowInstance([]), new Dictionary<string, IActorType> { [model.Key] = actor });
            if (wrap is not null) actor = wrap(actor);
            var owner = new Worker(name, 0, store, actor, new() { Monotonic = () => time, MaxAttempts = 1, TrialGroup = 1 });
            await new DurableHost([owner]).ActivateAsync(0);
            return owner;
        }
        async Task Drive(Worker owner, int count) { for (var i = 0; i < count; i++) { await owner.WokenAsync(null); await owner.RunAsync(); } }
        await using var first = await Take("first");
        await store.AddAsync(new("start", new(id), Kinds.Start, Start: new(model.Key, "parent", Creation: qualified ? new(1, 0, "order") : null)));
        await Drive(first, 15);
        var child = store.Instances.Keys.Single(k => k != id);
        time += 11_000;
        await using var second = await Take("second");
        await Drive(second, 1);
        var driver = second.JobCompletion(id, "first", creation);
        Require(driver is not null && model.LastFirst is not null, "earlier job did not physically enter its genuine trial");
        model.LastFirst!.SetResult("done"); await driver!;
        await Drive(second, 1);
        Require(store.Messages.ContainsKey(InboxKey.Ticket(id, "first", creation)), "genuine first result was not held durably");
        Require(second.Post(new("finish", new(child), "finish", TargetCreation: qualified ? 1 : null)), "child finish post failed");
        await Drive(second, 2);
        var ended = store.Instances[id];
        Require(ended.Status == (ending == "throw" ? InstanceStatus.Faulted : ending == "cancel" ? InstanceStatus.Cancelled : InstanceStatus.Finished), "parent did not genuinely end");
        Require(store.Messages[InboxKey.Ticket(id, "second", creation)].Trial == second.Epoch, "later job was not on a genuine failed trial");
        if (qualified)
            Require(store.Messages.TryGetValue(InboxKey.Ticket(id, "first", creation), out var predecessor) && predecessor.Attempts == 0 && ended.Jobs is not null,
                "qualified earlier held predecessor was not retained at the loss boundary");
        else
            Require(!store.Messages.ContainsKey(InboxKey.Ticket(id, "first", creation)) && ended.Jobs is not null && !DurableKeyJson.ReadKeys(ended.Jobs).Contains("first"),
                "legacy batching did not naturally clean its earlier held result before the loss");
        if (healthy) await Drive(second, 5);
        time += 11_000;
        await using var third = await Take("third");
        if (late) await store.AddAsync(new("late-input", new(id), "late-input", TargetCreation: qualified ? 1 : null));
        await Drive(third, 30);
        time += 4_000; await third.BeatAsync();
        var closed = store.Instances[id];
        Require(closed.Status == ended.Status && closed.Fault == ended.Fault && closed.State == ended.State,
            "terminal cleanup overwrote original status/fault/state");
        Require(store.Messages.Count == 0 && closed.Jobs is null && third.Owns && store.Ghost.Count == 0 && !third.Ready.Any() && !third.Trial.Any(),
            "earlier raw terminal cleanup or later poison/ordinary input stayed stranded");
        Require(model.Entries.SequenceEqual(new[] { "first", "second", "first", "second" }), "terminal fencing admitted a new physical job");
    }
}
