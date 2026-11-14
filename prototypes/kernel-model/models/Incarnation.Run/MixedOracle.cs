using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

sealed partial class CreationWitness
{
    internal void MixedOracle()
    {
        NativeWorkflows.Registry.Add(Wf.Workflow("creation-probe", Wf.Event("receiver", "mixed-oracle")));
        var legacy = new InstanceRef(_target.Id + "@creation1");
        Apply(new("take", Actor.Id)); Send(Kinds.Start, 1, start: Spec(1)); Drain();
        Send(Kinds.Start, null, start: NativeModel.Spec("creation-probe", mode), target: legacy); Drain(legacy);
        Require(State.Frames.Keys.SequenceEqual(Json.Read<NativeState>(_world.Store.Instances[legacy.Id].State).Frames.Keys),
            "C13", "mixed oracle fixture did not use equal local frames");
        Apply(new("publish", B: 1, S: "event:mixed-oracle")); Drain(); Drain(legacy);
        Require(Row.Status == InstanceStatus.Finished && _world.Store.Instances[legacy.Id].Status == InstanceStatus.Finished,
            "C13", "mixed oracle actors did not both complete");
        Console.WriteLine($"PASS creation:mixed-oracle,{mode}");
    }
}
