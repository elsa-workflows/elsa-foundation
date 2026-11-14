namespace Kernel.Native.Run;

// Adapter witnesses provider-equal spellings using real InMemoryStore operations. It is not a server-provider run.
internal static class ParkR14AliasChecks
{
    private sealed class AliasStore(IKernelStore inner) : IKernelStore
    {
        public bool SupportsCommitWindow => inner.SupportsCommitWindow;
        public bool CreationIdentityAvailable => inner.CreationIdentityAvailable;
        public bool CommitClockAvailable => inner.CommitClockAvailable;
        public ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
        {
            return inner.CommitAsync(batch, cancellationToken);
        }
    
        public ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default) => inner.TakeAsync(partition, node, lease, cancellationToken);
        public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default) => inner.HeartbeatAsync(node, window, cancellationToken);
        public ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default) => inner.ReleaseAsync(partition, epoch, cancellationToken);
        public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) => inner.PollAsync(partition, epoch, horizon, limit, cancellationToken);
        public ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default) => inner.AddAsync(message, cancellationToken);
        public ValueTask<EventAcceptance> AddEventAsync(EventDelivery delivery, CancellationToken cancellationToken = default) => inner.AddEventAsync(delivery, cancellationToken);
        public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => inner.LoadAsync(ids, cancellationToken);
        public bool HasActorAliases => true;
        public async ValueTask<IReadOnlyList<ActorLookup>> LoadActorsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
        {
            var rows=await inner.LoadAsync(ids.Select(i=>i.ToLowerInvariant()).Distinct().ToArray(),cancellationToken);
            return ids.Select(id=>{ var canonical=id.ToLowerInvariant(); return new ActorLookup(id,canonical,rows.SingleOrDefault(i=>i.Id==canonical)); }).ToArray();
        }
        public ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default) => inner.PublishAsync(@event, cancellationToken);
        public ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default) => inner.SubscribeAsync(subscription, cancellationToken);
        public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default) => inner.FindSubscriptionsAsync(key, cancellationToken);
        public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) => inner.FindEventsAsync(keys, cancellationToken);
        public ValueTask<IReadOnlyList<EventRoute>> PublishedMatchesAsync(EventRow published, CancellationToken cancellationToken = default) => inner.PublishedMatchesAsync(published, cancellationToken);
        public ValueTask<IReadOnlyList<EventRoute>> KeptMatchesAsync(IReadOnlyList<SubscriptionRow> waits, CancellationToken cancellationToken = default) => inner.KeptMatchesAsync(waits, cancellationToken);
        public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default) => inner.ReadJournalAsync(partition, after, take, cancellationToken);
        public ValueTask<IReadOnlyList<JournalRow>> ReadJournalOfAsync(int partition, string instance, long after, CancellationToken cancellationToken = default) => inner.ReadJournalOfAsync(partition, instance, after, cancellationToken);
        public ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default) => inner.ProjectedAsync(partition, cancellationToken);
        public ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default) => inner.ProjectAsync(partition, from, to, rows, cancellationToken);
        public ValueTask<int> SweepAsync(CancellationToken cancellationToken = default) => inner.SweepAsync(cancellationToken);
        public ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default) => inner.ReadViewsAsync(instances, cancellationToken);
    }

    private sealed class Queue : TaskScheduler
    {
        public readonly List<Task> Pending = [];
        protected override void QueueTask(Task task) => Pending.Add(task);
        protected override bool TryExecuteTaskInline(Task task, bool queued) => false;
        protected override IEnumerable<Task> GetScheduledTasks() => Pending;
        public void Drain() { foreach (var task in Pending.ToArray()) { Pending.Remove(task); TryExecuteTask(task); } }
    }
    private sealed class Probe : Kernel.Sim.Model<List<string>>
    {
        public override string Key => "r14-alias";
        public int Entries;
        protected override void Turn(ITurn turn, List<string> state)
        { foreach (var message in turn.Inbox) { if (message.Kind == "spawn") turn.StartJob("work"); else state.Add(message.Kind); } }
        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken token)
        {
            Interlocked.Increment(ref Entries);
            var pending = new TaskCompletionSource<object?>(); token.Register(() => pending.TrySetCanceled(token)); return new(pending.Task);
        }
    }
    private static void Require(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    public static IEnumerable<(string Label, Func<Task> Run)> Cases(Func<IActorType, IActorType>? wrap = null)
    {
        foreach (var qualified in new[] { false, true })
        foreach (var alias in new[] { false, true })
        foreach (var exhausted in new[] { false, true })
        {
            foreach (var loaded in new[] { false, true })
                yield return ($"R14 alias effective inbox {qualified}/{alias}/{exhausted}/{loaded}", () => Run(qualified, alias, exhausted, loaded, false, wrap));
            yield return ($"R14 queued alias publication {qualified}/{alias}/{exhausted}", () => Run(qualified, alias, exhausted, true, true, wrap));
        }
    }
    private static async Task Run(bool qualified, bool alias, bool exhausted, bool loaded, bool queued, Func<IActorType, IActorType>? wrap)
    {
        const string id = "0/canonical";
        long time = 100;
        var store = new InMemoryStore(1, () => time); var adapter = new AliasStore(store); var model = new Probe(); var scheduler = new Queue();
        var checking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Worker> Take(string name)
        {
            IActorType actor = new WorkflowInstance([model]);
            var owner = new Worker(name, 0, adapter, wrap is null ? actor : wrap(actor), new()
            {
                Monotonic = () => time, Horizon = TimeSpan.FromSeconds(40), TrialGroup = 1, MaxAttempts = exhausted ? 1 : 3,
                JobScheduler = queued && name == "third" ? scheduler : null,
                BeforeScheduledJobCheck = queued && name == "third" ? () => { checking.TrySetResult(); release.Task.GetAwaiter().GetResult(); } : null
            });
            await new DurableHost([owner]).ActivateAsync(0); return owner;
        }
        async Task Drive(Worker owner, int count) { for (var i = 0; i < count; i++) { await owner.WokenAsync(null); await owner.RunAsync(); } }
        await using var first = await Take("first");
        await store.AddAsync(new("start", new(id), Kinds.Start, Start: new(model.Key, "definition", Creation: qualified ? new(1, 0, "alias") : null)));
        await Drive(first, 5);
        await store.AddAsync(new("timer", new(alias ? id.ToUpperInvariant() : id), "timer", Due: 5_000, TargetCreation: qualified ? 1 : null));
        await Drive(first, 2);
        Require(first.Post(new("spawn", new(id), "spawn", TargetCreation: qualified ? 1 : null)), "spawn post failed");
        await Drive(first, 4);
        Require(model.Entries == 1 && store.Messages["timer"].Seen == first.Epoch, "first real job/timer prefix was not established");
        time = 12_000;
        await using var second = await Take("second"); await Drive(second, 1);
        Require(store.Messages["timer"].Trial == second.Epoch, "alias timer had no genuine failed trial");
        time = 24_000;
        await using var third = await Take("third");
        if (loaded)
        {
            Require(third.Post(new("future", new(id), "future", Due: 60_000, TargetCreation: qualified ? 1 : null)), "canonical preload post failed");
            await third.LoadAsync();
        }
        await third.PollAsync();
        Task? callback = null;
        try
        {
            if (queued)
            {
                // Before provider alias resolution, this canonical job may fence/queue. Its body waits at a real executing-thread barrier.
                await third.FlushAsync();
                Require(scheduler.Pending.Count == (alias ? 1 : 0), "pre-resolution queued-driver control was not established");
                if (scheduler.Pending.Count != 0) { callback = Task.Run(scheduler.Drain); await checking.Task; }
            }
            await third.LoadAsync();
            release.TrySetResult(); if (callback is not null) await callback;
            if (!queued) await third.FlushAsync();
            var expectedEntries = queued && alias && !exhausted ? 2 : 1;
            Require(model.Entries == expectedEntries && store.Messages["timer"].Attempts == 1 && third.Owns && store.Ghost.Count == 0,
                "resolved canonical poison admitted a new physical body, or healthy queued entry was lost");
            if (exhausted && !queued)
                Require(store.Messages[InboxKey.Ticket(id, "work", qualified ? 1 : 0)].Trial != third.Epoch,
                    "resolved alias poison incorrectly fenced a fresh active sibling job");
        }
        finally { release.TrySetResult(); if (callback is not null) await callback; }
    }
}
