using System.Text.Json;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static NativeInternalWitness;

internal static class NativeCheckpointWitness
{
    private static string[] Cuts => ["steady", "lost-before", "lost-answer", "cancel"];
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from nested in new[] { false, true }
        from stage in new[] { "evaluation-true", "evaluation-false", "evaluation-fault", "selection" } from cut in Cuts
        select $"native-internal-checkpoint:{m},nested={nested},stage={stage},cut={cut}";
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var nested in new[] { false, true })
        foreach (var stage in new[] { "evaluation-true", "evaluation-false", "evaluation-fault", "selection" }) foreach (var cut in Cuts) Run(mode, nested, stage, cut);
    }
    private static void Run(string mode, bool nested, string stage, string cut)
    {
        ChartMark.Calls.Clear(); var h = new MachineInboxHarness();
        var definition = new StructuralDefinition([new("root", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "root"), new("Done", NodeKind.Final, "root")],
            [new("go", 0, "A", "Done", VisitMode.Reenter, "go", "action")]);
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(definition)),
            ("InputInbox", Binding.Literal(new MachineInboxSettings(3, 8, 120000))),
            ("InternalExecution", Binding.Literal(new InternalExecutionSettings(8, "internal", new() { ["go"] = "permission" }))))
            .With(Control(), Wf.Node<ChartMark>("action", ("Name", Binding.Literal("action")), ("Local", Binding.Literal(17))));
        var wf = Wf.Workflow("machine-inbox", nested ? Wf.Flowchart("flow", [machine, Wf.Set("returned", "returned", true)], [new("machine", "returned")]) : machine,
            ("permission", stage == "evaluation-fault" ? "wrong-type" : (object)(stage != "evaluation-false")));
        wf.InterventionRequested = true; Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(mode);
        var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        MachineState Chart() => h.State.Frames[frame].State!.Deserialize<MachineState>()!;
        var visits = Json.Write(Chart().Visits); var scopes = Json.Write(h.State.LexicalScopes);
        var request = h.Send(InvocationInput.Kind, new InvocationInput(frame, "go", "go")); h.Drain(request);
        Check(h.State.Inboxes![frame].Receipts["go"] is { Status: "Queued", EvaluationStarted: false }, "NC1: staging did not start after a distinct committed admission");
        PrepareTurn(h);
        Check(h.State.Inboxes![frame].Receipts["go"].Status == "Queued" && ChartMark.Calls.Count == 0 && new NativeOptions().MaxActivitiesPerTurn == 1000,
            "NC2: staging ran authored work before its physical checkpoint with the normal large activity budget");
        if (stage == "selection")
        {
            h.Apply(new("flush", h.Actor.Id));
            EvaluationRow();
            PrepareTurn(h);
            Check(h.State.Inboxes![frame].Receipts["go"].Status == "Evaluating" && ChartMark.Calls.Count == 0,
                "NC3: selected work ran before its selected-plan commit");
        }
        var prior = h.World.Store.Instances[h.Target.Id].State;
        if (cut is "lost-before" or "lost-answer")
        {
            h.World.Auto = false; var actor = h.Actor; var call = actor.Calls.Count;
            h.Apply(new("flush", actor.Id));
            Check(actor.Calls.Count == call + 1 && actor.Calls[call].Kind == "commit", "NC4: cut missed its physical commit request");
            h.Apply(new(cut == "lost-answer" ? "exec" : "lose", actor.Id, call));
            if (cut == "lost-answer") CommittedRow();
            else Check(h.World.Store.Instances[h.Target.Id].State == prior, "NC4: before-apply loss changed the authoritative checkpoint");
            Check(ChartMark.Calls.Count == 0 && !h.State.Frames.Values.Any(f => f.Intervention is not null), "NC4: unresolved commit executed a guard/helper/phase child");
            h.Apply(new("answer", actor.Id, call, Flag: true));
            Check(ChartMark.Calls.Count == 0, "NC4: lost acknowledgement executed selected work before resolution");
            h.World.Auto = true; h.Reload();
        }
        else
        {
            h.Apply(new("flush", h.Actor.Id)); CommittedRow();
            if (cut == "cancel")
            {
                // Deliver the command to the owning worker before handing over the owed Continue.
                // A store-only add is not yet an engine delivery and can be observed after that continuation.
                h.Apply(new("post", h.Actor.Id, M: new("cancel", h.Target, NativeKinds.Cancel, TargetCreation: 1)));
                Check(h.Actor.Worker.Messages.Any(m => m.Id == "cancel"), "NC5: cancellation was not delivered to the owning worker");
                h.Drain();
                Check(h.State.Inboxes![frame].Receipts["go"] is { Outcome: "cancelled", EvaluationStarted: true } cancelled &&
                    cancelled.Status == (stage == "selection" ? "Interrupted" : "NotProcessed") && h.State.Inboxes[frame].Pending.Count == 0 && ChartMark.Calls.Count == 0,
                    "NC5: cancellation at a physical boundary lost the evaluation/selection distinction or ran a pending child; receipt=" +
                    Json.Write(h.State.Inboxes[frame].Receipts["go"]) + "; calls=" + Json.Write(ChartMark.Calls.Select(c => c.Name).ToArray()));
                Kernel.Sim.Native.NativeWorkflows.Registry.Remove(wf.Id); h.Send(ReceiptQuery.Kind, new ReceiptQuery(frame, "go")); h.Drain();
                Check(h.Replies[^1] is { Result: "Found", EvaluationStarted: true, FinalizedAt: not null, ValidUntil: not null },
                    "NC5: registry-free terminal query lost the physical evaluation marker or clocks");
                Passed(); return;
            }
            h.Reload();
        }
        var receipt = h.State.Inboxes![frame].Receipts["go"];
        if (stage == "evaluation-fault")
            Check(receipt is { Status: "Evaluating", EvaluationStarted: true } && Chart().Evaluating == "go" &&
                h.State.Frames.Values.Single(f => f.Node == "internal").Intervention is { Fault.Code: "statechart.guard" } &&
                ChartMark.Calls.Count == 0 && Json.Write(Chart().Visits) == visits && Json.Write(h.State.LexicalScopes) == scopes,
                "NC6: physical uncertainty lost the retained faulting evaluation/configuration");
        else if (stage == "evaluation-false")
            Check(receipt is { Status: "Completed", Outcome: "unhandled", EvaluationStarted: true } && Chart().Settling is { Stable: true } &&
                ChartMark.Calls.Count == 0 && Json.Write(Chart().Visits) == visits, "NC6: false guard was selected or failed to settle after physical uncertainty");
        else Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished && ChartMark.Calls.SequenceEqual(new[] { ("action", 17) }) &&
                receipt is { Status: "Completed", Outcome: "handled", EvaluationStarted: true } && (!nested || h.State.Variables["returned"]?.ToString() == "true"),
                "NC6: physically staged selected work was lost/replayed or failed ordinary return");
        Check(h.History.Count(s => s.StartsWith("statechart:", StringComparison.Ordinal) && Json.Read<string?[]>(s[11..]) is var p && p[1] == "go" && p[2] == "evaluated") == 1,
            "NC7: commit uncertainty duplicated the committed semantic evaluation");
        Passed();

        void EvaluationRow() => Check(h.State.Inboxes![frame].Receipts["go"] is { Status: "Evaluating", EvaluationStarted: true } &&
            Chart() is { Evaluating: "go", Pending: null, Control.Spec.Purpose: ControlPurpose.Continue, Settling: { Steps: 0, Stable: false } } state &&
            h.State.Agenda.Any(w => w.Frame == state.Control.Current && w.Node == "internal") &&
            Json.Write(state.Visits) == visits && Json.Write(h.State.LexicalScopes) == scopes && ChartMark.Calls.Count == 0 &&
            !h.State.Frames.Values.Any(f => f.Intervention is not null), "NC8: pre-guard checkpoint lacks its exact evaluation/configuration/owed continuation");
        void CommittedRow()
        {
            if (stage != "selection") { EvaluationRow(); return; }
            Check(h.State.Inboxes![frame].Receipts["go"] is { Status: "Selected", EvaluationStarted: true } &&
                Chart() is { Evaluating: null, Pending.EventId: "go", Selection.Selected: ["go"], Progress: not null } selected &&
                selected.Progress.Running.Keys.All(id => h.State.Agenda.Any(w => w.Frame == id && w.Node == "action")) &&
                selected.Progress.Running.Count == 1 && ChartMark.Calls.Count == 0, "NC9: selected-plan checkpoint lacks its exact owed ordinary phase work");
        }
        void Passed() => Console.WriteLine($"PASS native-internal-checkpoint:{mode},nested={nested},stage={stage},cut={cut}");
    }

    internal static void PrepareTurn(MachineInboxHarness h)
    {
        for (var i = 0; i < 30; i++)
        {
            var actor = h.Actor;
            if (actor.Worker.NeedsMeet) h.Apply(new("meet", actor.Id));
            h.Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) h.Apply(new("load", actor.Id));
            if (actor.Worker.Ready.Contains(h.Target.Id))
            {
                h.Apply(new("turn", actor.Id, S: h.Target.Id));
                Check(actor.Worker.Queued > 0, "NC0: actual chart turn did not owe a normal checkpoint"); return;
            }
            if (actor.Worker.NeedsFlush) h.Apply(new("flush", actor.Id));
        }
        throw new InvalidOperationException("NC0: native chart turn was not ready within the bounded schedule");
    }
}
