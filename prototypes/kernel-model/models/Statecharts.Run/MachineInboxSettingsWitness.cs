using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static MachineInboxHarness;

internal static class MachineInboxSettingsWitness
{
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        {
            ChartDependency.Calls.Clear(); var h = new MachineInboxHarness();
            var def = new StructuralDefinition([new("root", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "root", Entry: "phase"), new("B", NodeKind.Final, "root")],
                [new("go", 0, "A", "B", VisitMode.Reenter, "go")]);
            var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(def)), ("InputInbox", Binding.Var("settings")))
                .With(Wf.Sequence("phase", Wf.Set("change", "settings", "incompatible-after-entry"), Wf.Node<ChartDependency>("dependency", ("Local", Binding.Literal(17)))));
            var wf = Wf.Workflow("machine-inbox", machine); wf.InterventionRequested = true;
            wf.Variables["settings"] = Values.From(new { Capacity = 2, ReceiptCapacity = 8, RetentionMilliseconds = 120000L });
            Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(mode);
            var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
            Check(h.State.Variables["settings"]?.ToString() == "incompatible-after-entry", "Q17: settings fixture did not change its ordinary bound value");
            h.Send(InvocationInput.Kind, new InvocationInput(frame, "go", "go")); h.Drain();
            Check(h.State.Inboxes![frame].Capacity == 2 && h.State.Inboxes[frame].Receipts["go"].Status == "Queued", "Q17: pending receiver reevaluated frozen entry settings");
            for (var i = 0; i < 2; i++)
            {
                var source = h.State.Frames.Values.Single(f => f.Node == "dependency");
                var request = h.Send(NativeRetry.Kind, new NativeRetry("fix-" + i, source.Id, source.Intervention!.Id, "frozen settings")); h.Drain(request); h.Reload();
            }
            Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished && h.State.Inboxes![frame].Receipts["go"].Outcome == "handled", "Q17: ordinary callback reevaluated frozen settings");
            Console.WriteLine("PASS machine-inbox-frozen-expression:" + mode);
        }
    }
}
