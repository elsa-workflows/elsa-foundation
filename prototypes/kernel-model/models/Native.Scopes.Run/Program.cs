using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

var cases = (from mode in new[] { Modes.Balanced, Modes.Durable } from nested in new[] { false, true } from reload in new[] { false, true }
    select (mode, nested, reload)).ToArray();
string Name((string mode, bool nested, bool reload) c) => $"lexical:cross-phase,{c.mode},nested={c.nested},reload={c.reload}";
if (args is ["--list"]) { foreach (var c in cases) Console.WriteLine(Name(c)); foreach (var name in LifecycleWitness.Names.Concat(SafetyWitness.Names).Concat(OwnershipWitness.Names)) Console.WriteLine(name); return; }
if (args.Length != 0) throw new ArgumentException("Use --list or no argument.");
foreach (var c in cases)
{
    var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
    var target = InstanceRef.In(0, "scope-proof"); var owner = 0;
    Actor Actor() => world.Of(owner, 0);
    var session = Wf.Node<ScopeSession>("session").With(
        Wf.Sequence("entry", Wf.Set("change", "counter", 1), Wf.Set("fallback", "outer-read", Binding.Var("outer")), Wf.Event("wait", "entry-release")),
        Wf.Set("action", "observed", Binding.Var("counter")));
    var root = c.nested ? Wf.Flowchart("flow", [session, Wf.Set("returned", "returned", true)], [new("session", "returned")]) : session;
    NativeWorkflows.Registry.Add(Wf.Workflow("scope-proof", root, ("outer", 7)));
    void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
    void Apply(Act act) { world.Apply(act); if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error); }
    void Drain()
    {
        for (var i = 0; i < 100; i++)
        {
            var actor = Actor();
            Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
            if (actor.Worker.Ready.Contains(target.Id)) Apply(new("turn", actor.Id, S: target.Id));
            else if (!actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) return;
            if (actor.Worker.NeedsFlush) Apply(new("flush", actor.Id)); if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
        }
        throw new InvalidOperationException("L0: scope proof exceeded its bounded drain");
    }
    NativeState State() => Json.Read<NativeState>(world.Store.Instances[target.Id].State);
    Apply(new("take", Actor().Id));
    Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec("scope-proof", c.mode)))); Drain();
    var saved = State();
    Check(saved.LexicalScopes is { Count: 1 }, "L2: lexical scope was not stored in the ordinary native checkpoint");
    var resource = saved.LexicalScopes!.Single(); var frame = saved.Frames.Values.Single(f => f.Node == "session");
    Check(resource.Value.Owner == frame.Id && resource.Value.Values["counter"]!.ToString() == "1", "L2: lexical owner/value changed");
    if (c.reload)
    {
        Apply(new("crash", owner)); Apply(new("tick", 20_000)); owner = 1 - owner; Apply(new("take", Actor().Id)); Drain();
        Check(State().LexicalScopes!.Single().Key == resource.Key && State().LexicalScopes!.Single().Value.Values["counter"]!.ToString() == "1", "L2: lexical handle/value changed on reload");
    }
    var wait = world.Store.Subscriptions.Single(s => s.Instance == target.Id && s.Key == "event:entry-release");
    Apply(new("add", M: Events.Deliver(new("event:entry-release", "release", null, world.Time + 120_000), wait))); Drain();
    var state = State();
    Check(state.Variables.GetValueOrDefault("observed")?.ToString() == "1", "L1: ordinary phase work did not retain the scope value");
    Check(state.Variables["outer-read"]!.ToString() == "7" && !state.Variables.ContainsKey("counter"), "L3: normal fallback or local write ownership changed");
    Check(state.LexicalScopes is null && world.Store.Instances[target.Id].Status == InstanceStatus.Finished, "L4: completed owner leaked a lexical resource");
    if (c.nested) Check(state.Variables["returned"]!.ToString() == "true", "L4: enclosing workflow did not resume");
    Console.WriteLine("PASS " + Name(c));
}
LifecycleWitness.Run();
SafetyWitness.Run();
OwnershipWitness.Run();
Console.WriteLine("8/8 ordinary cross-phase scope schedules passed; StateMachine attachment and broader scope lifecycle remain unqualified.");

// The resource belongs to this live composite; separate ordinary phases share its declared values.
internal sealed class ScopeSession : CompositeActivity
{
    public ScopeSession() { }
    protected internal override void Execute(CompositeContext context)
    {
        var scope = context.CreateLexicalScope(new Dictionary<string, JsonNode?> { ["counter"] = Values.From(0) });
        context.SetState(scope);
        Schedule(context, "entry");
    }
    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        if (child.Node == "entry") Schedule(context, "action");
        else context.Complete();
    }
    private static void Schedule(CompositeContext context, string node) =>
        context.ScheduleInScope(context.Children.Single(n => n.Id == node), context.ReadState<string>()!);
}
