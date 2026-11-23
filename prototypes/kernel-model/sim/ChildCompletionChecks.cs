namespace Kernel.Sim;

internal static class ChildCompletionChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var generation in new[] { 0, 1 })
        foreach (var terminal in generation == 0 ? new[] { false } : new[] { false, true })
        foreach (var history in new[] { History.Full, History.State, History.Minimal })
        {
            var model = new Family();
            var store = new InMemoryStore(1, () => 100);
            await using var worker = new Worker("owner", 0, store, new WorkflowInstance([model]), new() { Monotonic = () => 100 });
            await new DurableHost([worker]).ActivateAsync(0);
            var parent = InstanceRef.In(0, "parent#notice");
            long? addressed = generation == 0 ? null : generation;
            var spec = new StartSpec(model.Key, "parent", History: history, Creation: generation == 0 ? null : new(1, 0, "family"));
            await store.AddAsync(new("create", parent, Kinds.Start, Start: spec, TargetCreation: addressed));
            await Pump(worker);
            var opened = store.Instances[parent.Id];
            var children = Json.Read<List<string>>(opened.State);
            var scope = $"g{generation}, terminal={terminal}, {history}";
            check(opened is { Status: InstanceStatus.Running, Revision: 1 } && children.Count == 2 &&
                children.Distinct().Count() == 2 && children.All(id => store.Instances[id] is { Status: InstanceStatus.Running } child &&
                    child.Parent == parent.Id && child.Creation == generation && child.ParentCreation == generation),
                $"the parent started two independently registered children, {scope}");
            if (!terminal && history == History.Minimal)
                await CheckSignedRevision(store, parent, children[0], generation, check);
            if (terminal)
            {
                await store.AddAsync(new("finish-parent", parent, "finish", TargetCreation: addressed)); await Pump(worker);
            }
            for (var index = 0; index < children.Count; index++)
            {
                var child = new InstanceRef(children[index]);
                var before = store.Instances[parent.Id];
                var turnsBefore = model.ParentTurns;
                await store.AddAsync(new("finish-" + index, child, "finish", TargetCreation: addressed));
                await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
                worker.RunTurn(child.Id); await worker.FlushAsync();
                var ended = store.Instances[child.Id];
                var notice = store.Messages.Values.Select(m => m.Message).Single(m =>
                    m.Kind == WorkflowKinds.ChildFinished && m.Sender == child.Id);
                var expectedId = FormattableString.Invariant($"{CreationProtocol.Origin(child.Id, generation)}#{ended.Revision}.end");
                check(ended.Status == InstanceStatus.Finished && notice.InboxKey == InboxKey.Issued(expectedId) &&
                    notice.Target == parent && notice.TargetCreation == addressed && notice.SenderCreation == generation &&
                    notice.Read<ChildFinished>() == new ChildFinished(child.Id, InstanceStatus.Finished, Json.Write("done"), generation),
                    $"child {index} issued its own terminal revision, result and exact parent address, {scope}");
                await Pump(worker);
                var after = store.Instances[parent.Id];
                var remaining = children.Skip(index + 1).ToArray();
                var registry = remaining.Length == 0 ? null : generation == 0 ? Json.Write(remaining)
                    : Json.Write(remaining.Select(id => new CreationAddress(id, generation)));
                if (terminal)
                {
                    var entry = store.Journal.Values.SingleOrDefault(j => j.Instance == parent.Id && j.Revision == before.Revision + 1);
                    var journal = history switch { History.Full => Json.Write(new[] { WorkflowKinds.ChildFinished }), History.State => "[]", _ => null };
                    check(before.Status == InstanceStatus.Finished && after == (before with { Children = registry, Revision = before.Revision + 1 }) &&
                        entry?.Entries == journal && model.ParentTurns == turnsBefore && !store.Messages.ContainsKey(notice.InboxKey),
                        $"child {index} closes only its own terminal-parent registry entry without running the model, {scope}; revision {before.Revision} to {after.Revision}, children {after.Children ?? "none"}, model turns {turnsBefore} to {model.ParentTurns}");
                }
                else
                    check(after.Children == registry && after.Revision == before.Revision + 1 && model.ParentTurns == turnsBefore + 1 &&
                        model.Received.SequenceEqual(children.Take(index + 1)) && !store.Messages.ContainsKey(notice.InboxKey),
                        $"the running parent applied exactly child {index}'s genuine result, {scope}");
            }
        }
    }

    private static async Task CheckSignedRevision(InMemoryStore original, InstanceRef parent, string child, int generation, Action<bool, string> check)
    {
        var store = new InMemoryStore(1, () => 100);
        foreach (var (id, row) in original.Instances) store.Instances[id] = id == child ? row with { Revision = -2 } : row;
        var model = new Family();
        await using var worker = new Worker("recovery", 0, store, new WorkflowInstance([model]), new() { Monotonic = () => 100 });
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var local = (System.Globalization.CultureInfo)previousCulture.Clone();
            local.NumberFormat.NegativeSign = "minus";
            System.Globalization.CultureInfo.CurrentCulture = local;
            await new DurableHost([worker]).ActivateAsync(0);
            await store.AddAsync(new("finish-import", new(child), "finish", TargetCreation: generation == 0 ? null : generation));
            await worker.PollAsync(); await worker.LoadAsync(); await worker.FlushAsync();
            worker.RunTurn(child); await worker.FlushAsync();
            var ended = store.Instances[child];
            var notice = store.Messages.Values.Select(m => m.Message).Single(m => m.Kind == WorkflowKinds.ChildFinished);
            var origin = CreationProtocol.Origin(child, generation);
            var expected = generation == 0 ? origin + "#minus1.end" : origin + "#-1.end";
            await Pump(worker);
            check(ended is { Revision: -1, Status: InstanceStatus.Finished } && notice.InboxKey == InboxKey.Issued(expected) &&
                model.Received.SequenceEqual([child]) && store.Instances[parent.Id].Revision == original.Instances[parent.Id].Revision + 1,
                $"the genuine signed terminal notice from an imported checkpoint keeps its generation's wire format, g{generation}, issued {notice.Id}, expected {expected}");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previousCulture; }
    }

    private static async Task Pump(Worker worker)
    {
        for (var pass = 0; pass < 8; pass++) { await worker.PollAsync(); await worker.RunAsync(); }
    }

    private sealed class Family : Model<List<string>>
    {
        public override string Key => "notice-family";
        public int ParentTurns { get; private set; }
        public List<string> Received { get; } = [];

        protected override void Turn(ITurn turn, List<string> state)
        {
            var parent = turn.Instance.Definition == "parent";
            if (parent) ParentTurns++;
            foreach (var message in turn.Inbox)
                if (parent && message.Kind == Kinds.Start)
                {
                    state.Add(turn.Start(new(Key, "child")).Id);
                    state.Add(turn.Start(new(Key, "child")).Id);
                }
                else if (message.Kind == "finish") turn.SetStatus(InstanceStatus.Finished, output: "done");
                else if (parent && message.Kind == WorkflowKinds.ChildFinished) Received.Add(message.Read<ChildFinished>().Child);
        }
    }
}
