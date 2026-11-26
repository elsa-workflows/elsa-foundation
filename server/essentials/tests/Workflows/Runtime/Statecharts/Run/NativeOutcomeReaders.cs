using System.Text.Json;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static NativeInternalWitness;

// Malformed copies and version relabellings are constructed readers, not historical writer/binary runs.
internal static class NativeOutcomeReaders
{
    private static string[] Cases => ["old-map", "missing", "unknown", "nested", "empty-outcome", "null-map", "empty-map", "missing-key", "future", "roundtrip", "old-wire", "native12-upgrade"];
    private static string[] Startup => ["missing-key", "unknown", "nested", "empty-outcome", "null-map", "no-internal", "nonliteral"];
    internal static IEnumerable<string> ReaderNames => from m in new[] { Modes.Balanced, Modes.Durable } from c in Cases select $"native-outcome-reader:{m},{c}";
    internal static IEnumerable<string> RefusalNames => from m in new[] { Modes.Balanced, Modes.Durable } from c in Startup select $"native-outcome-refusal:{m},{c}";
    internal static IEnumerable<string> TamperNames => from m in new[] { Modes.Balanced, Modes.Durable } select "native-outcome-tamper-refusal:" + m;
    private static FinalOutcomeSettings Alter(string name)
    {
        var map = NativeOutcomeWitness.Mapping().Outcomes;
        switch (name)
        {
            case "unknown": map["unknown"] = "Other"; break;
            case "nested": map["inner-final"] = "Other"; break;
            case "empty-outcome": map[NativeOutcomeWitness.Finals[0]] = ""; break;
            case "null-map": return new(null!);
            case "empty-map": map.Clear(); break;
            case "missing-key": map.Remove(NativeOutcomeWitness.Finals[0]); break;
        }
        return new(map);
    }
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        {
            Readers(mode);
            foreach (var name in Startup) Refuse(mode, name);
            Tamper(mode);
        }
    }
    private static void Readers(string mode)
    {
        ChartMark.Calls.Clear(); var h = new MachineInboxHarness(); var machine = NativeOutcomeWitness.Machine();
        var wf = Wf.Workflow("machine-inbox", machine); wf.InterventionRequested = true; using var registration = Register(wf); h.Start(mode);
        var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        NativeGuardWitness.Release(h, "initial-entry");
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "go", NativeOutcomeWitness.Finals[0])); h.Drain();
        var host = h.State; var state = host.Frames[frame].State!.Deserialize<MachineState>()!;
        var d = NativeOutcomeWitness.Definition(); var graph = new SelectionChart(d.Nodes, d.Rules);
        foreach (var name in Cases)
        {
            var bad = state;
            switch (name)
            {
                case "roundtrip":
                    Check(Json.Write(StateMachine.Restore(Json.Read<MachineState>(Json.Write(state)), graph, frame)) == Json.Write(state), "NOCR1: mapped checkpoint roundtrip changed frozen work"); break;
                case "old-wire":
                    var old = state with { Version = 6, FinalOutcomes = null };
                    Check(!Json.Write(old).Contains("\"FinalOutcomes\"", StringComparison.Ordinal) &&
                        Json.Write(StateMachine.Restore(Json.Read<MachineState>(Json.Write(old)), graph, frame)) == Json.Write(old), "NOCR2: old wire acquired mapping data"); break;
                case "native12-upgrade":
                    host.Frames[frame].State = JsonSerializer.SerializeToNode(state with { Version = 6, FinalOutcomes = null });
                    Check(host.CheckpointControl && host.Inboxes![frame].Receipts["go"].EvaluationStarted &&
                        Json.Write(new NativeModel(new()).UpgradeState(Json.Write(host), 12)) == Json.Write(host), "NOCR3: native12 upgrade lost its evaluation/phase checkpoint"); break;
                default:
                    bad = name switch { "old-map" => state with { Version = 6 }, "missing" => state with { FinalOutcomes = null },
                        "future" => state with { Version = 8 }, _ => state with { FinalOutcomes = Alter(name) } };
                    var refused = false;
                    try { StateMachine.Restore(bad, graph, frame); }
                    catch (NotSupportedException) when (name == "future") { refused = true; }
                    catch (InvalidOperationException e) when (name != "future" && e.Message is "Invalid statechart final-outcome mapping." or "Mixed statechart final-outcome checkpoint.") { refused = true; }
                    Check(refused, "NOCR4: accepted invalid outcome checkpoint: " + name); break;
            }
            Console.WriteLine($"PASS native-outcome-reader:{mode},{name}");
        }
    }
    private static void Refuse(string mode, string name)
    {
        ChartMark.Calls.Clear(); var h = new MachineInboxHarness(); var machine = NativeOutcomeWitness.Machine();
        var d = NativeOutcomeWitness.Definition(); machine.Inputs["StructuralDefinition"] = Binding.Literal(d with
        { Nodes = d.Nodes.Select(n => n.Id == "inner-final" ? n with { Entry = "action" } : n).ToArray() });
        if (name == "no-internal") machine.Inputs.Remove("InternalExecution");
        else machine.Inputs["FinalOutcomes"] = name == "nonliteral" ? Binding.Var("map") : Binding.Literal(Alter(name));
        var wf = Wf.Workflow("machine-inbox", machine, ("map", NativeOutcomeWitness.Mapping())); wf.InterventionRequested = true; using var registration = Register(wf);
        var expected = name == "no-internal" ? "Statechart final outcomes require the qualified internal profile."
            : name == "nonliteral" ? "Statechart final outcomes must be literal for this prototype." : "Invalid statechart final-outcome mapping.";
        var refused = false;
        try { h.Start(mode); }
        catch (InvalidOperationException e) when (e.Message.StartsWith("Kernel: I14 ", StringComparison.Ordinal) && e.Message.Contains(expected, StringComparison.Ordinal)) { refused = true; }
        Check(refused && ChartMark.Calls.Count == 0 && h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Faulted,
            "NOCR5: fresh outcome profile missed its precise refusal or ran Entry: " + name);
        Console.WriteLine($"PASS native-outcome-refusal:{mode},{name}");
    }
    private static void Tamper(string mode)
    {
        ChartMark.Calls.Clear(); var h = new MachineInboxHarness(); var machine = NativeOutcomeWitness.Machine();
        var wf = Wf.Workflow("machine-inbox", machine); wf.InterventionRequested = true; using var registration = Register(wf); h.Start(mode);
        var changed = NativeOutcomeWitness.Mapping(); changed.Outcomes[NativeOutcomeWitness.Finals[0]] = "Different";
        machine.Inputs["FinalOutcomes"] = Binding.Literal(changed); h.Reload();
        var refused = false;
        try { NativeGuardWitness.Release(h, "initial-entry"); }
        catch (InvalidOperationException e) when (e.Message.StartsWith("Kernel: I14 ", StringComparison.Ordinal) &&
            e.Message.Contains("Suspended recovery material is unavailable.", StringComparison.Ordinal)) { refused = true; }
        Check(refused && ChartMark.Calls.Count == 0 && h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Faulted,
            "NOCR6: changed authored definition bypassed normal pinned recovery material");
        Console.WriteLine("PASS native-outcome-tamper-refusal:" + mode);
    }

}
