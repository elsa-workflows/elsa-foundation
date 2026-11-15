using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static NativeInternalWitness;

internal static class NativeInternalRegionalWitness
{
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from nested in new[] { false, true }
        from phase in new[] { "exit", "action", "entry" } from cut in new[] { "steady", "lost-before", "lost-answer" }
        select $"native-internal-regional:{m},nested={nested},phase={phase},cut={cut}";
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var nested in new[] { false, true })
        foreach (var phase in new[] { "exit", "action", "entry" }) foreach (var cut in new[] { "steady", "lost-before", "lost-answer" }) Run(mode, nested, phase, cut);
    }
    private static void Run(string mode, bool nested, string phase, string cut)
    {
        ChartDependency.Calls.Clear(); ChartMark.Calls.Clear(); var h = new MachineInboxHarness();
        Dictionary<string, JsonNode?> D(int value) => new() { ["local"] = Values.From(value) };
        var definition = new StructuralDefinition([new("root", NodeKind.Root, Initial: "P"), new("P", NodeKind.Parallel, "root", Entry: "start"),
            new("E", NodeKind.Region, "P", "E0"), new("L", NodeKind.Region, "P", "L0"),
            new("E0", NodeKind.Atomic, "E", Exit: phase == "exit" ? "e-work" : null, Defaults: D(10)),
            new("L0", NodeKind.Atomic, "L", Exit: phase == "exit" ? "dependency" : null, Defaults: D(100)),
            new("EF", NodeKind.Final, "E", Entry: phase == "entry" ? "e-work" : null, Defaults: D(20)),
            new("LF", NodeKind.Final, "L", Entry: phase == "entry" ? "dependency" : null, Defaults: D(200)),
            new("Done", NodeKind.Final, "root")],
            [new("e", 0, "E0", "EF", VisitMode.Reenter, "", phase == "action" ? "e-work" : null, Kind: TriggerKind.Automatic),
             new("l", 1, "L0", "LF", VisitMode.Reenter, "", phase == "action" ? "dependency" : null, Kind: TriggerKind.Automatic),
             new("complete", 2, "P", "Done", VisitMode.Reenter, "", Kind: TriggerKind.Completion)]);
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(definition)),
            ("InputInbox", Binding.Literal(new MachineInboxSettings(3, 8, 120000))),
            ("InternalExecution", Binding.Literal(new InternalExecutionSettings(8, "internal"))))
            .With(Control(), Wf.Event("start", "regional-start"), Wf.Node<ChartDependency>("dependency", ("Local", Binding.Var("local"))),
                Wf.Sequence("e-work", Wf.Event("wait-e", "regional-e"), Wf.Node<ChartMark>("mark-e", ("Name", Binding.Literal("E")), ("Local", Binding.Var("local")))));
        var wf = Wf.Workflow("machine-inbox", nested ? Wf.Flowchart("flow", [machine, Wf.Set("returned", "returned", true)], [new("machine", "returned")]) : machine);
        wf.InterventionRequested = true; Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(mode);
        var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        MachineState Chart() => h.State.Frames[frame].State!.Deserialize<MachineState>()!;
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "later", "unknown")); h.Drain(); NativeGuardWitness.Release(h, "regional-start");
        var held = Chart(); var scopes = Json.Write(h.State.LexicalScopes);
        Check(held.Selection!.Selected.SequenceEqual(["e", "l"]) && held.Settling is { Steps: 1, PhasePending: true } &&
            held.Progress!.Running.Count == 2 && h.State.Frames.Values.Any(f => f.Node == "dependency" && f.Intervention is not null) &&
            h.State.Waits.Values.Any(w => w.Key == "event:regional-e") && ChartMark.Calls.Count == 0,
            "NR1: a regional fault blocked/cancelled independent same-phase work or split the compatible selection");
        h.Reload(); NativeGuardWitness.Release(h, "regional-e");
        var completed = Chart();
        Check(completed.Progress!.Running.Count == 1 && completed.Settling is { Steps: 1, PhasePending: true } &&
            Json.Write(h.State.LexicalScopes) == scopes && ChartMark.Calls.SequenceEqual(new[] { ("E", phase == "entry" ? 20 : 10) }) &&
            h.State.Inboxes![frame].Receipts["later"] is { Status: "Queued", EvaluationStarted: false },
            "NR2: successful sibling advanced the phase/final barrier or retired source scopes/evaluated later input");
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var source = h.State.Frames.Values.Single(f => f.Node == "dependency");
            var request = h.Send(NativeRetry.Kind, new NativeRetry("regional-repair-" + attempt, source.Id, source.Intervention!.Id, "repair required regional work"));
            if (attempt == 1 && cut != "steady") h.Cut(request, cut == "lost-answer"); else h.Drain(request);
            h.Reload();
            if (attempt == 1) Check(Json.Write(Chart().Progress!.Done) == Json.Write(completed.Progress.Done) &&
                Json.Write(Chart().Selection) == Json.Write(held.Selection) && Json.Write(Chart().Visits) == Json.Write(held.Visits) &&
                Json.Write(h.State.LexicalScopes) == scopes && ChartMark.Calls.Count == 1 && ChartDependency.Calls.Count == 2,
                "NR3: regional retry reset successful sibling work, selected set, visits or scopes");
        }
        Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished && h.State.LexicalScopes is null &&
            ChartMark.Calls.Count == 1 && ChartDependency.Calls.Count == 3 && ChartDependency.Calls.All(c => c.Local == (phase == "entry" ? 200 : 100)) &&
            ChartDependency.Calls.Select(c => c.Operation).Distinct().Count() == 1 && (!nested || h.State.Variables["returned"]?.ToString() == "true") &&
            h.State.Inboxes![frame].Receipts["later"] is { Status: "NotProcessed", Outcome: "completed", EvaluationStarted: false },
            "NR4: all-region completion/recovery repeated siblings or lost target/source values, normal return or queued terminal outcome");
        Console.WriteLine($"PASS native-internal-regional:{mode},nested={nested},phase={phase},cut={cut}");
    }
}
