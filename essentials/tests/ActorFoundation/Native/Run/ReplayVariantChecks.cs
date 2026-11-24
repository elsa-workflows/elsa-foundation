using System.Text.Json.Nodes;
using static Kernel.Native.Binding;
using static Kernel.Native.Wf;

namespace Kernel.Native.Run;

/// <summary>Round 5 C, the second review round's variants (set two): the alias case against the iteration-path key; a key seen twice within one run is two activations sharing one.</summary>
public static class ReplayVariantChecks
{
    private static int _failed;

    public static async Task<int> RunAsync()
    {
        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            Console.WriteLine($"==== {mode}");
            // V1 nested loops inside a branch: ForEach over [1,2] with a While of two passes inside; sibling Sets interleave.
            await Shape(mode, "V1 nested loops", Workflow("v", Sequence("root",
                    Parallel("par", null,
                        Sequence("b1", Event("e1", "v-1"),
                            ForEach("each", Expr("[1, 2]"), Sequence("fb", Set("z", "j", 0),
                                While("loop", Expr("j < 2"), Sequence("body", Set("inc", "j", Expr("j + 1")), Http("h", "https://partner.example/n", Expr("'i=' + CurrentIndex + ',j=' + j"))))))),
                        Sequence("b2", Event("e2", "v-2"), Set("t1", "tag", 1), Set("t2", "tag", 2), Set("t3", "tag", 3))),
                    Set("out", "output", "done")),
                ("j", 0), ("tag", 0), ("output", null)), ["v-1"], ["v-2"], ["v-1", "v-2"], expectA: 4, expectB: 4);

            // V2 ForEach over a collection a sibling changes (owner: same logical activation, same key, first content wins).
            await Shape(mode, "V2 ForEach over a changing collection", Workflow("v", Sequence("root",
                    Parallel("par", null,
                        Sequence("b1", Event("e1", "v-1"), ForEach("each", Expr("items"), Http("h", "https://partner.example/n", Expr("'v=' + CurrentItem")))),
                        Sequence("b2", Event("e2", "v-2"), Set("grow", "items", Expr("[9, 8, 7]")))),
                    Set("out", "output", "done")),
                ("items", new JsonArray(1, 2)), ("output", null)), ["v-1"], ["v-2"], ["v-2", "v-1"], expectA: 2, expectB: 3, contentMayDiffer: true);

            // V3 While whose condition reads data a sibling event sets.
            await Shape(mode, "V3 While reading event data", Workflow("v", Sequence("root",
                    Parallel("par", null,
                        Sequence("b1", Event("e1", "v-1"), While("loop", Expr("i < n"), Sequence("body", Set("inc", "i", Expr("i + 1")), Http("h", "https://partner.example/n", Expr("'i=' + i"))))),
                        Sequence("b2", Event("e2", "v-2"), Set("n3", "n", 3))),
                    Set("out", "output", "done")),
                ("i", 0), ("n", 1), ("output", null)), ["v-1"], ["v-2"], ["v-2", "v-1"], expectA: 1, expectB: 3);

            // V4 a merge inside a flowchart loop: two rounds, two tokens per round, the Http behind the merge fires 4 times.
            await Shape(mode, "V4 merge inside a loop", Workflow("v", Sequence("root",
                    Parallel("par", null,
                        Sequence("b1", Event("e1", "v-1"), Flowchart("flow",
                            [Set("head", "round", Expr("round + 1")), Set("pa", "src", "a"), Set("pb", "src", "b"), WriteLine("m", "merged"), Http("h", "https://partner.example/m", Expr("'r=' + round")), If("decide", Expr("round < 2")), Set("end", "done", "yes")],
                            [new("head", "pa"), new("head", "pb"), new("pa", "m"), new("pb", "m"), new("m", "h"), new("h", "decide"), new("decide", "head", "True"), new("decide", "end", "False")],
                            ("m", Gateway.Merge))),
                        Sequence("b2", Event("e2", "v-2"), Set("t1", "tag", 1), Set("t2", "tag", 2))),
                    Set("out", "output", "done")),
                ("round", 0), ("src", null), ("done", "no"), ("tag", 0), ("output", null)), ["v-1"], ["v-2"], ["v-1", "v-2"], expectA: 6, expectB: 6, contentMayDiffer: true);   // the decision behind the merge fires once per token: three sendings per round

            // V5 a join after a merge, both inbounds forward of the merge: does the join fire per token, and with what key behind it?
            await Shape(mode, "V5 join after a merge (both inbounds from the merge)", Workflow("v", Sequence("root",
                    Flowchart("flow",
                        [Set("s0", "x", 1), Event("ea", "v-1"), Event("eb", "v-2"), WriteLine("m", "merged"), Set("px", "src", "x"), Set("pz", "src", "z"), WriteLine("j", "joined"), Http("h", "https://partner.example/j", "token")],
                        [new("s0", "ea"), new("s0", "eb"), new("ea", "m"), new("eb", "m"), new("m", "px"), new("m", "pz"), new("px", "j"), new("pz", "j"), new("j", "h")],
                        ("m", Gateway.Merge), ("j", Gateway.ParallelJoin)),
                    Set("out", "output", "done")),
                ("x", 0), ("src", null), ("output", null)), ["v-1"], ["v-2"], ["v-1", "v-2"], expectA: 2, expectB: 2);

            // V6 a join after a merge with one inbound from the merge and one from outside it.
            await Shape(mode, "V6 join after a merge (one inbound from the merge, one from outside)", Workflow("v", Sequence("root",
                    Flowchart("flow",
                        [Set("s0", "x", 1), Event("ea", "v-1"), Event("eb", "v-2"), WriteLine("m", "merged"), Set("px", "src", "x"), Set("y", "src", "y"), WriteLine("j", "joined"), Http("h", "https://partner.example/j", "token")],
                        [new("s0", "ea"), new("s0", "eb"), new("s0", "y"), new("ea", "m"), new("eb", "m"), new("m", "px"), new("px", "j"), new("y", "j"), new("j", "h")],
                        ("m", Gateway.Merge), ("j", Gateway.ParallelJoin)),
                    Set("out", "output", "done")),
                ("x", 0), ("src", null), ("output", null)), ["v-1"], ["v-2"], ["v-1", "v-2"], expectA: 2, expectB: 2);

            // V7 a loop in a branch released by a timer beside a loop released by an event (Ephemeral short waits included).
            await Shape(mode, "V7 timer loop beside event loop", Workflow("v", Sequence("root",
                    Parallel("par", null,
                        Sequence("b1", Event("e1", "v-1"), While("l1", Expr("i < 2"), Sequence("body1", Set("inc1", "i", Expr("i + 1")), Http("h1", "https://partner.example/a", Expr("'i=' + i"))))),
                        Sequence("b2", Delay("d", 30), While("l2", Expr("k < 2"), Sequence("body2", Set("inc2", "k", Expr("k + 1")), Http("h2", "https://partner.example/b", Expr("'k=' + k")))))),
                    Set("out", "output", "done")),
                ("i", 0), ("k", 0), ("output", null)), ["v-1"], [], ["v-1"], expectA: 4, expectB: 4);
        }
        Console.WriteLine(_failed == 0 ? "replay variants: all passed" : $"replay variants: {_failed} FAILED");
        return _failed == 0 ? 0 : 1;
    }

    private static async Task Shape(string mode, string name, Workflow definition, string[] aFirst, string[] aSecond, string[] bFirst, int expectA, int expectB, bool contentMayDiffer = false)
    {
        var registry = new WorkflowRegistry();
        registry.Add(definition);
        var a = await Run(registry, mode, "x", aFirst, aSecond);
        var b = await Run(registry, mode, "x", bFirst, []);
        Console.WriteLine($"-- {name}: A sent {a.Count}, B sent {b.Count}");
        foreach (var o in a) Console.WriteLine($"   A {o.Node} frame={o.Frame} path={o.Path} key={o.Key} body={o.Body}");
        foreach (var o in b) Console.WriteLine($"   B {o.Node} frame={o.Frame} path={o.Path} key={o.Key} body={o.Body}");
        if (expectA >= 0) Check(a.Count == expectA, $"{name}: A sent {expectA} ({a.Count})");
        if (expectB >= 0) Check(b.Count == expectB, $"{name}: B sent {expectB} ({b.Count})");
        // No lost turn inside one run: a key seen twice within A (or within B) is two activations sharing one key.
        var dupA = a.GroupBy(o => o.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        var dupB = b.GroupBy(o => o.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Check(dupA.Count == 0 && dupB.Count == 0, $"{name}: no key names two activations within one run ({string.Join(", ", dupA.Concat(dupB))})");
        // Across the lost turn and its replay: one logical activation (same body) keeps its key.
        var moved = a.Count(x => b.Where(y => y.Body == x.Body && y.Node == x.Node).ToList() is [var y] && y.Key != x.Key);
        if (!contentMayDiffer) Check(moved == 0, $"{name}: every logical activation kept its key across the replay ({moved} moved)");
        // Where a body does not name its activation (a join sending the same body twice), the key sets must still agree.
        if (!contentMayDiffer && expectA == expectB && expectA >= 0)
            Check(a.Select(o => o.Key).OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(b.Select(o => o.Key).OrderBy(k => k, StringComparer.Ordinal)),
                $"{name}: the same key set across the replay ({string.Join(", ", a.Select(o => o.Key))} against {string.Join(", ", b.Select(o => o.Key))})");
        var cross = a.Count(x => b.Any(y => y.Key == x.Key && y.Body != x.Body));
        if (contentMayDiffer) Console.WriteLine($"   {name}: {cross} keys carried another content in the replay (owner: the receiver keeps the first)");
        else Check(cross == 0, $"{name}: no key carried two bodies across the replay ({cross})");
        var reused = a.Count(x => b.Any(y => y.Frame == x.Frame && (y.Body != x.Body || y.Node != x.Node)));
        Console.WriteLine($"   {name}: {reused} frame numbers handed to another activation by the replay");
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) _failed++;
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
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
        owner.Worker.Post(new Message("m1", InstanceRef.In(0, id), Kinds.Start, Start: NativeModel.Spec("v", mode, new JsonObject { ["key"] = id })), fresh: true);
        await owner.PumpAsync();
        foreach (var e in first) await Events.PublishAsync(store, new EventRow("event:" + e, "ev-" + e + "-1", "null", Environment.TickCount64 + 60_000));
        await owner.PumpAsync(poll: true, until: () => owner.Status("0/" + id) == InstanceStatus.Finished || (second.Length > 0 && !owner.Worker.Instances.Any(i => i.Busy)), keepPolling: true, timeoutMs: 5000);
        foreach (var e in second) await Events.PublishAsync(store, new EventRow("event:" + e, "ev-" + e + "-2", "null", Environment.TickCount64 + 60_000));
        await owner.PumpAsync(poll: true, until: () => owner.Status("0/" + id) == InstanceStatus.Finished, keepPolling: true, timeoutMs: 5000);
        Console.WriteLine($"   status: {owner.Status("0/" + id)}");
        return outbound;
    }
}
