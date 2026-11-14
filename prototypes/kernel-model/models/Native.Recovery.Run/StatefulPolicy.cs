using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

internal static class StatefulPolicy
{
    internal static IEnumerable<string> Names => (from m in new[] { Modes.Balanced, Modes.Durable } from p in new[] { 0, 1, 2 }
        select $"recovery:stateful-{(p == 0 ? "disabled-actual" : "seeded-v7")},{m},policy={p}").Concat(Readers().Select(c => "recovery-stateful-reader:" + c.Name));
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var policy in new[] { 0, 1, 2 }) Legacy(mode, policy);
        foreach (var c in Readers()) { c.Run(); Console.WriteLine("PASS recovery-stateful-reader:" + c.Name); }
    }
    // Construct only the old checkpoint shape; this is not an old-binary/writer claim.
    internal static void RemoveNewStatefulPins(NativeState state)
    {
        foreach (var f in state.Frames.Values.Where(f => f.RecoveryStateful))
        { f.RecoveryStateful = false; f.RecoveryInputs = null; f.RecoveryMaterial = null; f.OperationKey = null; }
    }
    private static void Legacy(string mode, int policy)
    {
        StatefulProbe.Calls.Clear(); StatefulProbe.StartFault = false;
        var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true }; var target = InstanceRef.In(0, "old-stateful"); var owner = 0;
        Actor Actor() => world.Of(owner, 0);
        var leaf = Wf.Node<StatefulProbe>("leaf", ("Value", Binding.Var("value")), ("Data", Binding.Literal(new JsonObject { ["kept"] = 9 })));
        var wf = Wf.Workflow("old-stateful", Wf.Node<AbsorbRecoveryFault>("outer").With(Wf.Sequence("serial", Wf.Event("gate", "start"),
            Wf.Parallel("parent", null, leaf, Wf.Sequence("sibling", Wf.Event("change", "change"), Wf.Set("changed", "value", -1))))), ("value", 17));
        wf.InterventionRequested = policy != 0; NativeWorkflows.Registry.Add(wf);
        NativeState State() => Json.Read<NativeState>(world.Store.Instances[target.Id].State);
        void Apply(Act a) { world.Apply(a); if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error); }
        void Drain()
        {
            for (var i = 0; i < 100; i++)
            {
                var a = Actor(); Apply(new("poll", a.Id)); if (a.Worker.NeedsLoad) Apply(new("load", a.Id));
                if (a.Worker.Ready.Contains(target.Id)) Apply(new("turn", a.Id, S: target.Id)); else if (!a.Worker.NeedsFlush && !a.Worker.NeedsMeet) return;
                if (a.Worker.NeedsFlush) Apply(new("flush", a.Id)); if (a.Worker.NeedsMeet) Apply(new("meet", a.Id));
            }
            throw new InvalidOperationException("R0: legacy stateful witness exceeded bounded drain");
        }
        void Name(string name) { Apply(new("add", M: new(name, target, NativeKinds.Resume, Json.Write(new ResumeStimulus(name, Values.From("payload"))), TargetCreation: 1))); Drain(); }
        Apply(new("take", Actor().Id)); Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec(wf.Id, mode) with { Creation = new(1, 0, "old-stateful") }))); Drain();
        if (policy != 0)
        {
            // Constructed old suspended checkpoint, never claimed as bytes produced by an old binary.
            var old = State(); old.InterventionPolicy = policy;
            RemoveNewStatefulPins(old);
            world.Store.Instances[target.Id] = world.Store.Instances[target.Id] with { State = Json.Write(new NativeModel(new()).UpgradeState(Json.Write(old), 7)) };
        }
        wf.InterventionRequested = policy == 0; Apply(new("crash", owner)); Apply(new("tick", 20_000)); owner = 1; Apply(new("take", Actor().Id)); Drain();
        Name("start");
        if (State().Frames.Values.Single(f => f.Node == "leaf").RecoveryInputs is not null) throw new InvalidOperationException("R39: existing profile silently adopted stateful input pins");
        Name("change"); Name("go");
        if (world.Store.Instances[target.Id].Status != InstanceStatus.Finished || StatefulProbe.Calls.Count != 2 || StatefulProbe.Calls[1].Value != -1 || State().InterventionPolicy != policy)
            throw new InvalidOperationException("R39: old stateful input/fault behavior or saved policy changed");
        Console.WriteLine($"PASS recovery:stateful-{(policy == 0 ? "disabled-actual" : "seeded-v7")},{mode},policy={policy}");
    }
    private static NativeState Pinned()
    {
        var s = RecoveryReader.Staged(); s.InterventionPolicy = 3; s.Retries!["operator"].Source.RecoveryStateful = true; return s;
    }
    private static IEnumerable<(string Name, Action Run)> Readers()
    {
        foreach (var queued in new[] { false, true }) foreach (var foreign in new[] { false, true }) yield return ($"root-waits,queued={queued},foreign={foreign}", () =>
        {
            var s = new NativeState { Started = true, InterventionEnabled = true, InterventionPolicy = 3 };
            var source = new Frame { Id = "old", Node = "leaf", RecoveryStateful = true, RecoveryInputs = new(), RecoveryMaterial = new() { ["root"] = "leaf" }, OperationKey = "op", Intervention = new("incident", new("dependency", "repair")) };
            s.Retries = new() { ["retry"] = new(new("retry", "old", "incident", "repair"), source, "fresh") };
            s.Frames.Add("fresh", new() { Id = "fresh", Node = "leaf", RetrySource = "old", RecoveryStateful = true, RecoveryInputs = new(), RecoveryMaterial = (JsonObject)source.RecoveryMaterial.DeepClone(), OperationKey = "op" });
            s.Waits.Add("w", new("fresh", "event", "event:go")); if (foreign) s.Waits.Add("foreign", new("absent", "event", "event:foreign"));
            if (queued) s.Agenda.Add(new() { Resume = true, Frame = "fresh", Wait = "w" });
            try { NativeInterventions.Validate(s); }
            catch (InvalidOperationException e) when (foreign && e.Message == "Invalid native staged root obligation.") { return; }
            if (foreign) throw new InvalidOperationException("R40: suspended root accepted a foreign wait");
        });
        foreach (var mismatch in new[] { false, true }) yield return ("nonroot-resume-owner-" + mismatch, () =>
        {
            var s = new NativeState { Started = true, InterventionEnabled = true, InterventionPolicy = 3 };
            s.Frames.Add("p", new() { Id = "p", Node = "parent" });
            s.Frames.Add("f", new() { Id = "f", Parent = "p", Node = "leaf", RecoveryStateful = true, RecoveryInputs = new(), RecoveryMaterial = new(), OperationKey = "logical" });
            s.Waits.Add("wait", new("f", "event", "event:go")); s.Agenda.Add(new() { Resume = true, Frame = mismatch ? "p" : "f", Wait = "wait" });
            try { NativeInterventions.Validate(s); }
            catch (InvalidOperationException e) when (mismatch && e.Message == "Invalid native qualified resume ownership.") { return; }
            if (mismatch) throw new InvalidOperationException("R40: non-root resume activated a different frame's wait");
        });
        foreach (var p in new[] { 0, 1, 2 }) yield return ("v7-policy-" + p, () =>
        {
            var s = new NativeState { Started = true, InterventionEnabled = p != 0, InterventionPolicy = p }; var json = Json.Write(s);
            if (Json.Write(new NativeModel(new()).UpgradeState(json, 7)) != json) throw new InvalidOperationException("R40: v7 policy was changed on upgrade");
        });
        foreach (var kind in new[] { "old-marker", "future-policy", "missing-inputs", "attempt" }) yield return (kind, () =>
        {
            var s = Pinned(); var f = s.Retries!["operator"].Source;
            if (kind == "old-marker") s.InterventionPolicy = 2; if (kind == "missing-inputs") f.RecoveryInputs = null; if (kind == "attempt") f.Attempt = 1;
            try
            {
                if (kind == "future-policy") new NativeModel(new()).UpgradeState(Json.Write(s), 7); else NativeInterventions.Validate(s);
            }
            catch (InvalidOperationException e) when (e.Message is "Invalid native stateful recovery ownership." or "Invalid native retry lineage." or "Legacy native state contains unsupported stateful recovery data.") { return; }
            throw new InvalidOperationException("R40: invalid stateful policy/marker accepted: " + kind);
        });
    }
}
