using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Sim.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static NativeInternalWitness;

internal static class NativeGuardWitness
{
    private static string[] ValuesUnderTest => ["true", "false", "null", "missing", "\"true\"", "1", "[]", "{}"];
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable }
        from k in new[] { TriggerKind.Event, TriggerKind.Automatic, TriggerKind.Completion }
        from v in ValuesUnderTest from repair in new[] { false, true }
        select $"native-guard:{m},kind={k},value={v},parallel-repair={repair}";

    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        foreach (var kind in new[] { TriggerKind.Event, TriggerKind.Automatic, TriggerKind.Completion })
        foreach (var value in ValuesUnderTest) foreach (var repair in new[] { false, true }) Run(mode, kind, value, repair);
    }

    private static void Run(string mode, TriggerKind kind, string value, bool repair)
    {
        ChartMark.Calls.Clear(); var h = new MachineInboxHarness();
        var source = kind == TriggerKind.Completion ? "C" : "A";
        var nodes = new List<GraphNode> { new("root", NodeKind.Root, Initial: source), new("Done", NodeKind.Final, "root", Entry: "done") };
        if (kind == TriggerKind.Completion) nodes.AddRange([new("C", NodeKind.Compound, "root", "F"), new("F", NodeKind.Final, "C", Entry: "entered")]);
        else nodes.Add(new("A", NodeKind.Atomic, "root", Entry: "entered"));
        var definition = new StructuralDefinition(nodes.ToArray(),
            [new("guarded", 0, source, "Done", VisitMode.Reenter, kind == TriggerKind.Event ? "go" : "", "action", Kind: kind),
             new("finish", 1, source, "Done", VisitMode.Reenter, "finish", "action")]);
        Node Mark(string id) => Wf.Node<ChartMark>(id, ("Name", Binding.Literal(id)), ("Local", Binding.Literal(17)));
        var settings = new InternalExecutionSettings(8, "internal", new() { ["guarded"] = "permission" });
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(definition)),
            ("InputInbox", Binding.Literal(new MachineInboxSettings(3, 12, 120000))), ("InternalExecution", Binding.Var("settings")))
            .With(Control(), Wf.Sequence("entered", Mark("mark"), Wf.Event("gate", "guard-entry")), Mark("action"), Mark("done"));
        var flow = Wf.Flowchart("flow", [machine, Wf.Set("returned", "returned", true)], [new("machine", "returned")]);
        var root = repair ? Wf.Parallel("parallel", null, flow, Wf.Sequence("repair", Wf.Event("repair-wait", "guard-repair"),
            Wf.Set("change", "permission", true), Wf.Set("change-settings", "settings", "incompatible-after-entry"))) : machine;
        var variables = new Dictionary<string, JsonNode?> { ["settings"] = Kernel.Native.Values.From(settings) };
        if (value != "missing") variables["permission"] = JsonNode.Parse(value);
        var wf = new Workflow("machine-inbox", root, variables); wf.InterventionRequested = true; NativeWorkflows.Registry.Add(wf); h.Start(mode);
        var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        MachineState Chart() => h.State.Frames[frame].State!.Deserialize<MachineState>()!;
        if (kind == TriggerKind.Event) { h.Send(InvocationInput.Kind, new InvocationInput(frame, "go", "go")); h.Drain(); }
        Release(h, "guard-entry");
        var fault = value is not ("true" or "false" or "null" or "missing");
        if (fault)
        {
            Check(h.State.Frames.ContainsKey(frame), "NG1: strict guard conversion completed the machine instead of retaining an incident");
            var held = Chart(); var scopes = Json.Write(h.State.LexicalScopes);
            Check(held.Control?.Spec is { Purpose: ControlPurpose.Guard, Rule: "guarded" } &&
                h.State.Frames.Values.Single(f => f.Node == "internal").Intervention is { Fault.Code: "statechart.guard" } &&
                ChartMark.Calls.SequenceEqual(new[] { ("mark", 17) }), "NG1: strict guard conversion did not retain a normal continuing incident");
            if (kind == TriggerKind.Event) Check(h.State.Inboxes![frame].Receipts["go"] is { Status: "Evaluating", EvaluationStarted: true } && held.Evaluating == "go",
                "NG2: guard incident settled or reclassified its evaluation as selected phase work");
            h.Send(InvocationInput.Kind, new InvocationInput(frame, "tail-1", "unknown")); h.Drain();
            h.Send(InvocationInput.Kind, new InvocationInput(frame, "tail-2", "unknown")); h.Drain();
            if (kind != TriggerKind.Event) { h.Send(InvocationInput.Kind, new InvocationInput(frame, "tail-3", "unknown")); h.Drain(); }
            h.Send(InvocationInput.Kind, new InvocationInput(frame, "full", "unknown")); h.Drain();
            Check(h.Replies[^1].Result == "Full", "NG3: guard blockage bypassed bounded admission capacity");
            var first = kind == TriggerKind.Event ? "go" : "tail-1";
            h.Send(InvocationInput.Kind, new InvocationInput(frame, first, kind == TriggerKind.Event ? "go" : "unknown")); h.Drain();
            Check(h.Replies[^1].Result == "Duplicate", "NG3: guard blockage lost deduplication");
            h.Send(InvocationInput.Kind, new InvocationInput(frame, first, "changed")); h.Drain();
            Check(h.Replies[^1].Result == "Conflict", "NG3: guard blockage admitted a conflicting retry");
            h.Reload();
            Check(Json.Write(Chart()) == Json.Write(held) && Json.Write(h.State.LexicalScopes) == scopes, "NG4: cold recovery changed held guard configuration/scopes");
            if (!repair)
            {
                h.Apply(new("add", M: new("cancel", h.Target, Kinds.Terminate, TargetCreation: 1))); h.Drain();
                var receipts = h.State.Inboxes![frame].Receipts;
                Check(receipts.Values.All(r => r is { Status: "NotProcessed", Outcome: "cancelled" }) &&
                    receipts.Values.Count(r => r.EvaluationStarted) == (kind == TriggerKind.Event ? 1 : 0) && h.State.Inboxes[frame].Pending.Count == 0,
                    "NG5: cancellation lost evaluation/queued terminal distinctions");
                NativeWorkflows.Registry.Remove(wf.Id); h.Send(ReceiptQuery.Kind, new ReceiptQuery(frame, first)); h.Drain();
                Check(h.Replies[^1] is { Result: "Found", FinalizedAt: not null, ValidUntil: not null, Status: "NotProcessed", Outcome: "cancelled" } &&
                    h.Replies[^1].EvaluationStarted == (kind == TriggerKind.Event), "NG5: terminal metadata lost retained evaluation evidence");
                Passed(); return;
            }
            Retry(h, "still-failing"); h.Reload();
            Check(Chart().Control is { Current: var current, Original: var original } && current == original &&
                h.State.Frames.Values.Single(f => f.Node == "internal").Intervention is { Fault.Code: "statechart.guard" } &&
                Chart().VisitSerial == held.VisitSerial && Chart().Settling!.Steps == held.Settling!.Steps,
                "NG6: a still-failing manual guard retry advanced visits or reset the causal counter");
            Release(h, "guard-repair");
            Check(Chart().Execution == held.Execution || Json.Write(Chart().Execution) == Json.Write(held.Execution),
                "NG7: independent ordinary repair reconfigured frozen execution settings");
            Retry(h, "repaired"); h.Reload();
        }
        else if (value != "true")
        {
            Check(Chart().Settling is { Stable: true, Block: null } && ChartMark.Calls.SequenceEqual(new[] { ("mark", 17) }),
                "NG8: false/null/missing guard was treated as true or a fault");
            if (kind == TriggerKind.Event) Check(h.State.Inboxes![frame].Receipts["go"] is { Status: "Completed", Outcome: "unhandled", EvaluationStarted: true },
                "NG8: rejected event guard did not finalize one unhandled evaluation");
            var offered = Json.Write(Chart().Settling!.Offered); var serial = Chart().VisitSerial;
            if (repair) Release(h, "guard-repair");
            h.Reload();
            if (kind == TriggerKind.Completion)
            {
                h.Send(InvocationInput.Kind, new InvocationInput(frame, "nudge", "unknown")); h.Drain();
                Check(Chart().VisitSerial == serial && Json.Write(Chart().Settling!.Offered) == offered && ChartMark.Calls.Count == 1,
                    "NG9: changed values revived a consumed completion opportunity");
            }
            h.Send(InvocationInput.Kind, new InvocationInput(frame, "finish", "finish")); h.Drain();
        }
        else if (repair) Release(h, "guard-repair");
        Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished && h.State.LexicalScopes is null &&
            ChartMark.Calls.SequenceEqual(new[] { ("mark", 17), ("action", 17), ("done", 17) }) &&
            (!repair || h.State.Variables["returned"]?.ToString() == "true"), "NG10: guarded completion/recovery replayed prior work or lost normal return/cleanup");
        if (kind == TriggerKind.Event && (fault || value == "true"))
            Check(h.State.Inboxes![frame].Receipts["go"] is { Status: "Completed", Outcome: "handled", EvaluationStarted: true } &&
                h.History.Count(s => s.StartsWith("statechart:", StringComparison.Ordinal) && Json.Read<string?[]>(s[11..]) is var p && p[1] == "go" && p[2] == "evaluated") == 1,
                "NG11: retry duplicated semantic input evaluation or lost its final receipt");
        if (fault) Check(h.State.Inboxes![frame].Receipts.Where(p => p.Key != "go").All(p => p.Value is { Status: "NotProcessed", Outcome: "completed", EvaluationStarted: false }),
            "NG11: blocked queued tails were evaluated after machine completion");
        Passed();
        void Passed() => Console.WriteLine($"PASS native-guard:{mode},kind={kind},value={value},parallel-repair={repair}");
    }

    internal static void Retry(MachineInboxHarness h, string id)
    {
        var source = h.State.Frames.Values.Single(f => f.Node == "internal");
        h.Send(NativeRetry.Kind, new NativeRetry(id, source.Id, source.Intervention!.Id, "manual guard/loop recovery")); h.Drain();
    }

    internal static void Release(MachineInboxHarness h, string name)
    {
        var wait = h.World.Store.Subscriptions.Single(s => s.Instance == h.Target.Id && s.Key == "event:" + name);
        var delivery = Events.Route(new(wait.Key, "release-" + name, null, h.World.Time + 120000), wait);
        Check(h.World.Store.AddEventAsync(delivery).Result is { Added: true } accepted && accepted.Target == h.Target, "NG0: release missed its actual native wait");
        h.World.Oracle.Note(delivery.Message); h.Drain();
    }
}
