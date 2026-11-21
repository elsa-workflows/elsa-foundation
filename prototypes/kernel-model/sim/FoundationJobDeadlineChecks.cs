using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class FoundationJobDeadlineChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long initial = 1_701_200_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        var requests = new Requests("timed-jobs");
        Contract.IIngress edge = new InMemoryIngress(store, requests, "jobs", (selected, _) => selected == scope);
        var model = new Probe();
        Action? atCutoff = null;
        Action? afterCutoff = null;

        async Task<Contract.ActorRef> Start(string name) =>
            (await edge.SendAsync(scope, new Contract.Target.Start(new("timed-job", "ready", name)), name, Contract.Kinds.Start)).Actor!.Value;

        async Task<IScopeActivation> Owner(string node)
        {
            var worker = new Worker(node, 0, store, new ActorAdapter(store, model), new WorkerOptions
            {
                Monotonic = () => now - initial,
                BeforeTimedJobCutoff = () => atCutoff?.Invoke(),
                AfterTimedJobCutoff = () => afterCutoff?.Invoke()
            });
            return (await new DurableHost([worker]).ActivateAsync(0))!;
        }

        var fast = await Start("fast");
        var expires = await Start("expires");
        var moves = await Start("moves");
        var cancelled = await Start("cancelled");
        var queued = await Start("queued-result");
        var held = await Start("held-success");
        var ended = await Start("ended");
        var replaces = await Start("replace-after-timeout");
        var poison = await Start("poison");
        var staged = await Start("staged-replacement");
        var suspended = await Start("suspended-success");
        var owner = await Owner("job-first");
        await Pump(owner);
        check(model.Attempts["fast"].Job is { Actor: var actor, Key: "job", Deadline: initial + 100, AttemptId.Length: > 0 } &&
            actor == fast && Results(fast).SequenceEqual(["fast-result"]) && Timers(fast) == 0,
            "a fast public job receives its durable deadline and recalls its one timer when the result lands");
        check(Timers(expires) == 1 && Timers(moves) == 1 && Timers(cancelled) == 1 &&
            model.Attempts["expires"].Job.Deadline == initial + 100 && Results(expires).Count == 0,
            "a pending public job and its absolute deadline commit before its attempt enters");

        await edge.SendAsync(scope, new Contract.Target.Ref(cancelled), "cancel", "cancel-job");
        await Pump(owner);
        check(model.Attempts["cancelled"].Cancelled && Timers(cancelled) == 0,
            "cancelling a job recalls its durable deadline and signals its driver");
        await edge.SendAsync(scope, new Contract.Target.Ref(cancelled), "restart", "restart-job", "restart");
        await Pump(owner);
        model.Attempts["cancelled"].Finish("old");
        model.Attempts["restart"].Finish("new");
        await Pump(owner);
        check(Results(cancelled).SequenceEqual(["new"]) && Timers(cancelled) == 0,
            "the old attempt cannot publish into a restarted job with the same key");

        store.AddFoundationMessage(scope.Tenant, expires.Id, "suspend", Kinds.Suspend);
        await edge.SendAsync(scope, new Contract.Target.Ref(suspended), "suspend", Contract.Kinds.Suspend);
        await Pump(owner);
        now = initial + 99;
        model.Attempts["suspended-success"].Finish("timely while suspended");
        model.Attempts["staged-replacement"].Finish("cancel staged");
        await ((Worker)owner).JobCompletion(Row(suspended).Id, "job", Row(suspended).Creation)!.WaitAsync(TimeSpan.FromSeconds(2));
        await ((Worker)owner).JobCompletion(Row(staged).Id, "job", Row(staged).Creation)!.WaitAsync(TimeSpan.FromSeconds(2));
        await Pump(owner);
        check(!model.Attempts["expires"].Cancelled && Results(expires).Count == 0,
            "the owner leaves its timed job active before the deadline");
        var stagedRow = Row(staged);
        check(Results(staged).SequenceEqual(["cancel staged"]) && stagedRow.Jobs is null && Timers(staged) == 0 &&
            !model.Attempts.ContainsKey("must-not-enter") &&
            !store.Messages.ContainsKey(InboxKey.Ticket(stagedRow.Id, "job", stagedRow.Creation)),
            "cancelling a staged same-key replacement retracts its ticket and timer in the predecessor's result turn");
        await edge.SendAsync(scope, new Contract.Target.Ref(staged), "restart-after-staged", "restart-job", "reused after staged");
        await Pump(owner);
        if (model.Attempts.TryGetValue("reused after staged", out var reused))
        {
            reused.Finish("reused successfully");
            await ((Worker)owner).JobCompletion(stagedRow.Id, "job", stagedRow.Creation)!.WaitAsync(TimeSpan.FromSeconds(2));
            await Pump(owner);
        }
        check(Results(staged).SequenceEqual(["cancel staged", "reused successfully"]) && Timers(staged) == 0,
            "the same owner can start and settle a fresh job after retracting the preceding staged replacement");
        var suspendedRow = Row(suspended);
        check(Results(suspended).Count == 0 && Timers(suspended) == 0 &&
            store.HasFoundationJobResult(suspendedRow.Id, suspendedRow.Creation, "job"),
            "foundation settles a timely result while suspension still withholds it from the actor");
        model.Attempts["held-success"].Finish("held-success");
        await ((Worker)owner).JobCompletion(Row(held).Id, "job", Row(held).Creation)!.WaitAsync(TimeSpan.FromSeconds(2));
        await edge.SendAsync(scope, new Contract.Target.Ref(ended), "end", "end");
        await edge.SendAsync(scope, new Contract.Target.Ref(replaces), "replacement", Contract.Kinds.Migrate,
            options: new Contract.AddOptions(DueAt: initial + 100));
        await Pump(owner);
        check(Results(held).Count == 0 && Timers(held) == 0 && store.HasFoundationJobResult(Row(held).Id, Row(held).Creation, "job"),
            "holding a timely success preserves its durable result and recalls its deadline");
        check(Row(ended).Status == InstanceStatus.Finished && Row(ended).Jobs is not null,
            "a natural end leaves an active timed job to settle under the same actor life");

        var queuedRow = Row(queued);
        var driver = ((Worker)owner).JobCompletion(queuedRow.Id, "job", queuedRow.Creation)!;
        atCutoff = () =>
        {
            atCutoff = null;
            model.Attempts["queued-result"].Finish("in time");
            driver.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            now = initial + 100;
        };
        await Pump(owner);
        check(Results(queued).SequenceEqual(["in time"]) && !model.Attempts["queued-result"].Cancelled && Timers(queued) == 0,
            "a result enqueued before cutoff after the owner drained its queue still wins");
        check(Row(ended).Status == InstanceStatus.Finished && Row(ended).Jobs is null && Timers(ended) == 0 &&
            Results(ended).SequenceEqual(["timeout"]),
            "expiry settles an ended actor's exact job without changing the terminal status");
        model.Attempts["replacement"].Finish("replacement result");
        await Pump(owner);
        check(Results(replaces).SequenceEqual(["restarted", "replacement result"]),
            "a genuine restart cancels the prior durable timeout before the replacement result applies");
        check(model.Attempts["expires"].Cancelled && Results(expires).Count == 0,
            "the deadline cancels a physical driver even while its actor withholds ordinary results");
        model.Attempts["expires"].Finish("too late");
        store.AddFoundationMessage(scope.Tenant, expires.Id, "resume", Kinds.Resume);
        await Pump(owner);
        check(Results(expires).SequenceEqual(["timeout"]) && Timers(expires) == 0,
            "the resumed actor gets one timeout result and the late driver publishes nothing");

        check(Results(moves).Count == 0 && Timers(moves) == 1, "the displaced owner leaves a pending job and deadline in the store");
        await owner.DisposeAsync();
        var exhausted = Row(poison);
        store.Messages[InboxKey.Ticket(exhausted.Id, "job", exhausted.Creation)].Attempts = 3;
        now = initial + 200;
        var entries = model.Entries;
        await using var successor = await Owner("job-successor");
        await edge.SendAsync(scope, new Contract.Target.Ref(held), "release", "release");
        await edge.SendAsync(scope, new Contract.Target.Ref(suspended), "resume", Contract.Kinds.Resume);
        await Pump(successor);
        model.Attempts["moves"].Finish("late from former owner");
        check(Results(moves).SequenceEqual(["timeout"]) && Timers(moves) == 0 && model.Entries == entries,
            "the successor has no extra physical entry for a ticket it finds past its deadline");
        check(Results(held).SequenceEqual(["held-success"]),
            "the successor hands the held timely success back without a substituted timeout");
        check(store.ReadFoundationActor(scope.Tenant, poison.Id).Actor is { Park: not null, Row.Status: InstanceStatus.Faulted } &&
            Results(poison).Count == 0, "an exhausted timed ticket parks across takeover even when its deadline has arrived");
        check(Results(suspended).SequenceEqual(["timely while suspended"]) && Timers(suspended) == 0,
            "a later owner resumes the actor with its one persisted success even after the deadline");

        var boundary = await Start("after-scan");
        await Pump(successor);
        var boundaryRow = Row(boundary);
        var worker = (Worker)successor;
        var callback = worker.JobCompletion(boundaryRow.Id, "job", boundaryRow.Creation)!;
        now += 99;
        afterCutoff = () =>
        {
            afterCutoff = null;
            model.Attempts["after-scan"].Finish("timely after scan");
            callback.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            now++;
        };
        worker.RunTurn(boundaryRow.Id);
        await Pump(successor);
        check(Results(boundary).SequenceEqual(["timely after scan"]) && Timers(boundary) == 0,
            "a deadline crossed after the cutoff scan waits for a new scan that sees the timely callback");

        InstanceRow Row(Contract.ActorRef reference) =>
            store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row;

        List<string> Results(Contract.ActorRef reference) => Json.Read<List<string>>(Row(reference).State);

        int Timers(Contract.ActorRef reference)
        {
            var row = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row;
            return store.Messages.Values.Count(message => !message.Done && message.Message.Target.Id == row.Id &&
                message.Message.Kind == Kinds.JobDeadline);
        }
    }

    private static async Task Pump(IScopeActivation activation)
    {
        for (var step = 0; step < 25; step++)
        {
            await activation.WokenAsync(null);
            await activation.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class Attempt(Contract.IJob job)
    {
        private readonly TaskCompletionSource<object?> _output = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Contract.IJob Job { get; } = job;
        public bool Cancelled { get; private set; }
        public Task<object?> Output => _output.Task;
        public void Cancel() => Cancelled = true;
        public void Finish(string value) => _output.TrySetResult(value);
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "timed-job";
        public Dictionary<string, Attempt> Attempts { get; } = new(StringComparer.Ordinal);
        public int Entries { get; private set; }
        public bool Withholds(Contract.ActorHeader header) => header.TypeData == "suspended";

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var results = turn.State is null ? [] : Json.Read<List<string>>(turn.State);
            foreach (var message in turn.Inbox)
                switch (message.Kind)
                {
                    case Contract.Kinds.Start or "restart-job":
                        turn.StartJob("job", message.Payload, TimeSpan.FromMilliseconds(message.Payload is "moves" or "poison" ? 200 : 100));
                        break;
                    case "end":
                        turn.End(Contract.ActorStatus.Finished);
                        break;
                    case Contract.Kinds.Migrate:
                        turn.CancelJob("job");
                        turn.StartJob("job", "replacement", TimeSpan.FromMilliseconds(100));
                        results.Add("restarted");
                        break;
                    case "cancel-job":
                        turn.CancelJob("job");
                        break;
                    case Contract.Kinds.Suspend or Contract.Kinds.Resume:
                        turn.SetHeader(message.Kind == Contract.Kinds.Suspend ? "suspended" : "ready", turn.Header.Labels);
                        break;
                    case Contract.Kinds.JobFinished:
                        var result = Json.Read<Contract.JobFinished>(message.Payload!);
                        var value = result.Error ?? Json.Read<string>(result.Result!);
                        if (value == "cancel staged")
                        {
                            turn.StartJob("job", "must-not-enter", TimeSpan.FromMilliseconds(100));
                            turn.CancelJob("job");
                        }
                        if (value == "held-success" && !turn.Inbox.Any(input => input.Kind == "release"))
                            turn.Hold(message);
                        else results.Add(value);
                        break;
                }
            turn.State = Json.Write(results);
            return default;
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token)
        {
            Entries++;
            var attempt = new Attempt(job);
            Attempts[job.Arguments!] = attempt;
            if (job.Arguments == "fast") return "fast-result";
            using var registration = token.Register(attempt.Cancel);
            return await attempt.Output;
        }

        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
