using System.Text.Json;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

// Actual native workflow writes/reloads. These visit records do not establish lexical state-local scopes.
internal sealed class NativeGraphWitness(string mode, bool nested, string phase, bool reverse, NativeGraphWitness.Profile profile)
{
    internal sealed record Profile(string Name, GraphNode[] Nodes, GraphRule[] Rules, string[] Selected, string[] Next, string[] Kept, bool Final = false);
    private static GraphRule R(string id, int order, string source, string? target, VisitMode mode = VisitMode.Reenter, string? when = null) =>
        new(id, order, source, target, mode, "go", When: when);
    private static GraphNode[] Simple() => [new("root", NodeKind.Root, Initial: "Review"), new("Review", NodeKind.Compound, "root", "Checking"),
        new("Checking", NodeKind.Atomic, "Review"), new("Changes", NodeKind.Atomic, "Review"), new("InnerFinal", NodeKind.Final, "Review"),
        new("Other", NodeKind.Compound, "Review", "Deep"), new("Deep", NodeKind.Atomic, "Other"), new("Done", NodeKind.Final, "root")];
    private static GraphNode[] Parallel(bool single = false) => [new("root", NodeKind.Root, Initial: "P"), new("P", NodeKind.Parallel, "root"),
        new("E", NodeKind.Region, "P", "E0"), new("E0", NodeKind.Atomic, "E"), new("E1", NodeKind.Atomic, "E"),
        .. single ? [] : new GraphNode[] { new("L", NodeKind.Region, "P", "L0"), new("L0", NodeKind.Atomic, "L"), new("L1", NodeKind.Atomic, "L") },
        new("Outside", NodeKind.Atomic, "root"), new("Done", NodeKind.Final, "root")];
    private static IEnumerable<Profile> Profiles()
    {
        yield return new("atomic-self", Simple(), [R("self", 0, "Checking", "Checking")], ["self"], ["Review", "Checking"], ["Review"]);
        yield return new("compound-self", Simple(), [R("self", 0, "Review", "Review")], ["self"], ["Review", "Checking"], []);
        yield return new("child-first", Simple(), [R("parent", 0, "Review", "Done"), R("child", 1, "Checking", "Changes")], ["child"], ["Review", "Changes"], ["Review"]);
        yield return new("false-child-fallback", Simple(), [R("parent", 0, "Review", "Done"), R("child", 1, "Checking", "Changes", when: "allow")], ["parent"], ["Done"], [], true);
        yield return new("compound-preserve", Simple(), [R("p", 0, "Review", "Changes", VisitMode.Preserve)], ["p"], ["Review", "Changes"], ["Review"]);
        yield return new("compound-reenter", Simple(), [R("p", 0, "Review", "Changes")], ["p"], ["Review", "Changes"], []);
        yield return new("internal", Simple(), [R("i", 0, "Checking", null, VisitMode.Internal)], ["i"], ["Review", "Checking"], ["Review", "Checking"]);
        yield return new("cousin", Simple(), [R("c", 0, "Checking", "Deep")], ["c"], ["Review", "Other", "Deep"], ["Review"]);
        yield return new("nested-final", Simple(), [R("f", 0, "Checking", "InnerFinal")], ["f"], ["Review", "InnerFinal"], ["Review"]);
        yield return new("top-final", Simple(), [R("f", 0, "Review", "Done")], ["f"], ["Done"], [], true);
        yield return new("proposed-ancestor-compound", Simple(), [R("a", 0, "Checking", "Review")], ["a"], ["Review", "Checking"], []);
        yield return new("parallel-target", Parallel(), [R("p", 0, "P", "E1")], ["p"], ["P", "E1", "L0"], []);
        yield return new("single-parallel-target", Parallel(true), [R("p", 0, "P", "E1")], ["p"], ["P", "E1"], []);
        yield return new("parallel-self", Parallel(), [R("p", 0, "P", "P")], ["p"], ["P", "E0", "L0"], []);
        yield return new("compatible-regions", Parallel(), [R("e", 0, "E0", "E1"), R("l", 1, "L0", "L1")], ["e", "l"], ["P", "E1", "L1"], ["P"]);
        yield return new("local-preempts-container", Parallel(), [R("e", 0, "E0", "E1"), R("l", 1, "L0", "Outside")], ["e"], ["P", "E1", "L0"], ["P", "L0"]);
        yield return new("container-preempts-local", Parallel(), [R("e", 1, "E0", "E1"), R("l", 0, "L0", "Outside")], ["l"], ["Outside"], []);
        yield return new("internal-preempts-container", Parallel(), [R("e", 0, "E0", null, VisitMode.Internal), R("l", 1, "L0", "Outside")], ["e"], ["P", "E0", "L0"], ["P", "E0", "L0"]);
        yield return new("container-preempts-internal", Parallel(), [R("e", 1, "E0", null, VisitMode.Internal), R("l", 0, "L0", "Outside")], ["l"], ["Outside"], []);
        yield return new("proposed-ancestor-parallel", Parallel(), [R("a", 0, "E0", "P")], ["a"], ["P", "E0", "L0"], []);
    }
    private static IEnumerable<(string Mode, bool Nested, string Phase, bool Reverse, Profile Profile)> Cases() =>
        from m in new[] { Modes.Balanced, Modes.Durable } from n in new[] { false, true }
        from p in Profiles() from phase in new[] { "exit", "transition", "entry" }
        where phase == "transition" || p.Name is not ("internal" or "internal-preempts-container")
        from reverse in new[] { false, true } select (m, n, phase, reverse, p);
    private static string Name(string m, bool n, string p, bool r, Profile profile) => $"native-graph:{profile.Name},{m},nested={n},{p},reverse={r}";
    private static string[] Refusals => ["missing-profile", "both-profiles", "unknown-entry", "unknown-exit", "unknown-action", "dynamic-profile"];
    internal static IEnumerable<string> Names => Cases().Select(c => Name(c.Mode, c.Nested, c.Phase, c.Reverse, c.Profile))
        .Concat(Refusals.Select(n => "native-refusal:" + n)).Concat(NativeGraphReaders.Names)
        .Concat(from m in new[] { Modes.Balanced, Modes.Durable } from v in new[] { 1, 2 } select $"identity-consumption:literal-v{v},{m}");
    internal static void Run()
    {
        foreach (var c in Cases()) new NativeGraphWitness(c.Mode, c.Nested, c.Phase, c.Reverse, c.Profile).Execute();
        foreach (var name in Refusals) new NativeGraphWitness(Modes.Durable, false, "transition", false, Profiles().First()).Refuse(name);
        NativeGraphReaders.Run();
        foreach (var m in new[] { Modes.Balanced, Modes.Durable }) foreach (var v in new[] { 1, 2 })
            new NativeGraphWitness(m, false, "transition", false, Profiles().First()).ConsumeReader(v);
        Console.WriteLine($"{Cases().Count()} actual native graph schedules, {Refusals.Length} native refusals and {NativeGraphReaders.Names.Length} literal identity readers and four literal-reader consumption schedules passed; no physical lexical-scope/recovery/source-wave guarantee.");
    }

    private readonly World _world = new(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
    private readonly InstanceRef _target = InstanceRef.In(0, "native-graph");
    private int _owner, _serial;
    private string _frame = "";
    private string? _pendingJson;
    private StateVisit[]? _before, _next;
    private string _event = "";
    private long _visitSerial;
    private Dictionary<string, int>? _beforeCounts;
    private string? _expectedRefusal;
    private bool _refusalSeen;
    private Actor Actor => _world.Of(_owner, 0);
    private NativeState Native => Json.Read<NativeState>(_world.Store.Instances[_target.Id].State);
    private MachineState? Machine => Native.Frames.TryGetValue(_frame, out var f) ? f.State!.Deserialize<MachineState>() : null;
    private static bool Same(IEnumerable<string> a, IEnumerable<string> b) => a.ToHashSet(StringComparer.Ordinal).SetEquals(b);
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private IEnumerable<string> Entries() => _world.Store.Journal.Values.OrderBy(j => j.Position).Where(j => j.Instance == _target.Id).SelectMany(j => Json.Read<string[]>(j.Entries));
    private IEnumerable<string?[]> Trace() => Entries().Where(e => e.StartsWith("statechart:", StringComparison.Ordinal)).Select(e => JsonSerializer.Deserialize<string?[]>(e[11..])!);
    private Dictionary<string, int> Completed() => Entries().Where(e => e.StartsWith("c:", StringComparison.Ordinal)).Select(e => e[(e.LastIndexOf(':') + 1)..])
        .GroupBy(id => id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    private void Execute()
    {
        var children = new List<Node>();
        string Work(string id, string workPhase)
        {
            var mark = Wf.Set(id + "-mark", id, "done");
            children.Add(workPhase == phase ? Wf.Sequence(id, Wf.Event(id + "-wait", "gate-" + id), mark) : Wf.Sequence(id, mark));
            return id;
        }
        var graph = new StructuralDefinition(profile.Nodes.Select(n => n.Kind is NodeKind.Root or NodeKind.Region ? n : n with
        { Entry = Work("entry-" + n.Id, "entry"), Exit = Work("exit-" + n.Id, "exit") }).ToArray(),
            profile.Rules.Select(r => r with { Action = Work("action-" + r.Id, "transition") }).ToArray());
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(graph))).With(children.ToArray());
        var root = nested ? Wf.Flowchart("outer", [machine, Wf.Set("returned", "returned", "yes")], [new("machine", "returned")]) : machine;
        NativeWorkflows.Registry.Add(Wf.Workflow("native-graph-proof", root));
        Apply(new("take", Actor.Id));
        Add(new("start", _target, Kinds.Start, Start: NativeModel.Spec("native-graph-proof", mode) with { Creation = new(1, 0, "native-graph") })); Drain();
        _frame = Native.Frames.Values.Single(f => f.Node == "machine").Id;
        Settle();
        React();
        if (profile.Name is "atomic-self" or "compound-self" or "parallel-self" or "internal") React();
        Console.WriteLine("PASS " + Name(mode, nested, phase, reverse, profile));
    }

    private void React()
    {
        _before = Machine!.Visits!.ToArray();
        var beforeCounts = Completed(); _beforeCounts = beforeCounts; var beforeSerial = Machine.VisitSerial;
        _pendingJson = null; _next = null; _event = "selected-" + ++_serial;
        Signal(_event, "go"); Drain();
        var pending = Machine;
        Check(pending?.Pending is not null, "N2: selected native phase was not durably pending");
        Check(pending!.Selection is { } selected && Same(selected.Selected, profile.Selected), "N2: native selection differs from authored rules");
        _pendingJson = Json.Write(pending.Selection); _next = pending.PlannedVisits!.ToArray();
        Check(Same(_next.Select(v => v.State), profile.Next), "N2: native planned configuration changed");
        CheckVisits(_before, _next);
        Check(pending.VisitSerial >= beforeSerial, "N3: visit serial moved backwards");
        Signal("later-" + _event, "unhandled"); Drain();
        Check(!Trace().Any(p => p[1] == "later-" + _event && p[2] == "evaluated"), "N4: later input crossed pending native work");
        Reload(); Settle();
        if (profile.Final)
        {
            Check(Machine is null && _world.Store.Instances[_target.Id].Status == InstanceStatus.Finished, "N6: final entry did not complete after its work");
            if (nested) Check(Native.Variables["returned"]!.ToString() == "yes", "N6: enclosing Flowchart did not resume");
        }
        else
        {
            Check(Machine?.Pending is null && Machine is not null && Same(Machine.Active, profile.Next), "N2: native result did not settle");
            Check(Json.Write(Machine!.Visits) == Json.Write(_next), "N3: native committed next visits changed");
            Check(Trace().Count(p => p[1] == "later-" + _event && p[2] == "unhandled") == 1, "N4: queued later input was lost or repeated");
            Reload(); Check(Json.Write(Machine!.Visits) == Json.Write(_next), "N3: settled visit identities changed on reload");
        }
        Check(Trace().Count(p => p[1] == _event && p[2] == "handled") == 1, "N5: selected reaction was repeated or lost");
        var plan = Json.Read<GraphPlan>(_pendingJson!); var afterCounts = Completed();
        foreach (var id in childrenFor(plan))
            Check(afterCounts.GetValueOrDefault(id) - beforeCounts.GetValueOrDefault(id) == 1, "N5: completed native phase activity repeated or disappeared");
        _before = null; _next = null; _pendingJson = null;
        static IEnumerable<string> childrenFor(GraphPlan plan) => plan.Steps.Select(s => s.Activity! + "-mark");
    }

    private void CheckVisits(StateVisit[] before, StateVisit[] next)
    {
        foreach (var v in next)
        {
            var prior = before.SingleOrDefault(p => p.State == v.State);
            Check(profile.Kept.Contains(v.State, StringComparer.Ordinal) ? prior?.Occurrence == v.Occurrence : before.All(p => p.Occurrence != v.Occurrence),
                "N3: native visit was preserved or re-entered incorrectly");
        }
        Check(next.Select(v => v.Occurrence).Distinct(StringComparer.Ordinal).Count() == next.Length, "N3: native visits alias");
    }
    private void Inspect()
    {
        if (_frame.Length == 0) return;
        var state = Machine;
        if (state is null)
        {
            if (_pendingJson is not null)
            {
                var completed = Completed();
                foreach (var step in Json.Read<GraphPlan>(_pendingJson).Steps.Where(s => s.Phase == "entry"))
                    Check(completed.GetValueOrDefault(step.Activity + "-mark") > _beforeCounts!.GetValueOrDefault(step.Activity + "-mark"),
                        "N6: machine completed before required entry work succeeded");
            }
            return;
        }
        Check(state.VisitSerial >= _visitSerial, "N3: persisted native visit serial moved backwards"); _visitSerial = state.VisitSerial;
        if (_before is null || state.Pending is null) return;
        Check(Json.Write(state.Visits) == Json.Write(_before), "N3: pending native source visits changed");
        if (_pendingJson is not null)
        {
            Check(Json.Write(state.Selection) == _pendingJson && Json.Write(state.PlannedVisits) == Json.Write(_next), "N5: selected native work was replanned across a phase/reload");
            Check(!Trace().Any(p => p[1] == "later-" + _event && p[2] == "evaluated"), "N4: later input crossed pending native work");
        }
        var current = state.Visits!.ToDictionary(v => v.State, v => v.Occurrence, StringComparer.Ordinal);
        foreach (var step in state.Selection!.Steps.Where(w => w.Phase != "entry"))
            Check(step.Visit == current[step.State], "N3: persisted action/exit source visit changed");
        var owned = Native.Frames.Values.Where(f => f.Parent == _frame).Select(f => f.Id)
            .Concat(Native.Agenda.Where(w => !w.Resume && w.Parent == _frame).Select(w => w.Frame));
        Check(owned.ToHashSet().SetEquals(state.Progress!.Running.Keys), "N5: pending native work lost an ordinary child");
    }
    private string[] Waiting() => _world.Store.Subscriptions.Where(s => s.Instance == _target.Id && s.Key.StartsWith("event:gate-", StringComparison.Ordinal)).Select(s => s.Key).ToArray();
    private void Settle()
    {
        for (var i = 0; i < 40; i++)
        {
            var waits = Waiting(); if (waits.Length == 0) { Check(Machine?.Pending is null, "N5: native phase stranded without a wait"); return; }
            if (_before is not null) Reload();
            var key = (reverse ? waits.OrderDescending(StringComparer.Ordinal) : waits.Order(StringComparer.Ordinal)).First();
            var wait = _world.Store.Subscriptions.Single(s => s.Instance == _target.Id && s.Key == key);
            Add(Events.Deliver(new(key, "release-" + ++_serial, null, _world.Time + 120_000), wait)); Drain();
        }
        throw new InvalidOperationException("N0: native graph phase exceeded its bounded work");
    }
    // Explicitly seeded reader input: not a historical writer/binary upgrade or a new-graph write proof.
    private void ConsumeReader(int version)
    {
        var machine = Wf.Node<StateMachine>("machine", ("ChartDefinition", Binding.Literal(new Definition(new("Working", [new("Working")]), []))));
        NativeWorkflows.Registry.Add(Wf.Workflow("reader-consumption", machine));
        Apply(new("take", Actor.Id)); Add(new("start", _target, Kinds.Start, Start: NativeModel.Spec("reader-consumption", mode) with { Creation = new(1, 0, "reader") })); Drain();
        var native = Native; var frame = native.Frames.Values.Single(f => f.Node == "machine");
        Apply(new("crash", _owner));
        frame.State = JsonSerializer.SerializeToNode(new { Active = new[] { "Working" }, Pending = (object?)null, Version = version });
        var row = _world.Store.Instances[_target.Id]; _world.Store.Instances[_target.Id] = row with { State = Json.Write(native) };
        Apply(new("tick", 20_000)); _owner = 1 - _owner; Apply(new("take", Actor.Id)); Drain(); _frame = frame.Id;
        Signal("old-unmatched", "unknown"); Drain();
        Check(Machine?.Version == 3 && Machine.Visits is [{ State: "Working" }] && Machine.VisitSerial == 1,
            "N8: unhandled input did not persist the literal reader migration");
        var visits = Json.Write(Machine!.Visits); Reload();
        Signal("next-unmatched", "unknown"); Drain();
        Check(Json.Write(Machine!.Visits) == visits && Machine.VisitSerial == 1 &&
            Trace().Count(p => p[1] == "old-unmatched" && p[2] == "unhandled") == 1 &&
            Trace().Count(p => p[1] == "next-unmatched" && p[2] == "unhandled") == 1,
            "N8: consumed reader visits were reallocated or its outcome repeated");
        Console.WriteLine($"PASS identity-consumption:literal-v{version},{mode}");
    }
    private void Refuse(string name)
    {
        _expectedRefusal = name switch { "missing-profile" or "both-profiles" => "Exactly one statechart definition profile",
            "dynamic-profile" => "Statechart definition must be literal", _ => "Unknown statechart activity reference: absent" };
        var nodes = Simple().Select(n => n.Id == "Review" ? n with { Entry = "proof" } : n).ToArray();
        var rules = new[] { R("rule", 0, "Checking", "Changes") };
        if (name == "unknown-entry") nodes = nodes.Select(n => n.Id == "Changes" ? n with { Entry = "absent" } : n).ToArray();
        if (name == "unknown-exit") nodes = nodes.Select(n => n.Id == "Changes" ? n with { Exit = "absent" } : n).ToArray();
        if (name == "unknown-action") rules[0] = rules[0] with { Action = "absent" };
        NativeGuardProbe.Calls = 0;
        var machine = Wf.Node<StateMachine>("machine").With(Wf.Node<NativeGuardProbe>("proof"));
        if (name != "missing-profile") machine.Inputs.Add("StructuralDefinition", Binding.Literal(new StructuralDefinition(nodes, rules)));
        if (name == "both-profiles") machine.Inputs.Add("ChartDefinition", Binding.Literal(new Definition(new("A", [new("A")]), [])));
        if (name == "dynamic-profile") machine.Inputs["StructuralDefinition"] = Binding.Var("graph");
        NativeWorkflows.Registry.Add(Wf.Workflow("native-refusal", machine, ("graph", new StructuralDefinition(nodes, rules))));
        Apply(new("take", Actor.Id)); Add(new("start", _target, Kinds.Start, Start: NativeModel.Spec("native-refusal", mode))); Drain();
        Check(_refusalSeen && NativeGuardProbe.Calls == 0, "N7: invalid graph/profile ran authored work or missed precise refusal: " + name);
        Console.WriteLine("PASS native-refusal:" + name);
    }
    private void Signal(string id, string name) => Add(new(id, _target, CompositeSignal.Kind, Json.Write(new CompositeSignal(_frame, name, "deny")), TargetCreation: 1));
    private void Add(Message message) => Apply(new("add", M: message));
    private void Apply(Act act)
    {
        _world.Apply(act); var error = _world.Check();
        if (_world.Store.Instances.TryGetValue(_target.Id, out var row) && row.Status == InstanceStatus.Faulted)
        {
            if (_expectedRefusal is not null && error is not null && error.StartsWith("I14 ", StringComparison.Ordinal) && error.Contains(_expectedRefusal, StringComparison.Ordinal))
            { _refusalSeen = true; return; }
            throw new InvalidOperationException("N1: full graph was not hydrated through the native StateMachine: " + error);
        }
        if (error is not null) throw new InvalidOperationException("Kernel: " + error);
        if (_world.Store.Instances.ContainsKey(_target.Id)) Inspect();
    }
    private void Drain()
    {
        for (var i = 0; i < 300; i++)
        {
            Apply(new("poll", Actor.Id)); if (Actor.Worker.NeedsLoad) Apply(new("load", Actor.Id));
            if (Actor.Worker.Ready.Contains(_target.Id)) Apply(new("turn", Actor.Id, S: _target.Id));
            else if (!Actor.Worker.NeedsFlush && !Actor.Worker.NeedsMeet) return;
            if (Actor.Worker.NeedsFlush) Apply(new("flush", Actor.Id)); if (Actor.Worker.NeedsMeet) Apply(new("meet", Actor.Id));
        }
        throw new InvalidOperationException("N0: native graph drain exceeded its bound");
    }
    private void Reload() { Apply(new("crash", _owner)); Apply(new("tick", 20_000)); _owner = 1 - _owner; Apply(new("take", Actor.Id)); Drain(); }
}

// A real ordinary activity probe: a late validation guard cannot hide execution by parking before a checkpoint.
[External]
internal sealed class NativeGuardProbe : Kernel.Native.Activity
{
    public NativeGuardProbe() { }
    internal static int Calls;
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
    { Calls++; return ValueTask.FromResult(Complete()); }
}
