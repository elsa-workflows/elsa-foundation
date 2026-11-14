using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

internal sealed class InterventionWitness
{
    private static IEnumerable<(string Mode, bool Parallel, bool Nested, bool Reverse, bool Reload)> Cases =>
        from m in new[] { Modes.Balanced, Modes.Durable } from p in new[] { false, true }
        from n in new[] { false, true } from r in new[] { false, true } from l in new[] { false, true } select (m, p, n, r, l);
    private static string Name((string Mode, bool Parallel, bool Nested, bool Reverse, bool Reload) c) =>
        $"recovery:intervention,{c.Mode},parallel={c.Parallel},nested={c.Nested},reverse={c.Reverse},reload={c.Reload}";
    internal static IEnumerable<string> Names => Cases.Select(Name).Concat(from m in new[] { Modes.Balanced, Modes.Durable }
        from k in new[] { "legacy", "custom-absorption", "unbound-input" } select $"recovery:policy,{m},{k}");
    internal static void Run()
    {
        foreach (var c in Cases) new InterventionWitness().Execute(c);
        foreach (var m in new[] { Modes.Balanced, Modes.Durable }) foreach (var k in new[] { "legacy", "custom-absorption", "unbound-input" })
            new InterventionWitness().Policy(m, k);
        Console.WriteLine("38/38 actual ordinary intervention schedules passed; operator retry and broader activation remain unqualified.");
    }
    private readonly World _world = new(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
    private readonly InstanceRef _target = InstanceRef.In(0, "intervention");
    private int _owner;
    private Actor Actor => _world.Of(_owner, 0);
    private NativeState State => Json.Read<NativeState>(_world.Store.Instances[_target.Id].State);
    private void Check(bool ok, string code) { if (!ok) throw new InvalidOperationException(code); }
    private void Apply(Act action)
    {
        _world.Apply(action);
        if (_world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
    }
    private void Drain()
    {
        for (var i = 0; i < 100; i++)
        {
            Apply(new("poll", Actor.Id)); if (Actor.Worker.NeedsLoad) Apply(new("load", Actor.Id));
            if (Actor.Worker.Ready.Contains(_target.Id)) Apply(new("turn", Actor.Id, S: _target.Id));
            else if (!Actor.Worker.NeedsFlush && !Actor.Worker.NeedsMeet) return;
            if (Actor.Worker.NeedsFlush) Apply(new("flush", Actor.Id));
            if (Actor.Worker.NeedsMeet) Apply(new("meet", Actor.Id));
        }
        throw new InvalidOperationException("R0: intervention exceeded its bounded drain");
    }
    private void Start(Workflow workflow, string mode)
    {
        MutableFault.Calls = 0; NativeWorkflows.Registry.Add(workflow); Apply(new("take", Actor.Id));
        Apply(new("add", M: new("start", _target, Kinds.Start, Start: NativeModel.Spec(workflow.Id, mode) with { Creation = new(1, 0, "intervention-proof") })));
        Drain();
    }
    private void Execute((string Mode, bool Parallel, bool Nested, bool Reverse, bool Reload) c)
    {
        var fault = Wf.Node<MutableFault>("fault", ("Value", Binding.Var("local")), ("Data", Binding.Literal(new JsonObject { ["kept"] = 9 })));
        var before = Wf.Set("before", "before", 1);
        var sibling = Wf.Sequence("sibling", before, Wf.Event("wait", "release"), Wf.Set("sibling-done", "sibling-done", 1));
        var children = c.Parallel ? (c.Reverse ? new[] { sibling, fault } : new[] { fault, sibling }) :
            (c.Reverse ? new[] { before, fault, Wf.Set("after", "after", 1) } : new[] { fault, before });
        var parent = c.Parallel ? Wf.Parallel("parent", null, children) : Wf.Sequence("parent", children);
        var session = Wf.Node<InterventionScope>("session").With(parent);
        var root = c.Nested ? Wf.Sequence("surrounding", session, Wf.Set("returned", "returned", 1)) : session;
        var workflow = Wf.Workflow("intervention", root); workflow.InterventionRequested = true;
        Start(workflow, c.Mode);
        var state = State; var source = state.Frames.Values.SingleOrDefault(f => f.Node == "fault") ?? throw new InvalidOperationException("R3: intervention lost its source or continuing parent");
        Check(_world.Store.Instances[_target.Id].Status == InstanceStatus.Running && source.Intervention is not null && state.Frames.Values.Any(f => f.Node == "parent"),
            "R3: intervention lost its source or continuing parent");
        Check(MutableFault.Calls == 1 && source.RecoveryInputs!["Value"]!.ToString() == "17" && source.RecoveryInputs["Data"]!["kept"]!.ToString() == "9" &&
            source.OperationKey == CreationProtocol.Origin(_target.Id, 1) + "/activity/" + source.Id,
            "R4: input pinning, invocation count or logical operation identity changed");
        Check(state.LexicalScopes is { Count: 1 } && state.LexicalScopes.Single().Value.Values["local"]!.ToString() == "17" && !state.Variables.ContainsKey("after") && !state.Variables.ContainsKey("returned"),
            "R5: intervention disposed source values or advanced blocked work");
        var retained = Json.Write(source); var resources = Json.Write(state.LexicalScopes);
        if (c.Reload)
        {
            workflow.InterventionRequested = false; // effective policy belongs to the saved invocation, not latest options
            Apply(new("crash", _owner)); Apply(new("tick", 20_000)); _owner = 1 - _owner; Apply(new("take", Actor.Id)); Drain();
            Check(State.InterventionEnabled && Json.Write(State.Frames[source.Id]) == retained && Json.Write(State.LexicalScopes) == resources && MutableFault.Calls == 1,
                "R6: cold owner changed intervention policy, source material or live values");
        }
        if (c.Parallel)
        {
            var wait = _world.Store.Subscriptions.Single(s => s.Instance == _target.Id && s.Key == "event:release");
            Apply(new("add", M: Events.Deliver(new("event:release", "release", null, _world.Time + 120_000), wait))); Drain();
            state = State; var progress = Values.To<ParallelState>(state.Frames.Values.Single(f => f.Node == "parent").State)!;
            Check(state.Variables["sibling-done"]!.ToString() == "1" && progress.Done == 1 && progress.Faulted == 0 && !progress.Joined && progress.Pending.SequenceEqual(new[] { source.Id }) &&
                Json.Write(state.Frames[source.Id]) == retained && MutableFault.Calls == 1,
                "R7: intervention cancelled or blocked an independent sibling or changed the parent obligation");
        }
        // Existing native cancellation owns the retained frame and ancestor scopes. A stale continuation cannot reopen them.
        Apply(new("add", M: new("cancel", _target, NativeKinds.Cancel, TargetCreation: 1))); Drain();
        Apply(new("add", M: new("stale", _target, NativeKinds.Continue, TargetCreation: 1))); Drain(); state = State;
        Check(_world.Store.Instances[_target.Id].Status == InstanceStatus.Cancelled && state.Frames.Count == 0 && state.Waits.Count == 0 && state.Agenda.Count == 0 && state.LexicalScopes is null && MutableFault.Calls == 1,
            "R8: ordinary cancellation leaked or resurrected intervention work");
        Console.WriteLine("PASS " + Name(c));
    }
    private void Policy(string mode, string kind)
    {
        var leaf = kind == "unbound-input" ? Wf.Node<MutableFault>("fault") : Wf.Node<MutableFault>("fault", ("Value", Binding.Literal(17)), ("Data", Binding.Literal(new JsonObject())));
        var root = Wf.Node<AbsorbRecoveryFault>("absorber").With(kind == "custom-absorption" ? leaf : Wf.Sequence("parent", leaf));
        var workflow = Wf.Workflow("intervention-policy", root); workflow.InterventionRequested = kind != "legacy"; Start(workflow, mode);
        Check(_world.Store.Instances[_target.Id].Status == InstanceStatus.Finished && State.Frames.Count == 0 && MutableFault.Calls == 1,
            "R9: intervention changed legacy, explicit absorption or unsupported unbound-input fault routing");
        Console.WriteLine($"PASS recovery:policy,{mode},{kind}");
    }
}

internal sealed class MutableFault : Kernel.Native.Activity<int>
{
    public MutableFault() { }
    [Input] public int Value { get; set; }
    [Input] public JsonObject Data { get; set; } = new();
    internal static int Calls;
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        Calls++; Data["kept"] = -1;
        return ValueTask.FromResult(Fault("intervene", "ordinary dependency failed"));
    }
}
internal sealed class InterventionScope : CompositeActivity
{
    public InterventionScope() { }
    protected internal override void Execute(CompositeContext context) => context.ScheduleInScope(context.Children.Single(),
        context.CreateLexicalScope(new Dictionary<string, JsonNode?> { ["local"] = Values.From(17) }));
    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.Complete();
    protected internal override void OnChildFaulted(CompositeContext context, ChildFaulted child) => context.Complete();
}
