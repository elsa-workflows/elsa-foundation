namespace Kernel.Native.Run;

internal static class ParkMetadataFailureChecks
{
    private const string Actor = "0/metadata-failure";
    private sealed class Probe(string mode) : Kernel.Sim.Model<List<string>>, IModelMetadata
    {
        public override string Key => "metadata-failure";
        public readonly Dictionary<string, TaskCompletionSource<object?>> Pending = [];
        public int Queries;
        public int Version = 1;
        public override int StateVersion => Version;
        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellation)
        {
            var result = new TaskCompletionSource<object?>();
            Pending.Add(job.Key, result);
            cancellation.Register(() => result.TrySetCanceled(cancellation));
            return new(result.Task);
        }
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
                if (message.Kind == Kinds.Start)
                {
                    state.Add("checkpoint");
                    turn.StartJob("first");
                    turn.StartJob("sibling");
                }
                else if (message.Kind == Kinds.JobFinished) throw new InvalidOperationException("first fault");
        }
        public bool HandlesMetadata(Message message) => message.Kind == "metadata";
        public ValueTask MetadataAsync(ITurn turn, InstanceStatus status, CancellationToken cancellation)
        {
            Queries++;
            ((List<string>)turn.State).Add("metadata edit");
            if (mode == "throw") throw new InvalidOperationException("metadata failure");
            if (mode == "running") turn.SetStatus(InstanceStatus.Running);
            if (mode == "finished") turn.SetStatus(InstanceStatus.Finished);
            return ValueTask.CompletedTask;
        }
        public void TerminateMetadata(object state) { }
        public bool ProtectsDeletion(object state, long now, ICommitClock clock) => false;
    }
    private sealed class Transport(InMemoryStore store) : IReplyTransport
    {
        public readonly List<Reply> Replies = [];
        public bool Committed = true;
        public void Send(string node, Reply reply)
        {
            if (reply.Request != "query") return;
            Replies.Add(reply);
            Committed &= !store.Messages.ContainsKey("query") && (reply.Status == store.Instances[Actor].Status || reply is { Channel: Request.Response, Item: ModelMetadataUnavailable });
        }
    }

    public static async Task RunAsync(string mode, bool qualified, History history)
    {
        var store = new InMemoryStore(2, () => 100);
        var model = new Probe(mode);
        var transport = new Transport(store);
        var generation = qualified ? 1 : 0;
        if (mode == "future") model.Version = 2;
        await using var owner = new Worker("metadata-owner", 0, store, new WorkflowInstance([model]),
            new() { Monotonic = () => 100, Transport = transport });
        await new DurableHost([owner]).ActivateAsync(0);
        async Task Pump(Func<bool> done)
        {
            for (var i = 0; i < 50 && owner.Owns && !done(); i++) await owner.RunAsync();
            if (!done()) throw new InvalidOperationException("metadata failure sequence did not settle");
        }
        void Require(bool condition, string error)
        {
            if (!condition) throw new InvalidOperationException(error);
        }
        var id = new InstanceRef(Actor);
        await store.AddAsync(new("start", id, Kinds.Start, Start: new(model.Key, "d", History: history, Parent: "1/parent",
            Creation: qualified ? new(1, 0, "metadata-failure") : null, ParentCreation: qualified ? 1 : 0)));
        await owner.WokenAsync(null);
        await Pump(() => model.Pending.ContainsKey("sibling"));
        var first = owner.JobCompletion(Actor, "first", generation)!;
        model.Pending["first"].SetResult("first");
        await first;
        await Pump(() => store.Instances[Actor].Status == InstanceStatus.Faulted);
        Require(store.Messages.Values.Count(m => m.Message.Kind == WorkflowKinds.ChildFinished) == 1, "Park must commit its parent notice");
        var before = store.Instances[Actor];
        if (mode == "future") model.Version = 1;
        var ticket = InboxKey.Ticket(Actor, "sibling", generation);
        Require(before.Fault == "first fault" && store.Messages.ContainsKey(ticket), "real Park did not retain its sibling");
        if (mode == "refused") store.Refuses = row => row.State.Contains("metadata edit");
        await store.AddAsync(new("query", id, "metadata", ReplyTo: ReplyTo.Of("client", "query", 100_000),
            TargetCreation: qualified ? generation : null));
        await owner.PollAsync();
        await owner.LoadAsync();
        await owner.FlushAsync();
        var sibling = owner.JobCompletion(Actor, "sibling", generation)!;
        model.Pending["sibling"].SetResult("done");
        await sibling;
        await Pump(() => !store.Messages.ContainsKey("query") && !store.Messages.ContainsKey(ticket));
        var after = store.Instances[Actor];
        Require(after.Status == before.Status && after.Fault == before.Fault && after.History == history,
            "late metadata replaced the original terminal status, fault or history policy");
        Require(after.Jobs is null && owner.Owns && model.Queries == (mode == "future" ? 0 : 1), "late completion did not settle exactly once");
        Require(mode is "throw" or "refused" or "future" ? after.State == before.State : after.State.Contains("metadata edit"),
            "failed metadata edits must roll back while successful edits commit");
        Require((mode == "future" ? transport.Replies is [{ Channel: Request.Response, Item: ModelMetadataUnavailable }] : transport.Replies is [{ Status: InstanceStatus.Faulted }]) && transport.Committed,
            "caller must receive the retained terminal status once, after its commit");
        for (var i = 0; i < 4; i++) await owner.RunAsync();
        Require(store.Messages.Values.Count(m => m.Message.Kind == WorkflowKinds.ChildFinished) == 1, "late metadata must not emit a second terminal parent notice");
        if (history == History.Minimal) Require(store.Journal.Values.All(j => j.Instance != Actor), "minimal metadata handling wrote history");
    }
}
