using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static MachineInboxHarness;

internal static class MachineInboxWitness
{
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var nested in new[] { false, true })
        foreach (var phase in new[] { "entry", "exit", "action" }) foreach (var cut in new[] { "steady", "lost-before", "lost-answer", "cancel" })
            Run(mode, nested, phase, cut);
    }
    private static void Run(string mode, bool nested, string phase, string cut)
    {
        ChartDependency.Calls.Clear(); ChartMark.Calls.Clear(); var h = new MachineInboxHarness();
        var def = new StructuralDefinition([new("root", NodeKind.Root, Initial: "A"),
            new("A", NodeKind.Atomic, "root", Entry: phase == "entry" ? "dependency" : "entry", Exit: phase == "exit" ? "dependency" : "exit",
                Defaults: new Dictionary<string, JsonNode?> { ["local"] = Values.From(17) }),
            new("B", NodeKind.Atomic, "root", Entry: "next", Defaults: new Dictionary<string, JsonNode?> { ["local"] = Values.From(99) }),
            new("C", NodeKind.Final, "root")],
            [new("go", 0, "A", "B", VisitMode.Reenter, "go", phase == "action" ? "dependency" : "action"),
             new("stop", 0, "B", "C", VisitMode.Reenter, "stop")]);
        Node Mark(string id) => Wf.Node<ChartMark>(id, ("Name", Binding.Literal(id)), ("Local", Binding.Var("local")));
        var children = new List<Node> { Wf.Node<ChartDependency>("dependency", ("Local", Binding.Var("local"))), Mark("next") };
        children.AddRange(new[] { "entry", "exit", "action" }.Where(n => n != phase).Select(Mark));
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(def)),
            ("InputInbox", Binding.Literal(new { Capacity = 4, ReceiptCapacity = 12, RetentionMilliseconds = 120000L }))).With(children.ToArray());
        var wf = Wf.Workflow("machine-inbox", nested ? Wf.Flowchart("flow", [machine, Wf.Set("after", "returned", true)], [new("machine", "after")]) : machine);
        wf.InterventionRequested = true; Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(mode);
        var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        Check(h.State.Inboxes?.ContainsKey(frame) == true, "Q1: actual machine did not register its bounded inbox");
        InvocationInbox Inbox() => h.State.Inboxes![frame];
        InvocationInput Input(string id, string name, string? value = null) => new(frame, id, name, value);
        var go = h.Send(InvocationInput.Kind, Input("go", "go"));
        if (cut.StartsWith("lost-", StringComparison.Ordinal)) h.Cut(go, cut == "lost-answer"); else h.Drain();
        var pending = Json.Write(h.State.Frames[frame].State); var resources = Json.Write(h.State.LexicalScopes);
        Check(ChartDependency.Calls.Count == 1 && h.State.Frames.Values.Any(f => f.Intervention is not null), "Q2: queue fixture did not reach an actual phase fault");
        Check(Inbox().Receipts["go"].Status == (phase == "entry" ? "Queued" : "Selected"), "Q2: selected receipt did not follow the pending reaction");
        // Remove authored material only across a metadata commit, stopping before its ordinary Continue wake.
        Kernel.Sim.Native.NativeWorkflows.Registry.Remove(wf.Id);
        var unknown = h.Send(InvocationInput.Kind, Input("unknown", "unknown")); h.Drain(unknown);
        Check(h.Replies[^1].Result == "Accepted" && Json.Write(h.State.Frames[frame].State) == pending && Json.Write(h.State.LexicalScopes) == resources,
            "Q3: blocked admission hydrated material or advanced chart work");
        Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Drain();
        h.Send(InvocationInput.Kind, Input("stop", "stop")); h.Drain();
        h.Send(InvocationInput.Kind, Input("tail", "go")); h.Drain();
        h.Send(InvocationInput.Kind, Input("overflow", "go")); h.Drain();
        Check(h.Replies[^1].Result == "Full" && Inbox().Pending.Select(i => i.Id).SequenceEqual(["go", "unknown", "stop", "tail"]), "Q4: blocked capacity evicted or reordered inputs");
        h.Send(InvocationInput.Kind, Input("go", "go")); h.Drain(); Check(h.Replies[^1].Result == "Duplicate" && h.Replies[^1].Order == 1, "Q5: accepted retry lost its original receipt/order");
        h.Send(InvocationInput.Kind, Input("go", "go", "different")); h.Drain(); Check(h.Replies[^1].Result == "Conflict", "Q5: admitted ID conflict was accepted");
        var raw = h.Send(CompositeSignal.Kind, new CompositeSignal(frame, "go"), false); h.Drain();
        Check(h.History.Contains($"signal-rejected:{raw}:{frame}:admitted-input-required") && Json.Write(h.State.Frames[frame].State) == pending && ChartDependency.Calls.Count == 1,
            "Q6: raw signal competed with admitted input or blocked rejection was not observable");
        if (cut == "cancel")
        {
            h.Apply(new("add", M: new("cancel", h.Target, Kinds.Terminate, TargetCreation: 1))); h.Drain();
            Check(Inbox().Closed == "cancelled" && Inbox().Pending.Count == 0 && Inbox().Receipts.Values.All(r => r.Outcome == "cancelled") &&
                Inbox().Receipts["go"].Status == (phase == "entry" ? "NotProcessed" : "Interrupted"), "Q7: cancellation lost queued/selected dispositions");
        }
        else
        {
            h.Reload();
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var source = h.State.Frames.Values.Single(f => f.Node == "dependency");
                h.Send(NativeRetry.Kind, new NativeRetry("repair-" + attempt, source.Id, source.Intervention!.Id, "inbox phase repair")); h.Drain();
                if (attempt == 1) Check(Json.Write(h.State.Frames[frame].State).Contains("SelectedInput", StringComparison.Ordinal) &&
                    Inbox().Receipts["go"].Status == (phase == "entry" ? "Queued" : "Selected"), "Q8: retry settled the admitted event before its phase succeeded");
                h.Reload();
            }
            Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished && (!nested || h.State.Variables["returned"]?.ToString() == "true") &&
                Inbox().Pending.Count == 0 && Inbox().Receipts["go"] is { Status: "Completed", Outcome: "handled" } &&
                Inbox().Receipts["unknown"] is { Status: "Completed", Outcome: "unhandled" } && Inbox().Receipts["stop"].Outcome == "handled" &&
                Inbox().Receipts["tail"] is { Status: "NotProcessed", Outcome: "completed" } && ChartDependency.Calls.Count == 3 &&
                ChartMark.Calls.All(c => c.Local == (c.Name == "next" ? 99 : 17)) && ChartMark.Calls.GroupBy(c => c.Name).All(g => g.Count() == 1),
                "Q10: recovery/FIFO replayed completed phases, lost values or terminal receipts");
            var evaluations = h.History.Where(s => s.StartsWith("statechart:", StringComparison.Ordinal)).Select(s => Json.Read<string?[]>(s[11..]))
                .Where(s => s[2] == "evaluated").Select(s => s[1]);
            Check(evaluations.SequenceEqual(["go", "unknown", "stop"]), "Q11: selected input was reevaluated or FIFO changed after recovery");
        }
        Kernel.Sim.Native.NativeWorkflows.Registry.Remove(wf.Id);
        h.Send(ReceiptQuery.Kind, new ReceiptQuery(frame, "go")); h.Drain();
        Check(h.Replies[^1] is { Result: "Found", ValidUntil: not null, FinalizedAt: not null }, "Q12: terminal query required authored material or lost retention");
        Console.WriteLine($"PASS machine-inbox:{mode},nested={nested},phase={phase},cut={cut}");
    }
}
