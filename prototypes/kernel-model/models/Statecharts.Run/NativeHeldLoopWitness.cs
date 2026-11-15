using System.Text.Json;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;
using static NativeInternalWitness;

internal static class NativeHeldLoopWitness
{
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from cancel in new[] { false, true } select $"native-held-loop:{m},cancel={cancel}";
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var cancel in new[] { false, true })
        {
            var h = new MachineInboxHarness();
            var graph = new StructuralDefinition([new("root", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "root"), new("Done", NodeKind.Final, "root")],
                [new("cycle", 0, "A", null, VisitMode.Internal, "", Kind: TriggerKind.Automatic), new("go", 1, "A", "Done", VisitMode.Reenter, "go")]);
            var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(graph)), ("InputInbox", Binding.Literal(new MachineInboxSettings(3, 8, 120000))),
                ("InternalExecution", Binding.Literal(new InternalExecutionSettings(4, "internal", new() { ["cycle"] = "enabled" })))).With(Control());
            var wf = Wf.Workflow("machine-inbox", Wf.Parallel("parallel", null, machine,
                Wf.Sequence("repair", Wf.Event("enable-wait", "held-enable"), Wf.Set("enable", "enabled", true),
                    Wf.Event("disable-wait", "held-disable"), Wf.Set("disable", "enabled", false))), ("enabled", false));
            wf.InterventionRequested = true; Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(mode);
            var frame = h.State.Frames.Values.Single(f => f.Node == "machine").Id;
            MachineState Chart() => h.State.Frames[frame].State!.Deserialize<MachineState>()!;
            NativeGuardWitness.Release(h, "held-enable");
            h.ObserveCommitted = native =>
            {
                var progress = native.Frames[frame].State!.Deserialize<MachineState>()!.Settling!;
                var committedSteps = h.History.Count(s => s.StartsWith("statechart:", StringComparison.Ordinal) &&
                    Json.Read<string?[]>(s[11..]) is var p && p[2] == "internal-evaluated" && p[3] == "Automatic");
                Check(progress.Steps == committedSteps,
                    "NHL0: continuation reset the retained semantic counter before explicit recovery");
            };
            h.Send(InvocationInput.Kind, new InvocationInput(frame, "go", "go")); h.Drain();
            h.ObserveCommitted = null;
            var held = Chart();
            Check(held is { Evaluating: "go", BusinessReady: false, Settling: { Steps: 4, Block: not null }, Control.Spec.Purpose: ControlPurpose.Loop } &&
                h.State.Inboxes![frame].Receipts["go"] is { Status: "Evaluating", EvaluationStarted: true }, "NHL1: pre-business loop lost its retained native evaluation");
            h.Send(InvocationInput.Kind, new InvocationInput(frame, "tail", "unknown")); h.Drain(); h.Reload();
            if (cancel)
            {
                h.Apply(new("add", M: new("cancel", h.Target, Kinds.Terminate, TargetCreation: 1))); h.Drain();
                Check(h.State.Inboxes![frame].Receipts["go"] is { Status: "NotProcessed", Outcome: "cancelled", EvaluationStarted: true },
                    "NHL2: pre-business loop cancellation mislabeled an unselected input");
            }
            else
            {
                NativeGuardWitness.Retry(h, "still-cycling"); h.Reload();
                Check(Chart() is { Evaluating: "go", BusinessReady: false, Settling: { Steps: 4, Block: not null } } &&
                    Chart().Control!.Original != held.Control!.Original, "NHL3: loop reset lost held evaluation or failed to require another incident");
                NativeGuardWitness.Release(h, "held-disable"); NativeGuardWitness.Retry(h, "repaired"); h.Reload();
                Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished &&
                    h.State.Inboxes![frame].Receipts["go"] is { Status: "Completed", Outcome: "handled", EvaluationStarted: true } &&
                    h.State.Inboxes[frame].Receipts["tail"] is { Status: "NotProcessed", Outcome: "completed", EvaluationStarted: false } &&
                    h.History.Count(s => s.StartsWith("statechart:", StringComparison.Ordinal) && Json.Read<string?[]>(s[11..]) is var p && p[1] == "go" && p[2] == "evaluated") == 1,
                    "NHL4: repaired prelude loop lost exactly one later business evaluation/FIFO finalization");
            }
            Console.WriteLine($"PASS native-held-loop:{mode},cancel={cancel}");
        }
    }
}
