using System.Text.Json;
using Kernel.Statecharts;

internal static class SelectionWitness
{
    private sealed record Sample(string Name, GraphNode[] Nodes, GraphRule[] Rules, string[] Expected,
        Dictionary<string, bool?>? Guards = null, bool AncestorProfile = false);
    private static GraphRule R(string id, int order, string source, string? target, VisitMode mode = VisitMode.Reenter) =>
        new(id, order, source, target, mode, "go", "work:" + id);
    private static GraphNode[] Simple() => [new("root", NodeKind.Root, Initial: "Review"),
        new("Review", NodeKind.Compound, "root", "Checking", "enter-review", "exit-review"),
        new("Checking", NodeKind.Atomic, "Review", Entry: "enter-checking", Exit: "exit-checking"),
        new("Changes", NodeKind.Atomic, "Review"), new("Ready", NodeKind.Final, "root", Entry: "final-entry")];
    private static GraphNode[] Parallel(bool single = false) => [new("root", NodeKind.Root, Initial: "P"), new("P", NodeKind.Parallel, "root"),
        new("E", NodeKind.Region, "P", "E0"), new("E0", NodeKind.Atomic, "E"), new("E1", NodeKind.Atomic, "E"),
        .. single ? [] : new GraphNode[] { new("L", NodeKind.Region, "P", "L0"), new("L0", NodeKind.Atomic, "L"), new("L1", NodeKind.Atomic, "L") },
        new("Outside", NodeKind.Atomic, "root"), new("Done", NodeKind.Final, "root")];
    private static IEnumerable<Sample> Samples()
    {
        yield return new("child-wins", Simple(), [R("parent", 0, "Review", "Ready"), R("child", 1, "Checking", "Changes")], ["child"]);
        yield return new("lazy-parent", Simple(), [R("parent", 0, "Review", "Ready"), R("child", 1, "Checking", "Changes")], ["child"], new() { ["parent"] = null });
        yield return new("false-child-fallback", Simple(), [R("parent", 0, "Review", "Ready"), R("child", 1, "Checking", "Changes")], ["parent"], new() { ["child"] = false });
        yield return new("same-source-next", Simple(), [R("false", 0, "Checking", "Changes"), R("chosen", 1, "Checking", "Changes"), R("later", 2, "Checking", "Changes")], ["chosen"], new() { ["false"] = false, ["later"] = null });
        yield return new("same-source-first", Simple(), [R("first", 0, "Checking", "Changes"), R("later", 1, "Checking", "Changes")], ["first"], new() { ["later"] = null });
        yield return new("compatible-regions", Parallel(), [R("e", 2, "E0", "E1"), R("l", 1, "L0", "L1")], ["e", "l"]);
        yield return new("local-preempts-container", Parallel(), [R("e", 0, "E0", "E1"), R("l", 1, "L0", "Outside")], ["e"]);
        yield return new("container-preempts-local", Parallel(), [R("e", 1, "E0", "E1"), R("l", 0, "L0", "Outside")], ["l"]);
        yield return new("internal-preempts-container", Parallel(), [R("e", 0, "E0", null, VisitMode.Internal), R("l", 1, "L0", "Outside")], ["e"]);
        yield return new("container-preempts-internal", Parallel(), [R("e", 1, "E0", null, VisitMode.Internal), R("l", 0, "L0", "Outside")], ["l"]);
        yield return new("no-conflict-backtracking", Parallel(), [R("e", 0, "E0", "E1"), R("l-exit", 1, "L0", "Outside"), R("l-later", 2, "L0", "L1"), R("parent", 3, "P", "Done")], ["e"], new() { ["l-later"] = null, ["parent"] = null });
        yield return new("internal-child-suppresses-parent", Parallel(), [R("e", 0, "E0", null, VisitMode.Internal), R("parent", 1, "P", "Done")], ["e"], new() { ["parent"] = null });
        yield return new("compound-preserve", Simple(), [R("p", 0, "Review", "Changes", VisitMode.Preserve)], ["p"]);
        yield return new("compound-reenter", Simple(), [R("p", 0, "Review", "Changes")], ["p"]);
        yield return new("self-atomic", Simple(), [R("self", 0, "Checking", "Checking")], ["self"]);
        yield return new("self-compound", Simple(), [R("self", 0, "Review", "Review")], ["self"]);
        yield return new("parallel-target", Parallel(), [R("p", 0, "P", "E1")], ["p"]);
        yield return new("parallel-self", Parallel(), [R("p", 0, "P", "P")], ["p"]);
        yield return new("single-region-parallel", Parallel(true), [R("p", 0, "P", "E1")], ["p"]);
        var nested = Simple().Concat([new GraphNode("Nested", NodeKind.Compound, "Review", "Deep"), new GraphNode("Deep", NodeKind.Atomic, "Nested")]).ToArray();
        yield return new("cousin-target", nested, [R("c", 0, "Checking", "Deep")], ["c"]);
        yield return new("deep-preserved-target", nested, [R("p", 0, "Review", "Deep", VisitMode.Preserve)], ["p"]);
        yield return new("enter-parallel-descendant", Simple().Concat(Parallel().Where(n => n.Id != "root")).ToArray(), [R("enter", 0, "Checking", "E1")], ["enter"]);
        yield return new("nested-final", Simple().Concat([new GraphNode("InnerFinal", NodeKind.Final, "Review", Entry: "inner-final-entry")]).ToArray(), [R("f", 0, "Checking", "InnerFinal")], ["f"]);
        yield return new("unhandled", Simple(), [], []);
        yield return new("false-only", Simple(), [R("f", 0, "Checking", "Changes")], [], new() { ["f"] = false });
        yield return new("final-entry", Simple(), [R("finish", 0, "Review", "Ready")], ["finish"]);
        yield return new("internal-no-work", Simple(), [R("i", 0, "Checking", null, VisitMode.Internal) with { Action = null }], ["i"]);
        yield return new("ancestor-compound", Simple(), [R("ancestor", 0, "Checking", "Review")], ["ancestor"], AncestorProfile: true);
        yield return new("ancestor-parallel", Parallel(), [R("ancestor", 0, "E0", "P")], ["ancestor"], AncestorProfile: true);
        yield return new("shared-parent-once", Parallel(), [R("parent", 0, "P", "Done")], ["parent"]);
    }

    private static StateVisit[] Visits(SelectionChart chart) => chart.Initial().Select(id => new StateVisit(id, "visit:" + id)).ToArray();
    private static GraphPlan Select(Sample sample, SelectionChart chart, StateVisit[] visits, List<string>? called = null) =>
        chart.Select(visits, (r, _) => r.Trigger == "go", (r, _) =>
        {
            called?.Add(r.Id);
            if (sample.Guards is null || !sample.Guards.TryGetValue(r.Id, out var value)) return true;
            return value ?? throw new InvalidOperationException("Q2: suppressed/later guard evaluated: " + r.Id);
        });
    private static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
    private static bool Same(IEnumerable<string> a, IEnumerable<string> b) => a.ToHashSet(StringComparer.Ordinal).SetEquals(b);
    private static void Verify(Sample s)
    {
        var chart = new SelectionChart(s.Nodes, s.Rules);
        var visits = Visits(chart);
        var called = new List<string>();
        var plan = Select(s, chart, visits, called);
        Check(Same(plan.Selected, s.Expected), "Q1: selected transitions for " + s.Name);
        Check(called.Distinct(StringComparer.Ordinal).Count() == called.Count, "Q2: guard evaluated twice for " + s.Name);
        var exits = plan.Steps.Where(p => p.Phase == "exit").ToArray();
        var entries = plan.Steps.Where(p => p.Phase == "entry").ToArray();
        var actions = plan.Steps.Where(p => p.Phase == "transition").ToArray();
        if (s.AncestorProfile)
        {
            var target = s.Rules[0].Target!;
            Check(exits.Any(p => p.State == target) && entries.Any(p => p.State == target) &&
                Same(entries.Select(p => p.State), chart.Initial()), "Q5: ancestor target visit was preserved or its regions were not reset");
        }
        Check(Same(plan.Selected, actions.Select(a => a.Transition!)), "Q4: selected action ownership lost");
        Check(exits.Select(p => p.State).Distinct().Count() == exits.Length && entries.Select(p => p.State).Distinct().Count() == entries.Length,
            "Q4: duplicated structural work");
        Check(!entries.Any(e => visits.Any(v => v.State == e.State) && exits.All(x => x.State != e.State)), "Q4: entry reused a retained visit");
        foreach (var e in exits) Check(e.Visit == visits.Single(v => v.State == e.State).Occurrence, "Q3: exit visit identity changed");
        foreach (var a in actions)
        {
            var source = s.Rules.Single(r => r.Id == a.Transition).Source;
            Check(a.Visit == visits.Single(v => v.State == source).Occurrence, "Q3: action source visit changed");
            Check(Same(a.After, exits.Select(e => e.Id)), "Q4: action lost all-exit barrier");
        }
        var nodes = s.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        string? Nearest(string id, GraphStep[] work)
        {
            for (var parent = nodes[id].Parent; parent is not null; parent = nodes[parent].Parent)
                if (work.Any(p => p.State == parent)) return parent;
            return null;
        }
        foreach (var e in exits)
            Check(Same(e.After, exits.Where(c => c.State != e.State && Nearest(c.State, exits) == e.State).Select(c => c.Id)), "Q4: exit hierarchy dependency lost");
        foreach (var e in entries)
        {
            var parent = Nearest(e.State, entries);
            Check(e.Visit is null && Same(e.After, parent is null ? actions.Select(a => a.Id) : entries.Where(p => p.State == parent).Select(p => p.Id)),
                "Q4: entry hierarchy/action barrier lost");
        }
        var next = plan.Next.Select(id => new StateVisit(id, entries.Any(e => e.State == id) ? "new:" + id : visits.Single(v => v.State == id).Occurrence));
        _ = chart.Select(next, (_, _) => false); // Independent complete-configuration validation of the produced successor.
        if (s.Name == "compound-preserve") Check(exits.All(p => p.State != "Review") && entries.All(p => p.State != "Review"), "Q5: preserved parent was re-entered");
        if (s.Name == "compound-reenter") Check(exits.Any(p => p.State == "Review") && entries.Any(p => p.State == "Review"), "Q5: re-entered parent was preserved");
        if (s.Name == "parallel-target") Check(Same(entries.Select(e => e.State), ["P", "E1", "L0"]), "Q5: parallel target lost other-region initial entry");
        if (s.Name == "nested-final") Check(!chart.IsTopFinal(plan.Next) && entries.Single().Activity == "inner-final-entry", "Q6: nested final ended the machine or lost its entry");
        if (s.Name == "final-entry") Check(chart.IsTopFinal(plan.Next) && entries.Single().Activity == "final-entry", "Q6: explicit machine final entry lost");
        Console.WriteLine("PASS selection:" + s.Name);
    }

    private static IEnumerable<(string Name, Action Run)> Extra()
    {
        yield return ("empty-action-phase", () =>
        {
            var chart = new SelectionChart(Simple(), []);
            Check(chart.Prerequisites([("exit", "Checking"), ("entry", "Changes")])[1].SequenceEqual([0]), "Q4: empty action phase lost the exit barrier");
        });
        yield return ("guard-fault", () =>
        {
            var chart = new SelectionChart(Simple(), [R("child", 0, "Checking", "Changes")]); var visits = Visits(chart); var before = visits.ToArray();
            try { chart.Select(visits, (_, _) => true, (_, _) => throw new InvalidOperationException("fixture-guard")); }
            catch (InvalidOperationException e) when (e.Message == "fixture-guard") { Check(visits.SequenceEqual(before), "Q2: guard fault mutated input"); return; }
            throw new InvalidOperationException("Q2: guard fault became false/unhandled");
        });
        yield return ("immutable-compiled-input", () =>
        {
            var nodes = Simple(); var rules = new[] { R("child", 0, "Checking", "Changes") }; var chart = new SelectionChart(nodes, rules);
            nodes[1] = nodes[1] with { Initial = "Changes" }; rules[0] = R("changed", 0, "Review", "Ready");
            Check(Same(chart.Initial(), ["Review", "Checking"]) && Same(chart.Select(Visits(chart), (_, _) => true).Selected, ["child"]), "Q7: caller arrays changed compiled semantics");
        });
        yield return ("enumeration-not-priority", () =>
        {
            var s = Samples().Single(s => s.Name == "container-preempts-local"); var chart = new SelectionChart(s.Nodes.Reverse(), s.Rules.Reverse());
            Check(Same(Select(s, chart, Visits(chart).Reverse().ToArray()).Selected, s.Expected), "Q7: incidental enumeration changed priority");
        });
        yield return ("scoped-matcher", () =>
        {
            var chart = new SelectionChart(Simple(), [R("child", 0, "Checking", "Changes")]);
            Check(chart.Select(Visits(chart), (_, v) => v.Occurrence == "old-visit").Selected.Length == 0, "Q3: matcher was retargeted from a stale visit");
        });
        yield return ("legacy-private-id-collision", () =>
        {
            var c = new Chart(new(new("$root", [new("$root"), new("other", Final: true)]), [new("$root", "go", "other")]));
            Check(c.Select(c.Start().Next, "id", "go", null)?.Next is ["other"], "Q7: private adapter identity aliased an authored state");
        });
        var invalid = new List<(string, GraphNode[], GraphRule[])>
        {
            ("missing-root", Simple().Where(n => n.Id != "root").ToArray(), []),
            ("duplicate-node", [.. Simple(), Simple()[1]], []),
            ("unknown-parent", Simple().Select(n => n.Id == "Checking" ? n with { Parent = "missing" } : n).ToArray(), []),
            ("parent-cycle", Simple().Select(n => n.Id == "Review" ? n with { Parent = "Checking" } : n).ToArray(), []),
            ("missing-initial", Simple().Select(n => n.Id == "Review" ? n with { Initial = "absent" } : n).ToArray(), []),
            ("region-under-compound", [.. Simple(), new("bad-region", NodeKind.Region, "Review", "Checking")], []),
            ("parallel-owns-state", Parallel().Select(n => n.Id == "E" ? n with { Kind = NodeKind.Compound } : n).ToArray(), []),
            ("leaf-owns-child", Simple().Select(n => n.Id == "Review" ? n with { Kind = NodeKind.Atomic } : n).ToArray(), []),
            ("empty-container", [.. Simple(), new("empty", NodeKind.Compound, "root", "none")], []),
            ("duplicate-rule-id", Simple(), [R("a", 0, "Checking", "Changes"), R("a", 1, "Review", "Ready")]),
            ("duplicate-order", Simple(), [R("a", 0, "Checking", "Changes"), R("b", 0, "Review", "Ready")]),
            ("final-source", Simple(), [R("f", 0, "Ready", "Review")]),
            ("target-region", Parallel(), [R("bad", 0, "E0", "E")]),
            ("cross-region", Parallel(), [R("bad", 0, "E0", "L1")]),
            ("parallel-preserve", Parallel(), [R("bad", 0, "P", "E1", VisitMode.Preserve)]),
            ("single-parallel-preserve", Parallel(true), [R("bad", 0, "P", "E1", VisitMode.Preserve)]),
            ("preserve-sibling", Simple(), [R("bad", 0, "Checking", "Changes", VisitMode.Preserve)]),
            ("preserve-self", Simple(), [R("bad", 0, "Review", "Review", VisitMode.Preserve)]),
            ("preserve-ancestor", Simple(), [R("bad", 0, "Checking", "Review", VisitMode.Preserve)]),
            ("internal-target", Simple(), [R("bad", 0, "Checking", "Changes", VisitMode.Internal)]),
            ("missing-target", Simple(), [R("bad", 0, "Checking", null)]),
            ("unknown-kind", Simple().Select(n => n.Id == "Checking" ? n with { Kind = (NodeKind)99 } : n).ToArray(), []),
            ("unknown-mode", Simple(), [R("bad", 0, "Checking", "Changes", (VisitMode)99)])
        };
        foreach (var (name, nodes, rules) in invalid)
            yield return ("invalid-" + name, () => Reject(() => _ = new SelectionChart(nodes, rules)));
        foreach (var (name, visits) in new (string, StateVisit[])[]
        {
            ("missing-ancestor", [new("Checking", "c")]), ("extra-branch", [new("Review", "r"), new("Checking", "c"), new("Changes", "x")]),
            ("duplicate-state", [new("Review", "r"), new("Checking", "c"), new("Checking", "c2")]),
            ("duplicate-visit", [new("Review", "same"), new("Checking", "same")]), ("missing-region", [new("P", "p"), new("E0", "e")])
        }) yield return ("invalid-active-" + name, () => Reject(() => new SelectionChart(name == "missing-region" ? Parallel() : Simple(), []).Select(visits, (_, _) => true)));
    }
    private static void Reject(Action run) { try { run(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Q8: invalid graph/configuration accepted"); }
    internal static IEnumerable<string> Names => Samples().Select(s => "selection:" + s.Name).Concat(Extra().Select(c => "selection:" + c.Name));
    internal static void Run()
    {
        foreach (var sample in Samples()) Verify(sample);
        foreach (var test in Extra()) { test.Run(); Console.WriteLine("PASS selection:" + test.Name); }
        Console.WriteLine($"{Names.Count()}/{Names.Count()} structural selection fixtures passed; logical visits, not native scope/runtime proof.");
    }

    internal static void Export()
    {
        var samples = Samples().Select(s =>
        {
            var chart = new SelectionChart(s.Nodes, s.Rules); var visits = Visits(chart); var p = Select(s, chart, visits);
            return new { name = s.Name, proposedAncestor = s.AncestorProfile,
                nodes = s.Nodes.Select(n => new { id = n.Id, kind = n.Kind.ToString().ToLowerInvariant(), parent = n.Parent, initial = n.Initial }),
                transitions = s.Rules.Select(r => new { id = r.Id, order = r.Order, source = r.Source, target = r.Target, mode = r.Mode.ToString().ToLowerInvariant(), trigger = r.Trigger }),
                visits = visits.Select(v => new { state = v.State, visit = v.Occurrence }), guards = s.Guards,
                plan = new { selected = p.Selected, candidates = p.Candidates,
                    exits = p.Steps.Where(a => a.Phase == "exit").Select(a => new { id = a.Id, state = a.State, visit = a.Visit, after = a.After }),
                    actions = p.Steps.Where(a => a.Phase == "transition").Select(a => new { id = a.Id, transition = a.Transition, sourceVisit = a.Visit, after = a.After }),
                    entries = p.Steps.Where(a => a.Phase == "entry").Select(a => new { id = a.Id, state = a.State, after = a.After }) } };
        });
        Console.WriteLine(JsonSerializer.Serialize(samples));
    }
}
