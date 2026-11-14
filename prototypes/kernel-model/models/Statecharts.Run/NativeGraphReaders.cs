using Kernel.Durable;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

// Literal fixtures, not historical writer, binary upgrade or physical scope compatibility evidence.
internal static class NativeGraphReaders
{
    internal static string[] Names => ["identity-reader:v2-pending", "identity-reader:v2-idle", "identity-reader:v3-roundtrip",
        "identity-reader:missing-visits", "identity-reader:mixed-old-visits", "identity-reader:serial-range", "identity-reader:duplicate-visit",
        "identity-reader:wrong-source", "identity-reader:reused-entry", "identity-reader:wrong-planned-config", "identity-reader:changed-step", "identity-reader:changed-dependencies"];
    internal static void Run()
    {
        var chart = new Chart(new(new("Working", [new("Working", [new("Leaf", [new("Leaf", Exit: "finished")])], Exit: "held"), new("Done", Final: true)]),
            [new("Working", "move", "Done", "action")]));
        var old = Json.Read<MachineState>("""
            {"Active":["Working","Leaf"],"Pending":{"EventId":"event","Next":["Done"],"Steps":[
              {"Phase":"exit","State":"Leaf","Activity":"finished"},
              {"Phase":"exit","State":"Working","Activity":"held"},
              {"Phase":"transition","State":"Working","Activity":"action"},
              {"Phase":"entry","State":"Done","Activity":null}]},"Version":2,
              "Progress":{"Requires":[[],[0],[0,1],[2]],"Done":[true,false,false,false],"Running":{"old-child":1}}}
            """);
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException("N8: " + reason); }
        void Pass(int i) => Console.WriteLine("PASS " + Names[i]);
        var before = Json.Write(old);
        var migrated = StateMachine.Restore(old, chart);
        Check(migrated.Version == 4 && migrated.LegacyScopes && migrated.LegacyPending && migrated.Selection is null && migrated.Visits!.Length == 2 && migrated.PlannedVisits is [{ State: "Done" }] &&
            migrated.Progress!.Done.SequenceEqual([true, false, false, false]) && migrated.Progress.Running.Single().Key == "old-child" &&
            Json.Write(old) == before && old.Visits is null && old.Version == 2, "v2 migration lost progress/provenance or mutated input"); Pass(0);
        var idle = StateMachine.Restore(old with { Pending = null, Progress = null }, chart);
        Check(idle.Visits!.Length == 2 && idle.PlannedVisits is null && !idle.LegacyPending && idle.Version == 4 && idle.LegacyScopes, "v2 idle migration failed"); Pass(1);
        var visits = new StateVisit[] { new("Working", "literal-reader/state/1"), new("Leaf", "literal-reader/state/2") };
        var plan = chart.Compiled.Select(visits, (r, _) => r.Trigger == "move");
        var indices = plan.Steps.Select((s, i) => (s.Id, i)).ToDictionary(p => p.Id, p => p.i);
        var current = new MachineState(["Working", "Leaf"], new("event", plan.Next, plan.Steps.Select(w => new Step(w.Phase, w.State, w.Activity)).ToArray()),
            Progress: new(plan.Steps.Select(w => w.After.Select(id => indices[id]).ToArray()).ToArray(), new bool[plan.Steps.Length], []), Version: 3,
            Visits: visits, PlannedVisits: [new("Done", "literal-reader/state/3")], VisitSerial: 3, Selection: plan);
        Check(Json.Write(StateMachine.Restore(Json.Read<MachineState>(Json.Write(current)), chart)) == Json.Write(current with { Version = 4, LegacyScopes = true }), "v3 migration changed ownership"); Pass(2);
        void Refuse(MachineState bad, int i)
        {
            try { StateMachine.Restore(bad, chart); }
            catch (InvalidOperationException) { Pass(i); return; }
            throw new InvalidOperationException("N8: invalid identity checkpoint accepted: " + Names[i]);
        }
        Refuse(current with { Visits = null }, 3);
        Refuse(old with { Visits = visits }, 4);
        Refuse(current with { VisitSerial = 1 }, 5);
        Refuse(current with { Visits = [visits[0], visits[1] with { Occurrence = visits[0].Occurrence }] }, 6);
        Refuse(current with { Selection = plan with { Steps = plan.Steps.Select(w => w.Phase == "transition" ? w with { Visit = "wrong" } : w).ToArray() } }, 7);
        Refuse(current with { PlannedVisits = [new("Done", visits[0].Occurrence)] }, 8);
        Refuse(current with { PlannedVisits = visits }, 9);
        Refuse(current with { Selection = plan with { Steps = plan.Steps.Select(w => w with { Activity = "wrong" }).ToArray() } }, 10);
        Refuse(current with { Progress = current.Progress! with { Requires = plan.Steps.Select(_ => Array.Empty<int>()).ToArray() } }, 11);
    }
}
