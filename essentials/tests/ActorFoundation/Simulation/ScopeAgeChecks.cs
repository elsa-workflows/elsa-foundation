namespace Kernel.Sim;

internal static class ScopeAgeChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long start = 1_701_700_000_000;
        var now = start;
        var store = new InMemoryStore(1, () => now);
        var turns = new List<InstanceRef>();
        var options = new WorkerOptions
        {
            PassTurnBudget = 1,
            Monotonic = () => now - start,
            OnTurn = (actor, _, _) => turns.Add(actor)
        };
        var actorType = new WorkflowInstance([new Probe()]);
        var refs = Enumerable.Range(0, 4).Select(i => InstanceRef.In(0, $"scope-age-{i}")).ToArray();
        await using var worker = new Worker("scope-age", 0, store, actorType, options);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        for (var i = 0; i < refs.Length; i++)
            await store.AddAsync(new Message($"birth-{i}", refs[i], "start", Start: Probe.Spec)
                with { Due = i == 3 ? start + 800 : null });

        await owner.WokenAsync(null);
        var first = await owner.RunWithAgeAsync();
        check(first.Delay == TimeSpan.Zero && turns.Count <= 1, "a ready-age pass still uses the configured turn budget");

        now = start + 120;
        await owner.WokenAsync(null);
        var next = await owner.RunWithAgeAsync();
        check(next.Delay == TimeSpan.Zero && next.OldestWaitingAge == TimeSpan.FromMilliseconds(120) && turns.Count > 0,
            "the Worker reports the age of already observed ready work across passes");

        ScopePass idle = default;
        for (var i = 0; i < 8; i++)
        {
            await owner.WokenAsync(null);
            idle = await owner.RunWithAgeAsync();
        }
        check(turns.SequenceEqual(refs.Take(3)) && idle.Delay > TimeSpan.Zero && idle.OldestWaitingAge is null,
            "a future timer supplies a delay and no ready-age hint");

        await SharedWorker(check);
    }

    private static async Task SharedWorker(Action<bool, string> check)
    {
        const long start = 1_701_700_000_000;
        var now = start;
        var store = new InMemoryStore(2, () => now);
        var host = DurableHost.ForNode("shared-age", store, new WorkflowInstance([new Probe()]), 2,
            new WorkerOptions { Partitions = 2, PassTurnBudget = 1, Monotonic = () => now - start });
        await using var left = (await host.ActivateAsync(0))!;
        await using var right = (await host.ActivateAsync(1))!;
        for (var i = 0; i < 3; i++)
            await store.AddAsync(new Message($"left-{i}", InstanceRef.In(0, $"left-{i}"), "start", Start: Probe.Spec));
        await store.AddAsync(new Message("right-timer", InstanceRef.In(1, "right"), "start", Start: Probe.Spec) with { Due = start + 60 });
        await left.WokenAsync(null);
        await left.RunWithAgeAsync();
        await right.RunWithAgeAsync();

        now = start + 120;
        var prior = host.PassCounts;
        await left.WokenAsync(null);
        var first = await left.RunWithAgeAsync();
        var second = await right.RunWithAgeAsync();
        var final = host.PassCounts;
        check(final.Calls - prior.Calls == 2 && final.Passes - prior.Passes == 1 &&
              first.OldestWaitingAge == TimeSpan.FromMilliseconds(120) &&
              second.OldestWaitingAge == TimeSpan.FromMilliseconds(60),
            $"the node reuses one budgeted Worker pass and reports each virtual scope's own ready age ({first}, {second}; calls {final.Calls - prior.Calls}, passes {final.Passes - prior.Passes})");
    }

    private sealed class Probe : Model<Dictionary<string, string>>
    {
        internal static readonly StartSpec Spec = new("scope-age", "probe");
        public override string Key => Spec.Model;
        protected override void Turn(ITurn turn, Dictionary<string, string> state) => state["ran"] = "yes";
    }
}
