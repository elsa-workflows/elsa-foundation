using System.Text.Json;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static NativeInternalWitness;

internal static class NativeLoopWitness
{
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable }
        from k in new[] { TriggerKind.Automatic, TriggerKind.Completion } from work in new[] { false, true } from repair in new[] { false, true }
        select $"native-loop:{m},kind={k},work={work},parallel-repair={repair}";
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var kind in new[] { TriggerKind.Automatic, TriggerKind.Completion })
        foreach (var work in new[] { false, true }) foreach (var repair in new[] { false, true }) Run(mode, kind, work, repair);
    }
    private static void Run(string mode, TriggerKind kind, bool work, bool repair)
    {
        ChartMark.Calls.Clear(); var h = new MachineInboxHarness();
        var nodes = new List<GraphNode> { new("root", NodeKind.Root, Initial: "A"), new("Done", NodeKind.Final, "root") };
        if (kind == TriggerKind.Completion) nodes.AddRange([new("A", NodeKind.Compound, "root", "F"), new("F", NodeKind.Final, "A")]);
        else nodes.Add(new("A", NodeKind.Atomic, "root"));
        var definition = new StructuralDefinition(nodes.ToArray(),
            [new("cycle", 0, "A", kind == TriggerKind.Automatic ? null : "A", kind == TriggerKind.Automatic ? VisitMode.Internal : VisitMode.Reenter,
                "", work ? "action" : null, Kind: kind), new("finish", 1, "A", "Done", VisitMode.Reenter, "finish")]);
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(definition)),
            ("InputInbox", Binding.Literal(new MachineInboxSettings(2, 8, 180000))),
            ("InternalExecution", Binding.Literal(new InternalExecutionSettings(4, "internal", new() { ["cycle"] = "enabled" }))))
            .With(Control(), Wf.Node<ChartMark>("action", ("Name", Binding.Literal("cycle")), ("Local", Binding.Literal(17))));
        var root = repair ? Wf.Parallel("parallel", null,
            Wf.Flowchart("flow", [machine, Wf.Set("returned", "returned", true)], [new("machine", "returned")]),
            Wf.Sequence("repair", Wf.Event("repair-wait", "stop-cycle"), Wf.Set("disable", "enabled", false))) : machine;
        var wf = Wf.Workflow("machine-inbox", root, ("enabled", true)); wf.InterventionRequested = true;
        Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(mode);
        var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        MachineState Chart() => h.State.Frames[frame].State!.Deserialize<MachineState>()!;
        var blocked = Chart(); var scopes = Json.Write(h.State.LexicalScopes);
        Check(blocked.Control?.Spec.Purpose == ControlPurpose.Loop && blocked.Settling is { Steps: 4, Limit: 4, Block: not null, PhasePending: false } &&
            h.State.Frames.Values.Single(f => f.Node == "internal").Intervention is { Fault.Code: "statechart.loop" } && ChartMark.Calls.Count == (work ? 4 : 0),
            "NL1: automatic/completion cycle evaded the semantic budget or ordinary continuing incident");
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "finish", "finish")); h.Drain();
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "tail", "unknown")); h.Drain();
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "full", "finish")); h.Drain();
        Check(h.Replies[^1].Result == "Full" && h.State.Inboxes![frame].Pending.Select(i => i.Id).SequenceEqual(["finish", "tail"]),
            "NL2: blocked cycle lost bounded FIFO admission");
        h.Send(ReceiptQuery.Kind, new ReceiptQuery(frame, "finish")); h.Drain();
        Check(h.Replies[^1] is { Result: "Found", Status: "Queued", EvaluationStarted: false, FinalizedAt: null }, "NL2: loop incident evaluated or finalized an accepted input");
        var source = h.State.Frames.Values.Single(f => f.Node == "internal");
        h.Send(NativeRetry.Kind, new NativeRetry("forged", source.Id, "wrong-incident", "not a recovery acknowledgement")); h.Drain();
        Check(h.World.Deliveries.Select(d => d.Reply.Item).OfType<NativeRetryResult>().Last().Result == "NotEligible", "NL3: forged incident released a loop");
        h.Send(CompositeSignal.Kind, new CompositeSignal(frame, "finish"), false); h.Drain();
        for (var i = 0; i < 2; i++)
        {
            h.Send(NativeKinds.Continue, new { }, false); h.Drain(); h.Reload();
            Check(Json.Write(Chart()) == Json.Write(blocked) && Json.Write(h.State.LexicalScopes) == scopes && ChartMark.Calls.Count == (work ? 4 : 0),
                "NL3: raw input/Continue/cold restart reset or advanced a sticky loop block");
        }
        if (!repair)
        {
            h.Apply(new("add", M: new("cancel", h.Target, Kinds.Terminate, TargetCreation: 1))); h.Drain();
            Check(h.State.Inboxes![frame].Pending.Count == 0 && h.State.Inboxes[frame].Receipts.Values.All(r => r is { Status: "NotProcessed", Outcome: "cancelled", EvaluationStarted: false }),
                "NL4: cancelling a loop incident lost final queued dispositions");
        }
        else
        {
            var request = new NativeRetry("reset", source.Id, source.Intervention!.Id, "explicit loop budget reset");
            var transport = h.Send(NativeRetry.Kind, request); h.Drain(transport);
            var reset = Chart();
            Check(reset.Control is { ReboundFrom: not null } && Json.Write(reset.Settling) == Json.Write(blocked.Settling) && Json.Write(reset.Visits) == Json.Write(blocked.Visits) &&
                ChartMark.Calls.Count == (work ? 4 : 0), "NL5: retry preparation reset progress before verified helper acknowledgement");
            h.Reload();
            var again = Chart();
            Check(again.Settling is { Steps: 4, Block: not null } && again.Control?.Original != blocked.Control!.Original &&
                ChartMark.Calls.Count == (work ? 8 : 0) &&
                (kind != TriggerKind.Completion || again.VisitSerial > blocked.VisitSerial && again.Settling.Serial > blocked.Settling!.Serial),
                "NL6: explicit recovery lost committed progress/completion identity or failed to require a new incident for the surviving loop");
            h.Send(NativeRetry.Kind, request); h.Drain();
            Check(h.World.Deliveries.Select(d => d.Reply.Item).OfType<NativeRetryResult>().Last().Result == "Duplicate" && Json.Write(Chart()) == Json.Write(again),
                "NL6: duplicate recovery automatically reset the new incident");
            h.Send(NativeRetry.Kind, request with { Reason = "changed" }); h.Drain();
            Check(h.World.Deliveries.Select(d => d.Reply.Item).OfType<NativeRetryResult>().Last().Result == "Conflict", "NL6: conflicting recovery ID was accepted");
            NativeGuardWitness.Release(h, "stop-cycle");
            Check(Json.Write(Chart()) == Json.Write(again), "NL7: ordinary value repair bypassed explicit loop acknowledgement");
            NativeGuardWitness.Retry(h, "stop-repaired"); h.Reload();
            Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished && h.State.LexicalScopes is null &&
                h.State.Variables["returned"]?.ToString() == "true" && ChartMark.Calls.Count == (work ? 8 : 0) &&
                h.State.Inboxes![frame].Receipts["finish"] is { Status: "Completed", Outcome: "handled", EvaluationStarted: true } &&
                h.State.Inboxes[frame].Receipts["tail"] is { Status: "NotProcessed", Outcome: "completed", EvaluationStarted: false },
                "NL8: repaired loop lost FIFO/return or replayed completed actions");
        }
        Kernel.Sim.Native.NativeWorkflows.Registry.Remove(wf.Id); h.Send(ReceiptQuery.Kind, new ReceiptQuery(frame, "tail")); h.Drain();
        Check(h.Replies[^1] is { Result: "Found", Status: "NotProcessed", EvaluationStarted: false, FinalizedAt: not null, ValidUntil: not null },
            "NL9: registry-free loop terminal query lost retained outcomes/clocks");
        Console.WriteLine($"PASS native-loop:{mode},kind={kind},work={work},parallel-repair={repair}");
    }
}
