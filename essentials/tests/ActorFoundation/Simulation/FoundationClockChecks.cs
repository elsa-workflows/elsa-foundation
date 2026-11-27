namespace Kernel.Sim;

/// <summary>FC-C1 and FC-4 clock witnesses on the built-in memory store.</summary>
internal static class FoundationClockChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long epochTime = 1_700_000_000_000;
        const long due = epochTime + 100;
        var time = epochTime;
        long ticks = 10;
        var store = new InMemoryStore(1, () => time);
        var wire = new DelayedCommit(store);
        var subject = InstanceRef.In(0, "foundation-clock");
        var model = new ClockModel();
        Worker MakeWorker(string node, long offset) => new(node, 0, wire, new WorkflowInstance([model]),
            new WorkerOptions { Monotonic = () => ticks + offset });

        var first = MakeWorker("first-clock", 0);
        var firstActivation = (await new DurableHost([first]).ActivateAsync(0))!;
        await store.AddAsync(new Message("arm-clock", subject, "arm", Start: ClockModel.Spec));
        await PumpUntil(firstActivation, () => model.Entered.Task.IsCompleted);
        check(model.Entered.Task.IsCompletedSuccessfully, "the clock witness enters a live turn");
        time += 20;
        ticks += 20;
        model.Proceed.SetResult();
        await PumpUntil(firstActivation, () => store.Instances.ContainsKey(subject.Id));
        ClockState? State() => store.Instances.TryGetValue(subject.Id, out var row) ? Json.Read<ClockState>(row.State) : null;
        check(State() is { Before: epochTime, After: epochTime }, "a turn keeps its admission time across an await");
        check(store.Messages.Values.Any(row => row.Message is { Kind: "firing", Due: due } message && message.Target == subject),
            "the timer stores the due time derived from the captured clock");
        await firstActivation.DisposeAsync();

        var successor = MakeWorker("next-clock", 19_000);
        var activation = (await new DurableHost([successor]).ActivateAsync(0))!;
        check(first.Now == time && successor.Now == time, "two workers with different monotonic origins read one foundation clock");

        // The store reads the timer just after its due time. Before that poll answer reaches the owner,
        // the shared source steps back. An elapsed-time estimate would deliver the prefetched row early.
        time = due + 20;
        ticks += 100;
        var steppedBack = false;
        wire.BeforePollReply = reply =>
        {
            if (!reply.Messages.Any(row => row.Message is { Kind: "firing", Due: due })) return;
            steppedBack = true;
            time = due - 1;
            ticks++;
            wire.BeforePollReply = null;
        };

        await PumpUntil(activation, () => false);
        check(steppedBack && State() is { FiredAt: null },
            "the old poll reading cannot admit a timer after the foundation clock steps back");
        time = due;
        ticks++;
        await PumpUntil(activation, () => State()?.FiredAt is not null);
        check(State()?.FiredAt == due, "the replacement owner's due turn captures the exact shared-clock boundary");
        await activation.DisposeAsync();
    }

    private static async Task PumpUntil(IScopeActivation activation, Func<bool> finished)
    {
        for (var step = 0; step < 40 && !finished(); step++)
        {
            await activation.WokenAsync(null);
            await activation.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class ClockModel : IExecutionModel
    {
        public static readonly StartSpec Spec = new("foundation-clock", "probe");
        public TaskCompletionSource Entered { get; } = new();
        public TaskCompletionSource Proceed { get; } = new();
        public string Key => Spec.Model;
        public Type StateType => typeof(ClockState);
        public int StateVersion => 1;
        public object CreateState(StartSpec start) => new ClockState();
        public object UpgradeState(string state, int fromVersion) => Json.Read<ClockState>(state);
        public ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken) => new((object?)null);

        public async ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken)
        {
            var state = (ClockState)turn.State;
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == "arm")
                {
                    state.Before = turn.Now;
                    Entered.SetResult();
                    await Proceed.Task.WaitAsync(cancellationToken);
                    state.After = turn.Now;
                    turn.Send(turn.Instance.Ref, "firing", options: new SendOptions(DueAt: state.After + 100));
                }
                else if (message.Kind == "firing")
                    state.FiredAt = turn.Now;
            }
        }
    }

    public sealed class ClockState
    {
        public long Before { get; set; }
        public long After { get; set; }
        public long? FiredAt { get; set; }
    }
}
