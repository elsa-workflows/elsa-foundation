namespace Kernel.Sim;

/// <summary>Deterministic delayed acknowledgement and in-flight effect witnesses, against the real worker and store.</summary>
public static class LeaseChecks
{
    public static async Task<int> Run(Flaw flaw)
    {
        int failures = 0;
        void Check(bool ok, string name) { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); if (!ok) failures++; }
        const string id = "0/subject";
        long time = 100;
        var store = new InMemoryStore(1, () => time);
        var wire = new DelayedCommit(store);
        int oldTurns = 0, newTurns = 0, oldJobs = 0, newJobs = 0, oldEffects = 0;
        var effectFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var newWork = new TaskCompletionSource<object?>();
        var oldWork = new TaskCompletionSource<object?>();
        async ValueTask<object?> OldEffect()
        {
            oldJobs++;
            var result = await oldWork.Task; // deliberately non-cooperative external call
            oldEffects++;
            effectFinished.TrySetResult();
            return result;
        }
        Worker Owner(string node) => new(node, 0, wire, new WorkflowInstance([new Counter(), new HistoryProbe(), new Runner((_, _) =>
        {
            if (node == "old") return OldEffect();
            newJobs++; return new ValueTask<object?>(newWork.Task);
        })]), new WorkerOptions { Monotonic = () => time, Flaws = flaw, OnTurn = (_, _, _) => { if (node == "old") oldTurns++; else newTurns++; } });
        async Task Start(Worker owner) { await new DurableHost([owner]).ActivateAsync(0); await owner.PollAsync(); }
        async Task Drain(Worker owner, Func<bool> done)
        {
            for (var i = 0; i < 1000 && !done(); i++) { await owner.WokenAsync(null); await owner.RunAsync(); await Task.Yield(); }
            if (!done()) throw new InvalidOperationException("recovery did not drain");
        }
        // A delayed take response must not grant a full new lease measured from its arrival.
        wire.DelayTake = true;
        var delayedOwner = Owner("old");
        var take = new DurableHost([delayedOwner]).ActivateAsync(0).AsTask();
        await wire.Taken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time += 11_000; wire.TakeReply.SetResult(); await take;
        Check(!delayedOwner.Owns, "a delayed take acknowledgement cannot grant an already-expired local lease");
        time += 20_000; store = new InMemoryStore(1, () => time); wire = new DelayedCommit(store);
        var fresh = Owner("fresh");
        await Start(fresh);
        fresh.Post(new Message("fresh", new InstanceRef(id), "probe", Start: new StartSpec("history-probe", "p")), fresh: true);
        await Drain(fresh, () => store.Instances.ContainsKey(id));
        Check(!Json.Read<HistoryState>(store.Instances[id].State).Again, "a fresh delivery has no observed history marker");
        time += 20_000; store = new InMemoryStore(1, () => time); wire = new DelayedCommit(store);
        var old = Owner("old");
        await Start(old);
        await store.AddAsync(new Message("mark", new InstanceRef(id), "probe", Start: new StartSpec("history-probe", "p")));
        await old.PollAsync(); await old.LoadAsync();
        wire.DelayNext = true;
        var mark = old.FlushAsync();
        await wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time += 11_000;
        var replacement = Owner("new");
        await Start(replacement);
        await Drain(replacement, () => store.Instances.ContainsKey(id));
        wire.Reply.SetResult(); await mark;
        old.RunTurn(id);
        Check(!old.Owns && oldTurns == 0, "delayed seen-mark acknowledgement cannot start an expired turn");
        var history = Json.Read<HistoryState>(store.Instances[id].State);
        Check(newTurns > 0 && history.Count == 1 && history.Again, "new owner observes prior delivery history and applies the message once");

        // A ticket's creating commit lands, but its reply is delayed until after a takeover.
        time += 20_000; store = new InMemoryStore(1, () => time); wire = new DelayedCommit(store);
        oldTurns = newTurns = oldJobs = newJobs = 0; old = Owner("old"); await Start(old);
        await store.AddAsync(new Message("go", new InstanceRef(id), "go", Json.Write("work"), Start: Runner.Spec));
        await old.PollAsync(); await old.LoadAsync(); await old.FlushAsync(); old.RunTurn(id);
        wire.DelayNext = true; var ticket = old.FlushAsync(); await wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time += 11_000; replacement = Owner("new"); await Start(replacement);
        await Drain(replacement, () => newJobs == 1);
        wire.Reply.SetResult(); await ticket;
        Check(!old.Owns && oldJobs == 0, "delayed ticket commit acknowledgement cannot start an expired job");
        newWork.SetResult("done");
        await Drain(replacement, () => Json.Read<RunnerState>(store.Instances[id].State).Log.Contains("result work"));
        Check(newJobs == 1 && Json.Read<RunnerState>(store.Instances[id].State).Log.Count(x => x == "result work") == 1,
            "fresh owner runs the recovered ticket and commits one result");

        // Scheduler dispatch itself may be delayed; it must recheck before invoking the job body.
        time += 20_000; store = new InMemoryStore(1, () => time); wire = new DelayedCommit(store);
        var scheduler = new HeldScheduler();
        oldJobs = 0;
        old = new Worker("scheduled", 0, wire, new WorkflowInstance([new Runner((_, _) => { oldJobs++; return new ValueTask<object?>((object?)null); })]),
            new WorkerOptions { Monotonic = () => time, Flaws = flaw, JobScheduler = scheduler });
        await Start(old);
        await store.AddAsync(new Message("scheduled", new InstanceRef(id), "go", Json.Write("work"), Start: Runner.Spec));
        await old.PollAsync(); await old.LoadAsync(); await old.FlushAsync(); old.RunTurn(id); await old.FlushAsync();
        Check(scheduler.Count > 0, "a job was queued on the scheduler before expiry");
        time += 11_000; scheduler.RunAll();
        await old.RunAsync();
        Check(oldJobs == 0 && !old.Owns, "a queued job does not begin after the local deadline");
        if (flaw.HasFlag(Flaw.IgnoreLeaseDeadline)) return failures == 0 ? 0 : 1; // the deliberately broken dispatch has already executed
        newJobs = 0; newWork = new TaskCompletionSource<object?>();
        replacement = Owner("new"); await Start(replacement); await Drain(replacement, () => newJobs == 1);
        newWork.SetResult("done");
        await Drain(replacement, () => Json.Read<RunnerState>(store.Instances[id].State).Log.Contains("result work"));
        Check(newJobs == 1, "fresh owner drains a ticket declined by the expired scheduler dispatch");

        // A renewal can land before the old deadline while its acknowledgement is delayed beyond it.
        // A job discarded in that gap must force this generation to stand down even when that reply arrives.
        foreach (var queued in new[] { true, false })
        {
            time += 20_000; store = new InMemoryStore(1, () => time); wire = new DelayedCommit(store);
            var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var running = new TaskCompletionSource<object?>();
            int attempts = 0;
            var pausedScheduler = new HeldScheduler();
            old = new Worker("overlap", 0, wire, new WorkflowInstance([new Runner((_, _) =>
            {
                attempts++;
                return new ValueTask<object?>(running.Task);
            })]), new WorkerOptions { Monotonic = () => time, OnExpiredJob = () => expired.TrySetResult(),
                JobScheduler = queued ? pausedScheduler : null });
            await Start(old);
            await store.AddAsync(new Message("overlap", new InstanceRef(id), "go", Json.Write("work"), Start: Runner.Spec));
            await old.PollAsync(); await old.LoadAsync(); await old.FlushAsync(); old.RunTurn(id); await old.FlushAsync();
            Check(queued ? pausedScheduler.Count > 0 : attempts == 1, $"{(queued ? "queued" : "running")} job prepared before renewal");
            time += 5_000;
            wire.DelayNext = true; var delayedBeat = old.BeatAsync();
            await wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            time += 6_000;
            if (queued) pausedScheduler.RunAll(); else running.SetResult("late");
            await expired.Task.WaitAsync(TimeSpan.FromSeconds(5));
            wire.Reply.SetResult(); await delayedBeat;
            await old.RunAsync();
            Check(!old.Owns, $"{(queued ? "queued dispatch" : "completed body")} expiry cannot be revived by a late renewal acknowledgement");
            if (old.Owns) continue; // red baseline: do not hide the stuck owner by taking over for it
            time += 10_000; newJobs = 0; newWork = new TaskCompletionSource<object?>();
            replacement = Owner("new"); await Start(replacement); await Drain(replacement, () => newJobs == 1);
            newWork.SetResult("done");
            await Drain(replacement, () => Json.Read<RunnerState>(store.Instances[id].State).Log.Contains("result work"));
            Check(newJobs == 1, $"replacement drains the {(queued ? "queued" : "completed")} ticket after stand-down");
        }

        // A timely acknowledged renewal advances the window; a body completing after the old deadline remains valid.
        time += 20_000; store = new InMemoryStore(1, () => time); wire = new DelayedCommit(store);
        oldJobs = oldEffects = 0; oldWork = new TaskCompletionSource<object?>();
        effectFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        old = Owner("old"); await Start(old);
        await store.AddAsync(new Message("renewed", new InstanceRef(id), "go", Json.Write("work"), Start: Runner.Spec));
        await old.PollAsync(); await old.LoadAsync(); await old.FlushAsync(); old.RunTurn(id); await old.FlushAsync();
        Check(oldJobs == 1, "renewal witness started its job");
        time += 5_000; await old.BeatAsync();
        time += 6_000; oldWork.SetResult("valid"); await effectFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Drain(old, () => Json.Read<RunnerState>(store.Instances[id].State).Log.Contains("result work"));
        Check(old.Owns && oldEffects == 1, "a timely renewal permits a result after the prior deadline");

        // A queued callback from an old generation must not use the same Worker's new lease after it retakes.
        time += 20_000; store = new InMemoryStore(1, () => time); wire = new DelayedCommit(store);
        scheduler = new HeldScheduler(); oldJobs = 0;
        old = new Worker("retaking", 0, wire, new WorkflowInstance([new Runner((_, _) => { oldJobs++; return new ValueTask<object?>((object?)null); })]),
            new WorkerOptions { Monotonic = () => time, Flaws = flaw, JobScheduler = scheduler });
        await Start(old);
        await store.AddAsync(new Message("retake", new InstanceRef(id), "go", Json.Write("work"), Start: Runner.Spec));
        await old.PollAsync(); await old.LoadAsync(); await old.FlushAsync(); old.RunTurn(id); await old.FlushAsync();
        Check(scheduler.Count > 0, "old-generation work was queued before retake");
        time += 11_000; await old.RunAsync(); await Start(old);
        scheduler.RunAll();
        Check(old.Owns && oldJobs == 0, "cancelled queued work does not run after the same worker retakes");

        // Enter a scheduled delegate before cancellation, then retake before it reaches the guard. This tests the
        // generation check itself; the earlier queued-work test is stopped by scheduler token cancellation.
        time += 20_000; store = new InMemoryStore(1, () => time); wire = new DelayedCommit(store);
        scheduler = new HeldScheduler(); oldJobs = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var allowGuard = new ManualResetEventSlim();
        old = new Worker("entered", 0, wire, new WorkflowInstance([new Runner((_, _) => { Interlocked.Increment(ref oldJobs); return new ValueTask<object?>((object?)null); })]),
            new WorkerOptions { Monotonic = () => time, Flaws = flaw, JobScheduler = scheduler,
                BeforeScheduledJobCheck = () => { entered.TrySetResult(); if (!allowGuard.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("guard release"); } });
        await Start(old);
        await store.AddAsync(new Message("entered", new InstanceRef(id), "go", Json.Write("work"), Start: Runner.Spec));
        await old.PollAsync(); await old.LoadAsync(); await old.FlushAsync(); old.RunTurn(id); await old.FlushAsync();
        var runningScheduler = Task.Run(scheduler.RunAll);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await old.DisposeAsync(); await Start(old);
        allowGuard.Set(); await runningScheduler.WaitAsync(TimeSpan.FromSeconds(5));
        Check(old.Owns && oldJobs == 0, "an entered old-generation callback cannot borrow a renewed lease");

        // Pause inside the clock read after the first generation check. Retake writes a later deadline before the
        // old callback returns from the clock. It must not combine an old generation with the replacement deadline.
        time += 20_000; store = new InMemoryStore(1, () => time); wire = new DelayedCommit(store);
        scheduler = new HeldScheduler(); oldJobs = 0;
        var clockEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var allowClock = new ManualResetEventSlim();
        int schedulerThread = 0, blockClock = 1;
        long BlockingClock()
        {
            if (Environment.CurrentManagedThreadId == Volatile.Read(ref schedulerThread) && Interlocked.Exchange(ref blockClock, 0) == 1)
            {
                clockEntered.TrySetResult();
                if (!allowClock.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("clock release");
            }
            return Volatile.Read(ref time);
        }
        old = new Worker("clock-race", 0, wire, new WorkflowInstance([new Runner((_, _) => { Interlocked.Increment(ref oldJobs); return new ValueTask<object?>((object?)null); })]),
            new WorkerOptions { Monotonic = BlockingClock, Flaws = flaw, JobScheduler = scheduler,
                BeforeScheduledJobCheck = () => Volatile.Write(ref schedulerThread, Environment.CurrentManagedThreadId) });
        await Start(old);
        await store.AddAsync(new Message("clock-race", new InstanceRef(id), "go", Json.Write("work"), Start: Runner.Spec));
        await old.PollAsync(); await old.LoadAsync(); await old.FlushAsync(); old.RunTurn(id); await old.FlushAsync();
        var clockRace = Task.Run(scheduler.RunAll);
        await clockEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Interlocked.Add(ref time, 11_000);
        await old.DisposeAsync(); await Start(old);
        allowClock.Set(); await clockRace.WaitAsync(TimeSpan.FromSeconds(5));
        Check(old.Owns && oldJobs == 0, "an old callback cannot combine its generation with a retaken lease's deadline");

        // Work that began before the local deadline may still have an outside effect after the lease expires.
        time += 20_000; store = new InMemoryStore(1, () => time); wire = new DelayedCommit(store);
        oldJobs = newJobs = oldEffects = 0; oldWork = new TaskCompletionSource<object?>(); newWork = new TaskCompletionSource<object?>();
        effectFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        old = Owner("old"); await Start(old);
        await store.AddAsync(new Message("go2", new InstanceRef(id), "go", Json.Write("work"), Start: Runner.Spec));
        await old.PollAsync(); await old.LoadAsync(); await old.FlushAsync(); old.RunTurn(id); await old.FlushAsync();
        Check(oldJobs == 1, "the first job began before expiry");
        time += 11_000; old.RunTurn("0/not-ready");
        replacement = Owner("new"); await Start(replacement); await Drain(replacement, () => newJobs == 1);
        oldWork.SetResult("late outside effect"); newWork.SetResult("done");
        await effectFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Drain(replacement, () => Json.Read<RunnerState>(store.Instances[id].State).Log.Contains("result work"));
        Check(oldJobs == 1 && oldEffects == 1 && newJobs == 1, "an already-running external effect can finish after expiry and overlap recovery");
        return failures == 0 ? 0 : 1;
    }
}

// Delay a successful commit's response, after the inner store has actually committed.
public sealed class DelayedCommit(IKernelStore inner) : IKernelStore
{
    public bool CreationIdentityAvailable => inner.CreationIdentityAvailable;
    public bool CommitClockAvailable => inner.CommitClockAvailable;
    public bool DelayNext, DelayTake;
    public TaskCompletionSource Taken = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource TakeReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Landed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
    {
        var result = await inner.CommitAsync(batch, cancellationToken);
        if (DelayNext) { DelayNext = false; Landed.TrySetResult(); await Reply.Task; }
        return result;
    }
    public async ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken ct = default)
    {
        var result = await inner.TakeAsync(partition, node, lease, ct);
        if (DelayTake) { DelayTake = false; Taken.TrySetResult(); await TakeReply.Task; }
        return result;
    }
    public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken ct = default) => inner.HeartbeatAsync(node, window, ct);
    public ValueTask ReleaseAsync(int partition, long epoch, CancellationToken ct = default) => inner.ReleaseAsync(partition, epoch, ct);
    public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken ct = default) => inner.PollAsync(partition, epoch, horizon, limit, ct);
    public ValueTask<bool> AddAsync(Message message, CancellationToken ct = default) => inner.AddAsync(message, ct);
    public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default) => inner.LoadAsync(ids, ct);
    public ValueTask PublishAsync(EventRow row, CancellationToken ct = default) => inner.PublishAsync(row, ct);
    public ValueTask SubscribeAsync(SubscriptionRow row, CancellationToken ct = default) => inner.SubscribeAsync(row, ct);
    public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken ct = default) => inner.FindSubscriptionsAsync(key, ct);
    public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => inner.FindEventsAsync(keys, ct);
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken ct = default) => inner.ReadJournalAsync(partition, after, take, ct);
    public ValueTask<Projection> ProjectedAsync(int partition, CancellationToken ct = default) => inner.ProjectedAsync(partition, ct);
    public ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken ct = default) => inner.ProjectAsync(partition, from, to, rows, ct);
    public ValueTask<int> SweepAsync(CancellationToken ct = default) => inner.SweepAsync(ct);
    public ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default) => inner.ReadViewsAsync(ids, ct);
}

// A deterministic scheduler which does not execute a queued body until the witness releases it.
public sealed class HeldScheduler : TaskScheduler
{
    private readonly Queue<Task> _tasks = new();
    public int Count => _tasks.Count;
    protected override void QueueTask(Task task) => _tasks.Enqueue(task);
    protected override bool TryExecuteTaskInline(Task task, bool previouslyQueued) => false;
    protected override IEnumerable<Task> GetScheduledTasks() => _tasks.ToArray();
    public void RunAll() { while (_tasks.TryDequeue(out var task)) TryExecuteTask(task); }
}

public sealed class HistoryState { public int Count { get; set; } public bool Again { get; set; } }
public sealed class HistoryProbe : Model<HistoryState>
{
    public override string Key => "history-probe";
    protected override void Turn(ITurn turn, HistoryState state)
    {
        state.Count++;
        state.Again = turn.Inbox.Any(turn.Redelivered);
        turn.SetStatus(InstanceStatus.Finished);
    }
}
