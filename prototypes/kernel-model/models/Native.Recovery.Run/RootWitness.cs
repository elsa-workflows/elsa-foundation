using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

internal static class RootWitness
{
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var root in new[] { true, false }) Run(mode, root);
    }
    private static void Run(string mode, bool root)
    {
        RootLeaf.Calls.Clear();
        var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
        var target = InstanceRef.In(0, "root-next"); var actor = world.Of(0, 0);
        var leaf = Wf.Node<RootLeaf>("leaf", ("Value", root ? Binding.Literal(7) : Binding.Var("CurrentItem")), ("Index", root ? Binding.Literal(0) : Binding.Var("CurrentIndex")));
        var workflow = Wf.Workflow("root-next", root ? leaf : Wf.Node<AbsorbRecoveryFault>("outer").With(Wf.ForEach("loop", Binding.Literal(new[] { 7, 9 }), leaf)));
        workflow.InterventionRequested = true; NativeWorkflows.Registry.Add(workflow);
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        void Apply(Act action)
        {
            world.Apply(action);
            // The independent expected-root checkpoint assertion must precede the general fault diagnostic.
            if (root && world.Store.Instances.TryGetValue(target.Id, out var committed) && committed.Status == InstanceStatus.Faulted)
                throw new InvalidOperationException("R22: opted-in root fault ended the run instead of retaining intervention");
            if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
        }
        void Drain(bool cut = false)
        {
            for (var i = 0; i < 100; i++)
            {
                Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
                if (actor.Worker.Ready.Contains(target.Id)) Apply(new("turn", actor.Id, S: target.Id));
                else if (!actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) return;
                if (actor.Worker.NeedsFlush)
                {
                    Apply(new("flush", actor.Id));
                    if (cut && world.Store.Journal.Values.Any(j => j.Entries.Contains("recovery:root-retry:", StringComparison.Ordinal))) return;
                }
                if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
            }
            throw new InvalidOperationException("R0: root/iteration witness exceeded bounded drain");
        }
        NativeState State() => Json.Read<NativeState>(world.Store.Instances[target.Id].State);
        Apply(new("take", actor.Id)); Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec(workflow.Id, mode) with { Creation = new(1, 0, "root-proof") }))); Drain();
        var state = State(); var source = state.Frames.Values.SingleOrDefault(f => f.Node == "leaf");
        Check(world.Store.Instances[target.Id].Status == InstanceStatus.Running && source?.Intervention is not null && RootLeaf.Calls.Count == 1,
            root ? "R22: opted-in root fault ended the run instead of retaining intervention" : "R23: direct ForEach fault lost its continuing iteration");
        Apply(new("add", M: new("retry", target, NativeRetry.Kind, Json.Write(new NativeRetry("root-retry", source!.Id, source.Intervention!.Id, "dependency repaired")), TargetCreation: 1))); Drain(cut: true);
        var work = State().Agenda.SingleOrDefault(w => w.Node == "leaf");
        Check(work is not null && work.Frame != source.Id && work.Parent == source.Parent && RootLeaf.Calls.Count == 1,
            "R24: root/iteration retry did not commit its exact fresh obligation before execution");
        if (!root) Check(Values.To<ForEachState>(State().Frames[source.Parent!].State)!.Index == 0 && work!.Scope!["CurrentItem"]!.ToString() == "7" && work.Scope["CurrentIndex"]!.ToString() == "0",
            "R25: retry advanced or changed the failed iteration before success");
        Drain();
        Check(world.Store.Instances[target.Id].Status == InstanceStatus.Finished && RootLeaf.Calls.Count == (root ? 2 : 3) && RootLeaf.Calls[0].Frame != RootLeaf.Calls[1].Frame && RootLeaf.Calls[0].Operation == RootLeaf.Calls[1].Operation &&
            RootLeaf.Calls.Select(c => c.Value).SequenceEqual(root ? new[] { 7, 7 } : new[] { 7, 7, 9 }) && RootLeaf.Calls.Select(c => c.Index).SequenceEqual(root ? new[] { 0, 0 } : new[] { 0, 0, 1 }) &&
            (root || RootLeaf.Calls[2].Operation != RootLeaf.Calls[1].Operation), "R26: recovery repeated, skipped or misrouted a root/iteration obligation");
        Console.WriteLine($"PASS recovery:root-iteration-minimal,{mode},root={root}");
    }
}
internal sealed class RootLeaf : Kernel.Native.Activity<int>
{
    public RootLeaf() { }
    [Input] public int Value { get; set; }
    [Input] public int Index { get; set; }
    internal static readonly List<(string Frame, string Operation, int Value, int Index)> Calls = [];
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        Calls.Add((context.FrameId, context.OperationKey, Value, Index));
        return ValueTask.FromResult(Calls.Count == 1 ? Fault("dependency", "repair required") : Complete(Value));
    }
}
