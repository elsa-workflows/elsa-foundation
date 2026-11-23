namespace Kernel.Bpmn.Run;

internal static class RepeatedJoinChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var repeated in new[] { false, true })
        foreach (var creation in new[] { 0, 1 })
        {
            var draft = new ProcessDraft("repeated-join");
            draft.B.StartEvent("start").ParallelGateway("fork").ExclusiveGateway("a1").ExclusiveGateway("b1")
                .ExclusiveGateway("left").ExclusiveGateway("right").ParallelGateway("join").EndEvent("end");
            draft.Chain("start", "fork", "a1", "left", "join", "end").Chain("fork", "b1", "right", "join");
            if (repeated)
            {
                draft.B.ExclusiveGateway("a2").ExclusiveGateway("b2");
                draft.Chain("fork", "a2", "left").Chain("fork", "b2", "right");
            }
            var registry = new ProcessRegistry().Add(new BpmnProcess(draft.Build(), new Dictionary<string, Work>()));
            var store = new InMemoryStore(1, () => 100);
            await using var worker = new Worker("joins", 0, store, new WorkflowInstance([new BpmnModel(registry)]),
                new() { Monotonic = () => 100 });
            await new DurableHost([worker]).ActivateAsync(0);
            var actor = InstanceRef.In(0, "joins");
            var spec = BpmnModel.Spec("repeated-join");
            if (creation > 0) spec = spec with { Creation = new(creation, 0, "joins") };
            await store.AddAsync(new("start", actor, Kinds.Start, Start: spec, TargetCreation: creation > 0 ? creation : null));
            for (var pass = 0; pass < 8; pass++) { await worker.PollAsync(); await worker.RunAsync(); }
            var row = store.Instances[actor.Id];
            var entries = store.Journal.Values.Where(j => j.Instance == actor.Id)
                .SelectMany(j => Json.Read<List<string>>(j.Entries)).ToArray();
            var joins = entries.Where(e => e.StartsWith("j join ", StringComparison.Ordinal)).Select(e => e.Split(' ')).ToArray();
            var arrivals = joins.Select(e => e.Length == 4 ? e[3].Split(',') : []).ToArray();
            var expected = repeated ? 2 : 1;
            var scope = $"repeated={repeated}, g{creation}";
            check(row is { Status: InstanceStatus.Finished, Revision: 1 } && store.Messages.Count == 0,
                $"the immediate graph completes in one committed evaluation, {scope}");
            check(joins.Length == expected && joins.All(e => e.Length == 4) &&
                joins.Select(e => e[2]).Distinct(StringComparer.Ordinal).Count() == expected &&
                arrivals.All(a => a.Length == 2 && a.Select(v => v[(v.LastIndexOf('/') + 1)..]).Distinct(StringComparer.Ordinal).Count() == 2) &&
                arrivals.SelectMany(a => a).Distinct(StringComparer.Ordinal).Count() == expected * 2,
                $"each join firing records its own two arrivals exactly once, {scope}: {string.Join("; ", entries)}");
        }
    }
}
