using System.Text.Json.Nodes;
using static Kernel.Native.Binding;
using static Kernel.Native.Wf;

namespace Kernel.Native.Run;

/// <summary>
/// Round 5 C, the second review round's variants (set one): shapes that try to break the iteration-path identities. Run A takes e1 alone,
/// then e2 (the lost turn and its successor); run B takes e1 and e2 in one turn (the replacement owner's replay over
/// the longer inbox). Both start from the same committed waiting state (same id, fresh store).
/// </summary>
public static class VariantChecks
{
    private sealed record Sending(string Node, string Frame, string Path, int Attempt, string Key, string Body);

    public static async Task<int> RunAsync(string mode)
    {
        var failures = 0;
        void Check(bool ok, string what) { Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}"); if (!ok) failures++; }

        // V1: a join forward of a merge. Two tokens pass the merge; both reach the fork x, y; the ParallelJoin j sends.
        {
            Console.WriteLine($"{mode} V1 join after merge");
            var registry = new WorkflowRegistry();
            registry.Add(Workflow("v", Sequence("root",
                    Flowchart("flow",
                        [Set("s0", "x", 1), Event("ea", "v-1"), Event("eb", "v-2"), WriteLine("m", "merged"), Set("x", "a", 1), Set("y", "b", 1), Http("j", "https://partner.example/join", "joined")],
                        [new("s0", "ea"), new("s0", "eb"), new("ea", "m"), new("eb", "m"), new("m", "x"), new("m", "y"), new("x", "j"), new("y", "j")],
                        ("m", Gateway.Merge), ("j", Gateway.ParallelJoin)),
                    Set("out", "output", "done")),
                ("x", 0), ("a", 0), ("b", 0), ("output", null)));
            var a = await Run(registry, mode, "x", ["v-1"], ["v-2"]);
            var b = await Run(registry, mode, "x", ["v-1", "v-2"], []);
            Print("A", a); Print("B", b);
            Check(a.Count == 2 && b.Count == 2, $"the join behind the merge fired once per token ({a.Count}, {b.Count})");
            Check(a.Select(s => s.Key).Distinct().Count() == a.Count, $"two firings of j in one round carry two keys in A ({string.Join(", ", a.Select(s => s.Key))})");
            Check(b.Select(s => s.Key).Distinct().Count() == b.Count, $"two firings of j in one round carry two keys in B ({string.Join(", ", b.Select(s => s.Key))})");
        }

        // V2: nested loops (ForEach outside, While inside) beside a sibling event branch with Sets.
        foreach (var sets in new[] { 0, 1, 2, 3, 4, 5 })
        {
            Console.WriteLine($"{mode} V2 nested loops, sibling with {sets} Sets");
            var tail = new List<Node> { Event("e2", "v-2") };
            for (var i = 1; i <= sets; i++) tail.Add(Set($"t{i}", "tag", $"tag{i}"));
            var registry = new WorkflowRegistry();
            registry.Add(Workflow("v", Sequence("root",
                    Parallel("par", null,
                        Sequence("b1", Event("e1", "v-1"),
                            ForEach("each", Expr("[10, 20]"), Sequence("outer", Set("k0", "k", 0),
                                While("loop", Expr("k < 2"), Sequence("body", Set("inc", "k", Expr("k + 1")), Http("h", "https://partner.example/o", Expr("'o=' + CurrentIndex + ' k=' + k"))))))),
                        Sequence("b2", tail.ToArray())),
                    Set("out", "output", "done")),
                ("k", 0), ("tag", "none"), ("output", null)));
            var a = await Run(registry, mode, "x", ["v-1"], ["v-2"]);
            var b = await Run(registry, mode, "x", ["v-1", "v-2"], []);
            if (sets == 3) { Print("A", a); Print("B", b); }
            Compare(Check, a, b, 4);
        }

        // V3: loops in both branches, both sending (ForEach in b1, While in b2 behind the event).
        {
            Console.WriteLine($"{mode} V3 loops in both branches");
            var registry = new WorkflowRegistry();
            registry.Add(Workflow("v", Sequence("root",
                    Parallel("par", null,
                        Sequence("b1", Event("e1", "v-1"), ForEach("each", Expr("[1, 2, 3]"), Http("h1", "https://partner.example/a", Expr("'n=' + CurrentIndex")))),
                        Sequence("b2", Event("e2", "v-2"), While("loop", Expr("k < 3"), Sequence("body", Set("inc", "k", Expr("k + 1")), Email("h2", "ops@example.com", Expr("'k=' + k"), "b"))))),
                    Set("out", "output", "done")),
                ("k", 0), ("output", null)));
            var a = await Run(registry, mode, "x", ["v-1"], ["v-2"]);
            var b = await Run(registry, mode, "x", ["v-1", "v-2"], []);
            Compare(Check, a, b, 6);
        }

        // V4: ForEach over a collection the sibling changes before the loop starts (B sees the longer one).
        {
            Console.WriteLine($"{mode} V4 ForEach over a changing collection");
            var registry = new WorkflowRegistry();
            registry.Add(Workflow("v", Sequence("root",
                    Parallel("par", null,
                        Sequence("b1", Event("e1", "v-1"), Set("w", "wait", 1), Set("w2", "wait", 2), Set("w3", "wait", 3), ForEach("each", Var("items"), Http("h", "https://partner.example/i", Expr("'n=' + CurrentIndex")))),
                        Sequence("b2", Event("e2", "v-2"), Set("grow", "items", Expr("[1, 2, 3]")))),
                    Set("out", "output", "done")),
                ("items", JsonNode.Parse("[1, 2]")), ("output", null)));
            var a = await Run(registry, mode, "x", ["v-1"], ["v-2"]);
            var b = await Run(registry, mode, "x", ["v-1", "v-2"], []);
            Print("A", a); Print("B", b);
            Check(a.Count == 2 || a.Count == 3, $"A iterated ({a.Count})");
            Compare(Check, a, b, null);
        }

        // V5: a While whose condition reads what the sibling's event sets (B runs more passes).
        {
            Console.WriteLine($"{mode} V5 While reading event data");
            var registry = new WorkflowRegistry();
            registry.Add(Workflow("v", Sequence("root",
                    Parallel("par", null,
                        Sequence("b1", Event("e1", "v-1"), Set("w", "wait", 1), Set("w2", "wait", 2), While("loop", Expr("k < limit"), Sequence("body", Set("inc", "k", Expr("k + 1")), Http("h", "https://partner.example/w", Expr("'k=' + k"))))),
                        Sequence("b2", Event("e2", "v-2"), Set("more", "limit", 3))),
                    Set("out", "output", "done")),
                ("k", 0), ("limit", 2), ("output", null)));
            var a = await Run(registry, mode, "x", ["v-1"], ["v-2"]);
            var b = await Run(registry, mode, "x", ["v-1", "v-2"], []);
            Print("A", a); Print("B", b);
            Compare(Check, a, b, null);
        }

        // V6: an exclusive split that rejoins at a Merge inside a Flowchart loop, beside a sibling event branch with Sets.
        foreach (var sets in new[] { 0, 2, 3, 4 })
        {
            Console.WriteLine($"{mode} V6 xor-merge inside a loop, sibling with {sets} Sets");
            var tail = new List<Node> { Event("e2", "v-2") };
            for (var i = 1; i <= sets; i++) tail.Add(Set($"t{i}", "tag", $"tag{i}"));
            var registry = new WorkflowRegistry();
            registry.Add(Workflow("v", Sequence("root",
                    Parallel("par", null,
                        Sequence("b1", Event("e1", "v-1"),
                            Flowchart("flow",
                                [Set("head", "round", Expr("round + 1")), If("split", Expr("round == 1")), Set("left", "side", "L"), Set("right", "side", "R"), WriteLine("m", "merged"),
                                 Http("h", "https://partner.example/loop", Expr("'round=' + round + ' side=' + side")), If("decide", Expr("round < 3")), Set("end", "done", "yes")],
                                [new("head", "split"), new("split", "left", "True"), new("split", "right", "False"), new("left", "m"), new("right", "m"), new("m", "h"), new("h", "decide"),
                                 new("decide", "head", "True"), new("decide", "end", "False")],
                                ("m", Gateway.Merge))),
                        Sequence("b2", tail.ToArray())),
                    Set("out", "output", "done")),
                ("round", 0), ("side", null), ("tag", "none"), ("done", "no"), ("output", null)));
            var a = await Run(registry, mode, "x", ["v-1"], ["v-2"]);
            var b = await Run(registry, mode, "x", ["v-1", "v-2"], []);
            if (sets == 3) { Print("A", a); Print("B", b); }
            Compare(Check, a, b, 3);
        }

        Console.WriteLine(failures == 0 ? $"{mode}: variants all passed" : $"{mode}: {failures} variant check(s) FAILED");
        return failures;
    }

    private static void Print(string run, List<Sending> s) => Console.WriteLine($"  {run}: " + string.Join("; ", s.Select(o => $"{o.Node} {o.Frame} [{o.Path}] {o.Body}")));

    /// <summary>One body (one logical activation) has one key in A and B; one key never carries two bodies; keys are distinct within a run.</summary>
    private static void Compare(Action<bool, string> check, List<Sending> a, List<Sending> b, int? expected)
    {
        if (expected is { } n) check(a.Count == n && b.Count == n, $"{n} sendings in A and B ({a.Count}, {b.Count})");
        var collisions = a.SelectMany(x => b.Where(y => x.Key == y.Key && x.Body != y.Body).Select(y => (x, y))).ToList();
        check(collisions.Count == 0, $"no key carried two bodies across A and B ({string.Join("; ", collisions.Select(c => $"{c.x.Key}: '{c.x.Body}' vs '{c.y.Body}'"))})");
        // Every activation A sent is in B (B may send more: a longer collection, a higher limit), with the same key.
        var missing = a.Where(x => !b.Any(y => y.Body == x.Body && y.Node == x.Node)).ToList();
        check(missing.Count == 0, $"every activation of A is sent in B ({string.Join("; ", missing.Select(m => $"{m.Node} {m.Body}"))})");
        var moved = a.Where(x => b.Any(y => y.Body == x.Body && y.Node == x.Node) && !b.Any(y => y.Body == x.Body && y.Node == x.Node && y.Key == x.Key)).ToList();
        check(moved.Count == 0, $"every logical activation kept its key across A and B ({string.Join("; ", moved.Select(m => $"{m.Node} {m.Body}: {m.Key}"))})");
        check(a.Select(s => s.Key).Distinct().Count() == a.Count, $"keys distinct within A ({a.Count} sendings, {a.Select(s => s.Key).Distinct().Count()} keys)");
        check(b.Select(s => s.Key).Distinct().Count() == b.Count, $"keys distinct within B ({b.Count} sendings, {b.Select(s => s.Key).Distinct().Count()} keys)");
    }

    private static async Task<List<Sending>> Run(WorkflowRegistry registry, string mode, string id, string[] first, string[] second)
    {
        var outbound = new List<Sending>();
        var store = new InMemoryStore(1, () => Environment.TickCount64);
        var owner = new Owner("n1", 0, store, registry, new Network(), native: new NativeOptions
        {
            OnOutbound = s => outbound.Add(new(s.Node, s.Frame, s.IterationPath, s.Attempt, s.Effect.Key,
                s.Effect is HttpSendEffect h ? h.Request.Body?.ToJsonString() ?? "null" : ((EmailSendEffect)s.Effect).Mail.Subject)),
        });
        await owner.StartAsync();
        owner.Worker.Post(new Message("m1", InstanceRef.In(0, id), Kinds.Start, Start: NativeModel.Spec("v", mode, new JsonObject { ["key"] = id })), fresh: true);
        await owner.PumpAsync();
        foreach (var e in first) await Events.PublishAsync(store, new EventRow("event:" + e, "ev-" + e + "-1", "null", Environment.TickCount64 + 60_000));
        await owner.PumpAsync(poll: true, until: () => owner.Status("0/" + id) == InstanceStatus.Finished || (second.Length > 0 && !owner.Worker.Instances.Any(i => i.Busy)), keepPolling: true, timeoutMs: 5000);
        foreach (var e in second) await Events.PublishAsync(store, new EventRow("event:" + e, "ev-" + e + "-2", "null", Environment.TickCount64 + 60_000));
        await owner.PumpAsync(poll: true, until: () => owner.Status("0/" + id) == InstanceStatus.Finished, keepPolling: true, timeoutMs: 5000);
        return outbound;
    }
}
