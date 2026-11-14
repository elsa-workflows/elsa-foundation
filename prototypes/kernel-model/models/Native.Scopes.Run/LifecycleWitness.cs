using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

internal sealed class LifecycleWitness(string mode, bool nested, bool reverse, bool reload)
{
    private static IEnumerable<(string Mode, bool Nested, bool Reverse, bool Reload)> Cases() =>
        from m in new[] { Modes.Balanced, Modes.Durable } from n in new[] { false, true }
        from r in new[] { false, true } from cold in new[] { false, true } select (m, n, r, cold);
    internal static IEnumerable<string> Names => Cases().Select(c => $"lexical:lifecycle,{c.Mode},nested={c.Nested},reverse={c.Reverse},reload={c.Reload}");
    internal static void Run()
    {
        foreach (var c in Cases()) new LifecycleWitness(c.Mode, c.Nested, c.Reverse, c.Reload).Execute();
        Console.WriteLine("16/16 ordinary lexical lifecycle schedules passed; state-visit attachment remains separate.");
    }
    private readonly World _world = new(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
    private readonly InstanceRef _target = InstanceRef.In(0, "lexical-lifecycle");
    private int _owner, _serial;
    private string _frame = "";
    private Actor Actor => _world.Of(_owner, 0);
    private NativeState Native => Json.Read<NativeState>(_world.Store.Instances[_target.Id].State);
    private ScopeStages.State Session => Native.Frames[_frame].State!.Deserialize<ScopeStages.State>()!;
    private static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
    private string? Value(string key) => Native.Variables.GetValueOrDefault(key)?.ToString();
    private string? Local(string scope, string key) => Native.LexicalScopes![scope].Values.GetValueOrDefault(key)?.ToString();
    private void Execute()
    {
        var children = new List<Node>();
        foreach (var region in new[] { "E", "L" })
        {
            children.Add(Wf.Sequence("entry-" + region,
                Wf.Set(region + "-increment", "counter", Binding.Expr("counter + 1")),
                Wf.Set(region + "-parent", "total", Binding.Expr("total + 1")),
                Wf.ForEach(region + "-loop", Binding.Literal(new[] { 7 }), Wf.Sequence(region + "-body",
                    Wf.Set(region + "-shadow-before", region + "-shadow-before", Binding.Var("CurrentItem")),
                    Wf.Set(region + "-shadow-change", "CurrentItem", 8),
                    Wf.Set(region + "-shadow-after", region + "-shadow-after", Binding.Var("CurrentItem")))),
                Wf.Set(region + "-outside-loop", region + "-outside-loop", Binding.Var("CurrentItem")),
                Wf.Set(region + "-payload", region + "-payload", Binding.Expr("payload.v")),
                Wf.Event(region + "-entry-wait", "entry-" + region)));
            children.Add(Wf.Sequence("action-" + region, Wf.Event(region + "-action-wait", "action-" + region),
                Wf.Set(region + "-action-increment", "counter", Binding.Expr("counter + 1")),
                Wf.Set(region + "-action-parent", "total", Binding.Expr("total + 1")),
                Wf.Node<EchoScopeValue>(region + "-echo", ("Value", Binding.Var("counter"))).Capture(region + "-captured"),
                Wf.Set(region + "-outer", region + "-outer", Binding.Var("outer"))));
            children.Add(Wf.Sequence("exit-" + region, Wf.Event(region + "-exit-wait", "exit-" + region),
                Wf.Set(region + "-exit-value", region + "-exit-value", Binding.Var("counter"))));
        }
        children.Add(Wf.Sequence("reset-E", Wf.Event("reset-wait", "reset-E"), Wf.Set("reset-read", "reset-read", Binding.Var("counter")), Wf.Set("reset-change", "counter", 1)));
        children.Add(Wf.Set("should-not-run", "escaped", true));
        var session = Wf.Node<ScopeStages>("session").With(children.ToArray());
        var root = nested ? Wf.Flowchart("outer", [session, Wf.Set("returned", "returned", true)], [new("session", "returned")]) : session;
        NativeWorkflows.Registry.Add(Wf.Workflow("lexical-lifecycle", root, ("outer", 9)));
        Apply(new("take", Actor.Id)); Add(new("start", _target, Kinds.Start, Start: NativeModel.Spec("lexical-lifecycle", mode) with { Creation = new(1, 0, "scopes") })); Drain();
        _frame = Native.Frames.Values.Single(f => f.Node == "session").Id;
        var first = Session; var parent = first.Parent; var e = first.Scopes["E"]; var l = first.Scopes["L"];
        Check(Native.LexicalScopes is { Count: 3 } && Local(parent, "total") == "12" && Local(e, "counter") == "1" && Local(l, "counter") == "1", "L6: independent lexical entry writes or parent assignment changed");
        foreach (var region in new[] { "E", "L" })
            Check(Value(region + "-shadow-before") == "7" && Value(region + "-shadow-after") == "8" && Value(region + "-outside-loop") == "99" && Value(region + "-payload") == "1",
                "L7: ordinary iteration shadowing, assignment or cloned payload changed");
        Settle(); Check(Session.Phase == "idle-entry", "L6: entry did not finish");
        if (reload) Reload(); Signal("action"); Drain();
        Check(Local(e, "counter") == "1" && Local(l, "counter") == "1", "L6: idle scope or waiting action reset a value");
        Settle();
        Check(Session.Phase == "idle-action" && Local(e, "counter") == "2" && Local(l, "counter") == "2" && Local(parent, "total") == "14", "L6: cross-phase values or shared parent writes changed");
        foreach (var region in new[] { "E", "L" }) Check(Value(region + "-captured") == "2" && Value(region + "-outer") == "9", "L7: normal result capture or outer fallback changed");
        Signal("reset"); Drain(); var fresh = Session.Scopes["E"];
        Check(fresh != e && !Native.LexicalScopes!.ContainsKey(e) && Session.Parent == parent && Session.Scopes["L"] == l && Local(fresh, "counter") == "0" && Local(l, "counter") == "2", "L8: release/re-entry reused a handle or reset a retained scope");
        Settle(); Check(Value("reset-read") == "0" && Local(fresh, "counter") == "1" && Local(parent, "total") == "14", "L8: fresh scope defaults or retained parent changed");
        Signal("exit"); Drain(); Settle();
        Check(Native.LexicalScopes is null && _world.Store.Instances[_target.Id].Status == InstanceStatus.Finished && Value("E-exit-value") == "1" && Value("L-exit-value") == "2" && Value("escaped") is null,
            "L4: lexical exit cleanup, final values or refused work changed");
        if (nested) Check(Value("returned") == "true", "L4: outer workflow did not resume");
        var records = _world.Store.Journal.Values.Where(j => j.Instance == _target.Id).SelectMany(j => Json.Read<string[]>(j.Entries)).ToArray();
        Check(records.Contains("lexical-refused:parent-children") && records.Contains("lexical-refused:queued-work") && records.Contains("lexical-refused:running-work") && records.Contains("lexical-refused:retired-handle"), "L5: lexical lifecycle guard proof was skipped");
        Console.WriteLine($"PASS lexical:lifecycle,{mode},nested={nested},reverse={reverse},reload={reload}");
    }
    private void Settle()
    {
        for (var i = 0; i < 16; i++)
        {
            var waits = _world.Store.Subscriptions.Where(s => s.Instance == _target.Id && s.Key.StartsWith("event:", StringComparison.Ordinal)).ToArray();
            if (waits.Length == 0) return;
            if (reload) Reload();
            var wait = (reverse ? waits.OrderByDescending(s => s.Key, StringComparer.Ordinal) : waits.OrderBy(s => s.Key, StringComparer.Ordinal)).First();
            Add(Events.Deliver(new(wait.Key, "release-" + ++_serial, null, _world.Time + 120_000), wait)); Drain();
        }
        throw new InvalidOperationException("L0: lexical phase exceeded its bound");
    }
    private void Signal(string name) => Add(new("signal-" + ++_serial, _target, CompositeSignal.Kind, Json.Write(new CompositeSignal(_frame, name)), TargetCreation: 1));
    private void Add(Message message) => Apply(new("add", M: message));
    private void Apply(Act action) { _world.Apply(action); if (_world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error); }
    private void Drain()
    {
        for (var i = 0; i < 300; i++)
        {
            Apply(new("poll", Actor.Id)); if (Actor.Worker.NeedsLoad) Apply(new("load", Actor.Id));
            if (Actor.Worker.Ready.Contains(_target.Id)) Apply(new("turn", Actor.Id, S: _target.Id));
            else if (!Actor.Worker.NeedsFlush && !Actor.Worker.NeedsMeet) return;
            if (Actor.Worker.NeedsFlush) Apply(new("flush", Actor.Id)); if (Actor.Worker.NeedsMeet) Apply(new("meet", Actor.Id));
        }
        throw new InvalidOperationException("L0: lexical drain exceeded its bound");
    }
    private void Reload()
    {
        var resources = Json.Write(Native.LexicalScopes);
        Apply(new("crash", _owner)); Apply(new("tick", 20_000)); _owner = 1 - _owner; Apply(new("take", Actor.Id)); Drain();
        Check(Json.Write(Native.LexicalScopes) == resources, "L2: lexical resources changed across a cold-owner restart");
    }
}

internal sealed class ScopeStages : CompositeActivity, ISignalledComposite
{
    public ScopeStages() { }
    internal sealed record State(string Parent, Dictionary<string, string> Scopes, string Phase, Dictionary<string, string> Pending);
    private static Dictionary<string, JsonNode?> Defaults() => new(StringComparer.Ordinal) { ["counter"] = Values.From(0), ["CurrentItem"] = Values.From(99), ["payload"] = new JsonObject { ["v"] = 1 } };
    protected internal override void Execute(CompositeContext context)
    {
        var parent = context.CreateLexicalScope(new Dictionary<string, JsonNode?> { ["total"] = Values.From(10) });
        var values = Defaults(); var e = context.CreateLexicalScope(values, parent); var l = context.CreateLexicalScope(values, parent);
        values["payload"]!["v"] = 999; // The creator cannot mutate either declared resource after allocation.
        var state = new State(parent, new() { ["E"] = e, ["L"] = l }, "entry", []);
        Start(context, state, "entry");
        Refuse(context, () => context.ReleaseLexicalScope(parent), "Lexical scope still has children or work.", "parent-children");
        Refuse(context, () => context.ReleaseLexicalScope(e), "Lexical scope still has children or work.", "queued-work");
    }
    public bool CanAccept(CompositeContext context) => context.ReadState<State>()!.Pending.Count == 0;
    public void Accept(CompositeContext context, string messageId, CompositeSignal signal)
    {
        var state = context.ReadState<State>()!;
        if (signal.Name == "reset")
        {
            var old = state.Scopes["E"]; context.ReleaseLexicalScope(old);
            Refuse(context, () => context.ScheduleInScope(context.Children.Single(n => n.Id == "should-not-run"), old), "Lexical scope is missing or inaccessible.", "retired-handle");
            state.Scopes["E"] = context.CreateLexicalScope(Defaults(), state.Parent);
            Start(context, state, "reset", ["E"]);
        }
        else Start(context, state, signal.Name);
    }
    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        var state = context.ReadState<State>()!;
        if (!state.Pending.Remove(child.Frame)) throw new InvalidOperationException("L6: lexical callback lost its phase owner");
        if (state.Pending.Count != 0)
        {
            var other = state.Pending.Values.First();
            Refuse(context, () => context.ReleaseLexicalScope(state.Scopes[other]), "Lexical scope still has children or work.", "running-work");
        }
        else if (state.Phase == "exit")
        {
            foreach (var scope in state.Scopes.Values) context.ReleaseLexicalScope(scope);
            context.ReleaseLexicalScope(state.Parent); context.Complete(); return;
        }
        else state = state with { Phase = "idle-" + state.Phase };
        context.SetState(state);
    }
    private static void Start(CompositeContext context, State state, string phase, string[]? regions = null)
    {
        foreach (var region in regions ?? ["E", "L"])
            state.Pending.Add(context.ScheduleInScope(context.Children.Single(n => n.Id == phase + "-" + region), state.Scopes[region], region), region);
        context.SetState(state with { Phase = phase });
    }
    private static void Refuse(CompositeContext context, Action action, string message, string name)
    {
        try { action(); }
        catch (InvalidOperationException e) when (e.Message == message) { context.Record("lexical-refused:" + name); return; }
        throw new InvalidOperationException("L5: lexical guard did not refuse: " + name);
    }
}

internal sealed class EchoScopeValue : Activity<JsonNode?>
{
    public EchoScopeValue() { }
    [Input] public JsonNode? Value { get; set; }
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) => ValueTask.FromResult(Complete(Value));
}
