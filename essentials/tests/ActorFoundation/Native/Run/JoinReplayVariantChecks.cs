using System.Text.Json.Nodes;
using static Kernel.Native.Binding;
using static Kernel.Native.Wf;

namespace Kernel.Native.Run;

/// <summary>Round 5 C, the third review round's variants (set two): attacks on the join ordinal, a join inside a loop, two joins in series, an outside inbound arriving last.</summary>
public static class JoinReplayVariantChecks
{
    private static int _failed;

    public static async Task<int> RunAsync()
    {
        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            Console.WriteLine($"==== {mode}");
            // V8 a join inside a flowchart loop, beside a sibling event branch: one firing per round, rounds told apart.
            await Shape(mode, "V8 join inside a loop", Workflow("v", Sequence("root",
                    Parallel("par", null,
                        Sequence("b1", Event("e1", "v-1"), Flowchart("flow",
                            [Set("head", "round", Expr("round + 1")), Set("pa", "a", 1), Set("pb", "b", 1), WriteLine("j", "joined"), Http("h", "https://partner.example/j", Expr("'r=' + round")), If("decide", Expr("round < 3")), Set("end", "done", "yes")],
                            [new("head", "pa"), new("head", "pb"), new("pa", "j"), new("pb", "j"), new("j", "h"), new("h", "decide"), new("decide", "head", "True"), new("decide", "end", "False")],
                            ("j", Gateway.ParallelJoin))),
                        Sequence("b2", Event("e2", "v-2"), Set("t1", "tag", 1), Set("t2", "tag", 2), Set("t3", "tag", 3))),
                    Set("out", "output", "done")),
                ("round", 0), ("a", 0), ("b", 0), ("done", "no"), ("tag", 0), ("output", null)), ["v-1"], ["v-2"], ["v-1", "v-2"], expectA: 3, expectB: 3);

            // V9 two joins in series after a merge: m -> u, v -> j1 -> w; m -> z; j2 joins w and z; h behind j2.
            await Shape(mode, "V9 two joins in series after a merge", Workflow("v", Sequence("root",
                    Flowchart("flow",
                        [Set("s0", "x", 1), Event("ea", "v-1"), Event("eb", "v-2"), WriteLine("m", "merged"), WriteLine("u", "u"), WriteLine("v", "v"), WriteLine("j1", "j1"), WriteLine("w", "w"), WriteLine("z", "z"), WriteLine("j2", "j2"), Http("h", "https://partner.example/j2", "joined")],
                        [new("s0", "ea"), new("s0", "eb"), new("ea", "m"), new("eb", "m"), new("m", "u"), new("m", "v"), new("u", "j1"), new("v", "j1"), new("j1", "w"), new("m", "z"), new("w", "j2"), new("z", "j2"), new("j2", "h")],
                        ("m", Gateway.Merge), ("j1", Gateway.ParallelJoin), ("j2", Gateway.ParallelJoin)),
                    Set("out", "output", "done")),
                ("x", 0), ("output", null)), ["v-1"], ["v-2"], ["v-1", "v-2"], expectA: -1, expectB: -1);

            // V10 a join with one inbound from a merge and one from outside that arrives LAST (a third event): does the firing count differ?
            await Shape(mode, "V10 join after merge, outside inbound arrives last", Workflow("v", Sequence("root",
                    Flowchart("flow",
                        [Set("s0", "x", 1), Event("ea", "v-1"), Event("eb", "v-2"), Event("ey", "v-3"), WriteLine("m", "merged"), WriteLine("px", "px"), WriteLine("j", "joined"), Http("h", "https://partner.example/j", "joined")],
                        [new("s0", "ea"), new("s0", "eb"), new("s0", "ey"), new("ea", "m"), new("eb", "m"), new("m", "px"), new("px", "j"), new("ey", "j"), new("j", "h")],
                        ("m", Gateway.Merge), ("j", Gateway.ParallelJoin)),
                    Set("out", "output", "done")),
                ("x", 0), ("output", null)), ["v-1", "v-3"], ["v-2"], ["v-1", "v-2"], expectA: -1, expectB: -1, bSecond: ["v-3"]);

        }
        Console.WriteLine(_failed == 0 ? "join replay variants: all passed" : $"join replay variants: {_failed} FAILED");
        return _failed == 0 ? 0 : 1;
    }

    private static async Task Shape(string mode, string name, Workflow definition, string[] aFirst, string[] aSecond, string[] bFirst, int expectA, int expectB, string[]? bSecond = null)
    {
        var registry = new WorkflowRegistry();
        registry.Add(definition);
        var a = await Run(registry, mode, "x", aFirst, aSecond);
        var b = await Run(registry, mode, "x", bFirst, bSecond ?? []);
        Console.WriteLine($"-- {name}: A sent {a.Count}, B sent {b.Count}");
        foreach (var o in a) Console.WriteLine($"   A {o.Node} frame={o.Frame} path={o.Path} key={o.Key} body={o.Body}");
        foreach (var o in b) Console.WriteLine($"   B {o.Node} frame={o.Frame} path={o.Path} key={o.Key} body={o.Body}");
        if (expectA >= 0) Check(a.Count == expectA, $"{name}: A sent {expectA} ({a.Count})");
        if (expectB >= 0) Check(b.Count == expectB, $"{name}: B sent {expectB} ({b.Count})");
        var dupA = a.GroupBy(o => o.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        var dupB = b.GroupBy(o => o.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Check(dupA.Count == 0 && dupB.Count == 0, $"{name}: no key names two activations within one run ({string.Join(", ", dupA.Concat(dupB))})");
        var keysA = a.Select(o => o.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var keysB = b.Select(o => o.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        // A join whose outside inbound arrives last fires twice when the events come one turn apart and once when they
        // come together (its author's V10): the firings that happen in both runs keep their keys; the count may differ.
        if (a.Count == b.Count) Check(keysA.SequenceEqual(keysB), $"{name}: the same key set across the lost turn and its replay (A {string.Join(" ", keysA)} | B {string.Join(" ", keysB)})");
        else Check(keysB.All(keysA.Contains), $"{name}: every firing of the shorter run keeps its key in the longer one (A {string.Join(" ", keysA)} | B {string.Join(" ", keysB)}); the firing count differs by design");
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
        try { await owner.PumpAsync(poll: true, until: () => owner.Status("0/" + id) == InstanceStatus.Finished, keepPolling: true, timeoutMs: 5000); }
        catch (TimeoutException) { Console.WriteLine($"   pump timeout; status: {owner.Status("0/" + id)}"); _failed++; }
        Console.WriteLine($"   status: {owner.Status("0/" + id)}");
        return outbound;
    }
}
