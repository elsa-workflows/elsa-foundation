using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Workflows;

internal static partial class ShortWaitRaces
{
    private sealed class RecallBehind : Model<List<string>>
    {
        public override string Key => "race-recall-behind";
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var m in turn.Inbox)
                if (m.Kind == Kinds.Start) state.Add(turn.Send(turn.Instance.Ref, "timer", options: new SendOptions(Delay: TimeSpan.FromMilliseconds(15))));
                else if (m.Kind == "recall") turn.Recall(state[0]);
                else if (m.Kind == "send") { turn.Send(turn.Instance.Ref, "probe"); turn.Subscribe("after-recall", "w"); }
        }
    }
    private sealed class LateTerminal : IExecutionModel
    {
        public string Key => "late-terminal";
        public Type StateType => typeof(List<string>);
        public int StateVersion => 1;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public object CreateState(StartSpec s) => new List<string>();
        public object UpgradeState(string s, int v) => throw new NotSupportedException();
        public ValueTask<object?> ExecuteJobAsync(IJob j, CancellationToken ct) => new((object?)null);
        public async ValueTask TurnAsync(ITurn t, CancellationToken ct)
        {
            foreach (var m in t.Inbox)
                if (m.Kind == Kinds.Start) { t.Record("start"); t.Send(t.Instance.Ref, "pause"); }
                else if (m.Kind == "pause") { Entered.TrySetResult(); await Resume.Task; t.Record("finished"); t.SetStatus(InstanceStatus.Finished); Done.TrySetResult(); }
        }
    }
    private sealed class AsyncTurn : IExecutionModel
    {
        public string Key => "async-short";
        public Type StateType => typeof(List<string>);
        public int StateVersion => 1;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public object CreateState(StartSpec s) => new List<string>();
        public object UpgradeState(string s, int v) => throw new NotSupportedException();
        public ValueTask<object?> ExecuteJobAsync(IJob j, CancellationToken ct) => throw new NotSupportedException();
        public async ValueTask TurnAsync(ITurn turn, CancellationToken ct)
        {
            var state = (List<string>)turn.State;
            foreach (var m in turn.Inbox)
                if (m.Kind == Kinds.Start) { state.Add("sealed"); turn.Send(turn.Instance.Ref, "wake"); }
                else if (m.Kind == "wake") { state.Add("unsealed"); Entered.TrySetResult(); await Resume.Task; state.Add("done"); Completed.TrySetResult(); }
        }
    }
    private sealed class JobOrdering : Model<List<string>>
    {
        public override string Key => "race-job-order";
        public readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Attempts;
        public override async ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken ct) { Interlocked.Increment(ref Attempts); await Resume.Task; return null; }
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var m in turn.Inbox)
                if (m.Kind == Kinds.Start)
                { turn.Send(turn.Instance.Ref, "timer", options: new SendOptions(Delay: TimeSpan.FromMilliseconds(20))); turn.StartJob("job"); }
                else if (m.Kind == Kinds.JobFinished) state.Add("result");
                else if (m.Kind == "timer") state.Add("timer");
        }
    }
    private sealed class LateRecall : IExecutionModel
    {
        public string Key => "late-recall";
        public Type StateType => typeof(List<string>);
        public int StateVersion => 1;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Go = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool RecallBeforeAwait;
        private int _attempt;
        public object CreateState(StartSpec s) => new List<string>();
        public object UpgradeState(string s, int v) => throw new NotSupportedException();
        public ValueTask<object?> ExecuteJobAsync(IJob j, CancellationToken ct) => new((object?)null);
        public async ValueTask TurnAsync(ITurn t, CancellationToken ct)
        {
            var state = (List<string>)t.State;
            foreach (var m in t.Inbox)
                if (m.Kind == Kinds.Start) { state.Add(t.Send(t.Instance.Ref, "timer", options: new SendOptions(Delay: TimeSpan.FromMilliseconds(100)))); t.Send(t.Instance.Ref, "pause"); }
                else if (m.Kind == "pause" && Interlocked.Increment(ref _attempt) == 1) { if (RecallBeforeAwait) t.Recall(state[0]); Entered.TrySetResult(); await Go.Task; if (!RecallBeforeAwait) t.Recall(state[0]); Done.TrySetResult(); }
                else if (m.Kind == "timer") state.Add("timer");
        }
    }
    private sealed class LateCancel : IExecutionModel
    {
        public string Key => "late-cancel";
        public Type StateType => typeof(List<string>);
        public int StateVersion => 1;
        public int Attempts, Turns;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public object CreateState(StartSpec s) => new List<string>();
        public object UpgradeState(string s, int v) => throw new NotSupportedException();
        public async ValueTask<object?> ExecuteJobAsync(IJob j, CancellationToken ct) { if (Interlocked.Increment(ref Attempts) == 2) Second.TrySetResult(); await Resume.Task; return null; }
        public async ValueTask TurnAsync(ITurn t, CancellationToken ct)
        {
            var state = (List<string>)t.State;
            foreach (var m in t.Inbox)
                if (m.Kind == Kinds.Start) { t.StartJob("job"); t.Send(t.Instance.Ref, "pause"); }
                else if (m.Kind == "pause" && Interlocked.Increment(ref Turns) == 1) { t.CancelJob("job"); Entered.TrySetResult(); await Resume.Task; }
                else if (m.Kind == Kinds.JobFinished) state.Add("result");
        }
    }
    private sealed class TieJobs : Model<List<string>>
    {
        public override string Key => "tie-jobs";
        public int Attempts;
        public readonly TaskCompletionSource A = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource B = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken ct) { Interlocked.Increment(ref Attempts); await (job.Key == "A" ? A.Task : B.Task); return null; }
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var m in turn.Inbox)
                if (m.Kind == Kinds.Start) { turn.StartJob("A"); turn.StartJob("B"); }
                else if (m.Kind == Kinds.JobFinished) state.Add(m.Read<JobFinished>().Key);
        }
    }
    private sealed class BusyTieJobs : IExecutionModel
    {
        public string Key => "busy-tie-jobs";
        public Type StateType => typeof(List<string>);
        public int StateVersion => 1;
        public int Attempts;
        public readonly TaskCompletionSource A = new(TaskCreationOptions.RunContinuationsAsynchronously), B = new(TaskCreationOptions.RunContinuationsAsynchronously), C = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Resume = new(TaskCreationOptions.RunContinuationsAsynchronously), Replayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly List<string[]> Batches = [];
        public object CreateState(StartSpec s) => new List<string>();
        public object UpgradeState(string s, int v) => throw new NotSupportedException();
        public async ValueTask<object?> ExecuteJobAsync(IJob j, CancellationToken ct) { Interlocked.Increment(ref Attempts); await (j.Key switch { "A" => A.Task, "B" => B.Task, _ => C.Task }); return null; }
        public async ValueTask TurnAsync(ITurn t, CancellationToken ct)
        {
            if (t.Inbox.Any(m => m.Kind == Kinds.Start)) { t.StartJob("A"); t.StartJob("B"); t.StartJob("C"); return; }
            var keys = t.Inbox.Where(m => m.Kind == Kinds.JobFinished).Select(m => m.Read<JobFinished>().Key).ToArray();
            Batches.Add(keys);
            if (Batches.Count == 1) { Entered.TrySetResult(); await Resume.Task; }
            else Replayed.TrySetResult();
            ((List<string>)t.State).AddRange(keys);
        }
    }
    private sealed class ParkJob : IExecutionModel
    {
        public string Key => "park-job";
        public Type StateType => typeof(List<string>);
        public int StateVersion => 1;
        public int Attempts;
        public bool RefuseWrite;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public object CreateState(StartSpec s) => new List<string>();
        public object UpgradeState(string s, int v) => throw new NotSupportedException();
        public async ValueTask<object?> ExecuteJobAsync(IJob j, CancellationToken ct) { Interlocked.Increment(ref Attempts); Started.TrySetResult(); await Resume.Task; return null; }
        public ValueTask TurnAsync(ITurn t, CancellationToken ct)
        {
            var state = (List<string>)t.State;
            foreach (var m in t.Inbox)
                if (m.Kind == Kinds.Start) { state.Add("sealed"); t.Record("sealed-entry"); state.Add(t.Send(t.Instance.Ref, "timer", options: new SendOptions(Delay: TimeSpan.FromMilliseconds(100)))); t.StartJob("body"); t.Send(t.Instance.Ref, "fail"); }
                else if (m.Kind == "fail") { t.Recall(state[1]); t.CancelJob("body"); if (!RefuseWrite) throw new InvalidOperationException("expected park"); t.Subscribe("force-write", "wait"); }
            return ValueTask.CompletedTask;
        }
    }
    private static async Task More(Func<string, Func<Task>, Task> Check)
    {
        foreach (var recover in new[] { false, true })
            await Check($"promotion preserves completed job priority for {(recover ? "recovered" : "live")} owner", async () =>
            {
                long clock = 100; var store = new InMemoryStore(1, () => clock);
                var model = new JobOrdering();
                var worker = new Worker("job-owner", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromMilliseconds(20) });
                await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); var id = InstanceRef.In(0, "job-order");
                worker.Post(new("start-job", id, Kinds.Start, Start: new("race-job-order", "job", Ephemeral: true)), fresh: true);
                await worker.RunAsync();
                var completion = worker.JobCompletion(id.Id, "job") ?? throw new InvalidOperationException("job did not start");
                model.Resume.TrySetResult(); await completion.WaitAsync(TimeSpan.FromSeconds(5));
                clock = 120; await worker.FlushAsync();
                if (recover)
                {
                    await worker.DisposeAsync();
                    worker = new Worker("job-next", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => clock });
                    await new DurableHost([worker]).ActivateAsync(0);
                }
                for (var i = 0; i < 8; i++) { await worker.PollAsync(); await worker.RunAsync(); }
                var state = Json.Read<List<string>>(store.Instances[id.Id].State);
                Need(state.SequenceEqual(["result", "timer"]), $"{(recover ? "recovered" : "live")} job order is " + string.Join(",", state));
                Need(model.Attempts == (recover ? 2 : 1), $"expected {(recover ? 2 : 1)} job attempts, got {model.Attempts}");
                await worker.DisposeAsync();
            });
        await Check("stand-down preserves the last sealed state and replays an in-flight input", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var model = new AsyncTurn(); var wire = new DelayedCommit(store);
            var worker = new Worker("async-owner", 0, wire, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromSeconds(2) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); var id = InstanceRef.In(0, "async");
            worker.Post(new("start-async", id, Kinds.Start, Start: new("async-short", "async", Ephemeral: true)), fresh: true); await worker.RunAsync();
            worker.RunTurn(id.Id); await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            wire.DelayNext = true; var release = worker.DisposeAsync().AsTask(); await wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Need(store.Instances.TryGetValue(id.Id, out var saved) && Json.Read<List<string>>(saved.State).SequenceEqual(["sealed"]), "release did not persist exactly the last sealed state");
            Need(store.Messages.Values.Any(x => x.Message.Kind == "wake"), "release lost the in-flight input");
            model.Resume.TrySetResult(); await model.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            wire.Reply.TrySetResult(); await release.WaitAsync(TimeSpan.FromSeconds(5));
            var replacement = new Worker("async-next", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromSeconds(2) });
            await new DurableHost([replacement]).ActivateAsync(0);
            for (var i = 0; i < 5; i++) { await replacement.PollAsync(); await replacement.RunAsync(); }
            Need(Json.Read<List<string>>(store.Instances[id.Id].State).SequenceEqual(["sealed", "unsealed", "done"]), "replacement did not replay the in-flight input exactly once");
            await replacement.DisposeAsync();
        });
        await Check("old stand-down cannot release a retaken generation", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var wire = new DelayedCommit(store);
            var worker = new Worker("same-owner", 0, wire, new WorkflowInstance([new Ordering()]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromSeconds(2) });
            var host = new DurableHost([worker]); await host.ActivateAsync(0); await worker.PollAsync(); var id = InstanceRef.In(0, "generation");
            worker.Post(new("start-generation", id, Kinds.Start, Start: new("race-order", "order", Ephemeral: true)), fresh: true); await worker.RunAsync();
            wire.DelayNext = true; var releasing = worker.DisposeAsync().AsTask(); await wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await host.ActivateAsync(0); var replacement = worker.Epoch; wire.Reply.TrySetResult(); await releasing.WaitAsync(TimeSpan.FromSeconds(5));
            Need(worker.Owns && worker.Epoch == replacement && replacement > 1 && store.Partitions[0].Epoch == replacement && store.Partitions[0].Owner == "same-owner", "old release reset or released the replacement");
            await worker.DisposeAsync();
        });
        foreach (var release in new[] { false, true })
            await Check(release ? "late old turn cannot recall replacement input" : "busy turn checkpoints at its short-wait deadline", async () =>
            {
                long clock = 100; var store = new InMemoryStore(1, () => clock); var model = new LateRecall();
                var worker = new Worker("late-owner", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromSeconds(1) });
                var host = new DurableHost([worker]); await host.ActivateAsync(0); await worker.PollAsync(); var id = InstanceRef.In(0, "late");
                worker.Post(new("start-late", id, Kinds.Start, Start: new("late-recall", "late", Ephemeral: true)), fresh: true);
                await worker.RunAsync(); worker.RunTurn(id.Id); await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (release) { await worker.DisposeAsync(); await host.ActivateAsync(0); await worker.PollAsync(); }
                else { clock = 1100; for (var i = 0; i < 5; i++) await worker.RunAsync(); }
                Need(store.Instances.ContainsKey(id.Id), "last sealed row was not checkpointed");
                model.Go.TrySetResult(); await model.Done.Task.WaitAsync(TimeSpan.FromSeconds(5)); clock = 1200;
                for (var i = 0; i < 12; i++) { await worker.PollAsync(); await worker.RunAsync(); }
                Need(Json.Read<List<string>>(store.Instances[id.Id].State).Contains("timer"), "old turn swallowed the timer: state=" + store.Instances[id.Id].State + " remaining=" + string.Join(",", store.Messages.Values.Select(m => m.Message.Kind)));
                await worker.DisposeAsync();
            });
        await Check("deadline invalidation unwinds an earlier recall reservation", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var model = new LateRecall { RecallBeforeAwait = true };
            var worker = new Worker("reservation", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromSeconds(1) });
            await new DurableHost([worker]).ActivateAsync(0); var id = InstanceRef.In(0, "reservation");
            worker.Post(new("start-reservation", id, Kinds.Start, Start: new("late-recall", "late", Ephemeral: true)), fresh: true);
            await worker.RunAsync(); worker.RunTurn(id.Id); await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock = 1100; for (var i = 0; i < 5; i++) await worker.RunAsync();
            model.Go.TrySetResult(); await model.Done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var i = 0; i < 12; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            Need(Json.Read<List<string>>(store.Instances[id.Id].State).Contains("timer"), "pre-await recall suppressed a replayed timer");
            await worker.DisposeAsync();
        });
        await Check("deadline invalidation retickets a provisionally cancelled job", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var model = new LateCancel();
            var worker = new Worker("cancel-reservation", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromSeconds(1) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); var id = InstanceRef.In(0, "cancel-reservation");
            worker.Post(new("start-cancel-reservation", id, Kinds.Start, Start: new("late-cancel", "late", Ephemeral: true)), fresh: true);
            await worker.RunAsync(); worker.RunTurn(id.Id); await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock = 1100; for (var i = 0; i < 5; i++) await worker.RunAsync();
            await model.Second.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var completion = worker.JobCompletion(id.Id, "job") ?? throw new InvalidOperationException("replacement job did not start");
            model.Resume.TrySetResult(); await completion.WaitAsync(TimeSpan.FromSeconds(5));
            for (var i = 0; i < 12; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            Need(Json.Read<List<string>>(store.Instances[id.Id].State).SequenceEqual(["result"]), "reticketed result was not selected once");
            await worker.DisposeAsync();
        });
        foreach (var maxBatch in new[] { 1, 500 })
        await Check($"release queues an expired busy checkpoint only once (batch {maxBatch})", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var model = new LateRecall(); var writes = 0;
            store.Refuses = row => { if (row.Id.EndsWith("/expired-release", StringComparison.Ordinal)) writes++; return false; };
            var worker = new Worker("expired-release", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromSeconds(1), MaxBatch = maxBatch });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); var id = InstanceRef.In(0, "expired-release");
            worker.Post(new("start-expired-release", id, Kinds.Start, Start: new("late-recall", "late", Ephemeral: true, History: History.Full)), fresh: true);
            await worker.RunAsync(); worker.RunTurn(id.Id); await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock = 1100; await worker.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var revisions = store.Journal.Values.Where(j => j.Instance == id.Id).Select(j => j.Revision).ToArray();
            Need(revisions.SequenceEqual([1L]), "journal revisions are " + string.Join(",", revisions));
            Need(writes == 1 && store.Instances.ContainsKey(id.Id) && store.Messages.Values.Any(m => m.Message.Kind == "pause") && store.Messages.Values.Any(m => m.Message.Kind == "timer"), $"checkpoint attempted {writes} times or lost state");
            model.Go.TrySetResult();
        });
        await Check("a queued expired checkpoint is not added again behind another batch", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var model = new LateRecall(); var writes = 0;
            store.Refuses = row => { if (row.Id.EndsWith("/queued-expiry", StringComparison.Ordinal)) writes++; return false; };
            var worker = new Worker("queued-expiry", 0, store, new WorkflowInstance([model, new Backlog()]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromSeconds(1), MaxBatch = 1 });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); var id = InstanceRef.In(0, "queued-expiry");
            worker.Post(new("start-queued-expiry", id, Kinds.Start, Start: new("late-recall", "late", Ephemeral: true)), fresh: true);
            await worker.RunAsync(); worker.RunTurn(id.Id); await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var other = InstanceRef.In(0, "other-first"); worker.Post(new("other-start", other, Kinds.Start, Start: new("race-backlog", "other")), fresh: true);
            worker.RunTurn(other.Id); clock = 1100; await worker.FlushAsync(); await worker.FlushAsync(); await worker.FlushAsync();
            Need(worker.Owns && writes == 1 && store.Instances.ContainsKey(id.Id), $"checkpoint attempted {writes} times or owner lost");
            model.Go.TrySetResult(); await worker.DisposeAsync();
        });
        await Check("a turn finishing after its short-wait deadline is durable", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var model = new LateTerminal();
            var worker = new Worker("terminal", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromSeconds(1) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); var id = InstanceRef.In(0, "terminal");
            worker.Post(new("start-terminal", id, Kinds.Start, Start: new("late-terminal", "terminal", Ephemeral: true, History: History.Full)), fresh: true);
            await worker.RunAsync(); worker.RunTurn(id.Id); await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock = 1100; model.Resume.TrySetResult(); await model.Done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (worker.Queued == 0 && worker.Unwritten.Contains(id.Id) && DateTime.UtcNow < timeout) await Task.Delay(1);
            for (var i = 0; i < 5; i++) await worker.RunAsync();
            Need(store.Instances.TryGetValue(id.Id, out var row) && row.Status == InstanceStatus.Finished && store.Journal.Values.Any(j => j.Instance == id.Id && j.Entries.Contains("finished")), "terminal turn lost its checkpoint or full history");
            await worker.DisposeAsync();
        });
        await Check("committed recall clears the catch-up barrier", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock);
            var worker = new Worker("recall-behind", 0, store, new WorkflowInstance([new RecallBehind()]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromMilliseconds(20), PollLimit = 1 });
            await new DurableHost([worker]).ActivateAsync(0); var id = InstanceRef.In(0, "recall-behind");
            await store.AddAsync(new("older-recall", id, "recall"));
            worker.Post(new("start-recall", id, Kinds.Start, Start: new("race-recall-behind", "recall", Ephemeral: true)), fresh: true);
            await worker.RunAsync(); clock = 120; await worker.FlushAsync();
            await worker.PollAsync(); for (var i = 0; i < 4; i++) await worker.RunAsync();
            for (var i = 0; i < 4; i++) { await worker.PollAsync(); await worker.RunAsync(); }
            worker.Post(new("send-after-recall", id, "send")); await worker.RunAsync();
            var probe = store.Messages.Values.Single(m => m.Message.Kind == "probe");
            Need(probe.Seen == worker.Epoch, "cancelled promoted message kept the worker behind");
            await worker.DisposeAsync();
        });
        foreach (var promote in new[] { false, true })
            await Check($"completed-job ties retain live order {(promote ? "with" : "without")} promotion", async () =>
            {
                long clock = 100; var store = new InMemoryStore(1, () => clock); var model = new TieJobs(); var order = new List<string>();
                var worker = new Worker("tie-owner", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromSeconds(1), OnTurn = (_, messages, _) => order.AddRange(messages.Where(m => m.Kind == Kinds.JobFinished).Select(m => m.Read<JobFinished>().Key)) });
                await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); var id = InstanceRef.In(0, "ties");
                worker.Post(new("start-ties", id, Kinds.Start, Start: new("tie-jobs", "ties", Ephemeral: true)), fresh: true); await worker.RunAsync();
                var a = worker.JobCompletion(id.Id, "A") ?? throw new InvalidOperationException("A did not start"); var b = worker.JobCompletion(id.Id, "B") ?? throw new InvalidOperationException("B did not start");
                model.A.TrySetResult(); await a.WaitAsync(TimeSpan.FromSeconds(5)); model.B.TrySetResult(); await b.WaitAsync(TimeSpan.FromSeconds(5));
                if (promote) { clock = 1100; await worker.FlushAsync(); }
                for (var i = 0; i < 5; i++) await worker.RunAsync();
                Need(order.SequenceEqual(["B", "A"]), "live completion order is " + string.Join(",", order));
                Need(model.Attempts == 2, $"unexpected job re-execution: {model.Attempts} attempts");
                await worker.DisposeAsync();
            });
        foreach (var (laterCompletion, caughtUp, batch) in new[] { (false, true, 500), (true, true, 500), (false, false, 500), (true, false, 500), (true, false, 1) })
            await Check($"busy checkpoint preserves taken result order{(laterCompletion ? " with a later result" : "")}{(caughtUp ? "" : " while catching up")}, batch {batch}", async () =>
            {
                long clock = 100; var store = new InMemoryStore(1, () => clock); var model = new BusyTieJobs();
                var worker = new Worker("busy-ties", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromSeconds(1), MaxBatch = batch, PollLimit = batch });
                await new DurableHost([worker]).ActivateAsync(0); if (caughtUp) await worker.PollAsync(); var id = InstanceRef.In(0, "busy-ties");
                worker.Post(new("start-busy-ties", id, Kinds.Start, Start: new("busy-tie-jobs", "busy", Ephemeral: true)), fresh: true); await worker.RunAsync();
                var a = worker.JobCompletion(id.Id, "A") ?? throw new InvalidOperationException("A did not start"); var b = worker.JobCompletion(id.Id, "B") ?? throw new InvalidOperationException("B did not start");
                var c = worker.JobCompletion(id.Id, "C") ?? throw new InvalidOperationException("C did not start");
                model.A.TrySetResult(); await a.WaitAsync(TimeSpan.FromSeconds(5)); model.B.TrySetResult(); await b.WaitAsync(TimeSpan.FromSeconds(5));
                worker.RunTurn(id.Id); await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (laterCompletion) { model.C.TrySetResult(); await c.WaitAsync(TimeSpan.FromSeconds(5)); }
                clock = 1100; await worker.FlushAsync();
                if (!caughtUp) for (var i = 0; i < 8; i++) { await worker.PollAsync(); await worker.FlushAsync(); }
                worker.RunTurn(id.Id); await model.Replayed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Need(model.Batches[0].SequenceEqual(["B", "A"]) && model.Batches[1].SequenceEqual(laterCompletion ? ["C", "B", "A"] : ["B", "A"]), "live batches are " + string.Join(" / ", model.Batches.Select(batch => string.Join(",", batch))));
                Need(model.Attempts == 3, $"unexpected job re-execution: {model.Attempts} attempts");
                model.Resume.TrySetResult(); model.C.TrySetResult(); await worker.DisposeAsync();
            });
        foreach (var refusal in new[] { false, true })
        await Check($"unwritten Park after {(refusal ? "refusal" : "throw")} does not restart a cancelled job", async () =>
        {
            long clock = 100; var store = new InMemoryStore(1, () => clock); var model = new ParkJob { RefuseWrite = refusal };
            if (refusal) store.Refuses = row => row.Id.EndsWith("/park-job", StringComparison.Ordinal) && row.Status == InstanceStatus.Running;
            var worker = new Worker("park-owner", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => clock, ShortWait = TimeSpan.FromSeconds(1) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); var id = InstanceRef.In(0, "park-job");
            worker.Post(new("start-park-job", id, Kinds.Start, Start: new("park-job", "park", Ephemeral: true, History: History.Full)), fresh: true); await worker.RunAsync();
            await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); var oldJob = worker.JobCompletion(id.Id, "body") ?? throw new InvalidOperationException("job not started");
            for (var i = 0; i < 5; i++) await worker.RunAsync();
            Need(store.Instances.TryGetValue(id.Id, out var row) && row.Status == InstanceStatus.Faulted && (refusal ? row.State == "null" : row.State.Contains("sealed")) && row.Jobs is null && store.Journal.Values.Any(j => j.Instance == id.Id && j.Entries.Contains("parked") && j.Entries.Contains("sealed-entry")), "park did not retain its accepted state/history or left a live job in the row");
            Need(!store.Messages.Values.Any(m => m.Message.Kind == Kinds.Job), "park persisted a runnable job ticket");
            Need(store.Messages.Values.Any(m => m.Message.Kind == "timer"), "park lost the failed turn's recalled timer");
            await worker.DisposeAsync();
            var next = new Worker("park-next", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => clock });
            await new DurableHost([next]).ActivateAsync(0); clock = 200;
            for (var i = 0; i < 6; i++) { await next.PollAsync(); await next.RunAsync(); }
            model.Resume.TrySetResult(); await oldJob.WaitAsync(TimeSpan.FromSeconds(5));
            for (var i = 0; i < 6; i++) { await next.PollAsync(); await next.RunAsync(); }
            Need(model.Attempts == 1 && store.Instances[id.Id].Status == InstanceStatus.Faulted && !store.Messages.Values.Any(m => m.Message.Kind == "timer" || m.Message.Kind == Kinds.JobFinished), "takeover restarted work, left a reservation, or accepted a late result");
            await next.DisposeAsync();
        });
    }
}
