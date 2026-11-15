using System.Text.Json;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static NativeInternalWitness;

internal static class NativeOutcomeWitness
{
    internal static string[] Finals => ["accepted-state", "ended-state"];
    internal static FinalOutcomeSettings Mapping() => new(new() { [Finals[0]] = "KeepResource", [Finals[1]] = "ReleaseResource" });
    private static string[] Cuts => ["steady", "lost-before", "lost-answer"];
    internal static IEnumerable<string> Names => from mode in new[] { Modes.Balanced, Modes.Durable }
        from final in Finals from nested in new[] { false, true } from work in new[] { "wait", "retry" } from cut in Cuts
        select $"native-outcome:{mode},final={final},nested={nested},work={work},cut={cut}";
    internal static IEnumerable<string> LegacyNames => from mode in new[] { Modes.Balanced, Modes.Durable } from final in Finals
        select $"native-outcome-legacy:{mode},final={final}";
    internal static StructuralDefinition Definition() => new([
        new("root", NodeKind.Root, Initial: "Review"), new("Review", NodeKind.Compound, "root", "inner-final"),
        new("inner-final", NodeKind.Final, "Review", Entry: "gate"),
        new(Finals[0], NodeKind.Final, "root", Entry: "final-work"), new(Finals[1], NodeKind.Final, "root", Entry: "final-work")],
        Finals.Select((f, i) => new GraphRule("finish-" + i, i, "Review", f, VisitMode.Reenter, f, "action")).ToArray());
    internal static Node Machine(bool retry = false, bool mapped = true)
    {
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(Definition())),
            ("InputInbox", Binding.Literal(new MachineInboxSettings(4, 8, 120000))),
            ("InternalExecution", Binding.Literal(new InternalExecutionSettings(8, "internal"))))
            .With(Control(), Wf.Event("gate", "initial-entry"), Mark("action"),
                Wf.Sequence("final-work", Mark("final-mark"), retry
                    ? Wf.Node<ChartDependency>("dependency", ("Local", Binding.Literal(17))) : Wf.Event("final-wait", "final-entry")));
        if (mapped) machine.Inputs["FinalOutcomes"] = Binding.Literal(Mapping());
        return machine;
    }
    private static Node Mark(string id) => Wf.Node<ChartMark>(id, ("Name", Binding.Literal(id)), ("Local", Binding.Literal(17)));
    internal static Node Root(Node machine, bool nested) => nested
        ? Wf.Flowchart("flow", [machine, Wf.Set("kept", "branch", "KeepResource"), Wf.Set("released", "branch", "ReleaseResource"), Wf.Set("legacy", "branch", "Done")],
            [new("machine", "kept", "KeepResource"), new("machine", "released", "ReleaseResource"), new("machine", "legacy", "Done")]) : machine;
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var final in Finals)
        {
            foreach (var nested in new[] { false, true }) foreach (var work in new[] { "wait", "retry" }) foreach (var cut in Cuts)
                Run(mode, final, nested, work == "retry", cut, mapped: true);
            Run(mode, final, true, false, "steady", mapped: false);
        }
    }
    private static void Run(string mode, string final, bool nested, bool retry, string cut, bool mapped)
    {
        ChartMark.Calls.Clear(); ChartDependency.Calls.Clear(); var h = new MachineInboxHarness(); var machine = Machine(retry, mapped);
        var wf = Wf.Workflow("machine-inbox", Root(machine, nested)); wf.InterventionRequested = true; using var registration = Register(wf); h.Start(mode);
        var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        MachineState Chart() => h.State.Frames[frame].State!.Deserialize<MachineState>()!;
        Check(Chart().Version == (mapped ? 7 : 6) && (Chart().FinalOutcomes is not null) == mapped,
            "NOC1: birth did not freeze the requested outcome profile");
        // Native callbacks restore a fresh unhydrated composite; the published definition stays fixed.
        h.Reload();
        NativeGuardWitness.Release(h, "initial-entry");
        Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Running && Chart().Active.Contains("inner-final") &&
            !h.State.Variables.ContainsKey("branch") && ChartMark.Calls.Count == 0,
            "NOC2: nested final returned a mapped machine or ran its top-level final work");
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "finish", final)); h.Drain();
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "tail", "unknown")); h.Drain();
        Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Running && Chart().Pending is not null &&
            !h.State.Variables.ContainsKey("branch") && ChartMark.Calls.Select(c => c.Name).SequenceEqual(["action", "final-mark"]) &&
            h.State.Inboxes![frame].Receipts["tail"] is { Status: "Queued", EvaluationStarted: false } &&
            (!mapped || Chart().FinalOutcomes!.Outcomes[final] == Mapping().Outcomes[final]),
            "NOC3: final Entry completed early or selected work lost its frozen mapping/queued tail");
        h.Reload();
        if (retry)
        {
            for (var i = 0; i < 2; i++)
            {
                var child = h.State.Frames.Values.Single(f => f.Node == "dependency");
                var id = h.Send(NativeRetry.Kind, new NativeRetry("repair-" + i, child.Id, child.Intervention!.Id, "final Entry repair"));
                if (i == 0 && cut != "steady") h.Cut(id, cut == "lost-answer"); else h.Drain(id);
                h.Reload();
            }
            Check(ChartDependency.Calls.Count == 3 && ChartDependency.Calls.Select(c => c.Operation).Distinct().Count() == 1,
                "NOC4: final Entry recovery changed its ordinary operation or replayed completed work");
        }
        else
        {
            var key = Release(h);
            if (cut != "steady") h.Cut(key, cut == "lost-answer"); else h.Drain();
            h.Reload();
        }
        var expected = mapped ? Mapping().Outcomes[final] : "Done";
        Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished && h.State.LexicalScopes is null &&
            (!nested || h.State.Variables.GetValueOrDefault("branch")?.ToString() == expected) &&
            ChartMark.Calls.Select(c => c.Name).SequenceEqual(["action", "final-mark"]) &&
            h.State.Inboxes![frame].Receipts["finish"] is { Status: "Completed", Outcome: "handled", EvaluationStarted: true } &&
            h.State.Inboxes[frame].Receipts["tail"] is { Status: "NotProcessed", Outcome: "completed", EvaluationStarted: false },
            "NOC5: normal completion lost its mapped Flowchart branch, old Done, phase progress or final receipts");
        Console.WriteLine(mapped ? $"PASS native-outcome:{mode},final={final},nested={nested},work={(retry ? "retry" : "wait")},cut={cut}"
            : $"PASS native-outcome-legacy:{mode},final={final}");
    }
    private static InboxKey Release(MachineInboxHarness h)
    {
        var wait = h.World.Store.Subscriptions.Single(s => s.Instance == h.Target.Id && s.Key == "event:final-entry");
        var delivery = Events.Route(new(wait.Key, "finish-entry", null, h.World.Time + 120000), wait);
        Check(h.World.Store.AddEventAsync(delivery).Result is { Added: true } a && a.Target == h.Target, "NOC0: final Entry release missed its real wait");
        h.World.Oracle.Note(delivery.Message); return delivery.Message.InboxKey;
    }
}
