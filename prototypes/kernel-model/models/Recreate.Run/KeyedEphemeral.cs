using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Workflows;

internal static class KeyedEphemeral
{
    public static async Task Run(string label, IKernelStore store, IKeyAdmission ingress, Func<long, Task> waitUntil, Func<long>? trips = null)
    {
        foreach (var history in new[] { History.Full, History.State, History.Minimal })
            await RunProfile(label, store, ingress, waitUntil, history, trips);
    }

    private static async Task RunProfile(string label, IKernelStore store, IKeyAdmission ingress, Func<long, Task> waitUntil, History history, Func<long>? trips)
    {
        var key = "ephemeral-" + Guid.NewGuid().ToString("N");
        var request = new KeyRequest(key, "first", Kinds.Start, Start: new("probe", "child", Ephemeral: true, History: history), KeepForMillis: 1);
        var receipt = await ingress.AcceptAsync(request);
        var id = receipt.RunId!;
        Worker Owner(string node) => new(node, 0, store, new WorkflowInstance([new Probe()]));
        async Task Activate(Worker w)
        {
            await new DurableHost([w]).ActivateAsync(0);
            await w.PollAsync();
        }
        var crashed = Owner("key-eph-lost");
        await Activate(crashed);
        await crashed.LoadAsync();
        await crashed.FlushAsync(); // the durable creator is marked, its terminal turn has not run
        Check((await store.LoadAsync([id])).Count == 0, "crash cut must be before the first terminal commit");
        Check((await ingress.AcceptAsync(new(key, "delete", Kinds.Delete))).RunId == id,
            "delete accepted before takeover lost its physical target");
        await crashed.DisposeAsync();
        var recovered = Owner("key-eph-recovery");
        await Activate(recovered);
        async Task Pump()
        {
            await recovered.WokenAsync(null);
            for (int i = 0; i < 12; i++) await recovered.RunAsync();
        }
        // Stop exactly at the recovered start commit; the already accepted Delete is a separate following turn.
        for (int i = 0; i < 12 && (await store.LoadAsync([id])).Count == 0; i++) await recovered.RunAsync();
        var ended = (await store.LoadAsync([id])).SingleOrDefault();
        Check(ended is { Status: InstanceStatus.Finished, State: "null", Revision: 1 },
            "recovered keyed ephemeral completion must leave deletable identity without checkpoint payload");
        var journal = (await store.ReadJournalAsync(0, 0, 1000)).Where(r => r.Instance == id && r.Revision > 0).ToArray();
        var projector = new Projector(store, 0);
        await projector.RoundAsync();
        var view = (await store.ReadViewsAsync([id])).SingleOrDefault();
        Check(ended!.History == history && (history == History.Minimal ? journal.Length == 0 && view is null :
            journal is [{ Revision: 1 }] && view == new ViewRow(id, 1, 1) &&
            Json.Read<string[]>(journal[0].Entries).SequenceEqual(history == History.Full ? ["finished"] : [])),
            "keyed terminal completion did not preserve " + history + " history or its projected revision");
        Check((await ingress.AcceptAsync(request with { Operation = "second" })).Outcome == "in-use", "key displaced a completed present run");
        Check((await ingress.AcceptAsync(new(key, "delete", Kinds.Delete))).RunId == id, "operator delete retry lost its physical target");
        await Pump();
        await waitUntil(receipt.KeepUntil!.Value);
        Check((await store.LoadAsync([id])).SingleOrDefault() is { Retired: true }, "operator delete failed to retire ephemeral identity");
        Check((await ingress.AcceptAsync(new(key, "stale-command", "signal"))).Outcome == "missing", "deleted ephemeral run accepted a command");
        var next = await ingress.AcceptAsync(request with { Operation = "second" });
        Check(next.Outcome == "accepted" && next.RunId != id && await ingress.AcceptAsync(request) == receipt, "ephemeral rebind or replay aliased old run");
        await Pump();

        // Isolate the flush which already had to consume the marked, durable keyed ingress row.
        // Compare it with an outside, ordinary ephemeral start that only consumes its durable inbox row.
        async Task<long?> TerminalFlush(bool keyed)
        {
            var target = new InstanceRef("0/named-" + Guid.NewGuid().ToString("N"));
            if (keyed) target = new InstanceRef((await ingress.AcceptAsync(request with { Key = key + "-meter" })).RunId!);
            else await store.AddAsync(new(key + "-control", target, Kinds.Start, Start: request.Start));
            await recovered.PollAsync();
            await recovered.LoadAsync();
            await recovered.FlushAsync(); // mark, excluded from both samples
            recovered.RunTurn(target.Id);
            var from = trips?.Invoke();
            await recovered.FlushAsync();
            var delta = from.HasValue ? trips!() - from.Value : (long?)null;
            Check(keyed == ((await store.LoadAsync([target.Id])).Count == 1), "terminal flush omitted or invented the key lifecycle row");
            return delta;
        }
        var control = await TerminalFlush(false);
        var keyedCost = await TerminalFlush(true);
        Check(keyedCost == control, "keyed ephemeral completion added a wire round trip");
        Console.WriteLine($"{label}: keyed ephemeral {history}, owner release before first terminal commit, takeover, operator delete, expiry, rebind and replay passed.");
        if (control.HasValue) Console.WriteLine($"{label}: terminal fenced flush ({history}) wire trips: durable-ingress ephemeral control {control}, keyed ephemeral {keyedCost}.");
        await recovered.DisposeAsync();
    }
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
