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
        Contract.IIngress edge = new InMemoryIngress(store, new Requests("timed-jobs"), "jobs");
        var model = new Probe();

        async Task<Contract.ActorRef> Start(string name) =>
            (await edge.SendAsync(scope, new Contract.Target.Start(new("timed-job", "ready", name)), name, Contract.Kinds.Start)).Actor!.Value;

        async Task<IScopeActivation> Owner(string node)
        {
            var worker = new Worker(node, 0, store, new ActorAdapter(store, model), new WorkerOptions { Monotonic = () => now - initial });
            return (await new DurableHost([worker]).ActivateAsync(0))!;
        }

        var fast = await Start("fast");
        var expires = await Start("expires");
        var moves = await Start("moves");
        var cancelled = await Start("cancelled");
        var queued = await Start("queued-result");
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
        await Pump(owner);
        now = initial + 99;
        await Pump(owner);
        check(!model.Attempts["expires"].Cancelled && Results(expires).Count == 0,
            "the owner leaves its timed job active before the deadline");
        var queuedRow = store.ReadFoundationActor(scope.Tenant, queued.Id).Actor!.Row;
        var driver = ((Worker)owner).JobCompletion(queuedRow.Id, "job", queuedRow.Creation)!;
        model.Attempts["queued-result"].Finish("in time");
        await driver.WaitAsync(TimeSpan.FromSeconds(2));
        now++;
        await Pump(owner);
        check(Results(queued).SequenceEqual(["in time"]) && !model.Attempts["queued-result"].Cancelled && Timers(queued) == 0,
            "a result published before the deadline wins even when the owner drains it at the deadline");
        check(model.Attempts["expires"].Cancelled && Results(expires).Count == 0,
            "the deadline cancels a physical driver even while its actor withholds ordinary results");
        model.Attempts["expires"].Finish("too late");
        store.AddFoundationMessage(scope.Tenant, expires.Id, "resume", Kinds.Resume);
        await Pump(owner);
        check(Results(expires).SequenceEqual(["timeout"]) && Timers(expires) == 0,
            "the resumed actor gets one timeout result and the late driver publishes nothing");

        check(Results(moves).Count == 0 && Timers(moves) == 1, "the displaced owner leaves a pending job and deadline in the store");
        await owner.DisposeAsync();
        now = initial + 200;
        await using var successor = await Owner("job-successor");
        await Pump(successor);
        model.Attempts["moves"].Finish("late from former owner");
        check(Results(moves).SequenceEqual(["timeout"]) && Timers(moves) == 0 && model.Attempts.Count == 6,
            "the successor has no extra physical entry for a ticket it finds past its deadline");

        List<string> Results(Contract.ActorRef reference) =>
            Json.Read<List<string>>(store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row.State);

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
        public bool Withholds(Contract.ActorHeader header) => header.TypeData == "suspended";

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var results = turn.State is null ? [] : Json.Read<List<string>>(turn.State);
            foreach (var message in turn.Inbox)
                switch (message.Kind)
                {
                    case Contract.Kinds.Start or "restart-job":
                        turn.StartJob("job", message.Payload, TimeSpan.FromMilliseconds(message.Payload == "moves" ? 200 : 100));
                        break;
                    case "cancel-job":
                        turn.CancelJob("job");
                        break;
                    case Contract.Kinds.Suspend or Contract.Kinds.Resume:
                        turn.SetHeader(message.Kind == Contract.Kinds.Suspend ? "suspended" : "ready", turn.Header.Labels);
                        break;
                    case Contract.Kinds.JobFinished:
                        var result = Json.Read<Contract.JobFinished>(message.Payload!);
                        results.Add(result.Error ?? Json.Read<string>(result.Result!));
                        break;
                }
            turn.State = Json.Write(results);
            return default;
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token)
        {
            var attempt = new Attempt(job);
            Attempts.Add(job.Arguments!, attempt);
            if (job.Arguments == "fast") return "fast-result";
            using var registration = token.Register(attempt.Cancel);
            return await attempt.Output;
        }

        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
