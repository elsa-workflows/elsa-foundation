using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Workflows;

internal static partial class ShortWaitRaces
{
    private sealed class Ordering : Model<List<string>>
    {
        public override string Key => "race-order";
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var m in turn.Inbox)
                if (m.Kind == Kinds.Start)
                { turn.Send(turn.Instance.Ref, "A", options: new SendOptions(Delay: TimeSpan.FromMilliseconds(20))); turn.Send(turn.Instance.Ref, "wake", options: new SendOptions(Delay: TimeSpan.FromMilliseconds(10))); }
                else if (m.Kind == "wake")
                { turn.Send(turn.Instance.Ref, "B", options: new SendOptions(Delay: TimeSpan.FromMilliseconds(10))); turn.Subscribe("outside", "wait"); }
                else if (m.Kind is "A" or "B") state.Add(m.Kind);
        }
    }
    private sealed class Backlog : Model<List<string>>
    {
        public override string Key => "race-backlog";
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var m in turn.Inbox)
                if (m.Kind == Kinds.Start) turn.Send(turn.Instance.Ref, "timer", options: new SendOptions(Delay: TimeSpan.FromMilliseconds(10)));
                else if (m.Kind is "older" or "timer") state.Add(m.Kind);
        }
    }
    private sealed class FutureBarrier : Model<List<string>>
    {
        public override string Key => "future-barrier";
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var m in turn.Inbox)
                if (m.Kind == Kinds.Start) { turn.Send(turn.Instance.Ref, "future", options: new SendOptions(Delay: TimeSpan.FromMilliseconds(50))); turn.Send(turn.Instance.Ref, "promote"); }
                else if (m.Kind == "promote") turn.Subscribe("future", "wait");
                else if (m.Kind is "now" or "future") state.Add(m.Kind);
        }
    }
    private sealed class DelayedControl(string kind) : Model<List<string>>
    {
        public override string Key => "delayed-control";
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var m in turn.Inbox)
                if (m.Kind == Kinds.Start) turn.Send(turn.Instance.Ref, kind, options: new SendOptions(Delay: TimeSpan.FromMilliseconds(50)));
                else if (m.Kind == "outside") state.Add("outside");
        }
    }
    private sealed class TrialCut : Model<List<string>>
    {
        public override string Key => "trial-cut";
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox) state.Add(message.Kind);
        }
    }
    private static void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    public static async Task<int> Run()
    {
        var failed = 0;
        async Task Check(string name, Func<Task> run) { try { await run(); Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
        await Check("promotion preserves timer order in store, live and recovered owners", async () =>
        {
          foreach (var recover in new[] { false, true })
          {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var worker = new Worker("ordering", 0, store,
                new WorkflowInstance([new Ordering()]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromMilliseconds(100) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); var id = InstanceRef.In(0, "order");
            worker.Post(new("start-order", id, Kinds.Start, Start: new("race-order", "order", Ephemeral: true)), fresh: true);
            await worker.RunAsync(); clock = 110; await worker.RunAsync();
            var rows = store.Messages.Values.Where(x => x.Message.Kind is "A" or "B").OrderBy(x => x.Seq).Select(x => x.Message.Kind).ToArray();
            Need(rows.SequenceEqual(["A", "B"]), "durable message order is " + string.Join(",", rows));
            if (recover)
            {
                await worker.DisposeAsync();
                worker = new Worker("replacement", 0, store, new WorkflowInstance([new Ordering()]),
                    new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromMilliseconds(100) });
                await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync();
            }
            clock = 120;
            for (var i = 0; i < 5; i++) { if (recover) await worker.PollAsync(); await worker.RunAsync(); }
            var state = Json.Read<List<string>>(store.Instances[id.Id].State);
            Need(state.SequenceEqual(["A", "B"]), $"{(recover ? "recovered" : "live")} message order is " + string.Join(",", state));
            await worker.DisposeAsync();
          }
        });
        await Check("promoted due input cannot overtake unpolled backlog", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock);
            var worker = new Worker("backlog", 0, store, new WorkflowInstance([new Backlog()]), new WorkerOptions { Monotonic = () => clock, PollLimit = 1, ShortWait = TimeSpan.FromMilliseconds(20) });
            await new DurableHost([worker]).ActivateAsync(0); var id = InstanceRef.In(0, "backlog");
            await store.AddAsync(new("older", id, "older"));
            worker.Post(new("start-backlog", id, Kinds.Start, Start: new("race-backlog", "backlog", Ephemeral: true)), fresh: true);
            await worker.RunAsync(); clock = 120; await worker.FlushAsync();
            for (var i = 0; i < 4; i++) await worker.RunAsync();
            for (var i = 0; i < 8; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            var state = Json.Read<List<string>>(store.Instances[id.Id].State);
            Need(state.SequenceEqual(["older", "timer"]), "delivery order is " + string.Join(",", state));
            await worker.DisposeAsync();
        });
        foreach (var (caughtUp, terminate) in new[] { (false, false), (false, true), (true, false), (true, true) })
            await Check($"future promoted timer does not block {(terminate ? "termination" : "immediate input")}{(caughtUp ? " after catch-up" : " while catching up")}", async () =>
            {
                long clock = 100; var store = new InMemoryStore(1, () => clock); var observed = new List<string>();
                var worker = new Worker("future-barrier", 0, store, new WorkflowInstance([new FutureBarrier()]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromMilliseconds(100), Horizon = TimeSpan.FromMilliseconds(1), OnTurn = (_, messages, _) => observed.AddRange(messages.Select(m => m.Kind)) });
                await new DurableHost([worker]).ActivateAsync(0); if (caughtUp) await worker.PollAsync(); var id = InstanceRef.In(0, "future-barrier");
                worker.Post(new("start-future", id, Kinds.Start, Start: new("future-barrier", "future", Ephemeral: true)), fresh: true);
                await worker.RunAsync(); await worker.RunAsync();
                Need(store.Instances.ContainsKey(id.Id) && store.Messages.Values.Any(m => m.Message.Kind == "future"), "future timer was not promoted");
                if (terminate) worker.Post(new("terminate-future", id, Kinds.Terminate));
                else await store.AddAsync(new("now-future", id, "now"));
                for (var i = 0; i < 8; i++) { await worker.PollAsync(); await worker.RunAsync(); }
                if (terminate) Need(store.Instances[id.Id].Status == InstanceStatus.Cancelled, "future timer blocked termination");
                else
                {
                    Need(Json.Read<List<string>>(store.Instances[id.Id].State).SequenceEqual(["now"]), "future timer blocked immediate input");
                    Need(store.Messages.Values.Any(m => m.Message.Kind == "future"), "future timer ran before its due time");
                    clock = 150;
                    if (!caughtUp)
                    {
                        worker.RunTurn(id.Id);
                        Need(!observed.Contains("future"), "unobserved timer ran without a poll and mark");
                    }
                    for (var i = 0; i < 5; i++) { await worker.PollAsync(); await worker.RunAsync(); }
                    Need(Json.Read<List<string>>(store.Instances[id.Id].State).SequenceEqual(["now", "future"]), "due timer did not run after poll and mark");
                }
                await worker.DisposeAsync();
            });
        foreach (var (caughtUp, ephemeral, command) in
                 from caught in new[] { false, true }
                 from eph in new[] { false, true }
                 from kind in new[] { Kinds.Terminate, Kinds.Suspend, Kinds.Resume }
                 select (caught, eph, kind))
            await Check($"delayed {command}: {(caughtUp ? "caught up" : "catching up")}, {(ephemeral ? "ephemeral" : "ordinary")}", async () =>
            {
                long clock = 100; var store = new InMemoryStore(1, () => clock);
                var worker = new Worker("delayed-control", 0, store, new WorkflowInstance([new DelayedControl(command)]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromMilliseconds(100), Horizon = TimeSpan.FromMilliseconds(1), PollLimit = 1 });
                await new DurableHost([worker]).ActivateAsync(0); if (caughtUp) await worker.PollAsync(); var id = InstanceRef.In(0, "delayed-control");
                worker.Post(new("start-delayed", id, Kinds.Start, Start: new("delayed-control", "delayed", Ephemeral: ephemeral)), fresh: true); await worker.RunAsync();
                await store.AddAsync(new("outside-delayed", id, "outside"));
                for (var i = 0; i < 8; i++) { await worker.PollAsync(); await worker.RunAsync(); }
                Need(store.Instances.TryGetValue(id.Id, out var row) && row.Status == InstanceStatus.Running && !row.Suspended && Json.Read<List<string>>(row.State).SequenceEqual(["outside"]), "future command ran early or blocked ordinary input");
                if (command == Kinds.Resume)
                {
                    worker.Post(new("suspend-for-resume", id, Kinds.Suspend));
                    for (var i = 0; i < 3; i++) await worker.RunAsync();
                    Need(store.Instances[id.Id].Suspended, "immediate suspend did not land");
                }
                clock = 149;
                for (var i = 0; i < 5; i++) { await worker.PollAsync(); await worker.RunAsync(); }
                row = store.Instances[id.Id];
                Need(row.Status == InstanceStatus.Running && row.Suspended == (command == Kinds.Resume), "command ran inside the poll horizon before its due time");
                clock = 150;
                for (var i = 0; i < 8; i++) { await worker.PollAsync(); await worker.RunAsync(); }
                row = store.Instances[id.Id];
                Need(command switch { Kinds.Terminate => row.Status == InstanceStatus.Cancelled, Kinds.Suspend => row.Suspended, _ => !row.Suspended }, "due command did not run");
                await worker.DisposeAsync();
            });
        await Check("a due unobserved command waits for its poll and mark", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock);
            var worker = new Worker("unmarked-command", 0, store, new WorkflowInstance([new DelayedControl(Kinds.Terminate)]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromMilliseconds(100), Horizon = TimeSpan.FromMilliseconds(1), PollLimit = 1 });
            await new DurableHost([worker]).ActivateAsync(0); var id = InstanceRef.In(0, "unmarked-command");
            worker.Post(new("start-unmarked-command", id, Kinds.Start, Start: new("delayed-control", "delayed", Ephemeral: true)), fresh: true); await worker.RunAsync();
            await store.AddAsync(new("force-command-write", id, "outside"));
            await worker.PollAsync(); await worker.RunAsync(); await worker.RunAsync();
            for (var i = 0; i < 4; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            Need(store.Instances[id.Id].Status == InstanceStatus.Running && store.Messages.Values.Any(m => m.Message.Kind == Kinds.Terminate), "setup did not leave a future durable command");
            clock = 150; worker.RunTurn(id.Id);
            Need(worker.Queued == 0 && store.Instances[id.Id].Status == InstanceStatus.Running, $"unmarked due command started before poll and mark (queued={worker.Queued}, rows={string.Join(",", store.Messages.Values.Select(m => $"{m.Message.Kind}:{m.Seen}"))})");
            for (var i = 0; i < 5; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            Need(store.Instances[id.Id].Status == InstanceStatus.Cancelled, "due command did not run after mark");
            await worker.DisposeAsync();
        });
        await Check("immediate resume passes unmarked ordinary input while suspended", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock);
            var worker = new Worker("resume-unmarked", 0, store, new WorkflowInstance([new DelayedControl(Kinds.Suspend)]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromMilliseconds(100), Horizon = TimeSpan.FromMilliseconds(1) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); var id = InstanceRef.In(0, "resume-unmarked");
            worker.Post(new("start-resume-unmarked", id, Kinds.Start, Start: new("delayed-control", "delayed")), fresh: true); await worker.RunAsync();
            worker.Post(new("suspend-immediate", id, Kinds.Suspend)); await worker.RunAsync();
            Need(store.Instances[id.Id].Suspended, "setup did not suspend");
            await store.AddAsync(new("unmarked-ordinary", id, "outside")); await worker.PollAsync();
            worker.Post(new("resume-immediate", id, Kinds.Resume)); worker.RunTurn(id.Id);
            Need(worker.Queued == 1, "immediate resume did not pass withheld unmarked input");
            await worker.FlushAsync(); Need(!store.Instances[id.Id].Suspended, "immediate resume did not commit");
            await worker.DisposeAsync();
        });
        await Check("a delayed command writes an unwritten instance first at its due time", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock);
            var worker = new Worker("write-before-command", 0, store, new WorkflowInstance([new DelayedControl(Kinds.Terminate)]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromMilliseconds(100), Horizon = TimeSpan.FromMilliseconds(1) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); var id = InstanceRef.In(0, "write-before-command");
            worker.Post(new("start-write-before-command", id, Kinds.Start, Start: new("delayed-control", "delayed", Ephemeral: true)), fresh: true); await worker.RunAsync();
            Need(!store.Instances.ContainsKey(id.Id), "instance was written before its command became due");
            clock = 150; worker.RunTurn(id.Id); await worker.FlushAsync();
            Need(store.Instances.TryGetValue(id.Id, out var row) && row.Status == InstanceStatus.Running, "due command ran without first writing the instance");
            worker.RunTurn(id.Id); await worker.FlushAsync();
            Need(store.Instances[id.Id].Status == InstanceStatus.Cancelled, "due command did not run after the first write");
            await worker.DisposeAsync();
        });
        await Check("a future command does not block a due suspect's trial", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "future-trial");
            store.Partitions[0].Epoch = 1;
            store.Instances[id.Id] = new InstanceRow(id.Id, "delayed-control", "delayed", InstanceStatus.Running, "[]", 1, null);
            await store.AddAsync(new("future-trial-command", id, Kinds.Terminate, Due: 150));
            await store.AddAsync(new("trial-suspect", id, "outside")); store.Messages["trial-suspect"].Seen = 1;
            var worker = new Worker("future-trial-owner", 0, store, new WorkflowInstance([new DelayedControl(Kinds.Terminate)]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
            Need(worker.Trial.Contains("trial-suspect"), "future command blocked the eligible suspect from starting trial");
            worker.RunTurn(id.Id); await worker.FlushAsync();
            Need(store.Instances[id.Id].Status == InstanceStatus.Running && Json.Read<List<string>>(store.Instances[id.Id].State).SequenceEqual(["outside"]), "eligible suspect did not complete before the future command");
            await worker.DisposeAsync();
        });
        foreach (var command in new[] { Kinds.Suspend, Kinds.Terminate, Kinds.Resume })
            await Check($"a {command} maturing during a trial waits for the suspect and releases the shared gate", async () =>
            {
                long clock = 100; var store = new InMemoryStore(2, () => clock); var id = InstanceRef.In(0, "trial-cut");
                store.Partitions[0].Epoch = 1;
                store.Instances[id.Id] = new InstanceRow(id.Id, "delayed-control", "delayed", InstanceStatus.Running, "[]", 1, null);
                await store.AddAsync(new("maturing-command", id, command, Due: 150));
                await store.AddAsync(new("cut-suspect", id, "outside")); store.Messages["cut-suspect"].Seen = 1;
                var gate = new TrialGate(); var turns = new List<string>();
                WorkerOptions Options() => new() { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100), Partitions = 2, Gate = gate, OnTurn = (actor, _, _) => turns.Add(actor.Id) };
                var actorType = new WorkflowInstance([new DelayedControl(Kinds.Suspend)]);
                var owner = new Worker("trial-cut-owner", 0, store, actorType, Options()); var peer = new Worker("trial-cut-peer", 1, store, actorType, Options());
                var host = new DurableHost([owner, peer]); await host.ActivateAsync(0); await host.ActivateAsync(1);
                await owner.PollAsync(); await owner.LoadAsync(); await owner.FlushAsync();
                Need(owner.Trial.Contains("cut-suspect"), "suspect was not marked on trial");
                var sibling = InstanceRef.In(0, "trial-sibling"); var other = InstanceRef.In(1, "trial-other-partition");
                owner.Post(new("start-trial-sibling", sibling, Kinds.Start, Start: new("delayed-control", "delayed", Ephemeral: true)), fresh: true);
                peer.Post(new("start-trial-other", other, Kinds.Start, Start: new("delayed-control", "delayed", Ephemeral: true)), fresh: true);
                clock = 150; owner.RunTurn(sibling.Id); peer.RunTurn(other.Id); owner.RunTurn(id.Id);
                Need(turns.SequenceEqual([id.Id]), "a different actor ran or the selected suspect was unreachable");
                await owner.FlushAsync();
                Need(!owner.Trial.Any() && store.Messages.ContainsKey("maturing-command") && Json.Read<List<string>>(store.Instances[id.Id].State).SequenceEqual(["outside"]), "trial did not commit before the command");
                for (var i = 0; i < 4; i++) { await owner.RunAsync(); await peer.RunAsync(); }
                Need(turns.Contains(sibling.Id) && turns.Contains(other.Id) && !store.Messages.ContainsKey("maturing-command"), "gate did not release or due command did not run");
                var row = store.Instances[id.Id];
                Need(command switch { Kinds.Suspend => row.Suspended, Kinds.Terminate => row.Status == InstanceStatus.Cancelled, _ => !row.Suspended }, "command did not apply after trial");
                await owner.DisposeAsync(); await peer.DisposeAsync();
            });
        foreach (var post in new[] { false, true })
            await Check($"a command maturing during trial excludes later {(post ? "local" : "durable")} input", async () =>
            {
                long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "trial-cutoff"); var batches = new List<string[]>();
                store.Partitions[0].Epoch = 1;
                store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
                await store.AddAsync(new("cutoff-control", id, Kinds.Terminate, Due: 150));
                await store.AddAsync(new("cutoff-suspect", id, "work")); store.Messages["cutoff-suspect"].Seen = 1;
                var worker = new Worker("cutoff-owner", 0, store, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100), OnTurn = (_, messages, _) => batches.Add(messages.Select(m => m.Id).ToArray()) });
                await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync();
                worker.Post(new("cutoff-before", id, "before")); await worker.FlushAsync();
                Need(worker.Trial.Contains("cutoff-suspect"), "suspect was not marked on trial");
                clock = 150;
                if (post) worker.Post(new("cutoff-later", id, "later"));
                else { await store.AddAsync(new("cutoff-later", id, "later")); await worker.PollAsync(); await worker.FlushAsync(); }
                for (var i = 0; i < 10; i++) { await worker.PollAsync(); await worker.RunAsync(); }
                var row = store.Instances[id.Id];
                Need(row.Status == InstanceStatus.Cancelled && Json.Read<List<string>>(row.State).SequenceEqual(["work", "before"]), "input after trial mark ran before termination: " + row.State);
                Need(batches.Count > 0 && batches[0].SequenceEqual(["cutoff-suspect", "cutoff-before"]), "trial batch admitted a later input or lost a pre-mark input");
                await worker.DisposeAsync();
            });
        await Check("a future command does not cut a trial batch before it is due", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "trial-no-cut");
            store.Partitions[0].Epoch = 1;
            store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
            await store.AddAsync(new("no-cut-control", id, Kinds.Terminate, Due: 150));
            await store.AddAsync(new("no-cut-suspect", id, "work")); store.Messages["no-cut-suspect"].Seen = 1;
            var worker = new Worker("no-cut-owner", 0, store, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
            Need(worker.Trial.Contains("no-cut-suspect"), "suspect was not marked on trial");
            clock = 149; worker.Post(new("no-cut-later", id, "later")); worker.RunTurn(id.Id); await worker.FlushAsync();
            Need(Json.Read<List<string>>(store.Instances[id.Id].State).SequenceEqual(["work", "later"]) && store.Instances[id.Id].Status == InstanceStatus.Running, "future command changed the pre-existing trial batch");
            clock = 150; for (var i = 0; i < 4; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            Need(store.Instances[id.Id].Status == InstanceStatus.Cancelled, "command did not run when due");
            await worker.DisposeAsync();
        });
        await Check("a control cut retains a same-commit marked input", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "trial-marked-together");
            store.Partitions[0].Epoch = 1;
            store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
            await store.AddAsync(new("together-control", id, Kinds.Terminate, Due: 150));
            await store.AddAsync(new("together-suspect", id, "work")); store.Messages["together-suspect"].Seen = 1;
            await store.AddAsync(new("together-ordinary", id, "before"));
            var worker = new Worker("together-owner", 0, store, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
            Need(worker.Trial.Contains("together-suspect") && store.Messages["together-ordinary"].Seen == worker.Epoch, "trial and ordinary input were not marked together");
            clock = 150; for (var i = 0; i < 8; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            Need(store.Instances[id.Id].Status == InstanceStatus.Cancelled && Json.Read<List<string>>(store.Instances[id.Id].State).SequenceEqual(["work", "before"]), "cut lost input marked with the trial");
            await worker.DisposeAsync();
        });
        await Check("a control cut retains an earlier input marked after the trial", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "trial-marked-later");
            store.Partitions[0].Epoch = 1;
            store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
            await store.AddAsync(new("later-mark-control", id, Kinds.Terminate, Due: 150));
            await store.AddAsync(new("later-mark-suspect", id, "work")); store.Messages["later-mark-suspect"].Seen = 1;
            await store.AddAsync(new("later-mark-ordinary", id, "before"));
            var worker = new Worker("later-mark-owner", 0, store, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100), MaxBatch = 1 });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
            Need(worker.Trial.Contains("later-mark-suspect") && store.Messages["later-mark-ordinary"].Seen == 0, "trial did not start before the ordinary mark");
            clock = 150; worker.RunTurn(id.Id);
            Need(worker.Queued == 0 && Json.Read<List<string>>(store.Instances[id.Id].State).Count == 0, "trial ran before its earlier cohort input was marked");
            await worker.FlushAsync();
            Need(store.Messages["later-mark-ordinary"].Seen == worker.Epoch, "ordinary input was not subsequently marked");
            for (var i = 0; i < 8; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            Need(store.Instances[id.Id].Status == InstanceStatus.Cancelled && Json.Read<List<string>>(store.Instances[id.Id].State).SequenceEqual(["work", "before"]), "cut lost an earlier input marked after the trial");
            await worker.DisposeAsync();
        });
        foreach (var timerDue in new[] { 125L, 175L })
        await Check($"a control cut handles a pre-existing timer due at {timerDue}", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "trial-earlier-timer");
            store.Partitions[0].Epoch = 1;
            store.Instances[id.Id] = new InstanceRow(id.Id, "trial-cut", "trial", InstanceStatus.Running, "[]", 1, null);
            await store.AddAsync(new("earlier-timer-control", id, Kinds.Terminate, Due: 150));
            await store.AddAsync(new("earlier-timer-suspect", id, "work")); store.Messages["earlier-timer-suspect"].Seen = 1;
            await store.AddAsync(new("earlier-timer", id, "before", Due: timerDue));
            var worker = new Worker("earlier-timer-owner", 0, store, new WorkflowInstance([new TrialCut()]), new WorkerOptions { Monotonic = () => clock, Horizon = TimeSpan.FromMilliseconds(100) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
            Need(worker.Trial.Contains("earlier-timer-suspect"), "suspect was not marked on trial");
            clock = 150; for (var i = 0; i < 8; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            var expected = timerDue < 150 ? new[] { "work", "before" } : new[] { "work" };
            Need(store.Instances[id.Id].Status == InstanceStatus.Cancelled && Json.Read<List<string>>(store.Instances[id.Id].State).SequenceEqual(expected), "cut misordered a pre-existing timer relative to the command");
            await worker.DisposeAsync();
        });
        await Check("a later suspect cannot overtake an earlier held delivery", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var id = InstanceRef.In(0, "held-before-suspect");
            store.Partitions[0].Epoch = 1;
            store.Instances[id.Id] = new InstanceRow(id.Id, "counter", "c", InstanceStatus.Running, Json.Write(new CounterState()), 1, null);
            await store.AddAsync(new("held-first", id, "add", Json.Write(new Add("first", Hold: true))));
            await store.AddAsync(new("suspect-second", id, "add", Json.Write(new Add("second"))));
            store.Messages["held-first"].Seen = 1; store.Messages["suspect-second"].Seen = 1;
            var worker = new Worker("held-trial", 0, store, new WorkflowInstance([new Counter()]), new WorkerOptions { Monotonic = () => clock });
            await new DurableHost([worker]).ActivateAsync(0);
            for (var i = 0; i < 15; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            var state = Json.Read<CounterState>(store.Instances[id.Id].State);
            Need(state.Tokens.SequenceEqual(["first", "second"]), "delivery order is " + string.Join(",", state.Tokens));
            await worker.DisposeAsync();
        });
        await More(Check);
        await Cuts(Check);
        await Maturity(Check);
        await TakeoverCuts(Check);
        await JobCuts(Check);
        await UnknownJobCuts(Check);
        Console.WriteLine($"short-wait races: {failed} failures"); return failed == 0 ? 0 : 1;
    }
}
