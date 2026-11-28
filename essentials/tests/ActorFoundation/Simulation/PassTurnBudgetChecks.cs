namespace Kernel.Sim;

internal static class PassTurnBudgetChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long now = 1_701_700_000_000;
        var store = new InMemoryStore(1, () => now);
        var hot = InstanceRef.In(0, "a-hot");
        var first = InstanceRef.In(0, "z-first");
        var second = InstanceRef.In(0, "b-second");
        var turns = new List<InstanceRef>();
        var type = new WorkflowInstance([new Probe()]);
        var options = new WorkerOptions
        {
            PassTurnBudget = 1,
            Monotonic = () => 0,
            OnTurn = (actor, _, _) => turns.Add(actor)
        };
        var invalid = false;
        try { _ = new Worker("invalid", 0, store, type, options with { PassTurnBudget = 0 }); }
        catch (ArgumentOutOfRangeException error) when (error.ParamName == nameof(WorkerOptions.PassTurnBudget)) { invalid = true; }
        check(invalid, "a pass turn budget must be positive");

        await store.AddAsync(new Message("hot", hot, "repeat", Json.Write(5), Start: Probe.Spec));
        await store.AddAsync(new Message("first", first, "note", Json.Write(10), Start: Probe.Spec));
        await store.AddAsync(new Message("second", second, "note", Json.Write(20), Start: Probe.Spec));
        await store.AddAsync(new Message("second-follow-up", second, "note", Json.Write(30)));

        await using var worker = new Worker("budget", 0, store, type, options);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var attempts = 0;
        var capped = true;
        while (turns.Count < 3 && attempts++ < 8)
        {
            var previous = turns.Count;
            await owner.WokenAsync(null);
            var delay = await owner.RunAsync();
            capped &= turns.Count - previous <= 1 && delay == TimeSpan.Zero;
        }
        check(capped && turns.Count == 3, "passes flush at most one turn and advise another pass while work remains");
        check(turns.SequenceEqual([hot, first, second]) &&
              store.Instances.ContainsKey(hot.Id) && store.Instances.ContainsKey(first.Id) &&
              store.Instances.TryGetValue(second.Id, out var checkpoint) &&
              Json.Read<List<int>>(checkpoint.State).SequenceEqual([20, 30]),
            "a continuously ready actor rotates behind waiting actors, and each actor keeps its input order");

        for (var i = 0; i < 20; i++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
        check(store.Instances.TryGetValue(hot.Id, out var finished) &&
              Json.Read<List<int>>(finished.State).SequenceEqual([5, 4, 3, 2, 1]) &&
              store.Messages.Values.All(message => message.Done),
            "subsequent budgeted passes drain the productive actor without losing its turns");
    }

    private sealed class Probe : Model<List<int>>
    {
        internal static readonly StartSpec Spec = new("pass-budget", "probe");
        public override string Key => Spec.Model;

        protected override void Turn(ITurn turn, List<int> state)
        {
            foreach (var input in turn.Inbox)
            {
                var value = input.Read<int>();
                state.Add(value);
                if (input.Kind == "repeat" && value > 1)
                    turn.Send(turn.Instance.Ref, "repeat", value - 1);
            }
        }
    }
}
