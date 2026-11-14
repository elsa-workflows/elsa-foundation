using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static NativeInternalWitness;

internal static class NativePreludeWitness
{
    private static IEnumerable<string> PhaseNames => from mode in new[] { Modes.Balanced, Modes.Durable }
        from kind in new[] { TriggerKind.Automatic, TriggerKind.Completion } from work in new[] { "wait", "fault" }
        from cut in new[] { "steady", "lost-before", "lost-answer", "cancel" }
        select $"native-prelude:{mode},kind={kind},work={work},cut={cut}";
    internal static IEnumerable<string> Names => PhaseNames.Concat(new[] { Modes.Balanced, Modes.Durable }.Select(m => "native-prelude-business-guard:" + m))
        .Concat(from mode in new[] { Modes.Balanced, Modes.Durable } from kind in new[] { TriggerKind.Automatic, TriggerKind.Completion }
            select $"native-prelude-seeded-reader:{mode},kind={kind}");
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var kind in new[] { TriggerKind.Automatic, TriggerKind.Completion })
        foreach (var work in new[] { "wait", "fault" }) foreach (var cut in new[] { "steady", "lost-before", "lost-answer", "cancel" }) Run(mode, kind, work, cut);
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) RetainedBusinessGuard(mode);
    }
    private static void Run(string mode, TriggerKind kind, string work, string cut)
    {
        ChartDependency.Calls.Clear(); ChartMark.Calls.Clear(); var h = new MachineInboxHarness();
        Dictionary<string, JsonNode?> D(int local) => new() { ["local"] = Values.From(local) };
        var nodes = new List<GraphNode> { new("root", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "root", Defaults: D(17)),
            new("B", NodeKind.Atomic, "root", Entry: "target", Defaults: D(43)), new("Done", NodeKind.Final, "root") };
        var rules = new List<GraphRule> { new("auto", 0, "A", kind == TriggerKind.Automatic ? "B" : "C", VisitMode.Reenter, "",
            kind == TriggerKind.Automatic ? "work" : null, Kind: TriggerKind.Automatic), new("go", 2, "B", "Done", VisitMode.Reenter, "go", "business") };
        if (kind == TriggerKind.Completion)
        {
            nodes.AddRange([new("C", NodeKind.Compound, "root", "F", Defaults: D(21)), new("F", NodeKind.Final, "C")]);
            rules.Add(new("complete", 1, "C", "B", VisitMode.Reenter, "", "work", Kind: TriggerKind.Completion));
        }
        Node Mark(string id) => Wf.Node<ChartMark>(id, ("Name", Binding.Literal(id)), ("Local", Binding.Var("local")));
        var activity = work == "wait" ? Wf.Sequence("work", Wf.Event("phase-wait", "prelude-work"), Mark("worked"))
            : Wf.Node<ChartDependency>("work", ("Local", Binding.Var("local")));
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(new StructuralDefinition(nodes.ToArray(), rules.ToArray()))),
            ("InputInbox", Binding.Literal(new MachineInboxSettings(3, 8, 120000))),
            ("InternalExecution", Binding.Literal(new InternalExecutionSettings(8, "internal", new() { ["auto"] = "permission" }))))
            .With(Control(), activity, Mark("target"), Mark("business"));
        var wf = Wf.Workflow("machine-inbox", Wf.Parallel("parallel", null, machine,
            Wf.Sequence("repair", Wf.Event("enable-wait", "prelude-enable"), Wf.Set("enable", "permission", true))), ("permission", false));
        wf.InterventionRequested = true; Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(mode);
        var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        MachineState Chart() => h.State.Frames[frame].State!.Deserialize<MachineState>()!;
        NativeGuardWitness.Release(h, "prelude-enable");
        var admission = h.Send(InvocationInput.Kind, new InvocationInput(frame, "go", "go")); h.Drain(admission);
        NativeCheckpointWitness.PrepareTurn(h); h.Apply(new("flush", h.Actor.Id));
        Check(Chart() is { Evaluating: "go", BusinessReady: false, Pending: null }, "NH1: held input lacks its durable pre-business gate");
        NativeCheckpointWitness.PrepareTurn(h);
        if (cut is "lost-before" or "lost-answer")
        {
            h.World.Auto = false; var actor = h.Actor; var call = actor.Calls.Count; h.Apply(new("flush", actor.Id));
            Check(actor.Calls.Count == call + 1 && actor.Calls[call].Kind == "commit", "NH2: internal plan cut missed its physical writer");
            h.Apply(new(cut == "lost-answer" ? "exec" : "lose", actor.Id, call));
            Check(ChartMark.Calls.Count == 0 && ChartDependency.Calls.Count == 0, "NH2: unknown internal plan commit ran authored work");
            h.Apply(new("answer", actor.Id, call, Flag: true)); h.World.Auto = true; h.Reload();
        }
        else { h.Apply(new("flush", h.Actor.Id)); h.Drain(); }
        var held = Chart(); var scopes = Json.Write(h.State.LexicalScopes);
        Check(held is { Evaluating: "go", BusinessReady: false, Pending.EventId: null, Settling.PhasePending: true } &&
            held.Settling!.Steps == (kind == TriggerKind.Automatic ? 1 : 2) && h.State.Inboxes![frame].Receipts["go"] is { Status: "Evaluating", EvaluationStarted: true } &&
            ChartMark.Calls.Count == 0 && (work == "wait" ? h.State.Waits.Values.Any(w => w.Key == "event:prelude-work") : ChartDependency.Calls.Count == 1),
            "NH3: internal phase lost or prematurely finished its held evaluation/input/counter");
        Check(!h.History.Any(s => s.StartsWith("statechart:", StringComparison.Ordinal) && Json.Read<string?[]>(s[11..]) is var p && p[1] == "go" && p[2] == "evaluated"),
            "NH3: internal prelude invented a business-rule evaluation before settling");
        if (work == "fault" && cut == "steady") ReadHeldPhase(h, machine, frame, mode, kind);
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "tail", "unknown")); h.Drain(); h.Reload();
        if (cut == "cancel")
        {
            h.Apply(new("add", M: new("cancel", h.Target, Kinds.Terminate, TargetCreation: 1))); h.Drain();
            Check(h.State.Inboxes![frame].Receipts["go"] is { Status: "NotProcessed", Outcome: "cancelled", EvaluationStarted: true } &&
                h.State.Inboxes[frame].Receipts["tail"] is { Status: "NotProcessed", Outcome: "cancelled", EvaluationStarted: false },
                "NH4: an internal plan mislabeled its held event as selected business phase work");
        }
        else
        {
            if (work == "wait") NativeGuardWitness.Release(h, "prelude-work");
            else for (var attempt = 1; attempt <= 2; attempt++)
            {
                var source = h.State.Frames.Values.Single(f => f.Node == "work");
                h.Send(NativeRetry.Kind, new NativeRetry("repair-" + attempt, source.Id, source.Intervention!.Id, "held prelude phase repair")); h.Drain(); h.Reload();
                if (attempt == 1) Check(Chart().Evaluating == "go" && !Chart().BusinessReady && Json.Write(Chart().Selection) == Json.Write(held.Selection) &&
                    Json.Write(h.State.LexicalScopes) == scopes && ChartMark.Calls.Count == 0, "NH5: prelude retry dropped held input or reset selected work/scopes");
            }
            var expected = work == "wait" ? new[] { ("worked", kind == TriggerKind.Automatic ? 17 : 21), ("target", 43), ("business", 43) }
                : new[] { ("target", 43), ("business", 43) };
            Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished && ChartMark.Calls.SequenceEqual(expected) &&
                (work != "fault" || ChartDependency.Calls.Count == 3 && ChartDependency.Calls.All(c => c.Local == (kind == TriggerKind.Automatic ? 17 : 21))) &&
                h.State.Inboxes![frame].Receipts["go"] is { Status: "Completed", Outcome: "handled", EvaluationStarted: true } &&
                h.State.Inboxes[frame].Receipts["tail"] is { Status: "NotProcessed", Outcome: "completed", EvaluationStarted: false } &&
                h.History.Count(s => s.StartsWith("statechart:", StringComparison.Ordinal) && Json.Read<string?[]>(s[11..]) is var p && p[1] == "go" && p[2] == "evaluated") == 1,
                "NH6: internal settling failed to precede exactly one business evaluation against the resulting configuration");
        }
        Console.WriteLine($"PASS native-prelude:{mode},kind={kind},work={work},cut={cut}");
    }
    private static void ReadHeldPhase(MachineInboxHarness h, Node definition, string frame, string mode, TriggerKind kind)
    {
        var native = h.State; var source = native.Frames.Values.Single(f => f.Node == "work"); var state = native.Frames[frame].State!;
        var inbox = Json.Read<InvocationInbox>(Json.Write(native.Inboxes![frame])); var parent = (IInterventionParent)new StateMachine();
        var obligation = new RetryChild(source.Id, "probe-successor", source.Node, source.Tag, source.Scope, frame, source.LexicalScope, inbox);
        var original = Json.Write(state); var originalSource = Json.Write(source); var originalInbox = Json.Write(inbox);
        var accepted = parent.PrepareRetry(definition, state.DeepClone(), obligation)!.Deserialize<MachineState>()!;
        Check(accepted.Progress!.Running.ContainsKey("probe-successor") && Json.Write(state) == original && Json.Write(source) == originalSource && Json.Write(inbox) == originalInbox,
            "NHR0: valid pure held-phase preparation did not rebind only its copied obligation");
        inbox.Pending[0] = inbox.Pending[0] with { Value = "tampered-after-admission" }; var altered = Json.Write(inbox); var refused = false;
        try { parent.PrepareRetry(definition, state.DeepClone(), obligation); }
        catch (InvalidOperationException error) when (error.Message == "Active input differs from its native receipt.") { refused = true; }
        Check(refused && Json.Write(state) == original && Json.Write(source) == originalSource && Json.Write(inbox) == altered,
            "NHR1: held-phase retry accepted divergent authoritative input or changed source/parent/inbox");
        Console.WriteLine($"PASS native-prelude-seeded-reader:{mode},kind={kind}");
    }
    private static void RetainedBusinessGuard(string mode)
    {
        ChartMark.Calls.Clear(); var h = new MachineInboxHarness();
        var definition = new StructuralDefinition([new("root", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "root"),
            new("Auto", NodeKind.Final, "root", Entry: "auto-mark"), new("Business", NodeKind.Final, "root", Entry: "business-mark")],
            [new("auto", 0, "A", "Auto", VisitMode.Reenter, "", Kind: TriggerKind.Automatic), new("go", 1, "A", "Business", VisitMode.Reenter, "go")]);
        Node Mark(string id) => Wf.Node<ChartMark>(id, ("Name", Binding.Literal(id)), ("Local", Binding.Literal(17)));
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(definition)), ("InputInbox", Binding.Literal(new MachineInboxSettings(3, 8, 120000))),
            ("InternalExecution", Binding.Literal(new InternalExecutionSettings(8, "internal", new() { ["auto"] = "automatic", ["go"] = "business" }))))
            .With(Control(), Mark("auto-mark"), Mark("business-mark"));
        var wf = Wf.Workflow("machine-inbox", Wf.Parallel("parallel", null, machine, Wf.Sequence("repair", Wf.Event("wait", "repair-both"),
            Wf.Set("change-auto", "automatic", true), Wf.Set("change-business", "business", true))), ("automatic", false), ("business", "wrong-type"));
        wf.InterventionRequested = true; Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(mode);
        var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "go", "go")); h.Drain();
        Check(h.State.Frames[frame].State!.Deserialize<MachineState>() is { BusinessReady: true, Evaluating: "go", Control.Spec.Purpose: ControlPurpose.Guard },
            "NH7: faulted business guard lost its already reached evaluation stage");
        NativeGuardWitness.Release(h, "repair-both"); NativeGuardWitness.Retry(h, "repair"); h.Reload();
        Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished && ChartMark.Calls.SequenceEqual(new[] { ("business-mark", 17) }),
            "NH7: business-guard recovery restarted unrelated internal settling instead of its retained evaluation");
        Console.WriteLine("PASS native-prelude-business-guard:" + mode);
    }
}
