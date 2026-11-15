using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static NativeInternalWitness;

internal static class NativeInternalPhaseWitness
{
    private static string[] Cuts => ["steady", "lost-before", "lost-answer", "cancel"];
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from nested in new[] { false, true }
        from kind in new[] { TriggerKind.Event, TriggerKind.Automatic, TriggerKind.Completion } from phase in new[] { "exit", "action", "entry" } from cut in Cuts
        select $"native-internal-phase:{m},nested={nested},kind={kind},phase={phase},cut={cut}";
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var nested in new[] { false, true })
        foreach (var kind in new[] { TriggerKind.Event, TriggerKind.Automatic, TriggerKind.Completion })
        foreach (var phase in new[] { "exit", "action", "entry" }) foreach (var cut in Cuts) Run(mode, nested, kind, phase, cut);
    }
    private static void Run(string mode, bool nested, TriggerKind kind, string phase, string cut)
    {
        ChartMark.Calls.Clear(); ChartDependency.Calls.Clear(); var h = new MachineInboxHarness();
        Dictionary<string, JsonNode?> D(int value) => new() { ["local"] = Values.From(value) };
        var nodes = new List<GraphNode> { new("root", NodeKind.Root, Initial: "A"),
            new("Done", NodeKind.Final, "root", Entry: phase == "entry" ? "dependency" : "entry", Defaults: D(99)) };
        if (kind == TriggerKind.Completion) nodes.AddRange([new("A", NodeKind.Compound, "root", "F", Exit: phase == "exit" ? "dependency" : "exit", Defaults: D(17)),
            new("F", NodeKind.Final, "A", Entry: "gate")]);
        else nodes.Add(new("A", NodeKind.Atomic, "root", Entry: "gate", Exit: phase == "exit" ? "dependency" : "exit", Defaults: D(17)));
        var definition = new StructuralDefinition(nodes.ToArray(), [new("go", 0, "A", "Done", VisitMode.Reenter,
            kind == TriggerKind.Event ? "go" : "", phase == "action" ? "dependency" : "action", Kind: kind)]);
        Node Mark(string id) => Wf.Node<ChartMark>(id, ("Name", Binding.Literal(id)), ("Local", Binding.Var("local")));
        var work = new List<Node> { Control(), Wf.Event("gate", "phase-start"), Wf.Node<ChartDependency>("dependency", ("Local", Binding.Var("local"))) };
        work.AddRange(new[] { "exit", "action", "entry" }.Where(id => id != phase).Select(Mark));
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(definition)),
            ("InputInbox", Binding.Literal(new MachineInboxSettings(3, 8, 120000))),
            ("InternalExecution", Binding.Literal(new InternalExecutionSettings(8, "internal")))).With(work.ToArray());
        var wf = Wf.Workflow("machine-inbox", nested ? Wf.Flowchart("flow", [machine, Wf.Set("returned", "returned", true)], [new("machine", "returned")]) : machine);
        wf.InterventionRequested = true; Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(mode);
        var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        MachineState Chart() => h.State.Frames[frame].State!.Deserialize<MachineState>()!;
        if (kind == TriggerKind.Event) { h.Send(InvocationInput.Kind, new InvocationInput(frame, "go", "go")); h.Drain(); }
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "tail", "unknown")); h.Drain();
        NativeGuardWitness.Release(h, "phase-start");
        var held = Chart(); var scopes = Json.Write(h.State.LexicalScopes); var completed = ChartMark.Calls.ToArray();
        Check(held.Pending is not null && held.Selection!.Selected.SequenceEqual(["go"]) && held.Settling!.PhasePending == (kind != TriggerKind.Event) &&
            ChartDependency.Calls.Count == 1 && h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Running &&
            h.State.Frames.Values.Single(f => f.Node == "dependency").Intervention is not null &&
            h.State.Inboxes![frame].Receipts["tail"] is { Status: "Queued", EvaluationStarted: false },
            "NP1: internal phase failure lost its selected plan/gate or advanced later inputs/final completion");
        if (kind == TriggerKind.Event) Check(h.State.Inboxes![frame].Receipts["go"] is { Status: "Selected", EvaluationStarted: true },
            "NP1: failed business phase lost its selected input reservation");
        h.Reload();
        if (cut == "cancel")
        {
            h.Apply(new("add", M: new("cancel", h.Target, Kinds.Terminate, TargetCreation: 1))); h.Drain();
            Check(h.State.Inboxes![frame].Receipts["tail"] is { Status: "NotProcessed", Outcome: "cancelled", EvaluationStarted: false } &&
                (kind != TriggerKind.Event || h.State.Inboxes[frame].Receipts["go"] is { Status: "Interrupted", Outcome: "cancelled", EvaluationStarted: true }) &&
                ChartDependency.Calls.Count == 1 && ChartMark.Calls.SequenceEqual(completed), "NP2: failed phase cancellation replayed completed work or lost terminal dispositions");
        }
        else
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var source = h.State.Frames.Values.Single(f => f.Node == "dependency");
                var request = h.Send(NativeRetry.Kind, new NativeRetry("phase-repair-" + attempt, source.Id, source.Intervention!.Id, "ordinary phase recovery"));
                if (attempt == 1 && cut.StartsWith("lost-", StringComparison.Ordinal)) h.Cut(request, cut == "lost-answer");
                else h.Drain(request);
                h.Reload();
                if (attempt == 1) Check(Chart().Pending is not null && Json.Write(Chart().Selection) == Json.Write(held.Selection) &&
                    Json.Write(Chart().Visits) == Json.Write(held.Visits) && Json.Write(h.State.LexicalScopes) == scopes &&
                    ChartMark.Calls.SequenceEqual(completed) && ChartDependency.Calls.Count == 2,
                    "NP3: still-failing retry reselected work, reset visits/scopes or reran successful phases");
            }
            Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished && h.State.LexicalScopes is null && ChartDependency.Calls.Count == 3 &&
                ChartDependency.Calls.All(c => c.Local == (phase == "entry" ? 99 : 17)) && ChartDependency.Calls.Select(c => c.Operation).Distinct().Count() == 1 &&
                ChartMark.Calls.GroupBy(c => c.Name).All(g => g.Count() == 1) && ChartMark.Calls.All(c => c.Local == (c.Name == "entry" ? 99 : 17)) &&
                ChartMark.Calls.Count == 2 && (!nested || h.State.Variables["returned"]?.ToString() == "true") &&
                h.State.Inboxes![frame].Receipts["tail"] is { Status: "NotProcessed", Outcome: "completed", EvaluationStarted: false },
                "NP4: internal/final-entry recovery repeated earlier work or lost normal values, completion, FIFO or return");
            if (kind == TriggerKind.Event) Check(h.State.Inboxes![frame].Receipts["go"] is { Status: "Completed", Outcome: "handled", EvaluationStarted: true },
                "NP4: completed phase recovery did not finalize its selected business input");
        }
        Console.WriteLine($"PASS native-internal-phase:{mode},nested={nested},kind={kind},phase={phase},cut={cut}");
    }
}
