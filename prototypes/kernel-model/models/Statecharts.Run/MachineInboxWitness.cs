using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

internal static class MachineInboxWitness
{
    internal static void Run()
    {
        var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
        var target = InstanceRef.In(0, "machine-inbox"); var actor = world.Of(0, 0);
        var def = new StructuralDefinition([new("root", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "root")], []);
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(def)),
            ("InputInbox", Binding.Literal(new { Capacity = 3, ReceiptCapacity = 8, RetentionMilliseconds = 120000L })));
        NativeWorkflows.Registry.Add(Wf.Workflow("machine-inbox", machine));
        void Apply(Act act) { world.Apply(act); if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error); }
        void Drain()
        {
            for (var i = 0; i < 100; i++)
            {
                Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
                if (actor.Worker.Ready.Contains(target.Id)) Apply(new("turn", actor.Id, S: target.Id));
                else if (!actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) return;
                if (actor.Worker.NeedsFlush) Apply(new("flush", actor.Id)); if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
            }
            throw new InvalidOperationException("Q0: machine inbox did not settle");
        }
        Apply(new("take", actor.Id));
        Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec("machine-inbox") with { Creation = new(1, 0, "inbox") }))); Drain();
        var state = Json.Read<NativeState>(world.Store.Instances[target.Id].State);
        var frame = state.Frames.Values.Single(f => f.Node == "machine").Id;
        if (state.Inboxes?.ContainsKey(frame) != true) throw new InvalidOperationException("Q1: actual machine did not register its bounded inbox");
        Console.WriteLine("PASS machine-inbox-registration");
    }
}
