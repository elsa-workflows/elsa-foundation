using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

internal static class NativeGraphWitness
{
    internal static string[] Names => ["native-graph:self-entry-wait"];
    internal static void Run()
    {
        var graph = new { Nodes = new GraphNode[] { new("root", NodeKind.Root, Initial: "Review"),
            new("Review", NodeKind.Compound, "root", "Checking"), new("Checking", NodeKind.Atomic, "Review"),
            new("Done", NodeKind.Final, "root") },
            Rules = new GraphRule[] { new("restart", 0, "Checking", "Checking", VisitMode.Reenter, "restart", "work") } };
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(graph)))
            .With(Wf.Sequence("work", Wf.Event("wait", "release"), Wf.Set("done", "worked", "yes")));
        NativeWorkflows.Registry.Add(Wf.Workflow("native-graph-proof", machine));
        var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
        var target = InstanceRef.In(0, "native-graph"); var actor = world.Of(0, 0);
        void Apply(Act act) { world.Apply(act); if (world.Check() is { } error) throw new InvalidOperationException(error); }
        void Drain()
        {
            for (var i = 0; i < 100; i++)
            {
                Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
                if (actor.Worker.Ready.Contains(target.Id)) Apply(new("turn", actor.Id, S: target.Id));
                else if (!actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) return;
                if (actor.Worker.NeedsFlush) Apply(new("flush", actor.Id));
                if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
            }
            throw new InvalidOperationException("N0: native graph drain exceeded its bound");
        }
        Apply(new("take", actor.Id));
        Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec("native-graph-proof", Modes.Durable)))); Drain();
        if (world.Store.Instances[target.Id].Status != InstanceStatus.Running)
            throw new InvalidOperationException("N1: full graph was not hydrated through the native StateMachine");
        var state = Json.Read<NativeState>(world.Store.Instances[target.Id].State);
        var frame = state.Frames.Values.Single(f => f.Node == "machine").Id;
        Apply(new("add", M: new("restart", target, CompositeSignal.Kind, Json.Write(new CompositeSignal(frame, "restart"))))); Drain();
        if (!world.Store.Subscriptions.Any(s => s.Instance == target.Id && s.Key == "event:release"))
            throw new InvalidOperationException("N1: native full-graph self reaction did not reach ordinary work");
        Console.WriteLine("PASS " + Names[0]);
    }
}
