namespace Kernel.Sim;

/// <summary>Exercise the absolute timer send through the real workflow turn, commit and replacement owner.</summary>
internal static class AbsoluteTimerChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long start = 1_700_000_000_000;
        const long due = start + 100;
        var now = start;
        var store = new InMemoryStore(1, () => now);
        var subject = InstanceRef.In(0, "absolute-timer");
        var recalled = InstanceRef.In(0, "recalled-timer");
        var invalid = InstanceRef.In(0, "mixed-timer");

        DurableHost Host(string node) => new([
            new Worker(node, 0, store, new WorkflowInstance([new TimerModel()]), new WorkerOptions { Monotonic = () => now - start })
        ]);

        await store.AddAsync(new Message("arm-fired", subject, "arm", Json.Write(due), Start: TimerModel.Spec));
        await store.AddAsync(new Message("arm-recalled", recalled, "arm", Json.Write(due), Start: TimerModel.Spec));
        await store.AddAsync(new Message("mixed", invalid, "mixed", Json.Write(due), Start: TimerModel.Spec));
        var old = (await Host("owner").ActivateAsync(0))!;
        await Pump(old);
        check(new[] { subject, recalled }.All(target => store.Messages.Values.Any(
                row => row.Message is { Kind: "firing", Due: due } timer && timer.Target == target)),
            "both absolute timers commit the exact supplied due time");
        check(store.Instances.GetValueOrDefault(invalid.Id)?.Status == InstanceStatus.Faulted &&
            !store.Messages.Values.Any(row => row.Message.Kind == "firing" && row.Message.Target == invalid),
            "a timer with both delay and due time faults without inserting a timer");

        await store.AddAsync(new Message("recall", recalled, "recall"));
        await Pump(old);
        check(!store.Messages.Values.Any(row => row.Message.Kind == "firing" && row.Message.Target == recalled),
            "recall removes the absolute timer before it is delivered");
        await old.DisposeAsync();

        now = due - 1;
        var successor = (await Host("successor").ActivateAsync(0))!;
        await Pump(successor);
        int Fired(InstanceRef reference) =>
            store.Instances.TryGetValue(reference.Id, out var row) ? Json.Read<TimerState>(row.State).Fired : -1;
        check(Fired(subject) == 0, "a replacement owner does not apply an absolute timer before its due time");
        now = due;
        await Pump(successor);
        check(Fired(subject) == 1 && Fired(recalled) == 0,
            $"the replacement owner applies the due timer once and never applies the recalled timer (fired {Fired(subject)}, recalled {Fired(recalled)})");
        await successor.DisposeAsync();
    }

    private static async Task Pump(IScopeActivation activation)
    {
        for (var step = 0; step < 20; step++)
        {
            await activation.WokenAsync(null);
            await activation.RunAsync();
            await Task.Yield();
        }
    }

    public sealed class TimerState
    {
        public string? Timer { get; set; }
        public int Fired { get; set; }
    }

    private sealed class TimerModel : Model<TimerState>
    {
        public static readonly StartSpec Spec = new("absolute-timer", "probe");
        public override string Key => Spec.Model;

        protected override void Turn(ITurn turn, TimerState state)
        {
            foreach (var message in turn.Inbox)
                switch (message.Kind)
                {
                    case "arm":
                        state.Timer = turn.Send(turn.Instance.Ref, "firing", options: new SendOptions(DueAt: message.Read<long>()));
                        break;
                    case "mixed":
                        turn.Send(turn.Instance.Ref, "firing", options: new SendOptions(Delay: TimeSpan.Zero, DueAt: message.Read<long>()));
                        break;
                    case "recall" when state.Timer is not null:
                        turn.Recall(state.Timer);
                        state.Timer = null;
                        break;
                    case "firing":
                        state.Fired++;
                        break;
                }
        }
    }
}
