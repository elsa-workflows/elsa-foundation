using Kernel.Durable;
using Kernel.Native;
using static MachineInboxHarness;

// Constructed v10/policy4 context passed through the pure upgrader; current outer row version/oracle is retained. No old writer/binary claim.
internal static class ClrLegacyWitness
{
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var kind in new[] { "activation", "leaf", "start", "resume", "external" })
        {
            ClrWork.Reset(kind); var h = new MachineInboxHarness();
            var dependency = new Node("dependency", kind == "external" ? typeof(ClrExternal) : kind is "start" or "resume" ? typeof(ClrWait) : typeof(ClrDependency)); dependency.Inputs["Local"] = Binding.Literal(17);
            var wf = Wf.Workflow("machine-inbox", Wf.Sequence("sequence", Wf.Event("gate", "gate"), dependency)); wf.InterventionRequested = true;
            Kernel.Sim.Native.NativeWorkflows.Registry.Add(wf); h.Start(mode);
            var old = h.State; old.InterventionPolicy = 4;
            h.World.Store.Instances[h.Target.Id] = h.World.Store.Instances[h.Target.Id] with { State = Json.Write(new NativeModel(new()).UpgradeState(Json.Write(old), 10)) };
            h.Reload(); var observed = false;
            var cause = kind == "activation" ? "Exception has been thrown by the target of an invocation." : "CLR dependency unavailable";
            string[] phases = kind switch { "activation" => [], "resume" => ["start", "resume"], _ => [kind] };
            bool Authored() => ClrWork.Failures == 1 && ClrWork.Calls.Select(c => c.Phase).SequenceEqual(phases) &&
                (kind != "activation" || ClrWork.Activations == 1);
            try
            {
                h.Send(NativeKinds.Resume, new ResumeStimulus("gate"), false); h.Drain();
                if (kind == "resume") { h.Send(NativeKinds.Resume, new ResumeStimulus("release"), false); h.Drain(); }
            }
            catch (InvalidOperationException e) when (Authored() && e.Message == $"Kernel: I14 {h.Target.Id} did nothing wrong and was parked: {cause}") { observed = true; }
            Check(observed && Authored() && h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Faulted && h.State.InterventionPolicy == 4 && h.World.Store.Instances[h.Target.Id].Fault == cause,
                "X21: old policy4 adopted new CLR interception");
            Console.WriteLine($"PASS clr-seeded-v10-policy4:{mode},kind={kind}");
        }
    }
}
