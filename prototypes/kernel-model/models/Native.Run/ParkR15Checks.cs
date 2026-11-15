namespace Kernel.Native.Run;

// Real store/trials behind a provider-equality adapter; no physical server alias-query qualification.
internal static class ParkR15Checks
{
    internal sealed class AliasStore(IKernelStore inner) : IKernelStore
    {
        public int TrialWrites;
        public bool SupportsCommitWindow => inner.SupportsCommitWindow;
        public bool CreationIdentityAvailable => inner.CreationIdentityAvailable;
        public bool CommitClockAvailable => inner.CommitClockAvailable;
        public ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
        {
            TrialWrites += batch.Trial.Count;
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
    internal sealed class Queue : TaskScheduler
    {
        public readonly List<Task> Pending = [];
        protected override void QueueTask(Task task) => Pending.Add(task);
        protected override bool TryExecuteTaskInline(Task task, bool queued) => false;
        protected override IEnumerable<Task> GetScheduledTasks() => Pending;
        public void Drain() { foreach (var task in Pending.ToArray()) { Pending.Remove(task); TryExecuteTask(task); } }
    }
    private sealed class Probe(bool holdBody) : Kernel.Sim.Model<List<string>>
    {
        public override string Key => "r15";
        public int Entries;
        public TaskCompletionSource<object?>? Pending;
        protected override void Turn(ITurn turn, List<string> state)
        { foreach (var message in turn.Inbox) { if (message.Kind == "spawn") turn.StartJob("work"); else state.Add(message.Kind); } }
        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken token)
        {
            if (++Entries > 1 && !holdBody) return ValueTask.FromResult<object?>("ok");
            var pending = new TaskCompletionSource<object?>(); if (Entries > 1) Pending = pending;
            token.Register(() => pending.TrySetCanceled(token)); return new(pending.Task);
        }
    }
    internal static void Require(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    public static IEnumerable<(string Label, Func<Task> Run)> Cases(Func<IActorType, IActorType>? wrap = null)
    {
        foreach (var qualified in new[] { false, true })
        foreach (var durable in new[] { false, true })
        {
            foreach (var command in new[] { "none", Kinds.Terminate, Kinds.Suspend, Kinds.Resume, Kinds.Delete })
            foreach (var alias in new[] { false, true })
            foreach (var exhausted in new[] { false, true })
            foreach (var later in new[] { false, true })
            foreach (var entered in alias ? new[] { false, true } : new[] { false })
                yield return ($"R15 selected job transition {qualified}/{durable}/{command}/{alias}/{exhausted}/{later}/{entered}",
                    () => Run(qualified, durable, command, alias, exhausted, later, entered, false, false, wrap));
            yield return ($"R15 deferred trial other partition {qualified}/{durable}",
                () => Run(qualified, durable, Kinds.Suspend, true, true, true, false, true, false, wrap));
            yield return ($"R15 deferred trial subsequent loss accounting {qualified}/{durable}",
                () => Run(qualified, durable, Kinds.Suspend, true, true, true, false, false, true, wrap));
        }
    }
    private static async Task Run(bool qualified, bool durable, string command, bool alias, bool exhausted, bool later,
        bool entered, bool cross, bool retake, Func<IActorType, IActorType>? wrap)
    {
        const string id = "0/canonical";
        var otherId = cross ? "1/other" : "0/other";
        var creation = qualified ? 1 : 0;
        long time = 100;
        var store = new InMemoryStore(cross ? 2 : 1, () => time); var wire = new AliasStore(store); var model = new Probe(entered);
        var scheduler = new Queue(); var processGate = new TrialGate(); Worker? peer = null; var observedRetakeAttempts = -1;
        IActorType Actor() { IActorType actor = new WorkflowInstance([model]); return wrap is null ? actor : wrap(actor); }
        async Task<Worker> Take(string name)
        {
            var owner = new Worker(name, 0, wire, Actor(), new()
            {
                Monotonic = () => time, Partitions = cross ? 2 : 1, Horizon = TimeSpan.FromSeconds(40), PollLimit = name == "second" ? 1 : name == "third" ? 2 : 500,
                TrialGroup = 1, MaxAttempts = exhausted ? 1 : 3, JobScheduler = name is "third" or "fourth" ? scheduler : null,
                Gate = name == "third" ? processGate : null,
                OnObservedDelivery = row => { if (name == "fourth" && row.Message.InboxKey == InboxKey.Ticket(id, "work", creation)) observedRetakeAttempts = row.Attempts; }
            });
            await new DurableHost([owner]).ActivateAsync(0); return owner;
        }
        async Task Drain(Worker owner)
        {
            scheduler.Drain();
            if (model.Pending?.Task.IsCompleted != false && owner.JobCompletion(id, "work", creation) is { } driver) await driver;
        }
        async Task Drive(Worker owner, int count, bool drain = false)
        {
            for (var i = 0; i < count; i++)
            {
                await owner.WokenAsync(null); await owner.RunAsync(); if (drain) await Drain(owner);
                if (peer is not null) { await peer.WokenAsync(null); await peer.RunAsync(); }
            }
        }
        await using var first = await Take("first");
        await store.AddAsync(new("start", new(id), Kinds.Start, Start: new(model.Key, "definition", Creation: qualified ? new(1, 0, "r15") : null)));
        await Drive(first, 5);
        await store.AddAsync(new("timer", new(alias ? id.ToUpperInvariant() : id), "timer", Due: 5_000, TargetCreation: qualified ? 1 : null));
        await Drive(first, 2);
        Require(first.Post(new("spawn", new(id), "spawn", TargetCreation: qualified ? 1 : null)), "spawn failed"); await Drive(first, 4);
        Require(model.Entries == 1 && store.Messages["timer"].Seen == first.Epoch, "real first body/timer prefix missing");
        time = 200;
        if (durable && command != "none")
            Require(await store.AddAsync(new("stop", new(id), command, Due: 4_000, TargetCreation: qualified ? 1 : null)), "future durable control was not accepted");
        time = 12_000; await using var second = await Take("second"); await Drive(second, 1);
        Require(store.Messages["timer"].Trial == second.Epoch, "timer did not genuinely select a trial before owner loss");
        time = 24_000; await using var third = await Take("third");
        Require(third.Post(new("future", new(id), "future", Due: 60_000, TargetCreation: qualified ? 1 : null)), "canonical preload failed");
        await third.LoadAsync(); await third.PollAsync(); await third.FlushAsync();
        if (alias) Require(third.Trial.Count() == 1 && scheduler.Pending.Count == 1, "healthy raw ticket was not genuinely fenced/queued before alias resolution");
        if (durable) { await third.PollAsync(); await third.FlushAsync(); }
        else if (command != "none") Require(third.Post(new("stop", new(id), command, Due: 4_000, TargetCreation: qualified ? 1 : null)), "volatile control failed");
        if (entered) { scheduler.Drain(); Require(model.Entries == 2 && model.Pending is not null, "entered control did not actually enter before alias resolution"); }
        await third.LoadAsync();
        if (!entered) await Drain(third);
        if (cross)
        {
            peer = new Worker("peer", 1, wire, Actor(), new() { Monotonic = () => time, Partitions = 2, Gate = processGate });
            await new DurableHost([peer]).ActivateAsync(1);
        }
        try
        {
            Require((peer ?? third).Post(new("other-start", new(otherId), Kinds.Start,
                Start: new(model.Key, "bystander", Creation: qualified ? new(1, 0, "other") : null))), "bystander was not accepted");
            if (later) Require(third.Post(new("later", new(id), "later", TargetCreation: qualified ? 1 : null)), "later input was not accepted");
            if (entered)
            {
                await Drive(third, 8, true);
                Require(third.Trial.Any(t => t.Contains("work")) && !store.Instances.ContainsKey(otherId), "a genuinely entered trial yielded before its real completion");
                var driver = third.JobCompletion(id, "work", creation)!; model.Pending!.SetResult("genuine"); await driver;
            }
            else if (alias && exhausted)
            {
                _ = third.Ready.ToArray();
                var ticket = store.Messages[InboxKey.Ticket(id, "work", creation)];
                Require(!third.Trial.Any() && ticket.Trial == third.Epoch && ticket.Attempts == 0,
                    "runtime yield erased durable trial evidence or retained a never-entered selection");
            }
            await Drive(third, 40, true); await third.BeatAsync();
            var active = third;
            Worker? fourth = null;
            try
            {
                if (command == Kinds.Suspend)
                {
                    Require(store.Instances[id].Suspended && store.Instances.ContainsKey(otherId) && !third.Trial.Any(), "suspend or partition progress remained behind a never-entered trial");
                    if (retake)
                    {
                        Require(store.Messages[InboxKey.Ticket(id, "work", creation)].Trial == third.Epoch, "deferred fence was not retained through suspension");
                        time += 11_000; fourth = await Take("fourth"); active = fourth; await fourth.PollAsync();
                        Require(observedRetakeAttempts == 1, "subsequent real loss did not charge the retained durable trial");
                    }
                    Require(active.Post(new("resume", new(id), Kinds.Resume, TargetCreation: qualified ? 1 : null)), "resume was not accepted");
                    await Drive(active, 40, true);
                }
                var row = store.Instances[id]; var state = Json.Read<List<string>>(row.State);
                var expected = command == Kinds.Terminate ? InstanceStatus.Cancelled : exhausted ? InstanceStatus.Faulted : InstanceStatus.Running;
                Require(row.Status == expected && row.Jobs is null && store.Messages.Count == 0 && store.Instances.ContainsKey(otherId) &&
                    active.Owns && !active.Trial.Any() && store.Ghost.Count == 0, "winning control, registry, durable input or partition progress stayed stranded");
                var healthyAdmitted = !exhausted && (alias || command != Kinds.Terminate);
                Require(model.Entries == (entered || healthyAdmitted ? 2 : 1), "a deferred body entered, or a genuine healthy body was lost");
                Require(state.Count(s => s == Kinds.JobFinished) == (healthyAdmitted || entered && command != "none" ? 1 : 0), "no-entry path synthesized an authored job result or duplicated a genuine result");
                if (later) Require(state.Count(s => s == "later") == (!exhausted && command != Kinds.Terminate ? 1 : 0), "later ordinary input crossed or stalled behind the winning control");
            }
            finally { if (fourth is not null) await fourth.DisposeAsync(); }
        }
        finally { if (peer is not null) await peer.DisposeAsync(); }
    }
}
