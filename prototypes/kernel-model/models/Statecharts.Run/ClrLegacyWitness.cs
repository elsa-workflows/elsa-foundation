using Kernel.Durable;
using Kernel.Native;
using static MachineInboxHarness;

// Actual ordinary fault delivery after an explicitly seeded v10/policy4 checkpoint; not an old writer/binary.
internal static class ClrLegacyWitness
{
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var kind in new[] { "activation", "leaf", "start", "resume" })
        {
            ClrWork.Reset(kind); var h = new MachineInboxHarness();
            var dependency = new Node("dependency", kind is "start" or "resume" ? typeof(ClrWait) : typeof(ClrDependency)); dependency.Inputs["Local"] = Binding.Literal(17);
            var wf = Wf.Workflow("machine-inbox", Wf.Sequence("sequence", Wf.Event("gate", "gate"), dependency)); wf.InterventionRequested = true;
            Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(mode);
            var old = h.State; old.InterventionPolicy = 4;
            h.World.Store.Instances[h.Target.Id] = h.World.Store.Instances[h.Target.Id] with { StateVersion = 10, State = Json.Write(old) };
            h.Reload(); var observed = false;
            try
            {
                h.Send(NativeKinds.Resume, new ResumeStimulus("gate"), false); h.Drain();
                if (kind == "resume") { h.Send(NativeKinds.Resume, new ResumeStimulus("release"), false); h.Drain(); }
            }
            catch (InvalidOperationException e) when (e.Message.StartsWith("Kernel: I14", StringComparison.Ordinal)) { observed = true; }
            Check(observed && h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Faulted && h.State.InterventionPolicy == 4,
                "X21: old policy4 adopted new CLR interception");
            Console.WriteLine($"PASS clr-seeded-v10-policy4:{mode},kind={kind}");
        }
    }
}
