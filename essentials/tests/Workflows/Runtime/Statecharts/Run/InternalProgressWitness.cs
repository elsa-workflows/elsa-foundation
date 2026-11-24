using System.Text.Json;
using Kernel.Statecharts;

// Pure entered-configuration/phase-acknowledgement fixtures. JSON is not an actor restart.
internal static class InternalProgressWitness
{
    private static GraphNode[] Compound() => [new("root", NodeKind.Root, Initial: "Review"),
        new("Review", NodeKind.Compound, "root", "Final"), new("Final", NodeKind.Final, "Review"),
        new("Other", NodeKind.Atomic, "root"), new("Done", NodeKind.Final, "root")];
    private static GraphRule Rule(string id, int order, string source, string? target, TriggerKind kind,
        VisitMode mode = VisitMode.Reenter) => new(id, order, source, target, mode, kind == TriggerKind.Event ? "business" : "", Kind: kind);
    private static GraphNode[] Regions() => [new("root", NodeKind.Root, Initial: "P"), new("P", NodeKind.Parallel, "root"),
        new("rA", NodeKind.Region, "P", "A"), new("A", NodeKind.Compound, "rA", "A0"),
        new("A0", NodeKind.Atomic, "A"), new("AF", NodeKind.Final, "A"), new("AZ", NodeKind.Final, "rA"),
        new("rB", NodeKind.Region, "P", "B"), new("B", NodeKind.Compound, "rB", "BF"),
        new("BF", NodeKind.Final, "B"), new("BZ", NodeKind.Final, "rB"), new("Done", NodeKind.Final, "root")];
    private static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
    private static T Copy<T>(T value, bool json) => json ? JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))! : value;
    private static StateVisit[] Visits(SelectionChart chart) => chart.Initial().Select(id => new StateVisit(id, "visit:" + id)).ToArray();
    private static StateVisit[] Apply(StateVisit[] current, GraphPlan plan, int serial) => plan.Next.Select(id =>
        plan.Steps.Any(s => s.Phase == "entry" && s.State == id) ? new StateVisit(id, $"visit:{serial}:{id}") : current.Single(v => v.State == id)).ToArray();
    private static void Refuse(Action action, string expected, string failure)
    {
        try { action(); }
        catch (InvalidOperationException e) when (e.Message == expected) { return; }
        throw new InvalidOperationException(failure);
    }
    private static IEnumerable<(string Name, Action<bool> Run)> Cases()
    {
        yield return ("automatic-before-completion", json =>
        {
            var chart = new SelectionChart(Compound(), [Rule("auto", 1, "Review", "Other", TriggerKind.Automatic),
                Rule("complete", 0, "Review", "Done", TriggerKind.Completion)]);
            var visits = Visits(chart); var start = new InternalProgress(8); var before = JsonSerializer.Serialize(start);
            var d = chart.NextInternal(visits, Copy(start, json));
            Check(d.Kind == InternalDecisionKind.Automatic && d.Plan!.Selected.SequenceEqual(["auto"]) && d.Progress.Ready is [{ State: "Review" }], "IP1: automatic did not precede completion");
            Check(JsonSerializer.Serialize(start) == before, "IP12: selection mutated input progress");
            visits = Apply(visits, d.Plan!, 1);
            var settled = chart.NextInternal(visits, Copy(d.Progress.CompletePhases(), json));
            Check(settled.Kind == InternalDecisionKind.Stable && settled.Progress.Offered!.Length == 0 && settled.Progress.Ready!.Length == 0,
                "IP7: exited completion survived automatic progress");
        });
        yield return ("automatic-rechecked-after-completion", json =>
        {
            var chart = new SelectionChart(Compound(), [Rule("auto", 0, "Review", "Done", TriggerKind.Automatic),
                Rule("complete", 1, "Review", null, TriggerKind.Completion, VisitMode.Internal)]);
            var visits = Visits(chart); var enabled = false;
            bool Guard(GraphRule r, StateVisit _) => r.Id != "auto" || enabled;
            var first = chart.NextInternal(visits, new(8), Guard);
            Check(first.Kind == InternalDecisionKind.Completion && first.Plan!.Selected.SequenceEqual(["complete"]), "IP2: completion not selected after false automatic");
            enabled = true;
            var next = chart.NextInternal(visits, Copy(first.Progress.CompletePhases(), json), Guard);
            Check(next.Kind == InternalDecisionKind.Automatic && next.Plan!.Selected.SequenceEqual(["auto"]), "IP2: automatic not rechecked after completion action");
        });
        yield return ("unhandled-completion-once-per-visit", json =>
        {
            var chart = new SelectionChart(Compound(), [Rule("complete", 0, "Review", "Done", TriggerKind.Completion)]);
            var visits = Visits(chart); var calls = 0;
            var first = chart.NextInternal(visits, new(8), (_, _) => { calls++; return false; });
            Check(first.Kind == InternalDecisionKind.Completion && first.Plan!.Selected.Length == 0 && !first.Progress.PhasePending, "IP3: false completion was not consumed");
            var stable = chart.NextInternal(visits, Copy(first.Progress, json), (_, _) => { calls++; return true; });
            Check(stable.Kind == InternalDecisionKind.Stable && calls == 1, "IP3: completion revived after value change");
            var external = stable.Progress.BeginExternal();
            visits = visits.Select(v => v.State == "Final" ? v with { Occurrence = "replacement-child" } : v).ToArray();
            var sameParent = chart.NextInternal(visits, Copy(external, json), (_, _) => { calls++; return true; });
            Check(sameParent.Kind == InternalDecisionKind.Stable && calls == 1, "IP3: child replacement revived surviving parent completion");
        });
        yield return ("successful-completion-reentry", json =>
        {
            var chart = new SelectionChart(Compound(), [Rule("complete", 0, "Review", null, TriggerKind.Completion, VisitMode.Internal)]);
            var visits = Visits(chart); var first = chart.NextInternal(visits, new(8));
            var stable = chart.NextInternal(visits, Copy(first.Progress.CompletePhases(), json));
            Check(stable.Kind == InternalDecisionKind.Stable, "IP3: successful internal completion rearmed");
            var nextVisits = visits.Select(v => v with { Occurrence = v.Occurrence + ":new" }).ToArray();
            var fresh = chart.NextInternal(nextVisits, Copy(stable.Progress.BeginExternal(), json));
            Check(fresh.Kind == InternalDecisionKind.Completion && fresh.Completion!.Visit != first.Completion!.Visit &&
                fresh.Progress.Offered!.Length == 1 && fresh.Progress.Serial == 2, "IP7: new parent visit did not get a fresh completion");
        });
        yield return ("parallel-requires-every-region-final", json =>
        {
            var nodes = Regions(); var chart = new SelectionChart(nodes, [Rule("done", 0, "P", "Done", TriggerKind.Completion)]);
            var partial = new StateVisit[] { new("P", "p"), new("AZ", "a"), new("B", "b"), new("BF", "bf") };
            var first = chart.NextInternal(partial, new(8));
            Check(first.Kind == InternalDecisionKind.Completion && first.Completion!.State == "B" && first.Plan!.Selected.Length == 0,
                "IP4: parallel completed while a region lacked its final child");
            var stable = chart.NextInternal(partial, Copy(first.Progress, json));
            Check(stable.Kind == InternalDecisionKind.Stable, "IP4: partial parallel did not settle locally");
            var complete = new StateVisit[] { partial[0], partial[1], new("BZ", "bz") };
            var done = chart.NextInternal(complete, Copy(stable.Progress.BeginExternal(), json));
            Check(done.Kind == InternalDecisionKind.Completion && done.Completion!.State == "P" && done.Plan!.Selected.SequenceEqual(["done"]), "IP4: all-final parallel did not complete");
        });
        yield return ("nested-completion-is-local", json =>
        {
            var nodes = Compound().Select(n => n.Id == "Review" ? n with { Initial = "Inner" } : n)
                .Concat([new GraphNode("Inner", NodeKind.Compound, "Review", "IF"), new("IF", NodeKind.Final, "Inner")]);
            var chart = new SelectionChart(nodes, [Rule("parent", 0, "Review", "Done", TriggerKind.Completion)]);
            var calls = 0; var first = chart.NextInternal(Visits(chart), new(8), (_, _) => { calls++; return true; });
            Check(first.Kind == InternalDecisionKind.Completion && first.Completion!.State == "Inner" && first.Plan!.Selected.Length == 0 && calls == 0,
                "IP5: child completion leaked to ancestor rule");
            Check(chart.NextInternal(Visits(chart), Copy(first.Progress, json)).Kind == InternalDecisionKind.Stable, "IP5: child completion finished incomplete ancestor");
        });
        yield return ("simultaneous-completion-ordinal-order", json =>
        {
            var chart = new SelectionChart(Regions(), []);
            var visits = new StateVisit[] { new("P", "p"), new("A", "a"), new("AF", "af"), new("B", "b"), new("BF", "bf") };
            var first = chart.NextInternal(visits, new(8));
            var second = chart.NextInternal(visits, Copy(first.Progress, json));
            Check(first.Completion!.State == "A" && second.Completion!.State == "B" &&
                first.Completion.Order < second.Completion.Order, "IP6: simultaneous completion order changed");
        });
        yield return ("recorded-ready-order-precedes-state-order", json =>
        {
            var chart = new SelectionChart(Regions(), [Rule("a", 0, "A0", "AF", TriggerKind.Automatic)]);
            var visits = Visits(chart); var first = chart.NextInternal(visits, new(8));
            Check(first.Kind == InternalDecisionKind.Automatic && first.Progress.Ready is [{ State: "B", Order: 1 }], "IP6: original completion not recorded before automatic work");
            visits = Apply(visits, first.Plan!, 1);
            var second = chart.NextInternal(visits, Copy(first.Progress.CompletePhases(), json));
            var third = chart.NextInternal(visits, Copy(second.Progress, json));
            Check(second.Completion!.State == "B" && third.Completion!.State == "A" && third.Completion.Order == 2, "IP6: later completion overtook recorded ready order");
        });
        yield return ("phase-barrier", json =>
        {
            var chart = new SelectionChart(Compound(), [Rule("a", 0, "Review", "Other", TriggerKind.Automatic)]);
            var first = chart.NextInternal(Visits(chart), new(8));
            Refuse(() => chart.NextInternal(Visits(chart), Copy(first.Progress, json)), "Internal phases have not completed.", "IP8: later evaluation crossed pending phases");
            Refuse(() => new InternalProgress(8).BeginExternal(), "Internal progress has not settled.", "IP8: business evaluation crossed unsettled internal progress");
        });
        yield return ("cycle-budget-survives-phases-and-json", json =>
        {
            var chart = new SelectionChart(Compound(), [Rule("a", 0, "Review", null, TriggerKind.Automatic, VisitMode.Internal)]);
            var visits = Visits(chart); var p = new InternalProgress(2);
            for (var i = 0; i < 2; i++)
            {
                var d = chart.NextInternal(visits, Copy(p, json));
                Check(d.Kind == InternalDecisionKind.Automatic && d.Progress.Steps == i + 1 && d.Plan!.Steps.Single().Activity is null, "IP9: empty-action cycle escaped persistent budget");
                p = Copy(d.Progress.CompletePhases(), json);
            }
            var blocked = chart.NextInternal(visits, p);
            Check(blocked.Kind == InternalDecisionKind.Blocked && blocked.Progress.Steps == 2 && blocked.Plan is null, "IP9: cycle did not stop before next work");
            var restored = chart.NextInternal(visits, Copy(blocked.Progress, json), (_, _) => throw new InvalidOperationException("IP10: blocked cycle evaluated guard"));
            Check(restored.Kind == InternalDecisionKind.Blocked, "IP10: blocked cycle restarted");
            Refuse(() => restored.Progress.BeginExternal(), "Internal progress has not settled.", "IP10: external input reset a blocked cycle");
            Refuse(() => restored.Progress.CompletePhases(), "No internal phases await completion.", "IP10: phase acknowledgement released a block");
        });
        yield return ("exact-limit-stable-and-new-input", json =>
        {
            var chart = new SelectionChart(Compound(), [Rule("a", 0, "Review", "Other", TriggerKind.Automatic)]);
            var visits = Visits(chart); var first = chart.NextInternal(visits, new(1));
            visits = Apply(visits, first.Plan!, 1);
            var stable = chart.NextInternal(visits, Copy(first.Progress.CompletePhases(), json));
            Check(stable.Kind == InternalDecisionKind.Stable && stable.Progress.Steps == 1 && stable.Progress.Block is null, "IP11: exactly-at-limit stable machine blocked");
            var fresh = Copy(stable.Progress, json).BeginExternal();
            Check(fresh.Steps == 0 && !fresh.Stable && fresh.Serial == stable.Progress.Serial, "IP11: new semantic input did not reset only its counter");
        });
        yield return ("unhandled-completion-counts-toward-budget", json =>
        {
            var chart = new SelectionChart(Regions(), []);
            var visits = new StateVisit[] { new("P", "p"), new("A", "a"), new("AF", "af"), new("B", "b"), new("BF", "bf") };
            var first = chart.NextInternal(visits, new(1));
            var second = chart.NextInternal(visits, Copy(first.Progress, json));
            Check(first.Progress.Steps == 1 && second.Kind == InternalDecisionKind.Blocked && second.Progress.Block is { Kind: InternalDecisionKind.Completion, CompletionVisit: "b" },
                "IP9: unhandled completions bypassed settling budget");
        });
        foreach (var kind in new[] { TriggerKind.Automatic, TriggerKind.Completion })
            yield return ("guard-fault-immutable-" + kind, json =>
            {
                var chart = new SelectionChart(Compound(), [Rule("rule", 0, "Review", "Done", kind)]);
                var progress = Copy(new InternalProgress(8), json); var before = JsonSerializer.Serialize(progress); var seen = false;
                try { chart.NextInternal(Visits(chart), progress, (_, _) => throw new InvalidOperationException("guard-proof")); }
                catch (InvalidOperationException e) when (e.Message == "guard-proof") { seen = true; }
                Check(seen && JsonSerializer.Serialize(progress) == before, "IP12: guard failure consumed or mutated progress");
            });
        yield return ("automatic-child-precedence", json =>
        {
            var nodes = Compound().Select(n => n.Id == "Final" ? n with { Kind = NodeKind.Atomic } : n);
            var chart = new SelectionChart(nodes, [Rule("parent", 0, "Review", "Done", TriggerKind.Automatic),
                Rule("child", 1, "Final", null, TriggerKind.Automatic, VisitMode.Internal)]);
            var d = chart.NextInternal(Visits(chart), Copy(new InternalProgress(8), json), (r, _) =>
                r.Id == "parent" ? throw new InvalidOperationException("IP13: suppressed parent guard evaluated") : true);
            Check(d.Plan!.Selected.SequenceEqual(["child"]), "IP13: automatic child did not suppress parent");
        });
        yield return ("compatible-automatic-regions-one-step", json =>
        {
            var chart = new SelectionChart(Regions(), [Rule("a", 1, "A", null, TriggerKind.Automatic, VisitMode.Internal),
                Rule("b", 0, "B", null, TriggerKind.Automatic, VisitMode.Internal)]);
            var d = chart.NextInternal(Visits(chart), Copy(new InternalProgress(8), json));
            Check(d.Plan!.Selected.SequenceEqual(["b", "a"]) && d.Progress.Steps == 1, "IP13: compatible automatic regions lost authored order or one evaluation budget");
        });
        yield return ("event-category-is-not-automatic", json =>
        {
            var chart = new SelectionChart(Compound(), [Rule("event", 0, "Review", "Done", TriggerKind.Event)]);
            var first = chart.NextInternal(Visits(chart), Copy(new InternalProgress(8), json));
            Check(first.Kind == InternalDecisionKind.Completion && first.Plan!.Selected.Length == 0, "IP14: business event rule ran as completion");
            Check(chart.NextInternal(Visits(chart), first.Progress).Kind == InternalDecisionKind.Stable, "IP14: business rule ran automatically");
        });
    }
    private static IEnumerable<(string Name, Action Run)> Readers()
    {
        var good = new InternalProgress(4, 1, 2, [new("A", "a"), new("B", "b")], [new("A", "a", 1), new("B", "b", 2)]);
        foreach (var (name, bad) in new (string, InternalProgress)[] {
            ("limit", good with { Limit = 0 }), ("steps", good with { Steps = 5 }), ("serial", good with { Serial = 0 }),
            ("duplicate-offer", good with { Offered = [new("A", "a"), new("B", "a")], Ready = [] }),
            ("duplicate-offer-state", good with { Offered = [new("A", "a"), new("A", "b")], Ready = [new("A", "a", 1), new("A", "b", 2)] }),
            ("offered-without-serial", good with { Ready = [], Serial = 0 }),
            ("phase-without-count", new(4, PhasePending: true)),
            ("unoffered-ready", good with { Offered = [] }), ("ready-order", good with { Ready = [good.Ready![1], good.Ready![0]] }),
            ("stable-ready", good with { Stable = true }), ("stable-phase", new(4, Stable: true, PhasePending: true)),
            ("block-before-limit", good with { Block = new(InternalDecisionKind.Automatic, ["a"], null) }),
            ("block-kind", good with { Steps = 4, Block = new(InternalDecisionKind.Stable, ["a"], null) }),
            ("block-missing-completion", good with { Steps = 4, Block = new(InternalDecisionKind.Completion, [], "unknown") }) })
            yield return ("reader-" + name, () => Refuse(() => bad.Validate(), name.StartsWith("block-", StringComparison.Ordinal) ? "Invalid internal-progress block." : "Invalid internal-progress checkpoint.", "IP15: malformed progress accepted: " + name));
        yield return ("reader-future-version", () =>
        {
            try { (good with { Version = 2 }).Validate(); }
            catch (NotSupportedException e) when (e.Message == "Unsupported internal-progress version.") { return; }
            throw new InvalidOperationException("IP15: future progress accepted");
        });
        foreach (var (name, rule) in new (string, GraphRule)[] {
            ("unknown-kind", Rule("x", 0, "Review", "Done", (TriggerKind)99)),
            ("automatic-event-name", Rule("x", 0, "Review", "Done", TriggerKind.Automatic) with { Trigger = "business" }),
            ("completion-event-name", Rule("x", 0, "Review", "Done", TriggerKind.Completion) with { Trigger = "business" }),
            ("empty-event-name", Rule("x", 0, "Review", "Done", TriggerKind.Event) with { Trigger = "" }),
            ("atomic-completion", Rule("x", 0, "Other", "Done", TriggerKind.Completion)),
            ("final-completion", Rule("x", 0, "Final", "Done", TriggerKind.Completion)) })
            yield return ("schema-" + name, () =>
            {
                try { _ = new SelectionChart(Compound(), [rule]); }
                catch (ArgumentException e) when (e.Message == (name == "atomic-completion" ? "Completion requires a compound or parallel source." : "Invalid transition source, trigger or mode.")) { return; }
                throw new InvalidOperationException("IP16: invalid trigger category accepted: " + name);
            });
    }
    internal static IEnumerable<string> Names => from c in Cases() from json in new[] { false, true } select $"internal:{c.Name},json={json}";
    internal static IEnumerable<string> ReaderNames => Readers().Select(c => "internal:" + c.Name);
    internal static void Run()
    {
        foreach (var c in Cases()) foreach (var json in new[] { false, true }) { c.Run(json); Console.WriteLine($"PASS internal:{c.Name},json={json}"); }
        foreach (var c in Readers()) { c.Run(); Console.WriteLine("PASS internal:" + c.Name); }
        Console.WriteLine($"{Names.Count()} pure protocol cases and {ReaderNames.Count()} constructed reader refusals passed; no native progress/incident/recovery or durable-restart guarantee.");
    }
}
