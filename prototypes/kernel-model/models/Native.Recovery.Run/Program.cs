using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

if (args is ["--retry-minimal"]) { RetryWitness.Run(); return; }
if (args is ["--retry"]) { RetryWitness.Run(full: true); return; }
if (args is ["--retry-refusals"]) { RetryWitness.Run(refusals: true); return; }
if (args is ["--list"]) { Console.WriteLine("recovery:ordinary-child-intervention-minimal"); foreach (var name in InterventionWitness.Names.Concat(RecoveryReader.Names).Concat(RetryWitness.Names).Concat(RetryWitness.RefusalNames)) Console.WriteLine(name); return; }
if (args.Length != 0 && args is not ["--minimal"]) throw new ArgumentException("Use --list, --minimal or no argument.");

// A real ordinary native fault, not a seeded incident record. The old runtime ignores the private
// profile request; the named assertion exposes the missing continuing-parent boundary.
var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
var target = InstanceRef.In(0, "native-recovery"); var actor = world.Of(0, 0);
var workflow = Wf.Workflow("native-recovery", Wf.Node<AbsorbRecoveryFault>("outer").With(Wf.Sequence("sequence",
    Wf.Set("before", "before", 1), Wf.Node<RecoveryLeaf>("leaf", ("Value", Binding.Literal(17))),
    Wf.Set("after", "after", 1))));
workflow.InterventionRequested = true; NativeWorkflows.Registry.Add(workflow);
void Apply(Act action)
{
    world.Apply(action);
    if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
}
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
    throw new InvalidOperationException("R0: bounded recovery witness could not drain");
}
Apply(new("take", actor.Id));
Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec("native-recovery", Modes.Balanced) with { Creation = new(1, 0, "native-recovery-proof") }))); Drain();
var row = world.Store.Instances[target.Id]; var state = Json.Read<NativeState>(row.State);
if (row.Status != InstanceStatus.Running || !state.Frames.Values.Any(f => f.Node == "sequence"))
    throw new InvalidOperationException("R1: the ordinary child fault did not preserve its continuing parent");
if (state.Variables["before"]!.ToString() != "1" || state.Variables.ContainsKey("after") || RecoveryLeaf.Observed != 17)
    throw new InvalidOperationException("R2: intervention repeated or advanced ordinary work");
Console.WriteLine("PASS recovery:ordinary-child-intervention-minimal");
if (args.Length == 0) { InterventionWitness.Run(); RecoveryReader.Run(); }

internal sealed class RecoveryLeaf : Kernel.Native.Activity<int>
{
    public RecoveryLeaf() { }
    [Input] public int Value { get; set; }
    internal static int Observed;
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        Observed = Value;
        return ValueTask.FromResult(Fault("expected-intervention", "ordinary recoverable-dependency witness"));
    }
}

// Preserve the simulator's genuine-fault guard: ordinary absorption lets the old workflow finish,
// while the missing intervention still fails the independent continuing-parent assertion.
internal sealed class AbsorbRecoveryFault : CompositeActivity
{
    public AbsorbRecoveryFault() { }
    protected internal override void Execute(CompositeContext context) => context.Schedule(context.Children.Single());
    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.Complete();
    protected internal override void OnChildFaulted(CompositeContext context, ChildFaulted child) => context.Complete();
}
