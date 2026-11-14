using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

if (args is ["--list"]) { Console.WriteLine("lexical:cross-phase-value"); return; }
if (args.Length != 0) throw new ArgumentException("Use --list or no argument.");
var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
var target = InstanceRef.In(0, "scope-proof"); var actor = world.Of(0, 0);
var session = Wf.Node<ScopeSession>("session").With(
    Wf.Sequence("entry", Wf.Set("change", "counter", 1), Wf.Event("wait", "entry-release")),
    Wf.Set("action", "observed", Binding.Var("counter")));
NativeWorkflows.Registry.Add(Wf.Workflow("scope-proof", session));
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
    throw new InvalidOperationException("L0: scope proof exceeded its bounded drain");
}
Apply(new("take", actor.Id));
Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec("scope-proof", Modes.Durable)))); Drain();
var wait = world.Store.Subscriptions.Single(s => s.Instance == target.Id && s.Key == "event:entry-release");
Apply(new("add", M: Events.Deliver(new("event:entry-release", "release", null, world.Time + 120_000), wait))); Drain();
var state = Json.Read<NativeState>(world.Store.Instances[target.Id].State);
if (state.Variables.GetValueOrDefault("observed")?.ToString() != "1")
    throw new InvalidOperationException("L1: ordinary phase work did not retain the scope value");
Console.WriteLine("PASS lexical:cross-phase-value");

// Ordinary child scopes are copied; later phases cannot reuse a completed child's local dictionary.
internal sealed class ScopeSession : CompositeActivity
{
    public ScopeSession() { }
    protected override void Execute(CompositeContext context)
    {
        context.SetState("entry");
        Schedule(context, "entry");
    }
    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        if (child.Node == "entry") Schedule(context, "action");
        else context.Complete();
    }
    private static void Schedule(CompositeContext context, string node) =>
        context.Schedule(context.Children.Single(n => n.Id == node), scope: new Dictionary<string, JsonNode?> { ["counter"] = Values.From(0) });
}
