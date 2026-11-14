using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

// Deliberate literal fixtures, separate from actual new writer/reload workflows.
internal static class ScopedReaders
{
    internal static string[] Names => ["scope-reader:legacy-v3-pure", "scope-reader:current-roundtrip", "scope-reader:missing-scopes",
        "scope-reader:mixed-old-scopes", "scope-reader:machine-alias", "scope-reader:visit-alias", "scope-reader:missing-handle",
        "scope-reader:extra-handle", "scope-reader:retired-idle", "scope-reader:mixed-legacy-bit", "scope-reader:retired-before-action",
        "scope-reader:entry-before-retirement", "scope-reader:pending-entry-roundtrip", "scope-reader:legacy-new-declarations",
        "scope-definition:root-declarations", "scope-definition:region-declarations", "scope-definition:snapshot-defaults"];
    internal static void Run()
    {
        var chart = new Chart(new(new("Working", [new("Working", [new("Leaf", [new("Leaf")])]), new("Done", Final: true)]), [new("Working", "go", "Done")]));
        var visits = new StateVisit[] { new("Working", "literal-reader/state/1"), new("Leaf", "literal-reader/state/2") };
        var current = new MachineState(["Working", "Leaf"], null, Version: 4, Visits: visits, VisitSerial: 2,
            Scopes: new("machine-scope", new() { [visits[0].Occurrence] = "parent-scope", [visits[1].Occurrence] = "child-scope" }, false));
        void Pass(int i) => Console.WriteLine("PASS " + Names[i]);
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("V11: " + message); }
        void Refuse(MachineState bad, int i)
        {
            try { StateMachine.Restore(bad, chart); }
            catch (InvalidOperationException) { Pass(i); return; }
            throw new InvalidOperationException("V11: invalid scope checkpoint accepted: " + Names[i]);
        }
        var old = current with { Version = 3, Scopes = null };
        var serialized = Json.Write(old); var read = StateMachine.Restore(old, chart);
        Check(read.Version == 4 && read.LegacyScopes && read.Scopes is null && Json.Write(old) == serialized, "legacy reader allocated/mutated resources"); Pass(0);
        Check(Json.Write(StateMachine.Restore(Json.Read<MachineState>(Json.Write(current)), chart)) == Json.Write(current), "current reader changed handles"); Pass(1);
        Refuse(current with { Scopes = null }, 2);
        Refuse(current with { Version = 3 }, 3);
        Refuse(current with { Scopes = current.Scopes! with { Machine = "child-scope" } }, 4);
        Refuse(current with { Scopes = current.Scopes! with { Visits = new() { [visits[0].Occurrence] = "same", [visits[1].Occurrence] = "same" } } }, 5);
        Refuse(current with { Scopes = current.Scopes! with { Visits = new() { [visits[0].Occurrence] = "parent-scope" } } }, 6);
        Refuse(current with { Scopes = current.Scopes! with { Visits = new(current.Scopes.Visits) { ["extra"] = "extra-scope" } } }, 7);
        Refuse(current with { Scopes = current.Scopes! with { Retired = true } }, 8);
        Refuse(current with { LegacyScopes = true }, 9);
        var plan = chart.Compiled.Select(visits, (r, _) => r.Trigger == "go");
        var indices = plan.Steps.Select((s, i) => (s.Id, i)).ToDictionary(p => p.Id, p => p.i);
        var progress = new PhaseProgress(plan.Steps.Select(w => w.After.Select(id => indices[id]).ToArray()).ToArray(), new bool[plan.Steps.Length], []);
        var pending = current with { Pending = new("event", plan.Next, plan.Steps.Select(w => new Step(w.Phase, w.State, w.Activity)).ToArray()),
            Progress = progress, PlannedVisits = [new("Done", "literal-reader/state/3")], VisitSerial = 3, Selection = plan };
        Refuse(pending with { Scopes = new("machine-scope", [], true) }, 10);
        var last = plan.Steps.Length - 1;
        var entryProgress = progress with { Done = plan.Steps.Select(w => w.Phase != "entry").ToArray(), Running = new() { ["child"] = last } };
        Refuse(pending with { Progress = entryProgress }, 11);
        var entering = pending with { Progress = entryProgress, Scopes = new("machine-scope", new() { ["literal-reader/state/3"] = "final-scope" }, true) };
        Check(Json.Write(StateMachine.Restore(Json.Read<MachineState>(Json.Write(entering)), chart)) == Json.Write(entering), "pending Entry required retired source scopes"); Pass(12);
        var defaults = new Dictionary<string, System.Text.Json.Nodes.JsonNode?> { ["payload"] = Wf.Object(("value", 7)) };
        var nodes = new GraphNode[] { new("r", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "r", Defaults: defaults) };
        var graph = new SelectionChart(nodes, [], defaults);
        try { StateMachine.Restore(old, graph, "literal-reader"); }
        catch (InvalidOperationException e) when (e.Message == "Legacy statechart checkpoint cannot acquire new declarations.") { Pass(13); goto definitions; }
        throw new InvalidOperationException("V11: legacy checkpoint silently acquired declarations");
        definitions:
        try { _ = new SelectionChart(nodes.Select(n => n.Kind == NodeKind.Root ? n with { Defaults = defaults } : n), []); }
        catch (ArgumentException) { Pass(14); goto region; }
        throw new InvalidOperationException("V11: structural root silently owned declarations");
        region:
        try { _ = new SelectionChart([new("r", NodeKind.Root, Initial: "P"), new("P", NodeKind.Parallel, "r"),
            new("region", NodeKind.Region, "P", "A", Defaults: defaults), new("A", NodeKind.Atomic, "region")], []); }
        catch (ArgumentException) { Pass(15); goto snapshot; }
        throw new InvalidOperationException("V11: structural region silently owned declarations");
        snapshot:
        defaults["payload"]!["value"] = 99;
        Check(graph.Defaults("A")["payload"]!["value"]!.ToString() == "7" && graph.MachineDefaults["payload"]!["value"]!.ToString() == "7", "declaration snapshot retained mutable authoring data"); Pass(16);
    }
}
