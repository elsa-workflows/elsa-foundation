namespace Kernel.Native.Run;

// Worker-created parked rows: no repair of the inconsistent rows from earlier prototypes.
internal static class ParkMetadataChecks
{
    private const string Id = "0/metadata-park";
    private sealed class Probe : Kernel.Sim.Model<List<string>>, IModelMetadata
    {
        public override string Key => "park-metadata";
        public Dictionary<string, TaskCompletionSource<object?>> Pending = [];
        public int Queries;
        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken token)
        {
            var completion = new TaskCompletionSource<object?>();
            Pending[job.Key] = completion;
            token.Register(() => completion.TrySetCanceled(token));
            return new(completion.Task);
        }
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
                if (message.Kind == Kinds.Start) { state.Add("checkpoint"); turn.StartJob("first"); turn.StartJob("sibling"); }
                else if (message.Kind == Kinds.JobFinished) throw new InvalidOperationException("original fault");
        }
        public bool HandlesMetadata(Message message) => message.Kind == "metadata";
        public ValueTask MetadataAsync(ITurn turn, InstanceStatus status, CancellationToken token)
        {
            foreach (var message in turn.Inbox.Where(HandlesMetadata))
            {
                Queries++;
                ((List<string>)turn.State).Add("metadata saved");
                turn.Reply(message, Request.Response, "answered");
            }
            return ValueTask.CompletedTask;
        }
        public void TerminateMetadata(object state) { }
        public bool ProtectsDeletion(object state, long now, ICommitClock clock) => false;
    }
    private sealed class Transport(InMemoryStore store) : IReplyTransport
    {
        public List<Reply> Replies = [];
        public bool Committed;
        public void Send(string node, Reply reply)
        {
            Replies.Add(reply);
            Committed = store.Instances[Id].State.Contains("metadata saved") && !store.Messages.ContainsKey("query");
        }
    }
    private sealed class World
    {
        private readonly bool qualified;
        private readonly Func<IActorType, IActorType> wrap;
        public long Time = 100;
        public readonly Probe Model = new();
        public readonly InMemoryStore Store;
        public Worker Owner = null!;
        public long Creation => qualified ? 1 : 0;
        public string Ticket => Worker.TicketId(Id, "sibling", Creation);
        public Transport Transport = null!;
        public World(bool qualified, Func<IActorType, IActorType>? wrap = null) { this.qualified = qualified; this.wrap = wrap ?? (actor => actor); Store = new(1, () => Time); }
        public async Task Take(string name)
        {
            Transport ??= new(Store);
            Owner = new(name, 0, Store, wrap(new WorkflowInstance([Model])), new WorkerOptions { Monotonic = () => Time, MaxAttempts = 1, Transport = Transport });
            await new DurableHost([Owner]).ActivateAsync(0);
        }
        public async Task Until(Func<bool> done)
        {
            for (var i = 0; i < 50 && Owner.Owns && !done(); i++) await Owner.RunAsync();
            Check(done(), "worker did not reach condition");
        }
        public async Task Start()
        {
            await Take("first");
            await Store.AddAsync(new("start", new(Id), Kinds.Start, Start: new(Model.Key, "self", Creation: qualified ? new(1, 0, "metadata") : null)));
            await Owner.WokenAsync(null);
            await Until(() => Model.Pending.ContainsKey("sibling"));
            Model.Pending["first"].SetResult("fault");
            await Until(() => Store.Instances[Id].Status == InstanceStatus.Faulted);
            Check(Store.Instances[Id].Jobs == Json.Write(new[] { "sibling" }), "park must retain genuine sibling");
        }
        public async Task Query()
        {
            await Store.AddAsync(new("query", new(Id), "metadata", ReplyTo: ReplyTo.Of("caller", "query", 100000), TargetCreation: qualified ? 1 : null));
        }
        public async Task Stop()
        {
            await Store.AddAsync(new("stop", new(Id), Kinds.Terminate, TargetCreation: qualified ? 1 : null));
            await Owner.WokenAsync(null);
            await Until(() => !Store.Messages.ContainsKey("stop"));
            Check(Owner.Owns && Store.Ghost.Count == 0 && !Store.Messages.ContainsKey(Ticket), "terminate must retain fence and consume ticket");
        }
    }
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    public static async Task CopyAsync(bool qualified)
    {
        var w = new World(qualified);
        await w.Start();
        var parked = w.Store.Instances[Id];
        await w.Owner.DisposeAsync();
        w.Time += 11000;
        await w.Take("cold");
        Check(w.Owner.Post(new(w.Ticket, new(Id), Kinds.JobFinished, Json.Write(new JobFinished("sibling", "copy", null)), TargetCreation: qualified ? 1 : null)), "copy must post");
        await w.Owner.LoadAsync();
        w.Owner.RunTurn(Id);
        await w.Owner.FlushAsync();
        Check(w.Store.Instances[Id].Jobs == parked.Jobs && w.Store.Messages.ContainsKey(w.Ticket), "volatile copy must not retire a durable job");
        await w.Stop();
        Check(w.Model.Pending["sibling"].Task.IsCanceled, "terminate must cancel the live sibling");
        await w.Owner.DisposeAsync();
    }

    public static async Task MetadataAsync(bool qualified, bool raw, Func<IActorType, IActorType>? wrap = null)
    {
        var w = new World(qualified, wrap);
        await w.Start();
        var parked = w.Store.Instances[Id];
        if (raw)
        {
            w.Time += 11000;
            await w.Take("trial");
            await w.Owner.WokenAsync(null);
            await w.Until(() => w.Store.Messages[w.Ticket].Trial == w.Owner.Epoch);
            w.Time += 11000;
            await w.Take("takeover");
            Check(w.Store.Messages[w.Ticket].Attempts == 1, "actual lost trial must reach threshold");
        }
        await w.Query();
        await w.Owner.PollAsync();
        await w.Owner.LoadAsync();
        await w.Owner.FlushAsync();
        if (!raw) w.Model.Pending["sibling"].SetResult("late");
        await w.Until(() => !w.Store.Messages.ContainsKey(w.Ticket) && !w.Store.Messages.ContainsKey("query"));
        var after = w.Store.Instances[Id];
        Check(after.Status == parked.Status && after.Fault == parked.Fault && after.Jobs is null && after.State.Contains("metadata saved"), "cleanup must preserve fault and save metadata while retiring ticket");
        Check(w.Model.Queries == 1 && w.Transport.Replies.Any(r => r.Channel == Request.Response && Equals(r.Item, "answered")) && w.Transport.Committed, "metadata reply must follow its committed change");
        await w.Stop();
        await w.Owner.DisposeAsync();
    }
}
