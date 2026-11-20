using System.Text.Json.Nodes;
using static Kernel.Native.Binding;
using static Kernel.Native.Wf;

namespace Kernel.Native.Run;

/// <summary>Round 5 C, the third review round's variants (set one): attacks on the join firing ordinal. A = e1 alone then e2 (the lost turn and its successor); B = e1 and e2 in one turn (the replay over the longer inbox).</summary>
public static class JoinVariantChecks
{
    private sealed record Sending(string Node, string Frame, string Path, int Attempt, string Key, string Body);

    public static async Task<int> RunAsync(string mode)
    {
        var failures = 0;
        void Check(bool ok, string what) { Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}"); if (!ok) failures++; }

        // W1: a join inside a Flowchart loop (three rounds), the Http on the join, beside a sibling event branch with Sets.
        foreach (var sets in new[] { 0, 2, 3, 5 })
        {
            Console.WriteLine($"{mode} W1 join inside a loop, sibling with {sets} Sets");
            var tail = new List<Node> { Event("e2", "v-2") };
            for (var i = 1; i <= sets; i++) tail.Add(Set($"t{i}", "tag", $"tag{i}"));
            var registry = new WorkflowRegistry();
            registry.Add(Workflow("v", Sequence("root",
                    Parallel("par", null,
                        Sequence("b1", Event("e1", "v-1"),
                            Flowchart("flow",
                                [Set("head", "round", Expr("round + 1")), Set("a", "x", 1), Set("b", "y", 1), Http("j", "https://partner.example/j", Expr("'round=' + round")), If("decide", Expr("round < 3")), Set("end", "done", "yes")],
                                [new("head", "a"), new("head", "b"), new("a", "j"), new("b", "j"), new("j", "decide"), new("decide", "head", "True"), new("decide", "end", "False")],
                                ("j", Gateway.ParallelJoin))),
                        Sequence("b2", tail.ToArray())),
                    Set("out", "output", "done")),
                ("round", 0), ("x", 0), ("y", 0), ("tag", "none"), ("done", "no"), ("output", null)));
            var a = await Run(registry, mode, "x", ["v-1"], ["v-2"]);
            var b = await Run(registry, mode, "x", ["v-1", "v-2"], []);
            if (sets == 3) { Print("A", a); Print("B", b); }
            Compare(Check, a, b, 3);
        }

        // W2: two joins in series after a merge: m -> u, v -> j1 -> p, q -> j2 (Http). Each token fires j1 and j2 once.
        {
            Console.WriteLine($"{mode} W2 two joins in series after a merge");
            var registry = new WorkflowRegistry();
            registry.Add(Workflow("v", Sequence("root",
                    Flowchart("flow",
                        [Set("s0", "x", 1), Event("ea", "v-1"), Event("eb", "v-2"), WriteLine("m", "merged"), Set("u", "a", 1), Set("v", "b", 1), WriteLine("j1", "j1"), Set("p", "c", 1), Set("q", "d", 1), Http("j2", "https://partner.example/j2", "j2"), Http("after", "https://partner.example/after", "after")],
                        [new("s0", "ea"), new("s0", "eb"), new("ea", "m"), new("eb", "m"), new("m", "u"), new("m", "v"), new("u", "j1"), new("v", "j1"), new("j1", "p"), new("j1", "q"), new("p", "j2"), new("q", "j2"), new("j2", "after")],
                        ("m", Gateway.Merge), ("j1", Gateway.ParallelJoin), ("j2", Gateway.ParallelJoin)),
                    Set("out", "output", "done")),
                ("x", 0), ("a", 0), ("b", 0), ("c", 0), ("d", 0), ("output", null)));
            var a = await Run(registry, mode, "x", ["v-1"], ["v-2"]);
            var b = await Run(registry, mode, "x", ["v-1", "v-2"], []);
            Print("A", a); Print("B", b);
            Check(a.Count == 4 && b.Count == 4, $"j2 and after fired once per token in A and B ({a.Count}, {b.Count})");
            Check(a.Select(s => s.Key).Distinct().Count() == a.Count && b.Select(s => s.Key).Distinct().Count() == b.Count, "keys distinct within A and within B");
            Check(a.Select(s => s.Key).OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(b.Select(s => s.Key).OrderBy(k => k, StringComparer.Ordinal)), "the same key set in A and B");
        }

        // W3: a join forward of a merge whose arrivals a replay can regroup: m -> u (Set) and m -> v (Delay) -> hj (Http).
        // A: e1, its timer, e2, its timer. B: e1 and e2 in one turn, then both timers. Does hj fire the same number of times, with the same keys, and does the instance finish?
        {
            Console.WriteLine($"{mode} W3 join forward of a merge with a timer on one inbound");
            var registry = new WorkflowRegistry();
            registry.Add(Workflow("v", Sequence("root",
                    Flowchart("flow",
                        [Set("s0", "x", 1), Event("ea", "v-1"), Event("eb", "v-2"), WriteLine("m", "merged"), Set("u", "a", 1), Delay("v", 20), Http("hj", "https://partner.example/hj", "joined"), Http("after", "https://partner.example/after", "after")],
                        [new("s0", "ea"), new("s0", "eb"), new("ea", "m"), new("eb", "m"), new("m", "u"), new("m", "v"), new("u", "hj"), new("v", "hj"), new("hj", "after")],
                        ("m", Gateway.Merge), ("hj", Gateway.ParallelJoin)),
                    Set("out", "output", "done")),
                ("x", 0), ("a", 0), ("output", null)));
            var (a, statusA) = await RunWithStatus(registry, mode, "x", ["v-1"], ["v-2"]);
            var (b, statusB) = await RunWithStatus(registry, mode, "x", ["v-1", "v-2"], []);
            Print("A", a); Print("B", b);
            Console.WriteLine($"  status A={statusA} B={statusB}");
            Check(statusA == InstanceStatus.Finished && statusB == InstanceStatus.Finished, $"both runs finished ({statusA}, {statusB})");
            Check(a.Count == b.Count, $"hj fired the same number of times in A and B ({a.Count}, {b.Count})");
            Check(a.Select(s => s.Key).Distinct().Count() == a.Count && b.Select(s => s.Key).Distinct().Count() == b.Count, "keys distinct within A and within B");
            Check(a.Select(s => s.Key).OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(b.Select(s => s.Key).OrderBy(k => k, StringComparer.Ordinal)), $"the same key set in A and B ({string.Join(", ", a.Select(s => s.Path))} / {string.Join(", ", b.Select(s => s.Path))})");
        }

        Console.WriteLine(failures == 0 ? $"{mode}: round 3 variants all passed" : $"{mode}: {failures} round 3 variant check(s) FAILED");
        return failures;
    }

    private static void Print(string run, List<Sending> s) => Console.WriteLine($"  {run}: " + string.Join("; ", s.Select(o => $"{o.Node} {o.Frame} [{o.Path}] {o.Body}")));

    private static void Compare(Action<bool, string> check, List<Sending> a, List<Sending> b, int? expected)
    {
        if (expected is { } n) check(a.Count == n && b.Count == n, $"{n} sendings in A and B ({a.Count}, {b.Count})");
        var collisions = a.SelectMany(x => b.Where(y => x.Key == y.Key && x.Body != y.Body).Select(y => (x, y))).ToList();
        check(collisions.Count == 0, $"no key carried two bodies across A and B ({string.Join("; ", collisions.Select(c => $"{c.x.Key}: '{c.x.Body}' vs '{c.y.Body}'"))})");
        var moved = a.Where(x => b.SingleOrDefault(y => y.Body == x.Body && y.Node == x.Node) is { } y && y.Key != x.Key).ToList();
        check(moved.Count == 0, $"every logical activation kept its key across A and B ({string.Join("; ", moved.Select(m => $"{m.Node} {m.Body}: {m.Key}"))})");
        check(a.Select(s => s.Key).Distinct().Count() == a.Count, $"keys distinct within A ({a.Count} sendings, {a.Select(s => s.Key).Distinct().Count()} keys)");
        check(b.Select(s => s.Key).Distinct().Count() == b.Count, $"keys distinct within B ({b.Count} sendings, {b.Select(s => s.Key).Distinct().Count()} keys)");
    }

    private static async Task<List<Sending>> Run(WorkflowRegistry registry, string mode, string id, string[] first, string[] second) => (await RunWithStatus(registry, mode, id, first, second)).Item1;

    private static async Task<(List<Sending>, InstanceStatus?)> RunWithStatus(WorkflowRegistry registry, string mode, string id, string[] first, string[] second)
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
        try { await owner.PumpAsync(poll: true, until: () => owner.Status("0/" + id) == InstanceStatus.Finished || (second.Length > 0 && !owner.Worker.Instances.Any(i => i.Busy) && store.Messages.Values.All(m => m.Done)), keepPolling: true, timeoutMs: 3000); }
        catch (TimeoutException) { Console.WriteLine("  (pump 1 timed out)"); }
        foreach (var e in second) await Events.PublishAsync(store, new EventRow("event:" + e, "ev-" + e + "-2", "null", Environment.TickCount64 + 60_000));
        try { await owner.PumpAsync(poll: true, until: () => owner.Status("0/" + id) == InstanceStatus.Finished, keepPolling: true, timeoutMs: 3000); }
        catch (TimeoutException) { Console.WriteLine("  (pump 2 timed out: the instance did not finish)"); }
        return (outbound, owner.Status("0/" + id));
    }
}
