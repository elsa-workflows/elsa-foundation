using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

internal sealed class ScopedWorkflows(string mode, bool nested, bool reverse)
{
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from n in new[] { false, true }
        from scenario in new[] { "lifecycle", "parallel", "nested-shadow", "cancel", "fault" }
        from r in scenario == "parallel" ? new[] { false, true } : new[] { false } select $"scoped:{scenario},{m},nested={n},reverse={r}";
    internal static void Run()
    {
        foreach (var m in new[] { Modes.Balanced, Modes.Durable }) foreach (var n in new[] { false, true })
        {
            new ScopedWorkflows(m, n, false).Lifecycle();
            foreach (var r in new[] { false, true }) new ScopedWorkflows(m, n, r).Parallel();
            new ScopedWorkflows(m, n, false).NestedShadow();
            new ScopedWorkflows(m, n, false).Dispose(false);
            new ScopedWorkflows(m, n, false).Dispose(true);
        }
        Console.WriteLine("24 actual scoped workflows passed; incident/retry, dynamic defaults and product/provider/Studio support remain open.");
    }
    private readonly World _world = new(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
    private readonly InstanceRef _target = InstanceRef.In(0, "scoped-workflow");
    private int _owner, _serial;
    private string _frame = "";
    private Actor Actor => _world.Of(_owner, 0);
    private NativeState Native => Json.Read<NativeState>(_world.Store.Instances[_target.Id].State);
    private MachineState? State => Native.Frames.TryGetValue(_frame, out var f) ? f.State!.Deserialize<MachineState>() : null;
    private JsonObject Resources => JsonSerializer.SerializeToNode(Native)!["LexicalScopes"]?.AsObject() ?? [];
    private static Dictionary<string, JsonNode?> D(params (string Name, object? Value)[] values) => values.ToDictionary(p => p.Name, p => Values.From(p.Value), StringComparer.Ordinal);
    private static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
    private static Node Inc(string id, string name, int by = 1) => Wf.Set(id, name, Binding.Expr(name + " + " + by));
    private string Handle(string state) => State!.Scopes!.Visits[State.Visits!.Single(v => v.State == state).Occurrence];
    private JsonObject ValuesAt(string handle) => Resources[handle]!["Values"]!.AsObject();
    private string Value(string state, string name) => ValuesAt(Handle(state))[name]!.ToString();
    private string? Observation(string name) => Native.Variables.GetValueOrDefault(name)?.ToString();
    private void Start(StructuralDefinition definition, params Node[] work)
    {
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(definition))).With(work);
        StartNode(machine);
    }
    private void StartNode(Node machine)
    {
        var root = nested ? Wf.Flowchart("outer", [machine, Wf.Set("returned", "returned", true)], [new("machine", "returned")]) : machine;
        NativeWorkflows.Registry.Add(Wf.Workflow("scoped-workflow", root, ("outer", 99)));
        Apply(new("take", Actor.Id));
        Add(new("start", _target, Kinds.Start, Start: NativeModel.Spec("scoped-workflow", mode))); Drain();
        _frame = Native.Frames.Values.Single(f => f.Node == "machine").Id;
        Inspect();
    }
    private void Pass(string scenario) => Console.WriteLine($"PASS scoped:{scenario},{mode},nested={nested},reverse={reverse}");
    private void Lifecycle()
    {
        var def = new StructuralDefinition([
            new("root", NodeKind.Root, Initial: "Review"),
            new("Review", NodeKind.Compound, "root", "A", "parent-entry", "parent-exit", D(("label", "parent"), ("p", 0))),
            new("A", NodeKind.Atomic, "Review", Entry: "a-entry", Exit: "a-exit", Defaults: D(("label", "child"), ("a", 0), ("item", 42))),
            new("B", NodeKind.Atomic, "Review", Entry: "b-entry", Defaults: D(("label", "sibling"))),
            new("Done", NodeKind.Final, "root", Entry: "final-entry", Defaults: D(("label", "final")))],
            [new("comment", 0, "A", null, VisitMode.Internal, "comment", "comment"),
             new("self", 1, "A", "A", VisitMode.Reenter, "self", "self-action"),
             new("preserve", 2, "Review", "B", VisitMode.Preserve, "preserve", "parent-action"),
             new("restart", 3, "Review", "A", VisitMode.Reenter, "restart", "parent-action"),
             new("finish", 4, "Review", "Done", VisitMode.Reenter, "finish", "parent-action")], D(("count", 0), ("label", "machine")));
        Start(def,
            Wf.Sequence("parent-entry", Wf.Set("parent-change", "p", 11), Wf.Set("parent-label", "label", "parent-entered"), Wf.Event("parent-wait", "parent")),
            Wf.Sequence("parent-exit", Wf.Set("exit-parent-read", "exit-parent", Binding.Var("label")), Wf.Event("parent-exit-wait", "parent-exit")),
            Wf.Sequence("a-entry", Wf.Set("a-label", "entry-label", Binding.Var("label")), Wf.Set("a-parent", "entry-parent", Binding.Var("p")),
                Wf.ForEach("iteration", Binding.Literal(new[] { 7 }), Wf.Set("iteration-read", "iteration-value", Binding.Var("item"))),
                Wf.Set("a-default-read", "entry-default", Binding.Var("a")), Wf.Set("a-change", "a", 7), Inc("count-entry", "count"), Wf.Event("a-wait", "a")),
            Wf.Sequence("a-exit", Inc("a-exit-change", "a"), Wf.Event("a-exit-wait", "a-exit")),
            Wf.Sequence("comment", Inc("comment-inc", "a"), Wf.Event("comment-wait", "comment")),
            Wf.Sequence("self-action", Wf.Set("source-read", "source-value", Binding.Var("a")), Wf.Event("self-wait", "action")),
            Wf.Sequence("parent-action", Wf.Set("parent-action-read", "action-parent", Binding.Var("label")), Inc("parent-action-inc", "p"), Wf.Event("parent-action-wait", "action")),
            Wf.Sequence("b-entry", Wf.Set("b-read", "target-label", Binding.Var("label")), Wf.Set("b-parent", "target-parent", Binding.Var("p")), Wf.Event("b-wait", "b")),
            Wf.Sequence("final-entry", Wf.Set("final-read", "final-label", Binding.Var("label")), Wf.Set("final-machine", "final-count", Binding.Var("count")), Wf.Event("final-wait", "final")));
        Check(State!.Scopes!.Visits.Count == 1 && State.PlannedVisits!.Length == 2, "V3: child values initialized before parent Entry completed");
        var machine = State.Scopes.Machine;
        Reload(); Release("parent");
        Check(Observation("entry-label") == "child" && Observation("entry-parent") == "11" && Observation("entry-default") == "0" && Observation("iteration-value") == "7",
            "V1: normal child/parent/ForEach lexical lookup failed");
        Settle(); var parent = Handle("Review"); var a = Handle("A");
        for (var i = 0; i < 2; i++) { Signal("comment"); Reload(); Settle(); }
        Check(Handle("A") == a && Handle("Review") == parent && Value("A", "a") == "9", "V4: internal reactions reset retained values or handles");
        Signal("self");
        Check(State!.Scopes!.Visits.Values.Contains(a) && !State.Scopes.Retired, "V2: source values retired before Exit completed");
        Reload(); Release("a-exit");
        Check(Observation("source-value") == "10" && Resources.ContainsKey(a) && !State!.Scopes!.Retired,
            "V2: Action did not retain exact source values after Exit");
        var during = Json.Write(State.Scopes); Reload(); Check(Json.Write(State!.Scopes) == during, "V5: resumed Action reset its source environment");
        Release("action");
        Check(!Resources.ContainsKey(a) && Observation("entry-default") == "0", "V6: exited scope leaked or re-entry did not initialize fresh defaults");
        Settle(); Check(Handle("A") != a && Handle("Review") == parent && State!.Scopes!.Machine == machine && Value("Review", "p") == "11", "V4: self re-entry reset parent or machine");
        Signal("preserve"); Reload(); Release("a-exit");
        Check(Observation("action-parent") == "parent-entered" && Value("Review", "p") == "12", "V2: parent Action used active child or machine environment");
        Release("action");
        Check(Observation("target-label") == "sibling" && Observation("target-parent") == "12", "V3: target Entry missed preceding source Action write");
        Settle(); Check(Handle("Review") == parent, "V4: preserved parent environment changed");
        Signal("restart"); Settle();
        Check(Handle("Review") != parent && Value("Review", "p") == "11" && Value("A", "a") == "7" && State!.Scopes!.Machine == machine,
            "V4: parent re-entry failed to reset visits while preserving machine values");
        Signal("finish");
        Release("a-exit"); Release("parent-exit"); Release("action");
        Check(State!.Pending is not null && Resources.Count == 2 && Observation("final-label") == "final" && Observation("final-count") == "3",
            "V7: final Entry lost its scope, leaked exits or completed before durable work");
        Reload(); Release("final");
        Check(State is null && Resources.Count == 0 && _world.Store.Instances[_target.Id].Status == InstanceStatus.Finished, "V7: final completion leaked resources or did not return");
        if (nested) Check(Observation("returned") == "true", "V7: final nested machine did not return");
        Pass("lifecycle");
    }

    private void Parallel()
    {
        var nodes = new GraphNode[] { new("root", NodeKind.Root, Initial: "P"), new("P", NodeKind.Parallel, "root", Defaults: D(("parent", 3))),
            new("E", NodeKind.Region, "P", "E0"), new("L", NodeKind.Region, "P", "L0"),
            new("E0", NodeKind.Atomic, "E", Exit: "exit-E", Defaults: D(("local", 10))), new("E1", NodeKind.Atomic, "E", Entry: "entry-E", Defaults: D(("local", 20))),
            new("L0", NodeKind.Atomic, "L", Exit: "exit-L", Defaults: D(("local", 100))), new("L1", NodeKind.Atomic, "L", Entry: "entry-L", Defaults: D(("local", 200))) };
        var work = new List<Node>();
        foreach (var region in new[] { "E", "L" })
        {
            work.Add(Wf.Sequence("exit-" + region, Inc("exit-inc-" + region, "local"), Wf.Event("exit-wait-" + region, "exit-" + region)));
            work.Add(Wf.Sequence("action-" + region, Wf.Set("action-read-" + region, "source-" + region, Binding.Var("local")), Wf.Event("action-wait-" + region, "action-" + region)));
            work.Add(Wf.Sequence("entry-" + region, Wf.Set("entry-read-" + region, "target-" + region, Binding.Var("local")), Wf.Event("entry-wait-" + region, "entry-" + region)));
        }
        Start(new(nodes, [new("e", 0, "E0", "E1", VisitMode.Reenter, "advance", "action-E"), new("l", 1, "L0", "L1", VisitMode.Reenter, "advance", "action-L"),
            new("reset", 2, "P", "E0", VisitMode.Reenter, "reset")]), work.ToArray());
        var parent = Handle("P"); var old = State!.Scopes!.Visits.Values.ToArray();
        Signal("advance"); Check(Waiting().Length == 2, "V8: independent exits did not run concurrently");
        Settle();
        Check(Observation("source-E") == "11" && Observation("source-L") == "101" && Observation("target-E") == "20" && Observation("target-L") == "200", "V2: regional source/target scopes aliased");
        Check(Handle("P") == parent && old.Where(h => h != parent).All(h => !Resources.ContainsKey(h)), "V6: regional exit retirement or preserved parent failed");
        Signal("reset"); Settle();
        Check(Handle("P") != parent && Value("E0", "local") == "10" && Value("L0", "local") == "100" && Resources.Count == 4,
            "V4: whole parallel re-entry did not reset all regions");
        Pass("parallel");
    }

    private void NestedShadow()
    {
        var inner = Wf.Node<StateMachine>("inner", ("StructuralDefinition", Binding.Literal(new StructuralDefinition([
            new("r", NodeKind.Root, Initial: "End"), new("End", NodeKind.Final, "r", Entry: "inner-entry", Defaults: D(("label", "inner")))], [], D(("own", 5))))))
            .With(Wf.Sequence("inner-entry", Wf.Set("inner-label-read", "inner-label", Binding.Var("label")), Wf.Set("inner-own-read", "inner-own", Binding.Var("own")),
                Wf.Set("inner-outer-read", "inner-outer", Binding.Var("p")), Wf.Set("inner-global-read", "inner-global", Binding.Var("outer")),
                Wf.ForEach("inner-loop", Binding.Literal(new[] { 8 }), Wf.Set("inner-loop-read", "inner-item", Binding.Var("item"))), Wf.Event("inner-wait", "inner")));
        Start(new([new("r", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "r", Entry: "outer-entry", Defaults: D(("label", "outer"), ("p", 12), ("item", 42)))], []),
            Wf.Sequence("outer-entry", inner, Wf.Set("after-inner-read", "after-inner", Binding.Var("label"))));
        Check(Observation("inner-label") == "inner" && Observation("inner-own") == "5" && Observation("inner-outer") == "12" && Observation("inner-global") == "99" && Observation("inner-item") == "8",
            "V9: nested machine or ordinary iteration broke lexical shadow/fallback");
        Check(Resources.Count == 4, "V9: nested machine resources were copied, lost or aliased");
        Reload(); Release("inner");
        Check(Observation("after-inner") == "outer" && Resources.Count == 2, "V9: nested completion disposed outer resources or leaked inner resources");
        Pass("nested-shadow");
    }

    private void Dispose(bool fault)
    {
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(new StructuralDefinition([
            new("r", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "r", Entry: "work", Defaults: D(("local", 1)))], []))))
            .With(Wf.Sequence("work", Wf.Event("dispose-wait", "dispose"), Wf.Node<ScopedFailure>("fail")));
        var root = fault ? Wf.Node<CatchScopedFailure>("catch").With(machine) : Wf.Parallel("cancel-parent", 1, machine, Wf.Event("winner", "cancel"));
        StartNode(root); Reload(); Release(fault ? "dispose" : "cancel");
        Check(Resources.Count == 0 && _world.Store.Instances[_target.Id].Status == InstanceStatus.Finished, "V10: ordinary cancellation/fault owner disposal leaked resources");
        Pass(fault ? "fault" : "cancel");
    }

    private string[] Waiting() => _world.Store.Subscriptions.Where(s => s.Instance == _target.Id && s.Key.StartsWith("event:", StringComparison.Ordinal)).Select(s => s.Key).ToArray();
    private void Settle()
    {
        for (var i = 0; i < 40; i++) { var waits = Waiting(); if (waits.Length == 0) return; Reload(); Release((reverse ? waits.OrderDescending(StringComparer.Ordinal) : waits.Order(StringComparer.Ordinal)).First()[6..]); }
        throw new InvalidOperationException("V0: scope workflow did not settle within bounded work");
    }
    private void Release(string key)
    {
        var sub = _world.Store.Subscriptions.Single(s => s.Instance == _target.Id && s.Key == "event:" + key);
        Add(Events.Deliver(new(sub.Key, "release-" + ++_serial, null, _world.Time + 120_000), sub)); Drain();
    }
    private void Signal(string name) { Add(new("signal-" + ++_serial, _target, CompositeSignal.Kind, Json.Write(new CompositeSignal(_frame, name)))); Drain(); }
    private void Reload()
    {
        var before = JsonSerializer.SerializeToNode(Native)!["LexicalScopes"]?.ToJsonString();
        Apply(new("crash", _owner)); Apply(new("tick", 20_000)); _owner = 1 - _owner; Apply(new("take", Actor.Id)); Drain();
        Check(JsonSerializer.SerializeToNode(Native)!["LexicalScopes"]?.ToJsonString() == before, "V5: cold-owner reload changed scope identity/values");
    }
    private void Add(Message message) => Apply(new("add", M: message));
    private void Apply(Act action)
    {
        _world.Apply(action); if (_world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
        if (_frame.Length != 0) Inspect();
    }
    private void Inspect()
    {
        if (State is not { Scopes: { } scopes } state) return;
        var resources = Resources;
        Check(resources[scopes.Machine]?["Owner"]?.ToString() == _frame, "V6: machine resource owner/reference missing");
        Check(scopes.Visits.Values.All(h => resources[h]?["Owner"]?.ToString() == _frame), "V6: visit resource owner/reference missing");
        var owned = resources.Where(p => p.Value!["Owner"]!.ToString() == _frame).Select(p => p.Key);
        Check(owned.ToHashSet().SetEquals(scopes.Visits.Values.Append(scopes.Machine)), "V6: committed StateMachine resources leak outside the handle map");
        if (state.Pending is null) Check(scopes.Visits.Count == state.Visits!.Length, "V6: settled visit resources are incomplete");
    }
    private void Drain()
    {
        for (var i = 0; i < 300; i++)
        {
            Apply(new("poll", Actor.Id)); if (Actor.Worker.NeedsLoad) Apply(new("load", Actor.Id));
            if (Actor.Worker.Ready.Contains(_target.Id)) Apply(new("turn", Actor.Id, S: _target.Id));
            else if (!Actor.Worker.NeedsFlush && !Actor.Worker.NeedsMeet) return;
            if (Actor.Worker.NeedsFlush) Apply(new("flush", Actor.Id));
            if (Actor.Worker.NeedsMeet) Apply(new("meet", Actor.Id));
        }
        throw new InvalidOperationException("V0: bounded native scope drain exceeded");
    }
}

internal sealed class ScopedFailure : CodeActivity
{
    public ScopedFailure() { }
    protected override void Execute(ActivityContext context) => context.Fault(new("scope-proof", "expected"));
}
internal sealed class CatchScopedFailure : CompositeActivity
{
    public CatchScopedFailure() { }
    protected override void Execute(CompositeContext context) => context.Schedule(context.Children.Single());
    protected override void OnChildFaulted(CompositeContext context, ChildFaulted child) => context.Complete();
}
