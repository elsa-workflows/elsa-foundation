using Kernel.Durable;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

internal static class ClrFaultWitness
{
    internal static void Red()
    {
        var h = new MachineInboxHarness();
        var def = new StructuralDefinition([new("root", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "root", Entry: "dependency")], []);
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(def)),
            ("InputInbox", Binding.Literal(new { Capacity = 2, ReceiptCapacity = 8, RetentionMilliseconds = 120000L }))).With(Wf.Node<ClrDependency>("dependency", ("Local", Binding.Literal(17))));
        var wf = Wf.Workflow("machine-inbox", machine); wf.InterventionRequested = true;
        Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(Modes.Balanced);
        MachineInboxHarness.Check(h.State.Frames.Values.Any(f => f.Node == "dependency" && f.Intervention is not null), "X1: authored CLR failure did not retain its ordinary intervention");
        Console.WriteLine("PASS clr-recovery-minimal");
    }
}
internal sealed class ClrDependency : Kernel.Native.Activity<int>
{
    public ClrDependency() { }
    [Input] public int Local { get; set; }
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext c) => throw new InvalidOperationException("CLR dependency unavailable");
}
