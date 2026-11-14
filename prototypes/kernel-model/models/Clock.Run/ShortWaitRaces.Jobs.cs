using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Workflows;

internal static partial class ShortWaitRaces
{
    private sealed class Spawner(bool finish, bool wait, bool finishOnTimer = false) : Model<List<string>>
    {
        public override string Key => "boundary-spawner";
        public int Starts;
        public readonly TaskCompletionSource<object?> Result = new();

        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken)
        {
            Starts++;
            return wait ? new(Result.Task.WaitAsync(cancellationToken)) : new((object?)42);
        }

        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
            {
                state.Add(message.Kind);
                if (message.Kind == "spawn") turn.StartJob("job");
                if (finish && message.Kind == Kinds.JobFinished || finishOnTimer && message.Kind == "later")
                    turn.SetStatus(InstanceStatus.Finished);
            }
        }
    }

    private sealed class JobCut
    {
        public long Clock = 100;
        public readonly InstanceRef Actor = InstanceRef.In(0, "job-boundary");
        public readonly InMemoryStore Store;
        public readonly DelayedCommit Wire;
        private readonly Spawner _model;

        public JobCut(Spawner model)
        {
            _model = model;
            Store = new(1, () => Clock);
            Wire = new(Store);
            Store.Instances[Actor.Id] = new(Actor.Id, model.Key, "trial", InstanceStatus.Running, "[]", 1, null);
        }

        public async Task Add(string kind, long? due = null) => await Store.AddAsync(new(kind, Actor, kind, Due: due));

        public async Task<Worker> Activate(string name, int group = 1, int attempts = 3, int lease = 5000)
        {
            var owner = new Worker(name, 0, Wire, new WorkflowInstance([_model]), new()
            {
                Monotonic = () => Clock, Horizon = TimeSpan.FromMilliseconds(100), TrialGroup = group,
                MaxAttempts = attempts, Lease = TimeSpan.FromMilliseconds(lease)
            });
            await new DurableHost([owner]).ActivateAsync(0);
            await owner.PollAsync();
            await owner.LoadAsync();
            return owner;
        }

        public async Task MarkPrevious(params string[] inputs)
        {
            foreach (var input in inputs) await Add(input);
            await using var previous = await Activate("former");
            await previous.FlushAsync();
            foreach (var input in inputs)
                Need(Store.Messages[input].Seen == previous.Epoch && Store.Messages[input].Trial == 0, "missing former mark for " + input);
        }

        public void ExpectTrial(Worker owner, string member)
        {
            Need(owner.Trial.SequenceEqual([member]) && Store.Messages[member].Trial == owner.Epoch
                && Store.Messages.Values.All(r => r.Attempts == 0), "incorrect fenced trial: " + string.Join(",", owner.Trial));
        }

        public async Task Drain(Worker owner)
        {
            for (var i = 0; i < 6; i++)
            {
                await owner.PollAsync();
                await owner.RunAsync();
            }
        }

        public void Expect(Worker owner, InstanceStatus status, params string[] inputs)
        {
            var row = Store.Instances[Actor.Id];
            Need(row.Status == status && Json.Read<List<string>>(row.State).SequenceEqual(inputs)
                && Store.Messages.Count == 0 && !owner.Trial.Any(),
                $"expected {status} [{string.Join(",", inputs)}], observed {row.Status} {row.State}, remaining={Store.Messages.Count}");
        }
    }

    private static async Task JobCuts(Func<string, Func<Task>, Task> Check)
    {
        foreach (var finish in new[] { false, true })
        foreach (var wake in new[] { 110, 160 })
        foreach (var post in new[] { false, true })
        foreach (var group in new[] { 1, 4 })
            await Check($"spawned result and termination, finish={finish}, wake={wake}, post={post}, group={group}", async () =>
            {
                var model = new Spawner(finish, wait: false);
                var f = new JobCut(model);
                await f.MarkPrevious("spawn");
                if (!post) await f.Add(Kinds.Terminate, 150);
                await using var owner = await f.Activate("next", group);
                if (post) Need(owner.Post(new("stop", f.Actor, Kinds.Terminate, Due: 150)), "post refused");
                await owner.FlushAsync();
                f.ExpectTrial(owner, "spawn");
                f.Clock = wake;
                await f.Drain(owner);
                f.Clock = 200;
                await f.Drain(owner);
                Need(model.Starts == 1, "job did not start exactly once");
                f.Expect(owner, finish && wake < 150 ? InstanceStatus.Finished : InstanceStatus.Cancelled,
                    wake < 150 ? ["spawn", Kinds.JobFinished] : ["spawn"]);
            });

        foreach (var post in new[] { false, true })
        foreach (var observeCommandFirst in new[] { false, true })
        foreach (var complete in new[] { 140, 150, 160 })
            await Check($"pending ordinary ticket becomes ready at {complete}, command first={observeCommandFirst}, post={post}", async () =>
            {
                var model = new Spawner(finish: false, wait: true);
                var f = new JobCut(model);
                await f.MarkPrevious("spawn", "second");
                await using var owner = await f.Activate("next");
                async Task Command()
                {
                    if (post) Need(owner.Post(new("stop", f.Actor, Kinds.Terminate, Due: 150)), "post refused");
                    else { await f.Add(Kinds.Terminate, 150); await owner.PollAsync(); }
                }
                if (observeCommandFirst) await Command();
                await owner.FlushAsync();
                f.ExpectTrial(owner, "spawn");
                f.Clock = 110;
                await owner.RunAsync();
                var completion = owner.JobCompletion(f.Actor.Id, "job") ?? throw new InvalidOperationException("job did not start");
                await owner.FlushAsync();
                f.ExpectTrial(owner, "second");
                f.Clock = complete;
                model.Result.SetResult(42);
                await completion.WaitAsync(TimeSpan.FromSeconds(5));
                if (!observeCommandFirst) await Command();
                f.Clock = 160;
                await f.Drain(owner);
                bool readyFirst = complete < 150 || complete == 150 && !observeCommandFirst;
                f.Expect(owner, InstanceStatus.Cancelled, readyFirst ? ["spawn", Kinds.JobFinished, "second"] : ["spawn", "second"]);
                Need(model.Starts == 1, "unexpected driver replay");
            });

        foreach (var post in new[] { false, true })
            await Check($"selected ticket completes through a mature termination, post={post}", async () =>
            {
                var model = new Spawner(finish: true, wait: true);
                var f = new JobCut(model);
                await f.Add("spawn");
                var ticket = Worker.TicketId(f.Actor.Id, "job");
                await using (var previous = await f.Activate("former"))
                {
                    await previous.FlushAsync();
                    await previous.RunAsync();
                    Need(model.Starts == 1 && f.Store.Messages[ticket].Seen == previous.Epoch, "former owner did not start job");
                }
                if (!post) await f.Add(Kinds.Terminate, 150);
                await using var owner = await f.Activate("next");
                if (post) Need(owner.Post(new("stop", f.Actor, Kinds.Terminate, Due: 150)), "post refused");
                await owner.FlushAsync();
                f.ExpectTrial(owner, ticket);
                var completion = owner.JobCompletion(f.Actor.Id, "job") ?? throw new InvalidOperationException("selected job did not start");
                f.Clock = 160;
                model.Result.SetResult(42);
                await completion.WaitAsync(TimeSpan.FromSeconds(5));
                await f.Drain(owner);
                f.Expect(owner, InstanceStatus.Finished, "spawn", Kinds.JobFinished);
                Need(model.Starts == 2, "expected former and replacement driver starts");
            });

        // Retain the original nonterminal sibling controls and exercise a terminal result
        // handler with and without another inherited input to hold the recovery boundary.
        foreach (var (finish, sibling) in new[] { (false, true), (true, false), (true, true) })
        foreach (var post in new[] { false, true })
        foreach (var due in new[] { 205, 215 })
            await Check($"an exhausted ticket's error becomes ready on poll, due={due}, post={post}, finish={finish}, sibling={sibling}", async () =>
            {
                var model = new Spawner(finish, wait: true);
                var f = new JobCut(model);
                var ticket = Worker.TicketId(f.Actor.Id, "job");
                await f.Add("spawn");
                await using (var former = await f.Activate("former"))
                {
                    await former.FlushAsync();
                    await former.RunAsync();
                    if (sibling)
                    {
                        await f.Add("sibling");
                        await former.PollAsync();
                        await former.FlushAsync();
                    }
                    Need(f.Store.Messages[ticket].Seen == former.Epoch && (!sibling || f.Store.Messages["sibling"].Seen == former.Epoch),
                        "former owner did not mark both inputs");
                }
                await using var lost = await f.Activate("lost", attempts: 1, lease: 100);
                await lost.FlushAsync();
                f.ExpectTrial(lost, ticket);
                if (!post) await f.Add(Kinds.Terminate, due);
                f.Clock = 210;
                await using var owner = await f.Activate("replacement", attempts: 1);
                Need(owner.Epoch > lost.Epoch && f.Store.Messages[ticket].Attempts == 1
                    && (!sibling || f.Store.Messages["sibling"].Attempts == 0), "takeover did not charge the isolated trial");
                if (post) Need(owner.Post(new("stop", f.Actor, Kinds.Terminate, Due: due)), "post refused");
                await owner.FlushAsync();
                bool siblingBefore = sibling && (!post || due > 210);
                Need(siblingBefore ? owner.Trial.SequenceEqual(["sibling"]) : !owner.Trial.Any(), "wrong sibling admission");
                f.Clock = 220;
                await f.Drain(owner);
                var expected = new List<string> { "spawn" };
                if (due > 210) expected.Add(Kinds.JobFinished);
                if (siblingBefore) expected.Add("sibling");
                f.Expect(owner, finish && due > 210 ? InstanceStatus.Finished : InstanceStatus.Cancelled, [.. expected]);
                Need(model.Starts == 2, "exhausted ticket restarted a driver");
            });

        foreach (var post in new[] { false, true })
            await Check($"command matures during the last exhausted result acknowledgement, post={post}", async () =>
            {
                var model = new Spawner(finish: false, wait: true);
                var f = new JobCut(model);
                await f.Add("spawn");
                var ticket = Worker.TicketId(f.Actor.Id, "job");
                await using (var issuer = await f.Activate("issuer"))
                {
                    await issuer.FlushAsync();
                    await issuer.RunAsync();
                }
                await using var lost = await f.Activate("lost", attempts: 1, lease: 100);
                await lost.FlushAsync();
                f.ExpectTrial(lost, ticket);
                await f.Add("later", 220);
                if (!post) await f.Add(Kinds.Terminate, 215);
                f.Clock = 210;
                await using var owner = await f.Activate("replacement", attempts: 1);
                if (post) Need(owner.Post(new("stop", f.Actor, Kinds.Terminate, Due: 215)), "post refused");
                await owner.FlushAsync();
                Need(!owner.Trial.Any() && f.Store.Messages[ticket].Attempts == 1, "expected an exhausted ticket without a trial");
                f.Wire.DelayNext = true;
                var flight = owner.RunAsync().AsTask();
                try
                {
                    await f.Wire.Landed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Need(!flight.IsCompleted && !f.Store.Messages.ContainsKey(ticket), "exhausted result was not committed");
                    f.Clock = 230;
                }
                finally { f.Wire.Reply.TrySetResult(); }
                await flight;
                await f.Drain(owner);
                f.Expect(owner, InstanceStatus.Cancelled, "spawn", Kinds.JobFinished);
                Need(model.Starts == 2, "exhausted ticket restarted a driver");
            });

        foreach (var post in new[] { false, true })
            await Check($"ordinary driver result keeps F7 priority outside recovery, post={post}", async () =>
            {
                var model = new Spawner(finish: true, wait: true);
                var f = new JobCut(model);
                await f.Add("spawn");
                await using var owner = await f.Activate("fresh");
                await owner.FlushAsync();
                await owner.RunAsync();
                var completion = owner.JobCompletion(f.Actor.Id, "job") ?? throw new InvalidOperationException("missing driver");
                if (post) Need(owner.Post(new("stop", f.Actor, Kinds.Terminate, Due: 150)), "post refused");
                else { await f.Add(Kinds.Terminate, 150); await owner.PollAsync(); }
                await owner.FlushAsync();
                f.Clock = 160;
                model.Result.SetResult(42);
                await completion.WaitAsync(TimeSpan.FromSeconds(5));
                await f.Drain(owner);
                f.Expect(owner, InstanceStatus.Finished, "spawn", Kinds.JobFinished);
                Need(model.Starts == 1, "unexpected driver replay");
            });
    }
}
