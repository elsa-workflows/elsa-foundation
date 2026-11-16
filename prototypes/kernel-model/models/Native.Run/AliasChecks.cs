using System.Text.Json.Nodes;
using static Kernel.Native.Binding;
using static Kernel.Native.Wf;

namespace Kernel.Native.Run;

/// <summary>
/// Round 5 C, from reviewer FIRST's alias demonstration against PR #4676: in Balanced and Ephemeral mode a replay of
/// the same committed state over a longer inbox mints the frame numbers in another order, so a key that named the frame
/// could give two different activations of one node the same key, and the same activation two keys. Instance A takes
/// e1 alone and e2 later (what a lost turn and its successor send); instance B, the same definition and the same
/// committed waiting state, takes e1 and e2 in one turn (what the replacement owner sends after the first turn's commit
/// was lost and e2 arrived meanwhile). The While body revisits "http" twice; a sibling branch's Sets interleave with it.
/// The owner's key (instance, node, iteration path, attempt) must give each iteration the same key in A and B and two
/// iterations two keys, whatever the sibling does. With the frame in the key (the PR's first shape) this fails at
/// three Sets: iteration 2 of A and iteration 1 of B shared one key.
/// </summary>
public static class AliasChecks
{
    private static Workflow Definition(int sets)
    {
        var tail = new List<Node> { Event("e2", "alias-2") };
        for (var i = 1; i <= sets; i++) tail.Add(Set($"t{i}", "tag", $"tag{i}"));
        return Workflow("alias", Sequence("root",
                Parallel("par", null,
                    Sequence("b1", Event("e1", "alias-1"),
                        While("loop", Expr("i < 2"), Sequence("body", Set("inc", "i", Expr("i + 1")), Http("http", "https://partner.example/orders", Expr("'i=' + i"))))),
                    Sequence("b2", tail.ToArray())),
                Set("out", "output", "done")),
            ("i", 0), ("tag", "none"), ("output", null));
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("effect keys under a replay over a longer inbox (round 5 C, reviewer FIRST's alias case)");
        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            var collisions = 0; var moved = 0; var reusedFrames = 0; var sent = 0;
            for (var sets = 0; sets <= 6; sets++)
            {
                var registry = new WorkflowRegistry();
                registry.Add(Definition(sets));
                var a = await Run(registry, mode, "x", ["alias-1"], ["alias-2"]);
                var b = await Run(registry, mode, "x", ["alias-1", "alias-2"], []);
                sent += a.Count + b.Count;
                // One key, one body: a receiver that deduplicates by the key never drops a different effect.
                foreach (var x in a) foreach (var y in b) if (x.Key == y.Key && x.Body != y.Body) collisions++;
                // One body (one logical activation: i=1, i=2), one key in A and in B, whatever the agenda order did to the frames.
                foreach (var x in a) if (b.SingleOrDefault(y => y.Body == x.Body) is { } y && y.Key != x.Key) moved++;
                foreach (var x in a) foreach (var y in b) if (x.Frame == y.Frame && x.Body != y.Body) reusedFrames++;
            }
            check(sent == 28, $"{mode}: 28 HTTP requests went out over 7 shapes ({sent})");
            check(collisions == 0, $"{mode}: no key carried two different bodies across the lost turn and its replay ({collisions} collisions)");
            check(moved == 0, $"{mode}: every logical activation kept its key across the replay ({moved} moved)");
            Console.WriteLine($"  {mode}: {reusedFrames} frame numbers were handed to another activation by the replay (the case the iteration path exists for)");
            await MergeAsync(mode, check);
        }
    }

    /// <summary>
    /// A Flowchart node forward of a Merge fires once per token (the owner's decision of 16 November: each arrival is
    /// identified by its source). Two branches, each released by an event, meet at a Merge; the Http behind it fires
    /// twice. A takes the events one turn apart, B takes both in one turn: the two firings must carry the same two
    /// keys in A and in B, whatever order the agenda ran them in, and the two keys must differ. With the round alone
    /// (no token) the two firings shared one key.
    /// </summary>
    private static async Task MergeAsync(string mode, Action<bool, string> check)
    {
        var registry = new WorkflowRegistry();
        registry.Add(Workflow("alias", Sequence("root",
                Flowchart("flow",
                    [Set("s0", "x", 1), Event("ea", "alias-1"), Event("eb", "alias-2"), WriteLine("m", "merged"), Http("h", "https://partner.example/merged", "token")],
                    [new("s0", "ea"), new("s0", "eb"), new("ea", "m"), new("eb", "m"), new("m", "h")], ("m", Gateway.Merge)),
                Set("out", "output", "done")),
            ("x", 0), ("output", null)));
        var a = await Run(registry, mode, "x", ["alias-1"], ["alias-2"]);
        var b = await Run(registry, mode, "x", ["alias-1", "alias-2"], []);
        var keysA = a.Select(o => o.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var keysB = b.Select(o => o.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        check(a.Count == 2 && b.Count == 2, $"{mode}, merge: the node behind the merge fired once per token in A and in B ({a.Count}, {b.Count})");
        check(keysA.Distinct(StringComparer.Ordinal).Count() == 2, $"{mode}, merge: the two tokens carried two keys ({string.Join(", ", keysA)})");
        check(keysA.SequenceEqual(keysB), $"{mode}, merge: the same two keys across the lost turn and its replay ({string.Join(", ", keysA)} against {string.Join(", ", keysB)})");
        check(a.All(o => o.Path.Contains("^m-e", StringComparison.Ordinal)), $"{mode}, merge: each token names the merge and its source ({string.Join(", ", a.Select(o => o.Path))})");
    }

    private static async Task<List<(string Node, string Frame, string Path, int Attempt, string Key, string Body)>> Run(WorkflowRegistry registry, string mode, string id, string[] first, string[] second)
    {
        var outbound = new List<(string Node, string Frame, string Path, int Attempt, string Key, string Body)>();
        var store = new InMemoryStore(1, () => Environment.TickCount64);
        var owner = new Owner("n1", 0, store, registry, new Network(), native: new NativeOptions
        {
            OnOutbound = s => outbound.Add((s.Node, s.Frame, s.IterationPath, s.Attempt, s.Effect.Key, ((HttpSendEffect)s.Effect).Request.Body?.ToJsonString() ?? "null")),
        });
        await owner.StartAsync();
        owner.Worker.Post(new Message("m1", InstanceRef.In(0, id), Kinds.Start, Start: NativeModel.Spec("alias", mode, new JsonObject { ["key"] = id })), fresh: true);
        await owner.PumpAsync();
        foreach (var e in first) await Events.PublishAsync(store, new EventRow("event:" + e, "ev-" + e + "-1", "null", Environment.TickCount64 + 60_000));
        await owner.PumpAsync(poll: true, until: () => owner.Status("0/" + id) == InstanceStatus.Finished || (second.Length > 0 && !owner.Worker.Instances.Any(i => i.Busy)), keepPolling: true, timeoutMs: 5000);
        foreach (var e in second) await Events.PublishAsync(store, new EventRow("event:" + e, "ev-" + e + "-2", "null", Environment.TickCount64 + 60_000));
        await owner.PumpAsync(poll: true, until: () => owner.Status("0/" + id) == InstanceStatus.Finished, keepPolling: true, timeoutMs: 5000);
        return outbound;
    }
}
