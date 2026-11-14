using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

internal static class PolicyWitness
{
    private static string[] Parents => ["sequence", "parallel", "foreach", "disabled", "unmarked"];
    private static string Origin(string kind) => kind == "disabled" ? "disabled-policy" : kind == "unmarked" ? "unmarked-policy" : "seeded-v6-policy";
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from k in Parents
        select $"recovery:{Origin(k)},{m},parent={k}";
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var kind in Parents)
        {
            RootLeaf.Calls.Clear();
            var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true }; var target = InstanceRef.In(0, "policy-next"); var owner = 0;
            Actor Actor() => world.Of(owner, 0);
            var leaf = Wf.Node<RootLeaf>("leaf", ("Value", Binding.Literal(7)), ("Index", Binding.Literal(0)));
            var work = kind == "foreach" ? Wf.ForEach("work", Binding.Literal(new[] { 7, 9 }), leaf) : kind == "parallel" ? Wf.Parallel("work", null, leaf) : kind == "unmarked" ? Wf.Node<UnmarkedFaultParent>("work").With(leaf) : Wf.Sequence("work", leaf);
            var workflow = Wf.Workflow("policy-next", Wf.Node<AbsorbRecoveryFault>("outer").With(Wf.Sequence("serial", Wf.Event("gate", "start"), work)));
            workflow.InterventionRequested = kind != "disabled"; NativeWorkflows.Registry.Add(workflow);
            void Apply(Act a) { world.Apply(a); if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error); }
            void Drain()
            {
                for (var i = 0; i < 100; i++)
                {
                    var actor = Actor(); Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
                    if (actor.Worker.Ready.Contains(target.Id)) Apply(new("turn", actor.Id, S: target.Id));
                    else if (!actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) return;
                    if (actor.Worker.NeedsFlush) Apply(new("flush", actor.Id)); if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
                }
                throw new InvalidOperationException("R0: policy witness exceeded bounded drain");
            }
            NativeState State() => Json.Read<NativeState>(world.Store.Instances[target.Id].State);
            Apply(new("take", Actor().Id)); Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec(workflow.Id, mode) with { Creation = new(1, 0, "policy-proof") }))); Drain();
            if (kind is "sequence" or "parallel" or "foreach")
            {
                // Deliberately constructed v6 migration context, not a historical writer/binary guarantee.
                var legacy = State(); StatefulPolicy.RemoveNewStatefulPins(legacy); legacy.InterventionPolicy = 0;
                var upgraded = new NativeModel(new()).UpgradeState(Json.Write(legacy), 6);
                world.Store.Instances[target.Id] = world.Store.Instances[target.Id] with { State = Json.Write(upgraded) };
            }
            workflow.InterventionRequested = kind == "disabled"; // latest request must neither enable nor replace saved policy
            Apply(new("crash", owner)); Apply(new("tick", 20_000)); owner = 1; Apply(new("take", Actor().Id)); Drain();
            var wait = world.Store.Subscriptions.Single(s => s.Instance == target.Id && s.Key == "event:start");
            Apply(new("add", M: Events.Deliver(new("event:start", "release", null, world.Time + 120_000), wait))); Drain();
            var state = State(); var expected = kind is "sequence" or "parallel";
            if (state.InterventionPolicy != (kind == "unmarked" ? 4 : 0) || RootLeaf.Calls.Count != 1 ||
                (expected ? world.Store.Instances[target.Id].Status != InstanceStatus.Running || !state.Frames.Values.Any(f => f.Node == "leaf" && f.Intervention is not null)
                    : world.Store.Instances[target.Id].Status != InstanceStatus.Finished || state.Frames.Count != 0))
                throw new InvalidOperationException("R29: latest host or new parent support silently changed an existing invocation policy");
            Console.WriteLine($"PASS recovery:{Origin(kind)},{mode},parent={kind}");
        }
    }
}
// Having a preparation method does not grant interception: explicit versioned parent support is required.
internal sealed class UnmarkedFaultParent : CompositeActivity, IInterventionParent
{
    public UnmarkedFaultParent() { }
    protected internal override void Execute(CompositeContext context) => context.Schedule(context.Children.Single());
    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.Complete();
    protected internal override void OnChildFaulted(CompositeContext context, ChildFaulted child) => context.Complete();
    System.Text.Json.Nodes.JsonNode? IInterventionParent.PrepareRetry(Node definition, System.Text.Json.Nodes.JsonNode? state, RetryChild child) => state?.DeepClone();
}
