using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

internal static class ScopedWitness
{
    // Actual workflow, using an unknown-to-the-old-reader declaration field to establish the missing bridge.
    internal static void Red()
    {
        var graph = JsonSerializer.SerializeToNode(new StructuralDefinition([
            new("root", NodeKind.Root, Initial: "Review"), new("Review", NodeKind.Atomic, "root", Entry: "entry")], []))!;
        graph["Nodes"]![1]!["Defaults"] = new JsonObject { ["local"] = 42 };
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(graph)))
            .With(Wf.Set("entry", "observed", Binding.Var("local")));
        NativeWorkflows.Registry.Add(Wf.Workflow("scoped-red", machine));
        var world = new World(new Config(1, 1, Flaw.None, Native: true)) { Auto = true };
        var target = InstanceRef.In(0, "scoped-red"); var actor = world.Of(0, 0);
        void Apply(Act action) { world.Apply(action); if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error); }
        Apply(new("take", actor.Id)); Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec("scoped-red", Modes.Durable))));
        for (var i = 0; i < 100; i++)
        {
            Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
            if (actor.Worker.Ready.Contains(target.Id)) Apply(new("turn", actor.Id, S: target.Id));
            else if (!actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) break;
            if (actor.Worker.NeedsFlush) Apply(new("flush", actor.Id));
            if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
        }
        var state = Json.Read<NativeState>(world.Store.Instances[target.Id].State);
        if (state.Variables.GetValueOrDefault("observed")?.ToString() != "42")
            throw new InvalidOperationException("V1: StateMachine Entry did not use its declared state-visit environment");
        Console.WriteLine("PASS scoped-red: ordinary Entry reads its declared visit value");
    }
}
