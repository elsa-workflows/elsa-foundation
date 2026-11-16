namespace Kernel.Sim;

/// <summary>Deterministic delayed acknowledgement and in-flight effect witnesses, against the real worker and store.</summary>
public static class LeaseChecks
{
    public static async Task<int> Run(Flaw flaw)
    {
        int failures = 0;
        void Check(bool ok, string name) { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); if (!ok) failures++; }
        await ReplyRegistrationChecks.Run(Check);
        await ReplyAdmissionChecks.Run(Check);
        await StarterContextChecks.Run(Check, CaseInsensitive);
        await ChildCompletionChecks.Run(Check);
        await ChildCompletionRecoveryChecks.Run(Check);
        await CheckLoadAfterEviction(Check);
        await CheckLoadDuringUnrelatedEvictions(Check);
        await CheckMissingAliasDuringLoad(Check);
        await CheckConcurrentProcessDeaths(Check);
        await CheckAliasLoadProgress(Check);
        await CheckChangedAliasSnapshot(Check);
        await CheckResidentAliasBirth(Check);
        await CheckSettledAliasMiss(Check);
        await CheckComparedBirths(Check);
        await CheckRecreatedNeighbors(Check);
        await CheckFutureMintedName(Check);
        await CheckMergedSpellings(Check);
        await CheckEphemeralDuringAliasRetry(Check);
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

    private static async Task CheckLoadDuringUnrelatedEvictions(Action<bool, string> check)
    {
        var store = new InMemoryStore(1, () => 100);
        var wire = new DelayedCommit(store);
        await using var owner = new Worker("one-read", 0, wire, new WorkflowInstance([new Counter()]),
            new() { MaxInstances = 0, Monotonic = () => 100 });
        await new DurableHost([owner]).ActivateAsync(0);
        var requested = InstanceRef.In(0, "requested");
        owner.Post(new("pending", requested, "wake"));
        var reply = new TaskCompletionSource();
        wire.AfterLoad = _ => reply.Task;
        var lookup = owner.LoadAsync();
        for (var n = 0; n < 16; n++)
        {
            var other = InstanceRef.In(0, $"other-{n}");
            owner.Post(new($"add-{n}", other, "add", Json.Write(new Add("one")), Start: Counter.Spec), fresh: true);
            owner.RunTurn(other.Id);
            await owner.FlushAsync();
        }
        reply.SetResult();
        await lookup;
        check(owner.Evicted == 16 && !owner.NeedsLoad && owner.Ready.Contains(requested.Id) && wire.LoadCalls == 1,
            "unrelated evictions preserve the pending ordinal lookup and its delivery");
    }

    private static async Task CheckMissingAliasDuringLoad(Action<bool, string> check)
    {
        foreach (var capacity in new[] { 0, 1 })
        {
            var store = new InMemoryStore(1, () => 100);
            var wire = CaseInsensitive(store);
            await using var owner = new Worker("alias-read", 0, wire, new WorkflowInstance([new Counter()]),
                new() { MaxInstances = capacity, Monotonic = () => 100 });
            await new DurableHost([owner]).ActivateAsync(0);
            var requested = InstanceRef.In(0, "novel");
            var actual = InstanceRef.In(0, "NOVEL");
            owner.Post(new("pending", requested, "wake"));
            var reply = new TaskCompletionSource();
            wire.AfterLoad = _ => reply.Task;
            var missing = owner.LoadAsync();
            owner.Post(new("create", actual, "add", Json.Write(new Add("one")), Start: Counter.Spec), fresh: true);
            owner.RunTurn(actual.Id);
            await owner.FlushAsync();
            var evicted = owner.Instances.All(s => s.Id != actual.Id);
            owner.Post(new("after-commit", requested, "add", Json.Write(new Add("two"))));
            reply.SetResult();
            await missing;
            var retry = owner.NeedsLoad;
            await owner.LoadAsync();
            owner.RunTurn(actual.Id);
            await owner.FlushAsync();
            var row = store.Instances[actual.Id];
            check(evicted == (capacity == 0) && retry && wire.LoadCalls == 2 && row.Revision == 2 &&
                Json.Read<CounterState>(row.State).Ids.SequenceEqual(["create", "after-commit"]) && !owner.Messages.Any(),
                $"a fresh equivalent spelling invalidates an earlier miss, evicted={evicted}, retried={retry}, revision={row.Revision}");
        }
    }

    private static async Task CheckConcurrentProcessDeaths(Action<bool, string> check)
    {
        var world = new World(new Config(2, 2, Flaw.None)) { Auto = true };
        var owners = new[] { world.Of(0, 0), world.Of(1, 1) };
        foreach (var actor in owners)
        {
            await actor.Runtime.ActivateAsync(actor.Partition);
            var target = InstanceRef.In(actor.Partition, "fatal");
            world.Apply(new("add", M: new($"birth-{actor.Node}", target, "dierun", Start: Fragile.Spec)));
            await actor.Worker.PollAsync();
            await actor.Worker.LoadAsync();
            await actor.Worker.FlushAsync();
            actor.Worker.RunTurn(target.Id);
        }
        // Both acknowledgements start their lethal jobs before the next public simulation action.
        foreach (var actor in owners) await actor.Worker.FlushAsync();
        world.Apply(new("tick", A: 1));
        foreach (var survivor in owners.Where(a => !a.Dead)) await survivor.Worker.RunAsync();
        var stopped = owners.All(a => a.Dead) && world.Store.Instances.Values.All(row => row.Revision == 1);
        var result = world.Settle();
        check(stopped && result is null && world.Store.Instances.Values.All(row => row.Status == InstanceStatus.Faulted),
            $"both lethal-job owners stop before a completion can advance either row: stopped={stopped}, settlement={result ?? "healthy"}");
    }

    private static DelayedCommit CaseInsensitive(InMemoryStore store) => new(store)
    {
        AliasLookup = names => names.Select(name =>
        {
            var row = store.Instances.Values.SingleOrDefault(r => string.Equals(r.Id, name, StringComparison.OrdinalIgnoreCase));
            return new ActorLookup(name, row?.Id ?? names.First(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)), row);
        }).ToList()
    };

    private static async Task CheckAliasLoadProgress(Action<bool, string> check)
    {
        foreach (var mode in new[] { "existing", "alternate-spelling", "missing" })
        {
            var store = new InMemoryStore(1, () => 100);
            var wire = CaseInsensitive(store);
            await using var owner = new Worker("arrival", 0, wire, new WorkflowInstance([new Counter()]),
                new() { MaxInstances = 0, Monotonic = () => 100 });
            await new DurableHost([owner]).ActivateAsync(0);
            var target = InstanceRef.In(0, "established");
            var requested = mode == "alternate-spelling" ? InstanceRef.In(0, "ESTABLISHED") : target;
            if (mode != "missing")
            {
                owner.Post(new("seed", target, "add", Json.Write(new Add("seed")), Start: Counter.Spec), fresh: true);
                owner.RunTurn(target.Id);
                await owner.FlushAsync();
            }
            var input = new Message("durable", requested, "add", Json.Write(new Add("work")), Start: mode == "missing" ? Counter.Spec : null);
            await store.AddAsync(input);
            await owner.PollAsync();
            var arrivals = 0;
            wire.AfterLoad = _ =>
            {
                var other = InstanceRef.In(0, $"other-{++arrivals}");
                owner.Post(new($"other-{arrivals}", other, "add", Json.Write(new Add("arrived")), Start: Counter.Spec), fresh: true);
                return Task.CompletedTask;
            };
            for (var pass = 0; pass < 3 && store.Messages.ContainsKey(input.InboxKey); pass++) await owner.RunAsync();
            var applied = store.Instances.GetValueOrDefault(target.Id);
            var reads = wire.LoadCalls;
            var progressed = applied is not null && Json.Read<CounterState>(applied.State).Ids.SequenceEqual(
                mode == "missing" ? ["durable"] : new[] { "seed", "durable" });
            wire.AfterLoad = null;
            await owner.RunAsync();
            await owner.RunAsync();
            check(progressed && reads == 2 && arrivals == 2 && store.Instances.Count == 3 && !owner.Messages.Any(),
                $"durable {mode} target advances during unrelated alias arrivals: reads={reads}, target revision={applied?.Revision}, drained rows={store.Instances.Count}");
        }
    }

    private static async Task CheckChangedAliasSnapshot(Action<bool, string> check)
    {
        var store = new InMemoryStore(1, () => 100);
        var wire = CaseInsensitive(store);
        await using var owner = new Worker("alias-snapshot", 0, wire, new WorkflowInstance([new Counter()]),
            new() { MaxInstances = 0, Monotonic = () => 100 });
        await new DurableHost([owner]).ActivateAsync(0);
        var canonical = InstanceRef.In(0, "SAVED");
        var alias = InstanceRef.In(0, "saved");
        owner.Post(new("first", canonical, "add", Json.Write(new Add("first")), Start: Counter.Spec), fresh: true);
        owner.RunTurn(canonical.Id);
        await owner.FlushAsync();
        owner.Post(new("update", canonical, "add", Json.Write(new Add("update"))));
        await owner.LoadAsync();
        owner.Post(new("alias", alias, "add", Json.Write(new Add("alias"))));
        var reply = new TaskCompletionSource();
        wire.AfterLoad = _ => reply.Task;
        var stale = owner.LoadAsync(); // sees the persisted r1 through the alternate spelling
        owner.RunTurn(canonical.Id);
        await owner.FlushAsync(); // installs and evicts r2
        owner.Post(new("after", alias, "add", Json.Write(new Add("after"))));
        reply.SetResult();
        await stale;
        var retried = owner.NeedsLoad;
        await owner.LoadAsync();
        var installed = owner.Instances.Single(i => i.Id == canonical.Id).Row?.Revision;
        owner.RunTurn(canonical.Id);
        await owner.FlushAsync();
        var row = store.Instances[canonical.Id];
        check(retried && installed == 2 && row.Revision == 3 && Json.Read<CounterState>(row.State).Ids.SequenceEqual(["first", "update", "alias", "after"]),
            $"a positive alias lookup cannot restore a committed and evicted predecessor: retried={retried}, installed={installed}, final={row.Revision}");
    }

    private static async Task CheckResidentAliasBirth(Action<bool, string> check)
    {
        foreach (var order in new[] { "commit-before-read", "commit-during-read", "reply-before-commit" })
        foreach (var capacity in new[] { 0, 3 })
        {
            var store = new InMemoryStore(1, () => 100);
            var wire = CaseInsensitive(store);
            await using var owner = new Worker("resident", 0, wire, new WorkflowInstance([new Counter()]),
                new() { MaxInstances = capacity, Monotonic = () => 100 });
            await new DurableHost([owner]).ActivateAsync(0);
            var lower = InstanceRef.In(0, "novel");
            var upper = InstanceRef.In(0, "NOVEL");
            owner.Post(new("future", lower, "wake", Due: 100_000));
            await owner.LoadAsync();
            owner.Post(new("create", upper, "add", Json.Write(new Add("first")), Start: Counter.Spec), fresh: true);
            owner.RunTurn(upper.Id);
            owner.Post(new("unrelated", InstanceRef.In(0, "other"), "wake", Due: 100_000));
            if (order == "commit-before-read") await owner.FlushAsync();
            var release = new TaskCompletionSource();
            wire.AfterLoad = _ => release.Task;
            var read = owner.LoadAsync();
            if (order == "commit-during-read") await owner.FlushAsync();
            if (order == "reply-before-commit") { release.SetResult(); await read; await owner.FlushAsync(); }
            owner.Post(new("after", upper, "add", Json.Write(new Add("second"))));
            if (!release.Task.IsCompleted) { release.SetResult(); await read; }
            for (var pass = 0; pass < 4; pass++) await owner.RunAsync();
            var result = store.Instances[upper.Id];
            check(result.Revision == 2 && Json.Read<CounterState>(result.State).Ids.SequenceEqual(["create", "after"]) &&
                !owner.Messages.Any(m => m.Id == new Message("after", upper, "add").InboxKey),
                $"a resident alias miss preserves the real birth and following input: {order}, capacity={capacity}, revision={result.Revision}");
        }
    }

    private static async Task CheckSettledAliasMiss(Action<bool, string> check)
    {
        foreach (var delivery in new[] { "posted", "polled", "due", "before-ack" })
        foreach (var capacity in new[] { 0, 3 })
        {
            long time = 100;
            var store = new InMemoryStore(1, () => time);
            var wire = CaseInsensitive(store);
            await using var owner = new Worker("completed-miss", 0, wire, new WorkflowInstance([new Counter()]),
                new() { MaxInstances = capacity, Monotonic = () => time });
            await new DurableHost([owner]).ActivateAsync(0);
            var lower = InstanceRef.In(0, "novel");
            var actual = InstanceRef.In(0, "NOVEL");
            var after = new Message("after", lower, "add", Json.Write(new Add("second")));
            owner.Post(delivery == "due" ? after with { Due = time + 50 } : new("future", lower, "wake", Due: time + 5_000));
            await owner.RunAsync();
            var resident = owner.Instances.Any(s => s.Id == lower.Id && s.Row is null);
            owner.Post(new("create", actual, "add", Json.Write(new Add("first")), Start: Counter.Spec), fresh: true);
            owner.RunTurn(actual.Id);
            wire.DelayNext = delivery == "before-ack";
            var flush = owner.FlushAsync();
            if (delivery == "before-ack") await wire.Landed.Task;
            else await flush;
            var birth = store.Instances[actual.Id].Revision;
            var dormant = !owner.NeedsLoad && wire.LoadCalls == 1;
            if (delivery == "due") time += 50;
            else if (delivery == "polled") { await store.AddAsync(after); await owner.PollAsync(); }
            else owner.Post(after);
            var needsRead = owner.NeedsLoad;
            if (delivery == "before-ack")
            {
                await owner.LoadAsync();
                owner.RunTurn(lower.Id);
                wire.Reply.SetResult();
                await flush;
            }
            for (var pass = 0; pass < 4; pass++) await owner.RunAsync();
            var row = store.Instances[actual.Id];
            check(resident && birth == 1 && dormant && needsRead && row.Revision == 2 &&
                Json.Read<CounterState>(row.State).Ids.SequenceEqual(["create", "after"]) &&
                !owner.Messages.Any(m => m.Id == after.InboxKey) && (!store.Messages.TryGetValue(after.InboxKey, out var input) || input.Done),
                $"completed alias miss revalidates {delivery} work after physical birth, capacity={capacity}, dormant={dormant}, requested={needsRead}, revision={row.Revision}");
        }
    }

    private static async Task CheckComparedBirths(Action<bool, string> check)
    {
        foreach (var traffic in new[] { false, true })
        {
            var store = new InMemoryStore(1, () => 100);
            var wire = CaseInsensitive(store);
            await using var owner = new Worker("compared", 0, wire, new WorkflowInstance([new Counter()]),
                new() { MaxBatch = 500, PollLimit = 500, Monotonic = () => 100 });
            await new DurableHost([owner]).ActivateAsync(0);
            Message Start(string name) => new(name, InstanceRef.In(0, name), "add", Json.Write(new Add(name)), Start: Counter.Spec);
            var waiting = traffic ? new[] { Start("z-waiting") } : Enumerable.Range(0, 12).Select(n => Start($"batch-{n}")).ToArray();
            if (traffic)
            {
                owner.Post(new("future", waiting[0].Target, "wake", Due: 100_000));
                await owner.RunAsync();
            }
            foreach (var item in waiting) await store.AddAsync(item);
            await owner.PollAsync();
            var reads = wire.LoadCalls;
            for (var pass = 0; pass < 2; pass++)
            {
                if (traffic) owner.Post(Start($"a-fresh-{pass}"), fresh: true);
                await owner.RunAsync();
            }
            var complete = waiting.All(m => store.Instances.TryGetValue(m.Target.Id, out var row) && row.Revision == 1 &&
                Json.Read<CounterState>(row.State).Ids.SequenceEqual([m.Id]) && store.Messages.GetValueOrDefault(m.InboxKey) is not { Done: false });
            var fresh = !traffic || store.Instances.Count == 3;
            check(complete && fresh && wire.LoadCalls - reads == 1,
                $"distinct compared starts complete in two ordinary passes, traffic={traffic}, completed={complete}, rows={store.Instances.Count}, additional lookups={wire.LoadCalls - reads}");
        }
    }

    private static async Task CheckFutureMintedName(Action<bool, string> check)
    {
        foreach (var durable in new[] { false, true })
        foreach (var mode in new[] { "new input spelling", "physical spelling", "pending future Start" })
        {
            var store = new InMemoryStore(1, () => 100);
            var wire = CaseInsensitive(store);
            await using var owner = new Worker("future-start", 0, wire, new WorkflowInstance([new Counter()]), new() { Monotonic = () => 100 });
            await new DurableHost([owner]).ActivateAsync(0);
            var physical = InstanceRef.In(0, "novel"); var minted = InstanceRef.In(0, "NOVEL");
            owner.Post(new("future-wake", physical, "wake", Due: 100_000));
            await owner.RunAsync();
            var planned = new Message("planned", minted, Kinds.Start, Start: Counter.Spec, Due: 90_000);
            if (mode != "new input spelling") owner.Post(planned, fresh: true);
            await store.AddAsync(new("creator", physical, "add", Json.Write(new Add("first")), Start: Counter.Spec));
            await owner.PollAsync();
            for (var pass = 0; pass < 3; pass++) await owner.RunAsync();
            var actualBirth = store.Instances[physical.Id].Revision == 1;
            var target = mode == "physical spelling" ? physical : minted;
            var after = new Message("after", target, "add", Json.Write(new Add("second")));
            if (durable) { await store.AddAsync(after); await owner.PollAsync(); } else owner.Post(after);
            for (var pass = 0; pass < 3; pass++) await owner.RunAsync();
            var row = store.Instances[physical.Id];
            check(actualBirth && store.Instances.Count == 1 && row.Revision == 2 && Json.Read<CounterState>(row.State).Ids.SequenceEqual(["creator", "after"]) &&
                store.Messages.GetValueOrDefault(after.InboxKey) is not { Done: false } && (mode == "new input spelling" || owner.Messages.Any(i => i.Id == planned.InboxKey)),
                $"ordinary input observes an equivalent birth before the minted Start is due, {mode}, durable={durable}, revision={row.Revision}");
        }
    }

    private static async Task CheckRecreatedNeighbors(Action<bool, string> check)
    {
        foreach (var mode in new[] { "no traffic", "early target", "late target" })
        {
            var store = new InMemoryStore(1, () => 100);
            var wire = CaseInsensitive(store);
            await using var owner = new Worker("reuse", 0, wire, new WorkflowInstance([new Counter()]), new() { MaxInstances = 100, Monotonic = () => 100 });
            await new DurableHost([owner]).ActivateAsync(0);
            var neighbors = Enumerable.Range(0, 3).Select(n => InstanceRef.In(0, $"middle-{n}")).ToArray();
            foreach (var id in neighbors)
            {
                owner.Post(new("birth-" + id.Id, id, "add", Json.Write(new Add("born")), TargetCreation: 1,
                    Start: Counter.Spec with { Creation = new(1, 0, id.Id) }), fresh: true);
                await owner.RunAsync();
                owner.Post(new("stop-" + id.Id, id, Kinds.Terminate, TargetCreation: 1)); await owner.RunAsync();
                owner.Post(new("delete-" + id.Id, id, Kinds.Delete, TargetCreation: 1)); await owner.RunAsync();
            }
            var victim = InstanceRef.In(0, mode == "early target" ? "early" : "z-waiting");
            var input = new Message("waiting", victim, "add", Json.Write(new Add("target")), Start: Counter.Spec);
            await store.AddAsync(input); await owner.PollAsync();
            var reads = wire.LoadCalls;
            var correct = neighbors.All(id => store.Instances[id.Id] is { Retired: true, Creation: 1 });
            for (var pass = 0; pass < 12; pass++)
            {
                if (mode != "no traffic")
                    foreach (var id in neighbors)
                    {
                        var previous = store.Instances[id.Id];
                        if (id == neighbors[pass % neighbors.Length])
                        {
                            var generation = previous.Creation + 1; var start = $"recreate-{pass}";
                            correct &= previous.Retired;
                            owner.Post(new(start, id, "add", Json.Write(new Add(start)), TargetCreation: generation,
                                Start: Counter.Spec with { Creation = new(generation, previous.Creation, start) }));
                        }
                        else if (!previous.Retired) owner.Post(new($"control-{pass}-{id.Id}", id,
                            previous.Status == InstanceStatus.Running ? Kinds.Terminate : Kinds.Delete, TargetCreation: previous.Creation));
                    }
                await owner.RunAsync();
                if (mode != "no traffic")
                {
                    var born = store.Instances[neighbors[pass % neighbors.Length].Id];
                    correct &= !born.Retired && Json.Read<CounterState>(born.State).Ids.SequenceEqual([$"recreate-{pass}"]);
                }
                if (pass == 1) correct &= store.Instances.ContainsKey(victim.Id);
            }
            check(correct && store.Instances.GetValueOrDefault(victim.Id) is { Revision: 1 } row && Json.Read<CounterState>(row.State).Ids.SequenceEqual(["waiting"]) &&
                store.Messages.GetValueOrDefault(input.InboxKey) is not { Done: false } && wire.LoadCalls - reads == 1 &&
                neighbors.All(id => store.Instances[id.Id].Creation == (mode == "no traffic" ? 1 : 5)),
                $"qualified recreations preserve progress at an unrelated physical key, {mode}, waiting applied={store.Instances.ContainsKey(victim.Id)}, lookups={wire.LoadCalls - reads}");
        }
    }

    private static async Task CheckMergedSpellings(Action<bool, string> check)
    {
        foreach (var keepPhysical in new[] { false, true })
        {
            long time = 100;
            var store = new InMemoryStore(1, () => time);
            var wire = CaseInsensitive(store);
            var lookup = wire.AliasLookup!;
            string[] requested = [];
            wire.AliasLookup = names => { requested = [.. names]; return lookup(names); };
            await using var owner = new Worker("rekey", 0, wire, new WorkflowInstance([new Counter()]), new() { MaxInstances = 0, Monotonic = () => time });
            await new DurableHost([owner]).ActivateAsync(0);
            var names = new[] { "role", "ROLE", "rOLE", "Role" }.Select(name => InstanceRef.In(0, name)).ToArray();
            var actual = names[^1];
            Message AddAt(string key, InstanceRef id, long? due = null) => new(key, id, "add", Json.Write(new Add(key)), Due: due, TargetCreation: 1);
            for (var i = 0; i < names.Length - 1; i++) owner.Post(AddAt($"due-{i}", names[i], time + 1));
            await owner.RunAsync(); // one absent canonical group with two known aliases
            owner.Post(AddAt("birth", actual) with { Start = Counter.Spec with { Creation = new(1, 0, "first") } }, fresh: true);
            if (keepPhysical) owner.Post(AddAt("pinned", actual, time + 1));
            await owner.RunAsync();
            owner.Post(AddAt("resolve", names[0])); await owner.RunAsync();
            var reads = wire.LoadCalls;
            for (var i = 0; i < names.Length; i++) owner.Post(AddAt($"hot-{i}", names[i]));
            await owner.RunAsync(); time++; await owner.RunAsync();
            var ids = Json.Read<CounterState>(store.Instances[actual.Id].State).Ids;
            var hot = reads == wire.LoadCalls && ids.Distinct().Count() == ids.Count && ids.Count == (keepPhysical ? 10 : 9);
            // After eviction every spelling must reach the provider under its original name.
            for (var i = 0; i < names.Length; i++) owner.Post(AddAt($"again-{i}", names[i]));
            await owner.RunAsync();
            var cold = requested.ToHashSet().SetEquals(names.Select(id => id.Id));
            owner.Post(new("stop", names[1], Kinds.Terminate, TargetCreation: 1)); await owner.RunAsync();
            owner.Post(new("erase", names[2], Kinds.Delete, TargetCreation: 1)); await owner.RunAsync();
            owner.Post(new("reborn", names[0], "add", Json.Write(new Add("new")), TargetCreation: 2,
                Start: Counter.Spec with { Creation = new(2, 1, "next") })); await owner.RunAsync();
            var row = store.Instances[actual.Id];
            check(hot && cold && row.Creation == 2 && row.Revision == 1 && Json.Read<CounterState>(row.State).Ids.SequenceEqual(["reborn"]) &&
                store.Instances.Count == 1 && !owner.Messages.Any(),
                $"rekeying retains every hot alias, evicts the entire group and recreates one actor; cached physical={keepPhysical}, hot={hot}, original lookups={cold}");
        }
    }

    private static async Task CheckEphemeralDuringAliasRetry(Action<bool, string> check)
    {
        var store = new InMemoryStore(1, () => 100);
        var wire = CaseInsensitive(store);
        await using var owner = new Worker("inline", 0, wire, new WorkflowInstance([new Counter(), new Sequence()]),
            new() { MaxInstances = 0, Monotonic = () => 100 });
        await new DurableHost([owner]).ActivateAsync(0);
        void Inline(string id) => owner.Post(new(id, InstanceRef.In(0, id), Kinds.Start, Start: Sequence.Spec(1, Sequence.Ephemeral)), fresh: true);
        Inline("standalone");
        await owner.RunAsync();
        var standalone = wire.LoadCalls == 0 && wire.CommitCalls == 0 && !owner.Messages.Any();
        var target = InstanceRef.In(0, "durable");
        owner.Post(new("seed", target, "add", Json.Write(new Add("seed")), Start: Counter.Spec), fresh: true);
        owner.RunTurn(target.Id);
        await owner.FlushAsync();
        owner.Post(new("second", target, "add", Json.Write(new Add("second"))));
        var arrivals = 0;
        wire.AfterLoad = _ => { Inline($"inline-{++arrivals}"); return Task.CompletedTask; };
        await owner.RunAsync();
        await owner.RunAsync();
        var row = store.Instances[target.Id];
        check(standalone && arrivals == 2 && wire.LoadCalls == 2 && wire.CommitCalls == 2 &&
            row.Revision == 2 && store.Instances.Count == 1 && !owner.Messages.Any() && !owner.NeedsLoad,
            $"minted inline Ephemeral actors finish without their own read or commit during a positive retry: standalone={standalone}, lookups={wire.LoadCalls}, commits={wire.CommitCalls}, arrivals={arrivals}");
    }

    private static async Task CheckLoadAfterEviction(Action<bool, string> check)
    {
        var store = new InMemoryStore(1, () => 100);
        var wire = new DelayedCommit(store);
        await using var owner = new Worker("owner", 0, wire, new WorkflowInstance([new Counter()]),
            new() { MaxInstances = 0, Monotonic = () => 100 });
        await new DurableHost([owner]).ActivateAsync(0);
        var target = InstanceRef.In(0, "evicted");
        owner.Post(new("first", target, "add", Json.Write(new Add("one")), Start: Counter.Spec), fresh: true);
        owner.RunTurn(target.Id);
        owner.Post(new("unloaded", InstanceRef.In(0, "other"), "wake"));
        var reply = new TaskCompletionSource();
        wire.AfterLoad = _ => reply.Task;
        var missing = owner.LoadAsync(); // includes the fresh actor before its first commit
        await owner.FlushAsync();
        var evicted = owner.Evicted > 0 && store.Instances[target.Id].Revision == 1 && owner.Instances.All(s => s.Id != target.Id);
        owner.Post(new("second", target, "add", Json.Write(new Add("two"))));
        reply.SetResult();
        await missing;
        var needsOwnRead = owner.NeedsLoad && owner.Instances.All(s => s.Id != target.Id);
        await owner.LoadAsync();
        var current = owner.Instances.Single(s => s.Id == target.Id).Row?.Revision;
        owner.RunTurn(target.Id);
        await owner.FlushAsync();
        var row = store.Instances[target.Id];
        check(evicted && needsOwnRead && current == 1 && row.Revision == 2 && Json.Read<CounterState>(row.State).Ids.SequenceEqual(["first", "second"]),
            $"delayed Load across same-owner eviction and new delivery: evicted={evicted}, needs own read={needsOwnRead}, loaded={current}, committed={row.Revision}");
    }
}

// Delay a successful commit's response, after the inner store has actually committed.
public sealed class DelayedCommit(IKernelStore inner) : IKernelStore
{
    public bool SupportsCommitWindow => inner.SupportsCommitWindow;
    public bool CreationIdentityAvailable => inner.CreationIdentityAvailable;
    public bool PhysicalRetirementAvailable => inner.PhysicalRetirementAvailable;
    public bool CommitClockAvailable => inner.CommitClockAvailable;
    public bool DelayNext, DelayTake;
    public Func<CancellationToken, Task>? BeforePoll, AfterLoad;
    public int PollCalls, LoadCalls, CommitCalls;
    public TaskCompletionSource Taken = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource TakeReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Landed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
    {
        CommitCalls++;
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
    public async ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken ct = default)
    {
        PollCalls++;
        if (BeforePoll is not null) await BeforePoll(ct);
        return await inner.PollAsync(partition, epoch, horizon, limit, ct);
    }
    public ValueTask<bool> AddAsync(Message message, CancellationToken ct = default) => inner.AddAsync(message, ct);
    public ValueTask<EventAcceptance> AddEventAsync(EventDelivery delivery, CancellationToken ct = default) => inner.AddEventAsync(delivery, ct);
    public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default) => inner.LoadAsync(ids, ct);
    public Func<IReadOnlyCollection<string>, IReadOnlyList<ActorLookup>>? AliasLookup;
    public bool HasActorAliases => AliasLookup is not null || inner.HasActorAliases;
    public async ValueTask<IReadOnlyList<ActorLookup>> LoadActorsAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default)
    {
        LoadCalls++;
        var rows = AliasLookup?.Invoke(ids) ?? await inner.LoadActorsAsync(ids, ct);
        if (AfterLoad is not null) await AfterLoad(ct);
        return rows;
    }
    public ValueTask PublishAsync(EventRow row, CancellationToken ct = default) => inner.PublishAsync(row, ct);
    public ValueTask SubscribeAsync(SubscriptionRow row, CancellationToken ct = default) => inner.SubscribeAsync(row, ct);
    public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken ct = default) => inner.FindSubscriptionsAsync(key, ct);
    public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => inner.FindEventsAsync(keys, ct);
    public ValueTask<IReadOnlyList<EventRoute>> PublishedMatchesAsync(EventRow published, CancellationToken ct = default) => inner.PublishedMatchesAsync(published, ct);
    public ValueTask<IReadOnlyList<EventRoute>> KeptMatchesAsync(IReadOnlyList<SubscriptionRow> waits, CancellationToken ct = default) => inner.KeptMatchesAsync(waits, ct);
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken ct = default) => inner.ReadJournalAsync(partition, after, take, ct);
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalOfAsync(int partition, string instance, long after, CancellationToken ct = default) => inner.ReadJournalOfAsync(partition, instance, after, ct);
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
