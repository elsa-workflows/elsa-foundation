using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static NativeInternalWitness;

internal static class NativePriorityWitness
{
    internal static IEnumerable<string> Names => new[] { Modes.Balanced, Modes.Durable }.Select(m => "native-priority:" + m);
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        {
            ChartMark.Calls.Clear(); var h = new MachineInboxHarness();
            var definition = new StructuralDefinition([new("root", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "root"),
                new("AutoDone", NodeKind.Final, "root", Entry: "auto-mark"), new("BusinessDone", NodeKind.Final, "root", Entry: "business-mark")],
                [new("auto", 0, "A", "AutoDone", VisitMode.Reenter, "", Kind: TriggerKind.Automatic),
                 new("go", 1, "A", "BusinessDone", VisitMode.Reenter, "go")]);
            Node Mark(string id) => Wf.Node<ChartMark>(id, ("Name", Binding.Literal(id)), ("Local", Binding.Literal(17)));
            var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(definition)),
                ("InputInbox", Binding.Literal(new MachineInboxSettings(3, 8, 120000))),
                ("InternalExecution", Binding.Literal(new InternalExecutionSettings(8, "internal", new() { ["auto"] = "permission" }))))
                .With(Control(), Mark("auto-mark"), Mark("business-mark"));
            var wf = Wf.Workflow("machine-inbox", Wf.Parallel("parallel", null, machine,
                Wf.Sequence("repair", Wf.Event("wait", "enable-auto"), Wf.Set("enable", "permission", true))), ("permission", false));
            wf.InterventionRequested = true; using var registration = Register(wf); h.Start(mode);
            var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
            NativeGuardWitness.Release(h, "enable-auto"); h.Reload();
            h.Send(InvocationInput.Kind, new InvocationInput(frame, "go", "go")); h.Drain();
            Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished && ChartMark.Calls.SequenceEqual(new[] { ("auto-mark", 17) }),
                "NB1: cached stability let business selection overtake a newly eligible automatic transition");
            Check(h.State.Inboxes![frame].Receipts["go"] is { Status: "NotProcessed", Outcome: "completed", EvaluationStarted: true },
                "NB2: automatic completion lost the held input's honest terminal evaluation evidence");
            Console.WriteLine("PASS native-priority:" + mode);
        }
    }
}
