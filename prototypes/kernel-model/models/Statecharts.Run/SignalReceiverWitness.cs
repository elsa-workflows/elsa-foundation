using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using static MachineInboxHarness;

internal static class SignalReceiverWitness
{
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        {
            var h = new MachineInboxHarness();
            Kernel.Sim.Native.NativeWorkflows.Registry.Add(Wf.Workflow("machine-inbox", Wf.Node<SignalWait>("wait")));
            h.Start(mode); var frame = h.State.Frames.Values.Single().Id;
            h.Send(CompositeSignal.Kind, new CompositeSignal(frame, "notice"), false); h.Drain();
            Check(h.State.Frames[frame].State?.GetValue<int>() == 1 && h.State.Waits.Values.Any(w => w.Frame == frame), "Q19: existing stateful signal receiver lost its handler or wait");
            h.Send(NativeKinds.Resume, new ResumeStimulus("release"), false); h.Drain();
            Check(h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished, "Q19: stateful signal receiver could not finish its normal wait");
            Console.WriteLine("PASS machine-inbox-existing-stateful-signal:" + mode);
        }
    }
}
internal sealed class SignalWait : StatefulActivity<Unit, int, JsonNode?>, ISignalledComposite
{
    public SignalWait() { }
    protected override ValueTask<Transition> ExecuteAsync(StartContext<JsonNode?> c) => new(Suspend(0, new EventTrigger("release")));
    protected override ValueTask<Transition> ResumeAsync(ResumeContext<int, JsonNode?> c) => new(Complete(default));
    public bool CanAccept(CompositeContext c) => true;
    public void Accept(CompositeContext c, string id, CompositeSignal s) => c.SetState(1);
}
