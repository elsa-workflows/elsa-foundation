using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Workflows;

internal static partial class ShortWaitRaces
{
    private static async Task Cuts(Func<string, Func<Task>, Task> Check)
    {
        await Check("a later trial input cannot overtake an earlier unselected suspect", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "trial-predecessor");
            store.Partitions[0].Epoch = 1; store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
            await store.AddAsync(new("predecessor-first", id, "work")); store.Messages["predecessor-first"].Seen = 1;
            await store.AddAsync(new("predecessor-second", id, "earlier")); store.Messages["predecessor-second"].Seen = 1;
            await store.AddAsync(new("predecessor-later", id, "late"));
            await store.AddAsync(new("predecessor-resume", id, Kinds.Resume));
            var worker = new Worker("predecessor-owner", 0, store, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, TrialGroup = 1 });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
            Need(worker.Trial.Contains("predecessor-first") && !worker.Trial.Contains("predecessor-second"), "trial was not isolated");
            for (var i = 0; i < 12; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            var row = store.Instances[id.Id];
            Need(Json.Read<List<string>>(row.State).SequenceEqual(["work", "earlier", "late"]), "input order or liveness: " + row.State);
            await worker.DisposeAsync();
        });
        await Check("earlier-due unselected suspect remains before a deferred control", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "trial-due-predecessor");
            store.Partitions[0].Epoch = 1; store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
            await store.AddAsync(new("due-first", id, "work")); store.Messages["due-first"].Seen = 1;
            await store.AddAsync(new("due-control", id, Kinds.Terminate, Due: 150));
            await store.AddAsync(new("due-earlier", id, "earlier", Due: 100)); store.Messages["due-earlier"].Seen = 1;
            await store.AddAsync(new("due-later", id, "late"));
            var worker = new Worker("due-predecessor-owner", 0, store, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, TrialGroup = 1, Horizon = TimeSpan.FromMilliseconds(100) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
            Need(worker.Trial.Contains("due-first") && !worker.Trial.Contains("due-earlier"), "first trial was not isolated"); clock = 150;
            for (var i = 0; i < 12; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            var row = store.Instances[id.Id];
            Need(row.Status == InstanceStatus.Cancelled && Json.Read<List<string>>(row.State).SequenceEqual(["work", "earlier", "late"]), "earlier due input lost precedence: " + row.State);
            await worker.DisposeAsync();
        });
        await Check("pre-cut input first polled after trial selection drains in order", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "trial-late-poll");
            store.Partitions[0].Epoch = 1; store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
            await store.AddAsync(new("late-poll-first", id, "work")); store.Messages["late-poll-first"].Seen = 1;
            await store.AddAsync(new("late-poll-earlier", id, "earlier"));
            await store.AddAsync(new("late-poll-later", id, "late"));
            await store.AddAsync(new("late-poll-control", id, Kinds.Resume));
            var worker = new Worker("late-poll-owner", 0, store, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, TrialGroup = 1, PollLimit = 1 });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
            Need(worker.Trial.Contains("late-poll-first"), "first trial was not selected before the other rows were observed");
            for (var i = 0; i < 3; i++) { await worker.PollAsync(); await worker.FlushAsync(); }
            for (var i = 0; i < 15; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            Need(Json.Read<List<string>>(store.Instances[id.Id].State).SequenceEqual(["work", "earlier", "late"]), "late poll blocked or reordered input: " + store.Instances[id.Id].State);
            await worker.DisposeAsync();
        });
        await Check("unmarked pre-cut predecessor is not overtaken by the control", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "unmarked-before-cut");
            store.Partitions[0].Epoch = 1; store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
            await store.AddAsync(new("unmarked-first", id, "work")); store.Messages["unmarked-first"].Seen = 1;
            var worker = new Worker("unmarked-owner", 0, store, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, TrialGroup = 1, MaxBatch = 1 });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
            Need(worker.Trial.Contains("unmarked-first"), "first trial was not selected");
            await store.AddAsync(new("unmarked-other", InstanceRef.In(0, "other"), "other"));
            await store.AddAsync(new("unmarked-earlier", id, "earlier")); await worker.PollAsync();
            worker.Post(new("unmarked-control", id, Kinds.Terminate));
            worker.RunTurn(id.Id); await worker.FlushAsync();
            Need(store.Instances[id.Id].State == "[\"work\"]", "selected trial did not commit first");
            Need(store.Messages["unmarked-earlier"].Seen != worker.Epoch, "predecessor was already marked");
            worker.RunTurn(id.Id); await worker.FlushAsync();
            for (var i = 0; i < 15; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            var row = store.Instances[id.Id];
            Need(row.Status == InstanceStatus.Cancelled && Json.Read<List<string>>(row.State).SequenceEqual(["work", "earlier"]), "control overtook unmarked predecessor: " + row.State);
            await worker.DisposeAsync();
        });
        await Check("mature command excludes later input present at trial selection", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "late-before-mark");
            store.Partitions[0].Epoch = 1; store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
            await store.AddAsync(new("before-mark-suspect", id, "work")); store.Messages["before-mark-suspect"].Seen = 1;
            var worker = new Worker("before-mark-owner", 0, store, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync(); clock = 150;
            worker.Post(new("before-mark-control", id, Kinds.Terminate, Due: 150)); worker.Post(new("before-mark-late", id, "late"));
            await worker.FlushAsync(); Need(worker.Trial.Contains("before-mark-suspect"), "suspect was not marked");
            for (var i = 0; i < 8; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            var row = store.Instances[id.Id]; Need(row.Status == InstanceStatus.Cancelled && Json.Read<List<string>>(row.State).SequenceEqual(["work"]), "late input overtook mature command: " + row.State);
            await worker.DisposeAsync();
        });
        await Check("deferred command prevents selecting a later suspect", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "late-suspect");
            store.Partitions[0].Epoch = 1; store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
            await store.AddAsync(new("first-suspect", id, "work")); store.Messages["first-suspect"].Seen = 1;
            var worker = new Worker("late-suspect-owner", 0, store, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync(); worker.Post(new("suspect-control", id, Kinds.Terminate, Due: 150)); await worker.FlushAsync();
            Need(worker.Trial.Contains("first-suspect"), "first suspect was not marked"); clock = 150;
            await store.AddAsync(new("second-suspect", id, "late")); store.Messages["second-suspect"].Seen = 1; await worker.PollAsync(); await worker.FlushAsync();
            worker.RunTurn(id.Id); await worker.FlushAsync(); await worker.PollAsync(); await worker.FlushAsync();
            Need(!worker.Trial.Contains("second-suspect"), "late suspect was selected ahead of deferred command");
            for (var i = 0; i < 8; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            var row = store.Instances[id.Id]; Need(row.Status == InstanceStatus.Cancelled && Json.Read<List<string>>(row.State).SequenceEqual(["work"]), "late suspect overtook command: " + row.State);
            await worker.DisposeAsync();
        });
        await Check("committed recall clears the deferred command cut", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "cut-recall");
            string control = id.Id + "#1.0000";
            store.Partitions[0].Epoch = 1; store.Instances[id.Id] = new InstanceRow(id.Id, "cut-recall", "trial", InstanceStatus.Running, Json.Write(new List<string> { control }), 1, null);
            await store.AddAsync(new(control, id, Kinds.Terminate, Due: 150));
            await store.AddAsync(new("recall-suspect", id, "work")); store.Messages["recall-suspect"].Seen = 1;
            var worker = new Worker("cut-recall-owner", 0, store, new WorkflowInstance([new CutRecall()]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
            Need(worker.Trial.Contains("recall-suspect"), "suspect was not selected"); clock = 150;
            await store.AddAsync(new("recall-late", id, "late")); await worker.PollAsync(); await worker.FlushAsync();
            for (var i = 0; i < 8; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            var row = store.Instances[id.Id];
            Need(row.Status == InstanceStatus.Running && Json.Read<List<string>>(row.State).SequenceEqual([control, "work", "late"]) && !store.Messages.ContainsKey(control), "recalled command left a stale cut: " + row.State);
            await worker.DisposeAsync();
        });
        foreach (var (timerDue, wake, commandAfterTimer, timerExpected) in new[]
                 { (125L, 150L, false, true), (175L, 150L, false, false), (175L, 200L, false, false),
                   (125L, 200L, false, true), (150L, 200L, false, false), (150L, 200L, true, true) })
            await Check($"trial cut: timer {timerDue}, wake {wake}, command {(commandAfterTimer ? "after" : "before")}", async () =>
            {
                long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "cut-timer");
                store.Partitions[0].Epoch = 1; store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
                if (!commandAfterTimer) await store.AddAsync(new("cut-timer-control", id, Kinds.Terminate, Due: 150));
                await store.AddAsync(new("cut-timer-suspect", id, "work")); store.Messages["cut-timer-suspect"].Seen = 1;
                await store.AddAsync(new("cut-timer-message", id, "timer", Due: timerDue));
                if (commandAfterTimer) await store.AddAsync(new("cut-timer-control", id, Kinds.Terminate, Due: 150));
                var worker = new Worker("cut-timer-owner", 0, store, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
                await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
                Need(worker.Trial.Contains("cut-timer-suspect"), "suspect was not marked on trial"); clock = wake;
                for (var i = 0; i < 10; i++) { await worker.PollAsync(); await worker.RunAsync(); }
                var row = store.Instances[id.Id]; var expected = timerExpected ? new[] { "work", "timer" } : new[] { "work" };
                Need(row.Status == InstanceStatus.Cancelled && Json.Read<List<string>>(row.State).SequenceEqual(expected), "state is " + row.State);
                await worker.DisposeAsync();
            });
        foreach (var (postCommand, postLate) in new[] { (false, false), (false, true), (true, false), (true, true) })
            await Check($"trial cut survives trial commit: command {(postCommand ? "Post" : "Add")}, late {(postLate ? "Post" : "Add")}", async () =>
            {
                long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "cut-ingress");
                store.Partitions[0].Epoch = 1; store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
                var control = new Message("cut-ingress-control", id, Kinds.Terminate, Due: 150);
                if (!postCommand) await store.AddAsync(control);
                await store.AddAsync(new("cut-ingress-suspect", id, "work")); store.Messages["cut-ingress-suspect"].Seen = 1;
                var worker = new Worker("cut-ingress-owner", 0, store, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
                await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync();
                if (postCommand) worker.Post(control); await worker.FlushAsync();
                Need(worker.Trial.Contains("cut-ingress-suspect"), "suspect was not marked on trial"); clock = 150;
                var late = new Message("cut-ingress-late", id, "late");
                if (postLate) worker.Post(late); else { await store.AddAsync(late); await worker.PollAsync(); await worker.FlushAsync(); }
                worker.RunTurn(id.Id); await worker.FlushAsync();
                Need(Json.Read<List<string>>(store.Instances[id.Id].State).SequenceEqual(["work"]), "late input joined the selected trial");
                for (var i = 0; i < 8; i++) { await worker.PollAsync(); await worker.RunAsync(); }
                var row = store.Instances[id.Id];
                Need(row.Status == InstanceStatus.Cancelled && Json.Read<List<string>>(row.State).SequenceEqual(["work"]), "deferred command was overtaken: " + row.State);
                await worker.DisposeAsync();
            });
        foreach (var (postCommand, postTimer, timerFirst) in
                 from c in new[] { false, true }
                 from t in new[] { false, true }
                 from first in new[] { false, true }
                 select (c, t, first))
            await Check($"equal-due cut: command {(postCommand ? "Post" : "Add")}, timer {(postTimer ? "Post" : "Add")}, {(timerFirst ? "timer" : "command")} first", async () =>
            {
                long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "cut-mixed-tie");
                store.Partitions[0].Epoch = 1; store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
                var worker = new Worker("cut-mixed-owner", 0, store, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
                await new DurableHost([worker]).ActivateAsync(0);
                async Task Put(Message message, bool post) { if (post) worker.Post(message); else { await store.AddAsync(message); await worker.PollAsync(); } }
                var control = new Message("mixed-control", id, Kinds.Terminate, Due: 150); var timer = new Message("mixed-timer", id, "timer", Due: 150);
                if (timerFirst) { await Put(timer, postTimer); await Put(control, postCommand); } else { await Put(control, postCommand); await Put(timer, postTimer); }
                await store.AddAsync(new("mixed-suspect", id, "work")); store.Messages["mixed-suspect"].Seen = 1;
                await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync(); Need(worker.Trial.Contains("mixed-suspect"), "suspect was not marked");
                clock = 200; for (var i = 0; i < 10; i++) { await worker.PollAsync(); await worker.RunAsync(); }
                var row = store.Instances[id.Id]; var expected = timerFirst ? (postTimer ? new[] { "work", "timer" } : new[] { "timer", "work" }) : new[] { "work" };
                Need(row.Status == InstanceStatus.Cancelled && Json.Read<List<string>>(row.State).SequenceEqual(expected), "equal-due order is " + row.State);
                await worker.DisposeAsync();
            });
        foreach (var mode in new[] { "transient", "definite", "unknown-before", "unknown-after", "release" })
            await Check($"deferred command survives {mode} at the trial commit", async () =>
            {
                long clock = 100; var store = new InMemoryStore(1, () => clock); var wire = new CutFaultStore(store); var id = InstanceRef.In(0, "cut-failure");
                store.Partitions[0].Epoch = 1; store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
                await store.AddAsync(new("cut-failure-control", id, Kinds.Terminate, Due: 150));
                await store.AddAsync(new("cut-failure-suspect", id, "work")); store.Messages["cut-failure-suspect"].Seen = 1;
                var worker = new Worker("cut-failure-owner", 0, wire, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
                var host = new DurableHost([worker]); await host.ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
                Need(worker.Trial.Contains("cut-failure-suspect"), "suspect was not marked on trial"); clock = 150;
                await store.AddAsync(new("cut-failure-late", id, "late")); await worker.PollAsync(); await worker.FlushAsync();
                worker.RunTurn(id.Id); if (mode != "release") wire.Arm(mode); await worker.FlushAsync();
                if (mode == "release")
                {
                    await worker.DisposeAsync();
                    worker = new Worker("cut-failure-next", 0, wire, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
                    host = new DurableHost([worker]); await host.ActivateAsync(0);
                }
                else if (mode.StartsWith("unknown", StringComparison.Ordinal))
                {
                    Need(!worker.Owns, "unknown commit did not reset ownership"); await host.ActivateAsync(0);
                }
                if (mode == "transient") Need(wire.Failures == 1 && wire.AfterFailureAttempts > 0, "transient commit did not retry");
                if (mode == "definite") Need(wire.Failures == 2, "definite refusal did not exercise isolation fallback");
                for (var i = 0; i < 12; i++) { await worker.PollAsync(); await worker.RunAsync(); }
                var row = store.Instances[id.Id]; var expected = mode is "definite" or "unknown-before" ? Array.Empty<string>() : new[] { "work" };
                var expectedStatus = mode == "definite" ? InstanceStatus.Faulted : InstanceStatus.Cancelled;
                Need(row.Status == expectedStatus && Json.Read<List<string>>(row.State).SequenceEqual(expected) && !store.Messages.ContainsKey("cut-failure-control"), $"failure or takeover outcome: status={row.Status}, state={row.State}, pending={string.Join(",", store.Messages.Keys)}");
                await worker.DisposeAsync();
            });
        foreach (var mode in new[] { "transient", "definite", "unknown-before", "unknown-after" })
            await Check($"deferred command's own commit handles {mode}", async () =>
            {
                long clock = 100; var store = new InMemoryStore(1, () => clock); var wire = new CutFaultStore(store); var id = InstanceRef.In(0, "cut-command-failure");
                store.Partitions[0].Epoch = 1; store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
                await store.AddAsync(new("command-failure-control", id, Kinds.Terminate, Due: 150));
                await store.AddAsync(new("command-failure-suspect", id, "work")); store.Messages["command-failure-suspect"].Seen = 1;
                var worker = new Worker("command-failure-owner", 0, wire, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
                var host = new DurableHost([worker]); await host.ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
                Need(worker.Trial.Contains("command-failure-suspect"), "suspect was not marked on trial"); clock = 150;
                await store.AddAsync(new("command-failure-late", id, "late")); await worker.PollAsync(); await worker.FlushAsync();
                worker.RunTurn(id.Id); await worker.FlushAsync();
                Need(!worker.Trial.Any() && Json.Read<List<string>>(store.Instances[id.Id].State).SequenceEqual(["work"]), "selected trial did not commit first");
                worker.RunTurn(id.Id); wire.Arm(mode); await worker.FlushAsync();
                if (mode.StartsWith("unknown", StringComparison.Ordinal)) { Need(!worker.Owns, "unknown command commit did not reset"); await host.ActivateAsync(0); }
                if (mode == "transient") Need(wire.Failures == 1 && wire.AfterFailureAttempts > 0, "transient command commit did not retry");
                if (mode == "definite") Need(wire.Failures == 2, "definite command refusal did not isolate the unit");
                for (var i = 0; i < 12; i++) { await worker.PollAsync(); await worker.RunAsync(); }
                var row = store.Instances[id.Id]; var expectedStatus = mode == "definite" ? InstanceStatus.Faulted : InstanceStatus.Cancelled;
                Need(row.Status == expectedStatus && Json.Read<List<string>>(row.State).SequenceEqual(["work"]) && !store.Messages.ContainsKey("command-failure-control"), $"command failure outcome: status={row.Status}, state={row.State}");
                await worker.DisposeAsync();
            });
        foreach (var mode in new[] { "release", "reset" })
            await Check($"a volatile deferred command's {mode} clears its in-memory cut", async () =>
            {
                long clock = 100; var store = new InMemoryStore(1, () => clock); var wire = new CutFaultStore(store); var id = InstanceRef.In(0, "cut-volatile-reset");
                store.Partitions[0].Epoch = 1; store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
                await store.AddAsync(new("volatile-reset-suspect", id, "work")); store.Messages["volatile-reset-suspect"].Seen = 1;
                var worker = new Worker("volatile-reset-owner", 0, wire, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
                var host = new DurableHost([worker]); await host.ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync();
                worker.Post(new("volatile-reset-control", id, Kinds.Terminate, Due: 150)); await worker.FlushAsync();
                Need(worker.Trial.Contains("volatile-reset-suspect"), "suspect was not marked on trial"); clock = 150;
                await store.AddAsync(new("volatile-reset-late", id, "late")); await worker.PollAsync(); await worker.FlushAsync();
                worker.RunTurn(id.Id); await worker.FlushAsync();
                Need(Json.Read<List<string>>(store.Instances[id.Id].State).SequenceEqual(["work"]), "trial did not commit before reset");
                if (mode == "release") await worker.DisposeAsync();
                else { wire.Arm("unknown-before"); await worker.BeatAsync(); Need(!worker.Owns, "unknown beat did not reset"); }
                await host.ActivateAsync(0);
                for (var i = 0; i < 10; i++) { await worker.PollAsync(); await worker.RunAsync(); }
                Need(store.Instances[id.Id].Status == InstanceStatus.Running && Json.Read<List<string>>(store.Instances[id.Id].State).SequenceEqual(["work", "late"]), "a stale in-memory cut blocked recovery");
                await worker.DisposeAsync();
            });
    }

    private sealed class CutRecall : Model<List<string>>
    {
        public override string Key => "cut-recall";
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox) { if (message.Kind == "work") turn.Recall(state[0]); state.Add(message.Kind); }
        }
    }

    private sealed class CutFaultStore(IKernelStore inner) : IKernelStore
    {
        private string? _mode; private int _remaining;
        public int Failures, AfterFailureAttempts;
        public bool CommitClockAvailable => inner.CommitClockAvailable;
        public void Arm(string mode) { _mode = mode; _remaining = mode == "definite" ? 2 : 1; }
        public async ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken ct = default)
        {
            if (_remaining == 0) { if (Failures > 0) AfterFailureAttempts++; return await inner.CommitAsync(batch, ct); }
            _remaining--; Failures++;
            if (_mode == "unknown-after") { await inner.CommitAsync(batch, ct); throw new StoreException(Failure.Unknown, "lost answer"); }
            throw new StoreException(_mode switch { "transient" => Failure.Transient, "definite" => Failure.Definite, _ => Failure.Unknown }, "injected commit failure");
        }
        public ValueTask<Lease?> TakeAsync(int p, string n, TimeSpan l, CancellationToken ct = default) => inner.TakeAsync(p, n, l, ct);
        public ValueTask<Cluster> HeartbeatAsync(string n, TimeSpan w, CancellationToken ct = default) => inner.HeartbeatAsync(n, w, ct);
        public ValueTask ReleaseAsync(int p, long e, CancellationToken ct = default) => inner.ReleaseAsync(p, e, ct);
        public ValueTask<Polled> PollAsync(int p, long e, TimeSpan h, int l, CancellationToken ct = default) => inner.PollAsync(p, e, h, l, ct);
        public ValueTask<bool> AddAsync(Message m, CancellationToken ct = default) => inner.AddAsync(m, ct);
        public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default) => inner.LoadAsync(ids, ct);
        public ValueTask PublishAsync(EventRow row, CancellationToken ct = default) => inner.PublishAsync(row, ct);
        public ValueTask SubscribeAsync(SubscriptionRow row, CancellationToken ct = default) => inner.SubscribeAsync(row, ct);
        public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken ct = default) => inner.FindSubscriptionsAsync(key, ct);
        public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => inner.FindEventsAsync(keys, ct);
        public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int p, long after, int take, CancellationToken ct = default) => inner.ReadJournalAsync(p, after, take, ct);
        public ValueTask<IReadOnlyList<JournalRow>> ReadJournalOfAsync(int p, string instance, long after, CancellationToken ct = default) => inner.ReadJournalOfAsync(p, instance, after, ct);
        public ValueTask<Projection> ProjectedAsync(int p, CancellationToken ct = default) => inner.ProjectedAsync(p, ct);
        public ValueTask<bool> ProjectAsync(int p, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken ct = default) => inner.ProjectAsync(p, from, to, rows, ct);
        public ValueTask<int> SweepAsync(CancellationToken ct = default) => inner.SweepAsync(ct);
        public ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default) => inner.ReadViewsAsync(ids, ct);
    }
}
