using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

internal static class RegionalRecovery
{
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from p in new[] { "entry", "exit", "action" } from r in new[] { false, true }
        select $"chart-regional-recovery:{m},phase={p},reverse={r}";
    internal static void Run()
    { foreach (var m in new[] { Modes.Balanced, Modes.Durable }) foreach (var p in new[] { "entry", "exit", "action" }) foreach (var r in new[] { false, true }) Run(m, p, r); }
    private static void Run(string mode, string phase, bool reverse)
    {
        RegionalLeaf.Calls.Clear(); RegionalWait.Calls.Clear(); RegionalWait.Faults = 1; ChartMark.Calls.Clear();
        var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true }; var target = InstanceRef.In(0, "regional-recovery"); var owner = 0;
        Actor Actor() => world.Of(owner, 0); NativeState State() => Json.Read<NativeState>(world.Store.Instances[target.Id].State);
        Dictionary<string, JsonNode?> D(int local) => new() { ["local"] = Values.From(local) };
        var nodes = new GraphNode[] { new("root", NodeKind.Root, Initial: "P"), new("P", NodeKind.Parallel, "root"), new("E", NodeKind.Region, "P", "E0"), new("L", NodeKind.Region, "P", "L0"),
            new("E0", NodeKind.Atomic, "E", Entry: phase == "entry" ? "e-work" : null, Exit: phase == "exit" ? "e-work" : null, Defaults: D(10)), new("E1", NodeKind.Atomic, "E", Entry: "e-target", Defaults: D(20)),
            new("L0", NodeKind.Atomic, "L", Entry: phase == "entry" ? "l-work" : null, Exit: phase == "exit" ? "l-work" : null, Defaults: D(100)), new("L1", NodeKind.Atomic, "L", Entry: "l-target", Defaults: D(200)), new("Done", NodeKind.Final, "root") };
        var def = new StructuralDefinition(nodes, [new("e", 0, "E0", "E1", VisitMode.Reenter, "advance", phase == "action" ? "e-work" : null), new("l", 1, "L0", "L1", VisitMode.Reenter, "advance", phase == "action" ? "l-work" : null), new("end", 2, "P", "Done", VisitMode.Reenter, "finish")]);
        var work = new[] { Wf.Node<RegionalLeaf>("e-work", ("Local", Binding.Var("local"))), Wf.Node<RegionalWait>("l-work", ("Local", Binding.Var("local"))),
            Wf.Node<ChartMark>("e-target", ("Name", Binding.Literal("E")), ("Local", Binding.Var("local"))), Wf.Node<ChartMark>("l-target", ("Name", Binding.Literal("L")), ("Local", Binding.Var("local"))) };
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(def))).With(work);
        var wf = Wf.Workflow("regional-recovery", machine); wf.InterventionRequested = true; NativeWorkflows.Registry.Add(wf);
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        void Apply(Act a) { world.Apply(a); if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error); }
        void Drain(string? staged = null)
        {
            for (var i = 0; i < 120; i++)
            {
                var a = Actor(); Apply(new("poll", a.Id)); if (a.Worker.NeedsLoad) Apply(new("load", a.Id));
                if (a.Worker.Ready.Contains(target.Id)) Apply(new("turn", a.Id, S: target.Id)); else if (!a.Worker.NeedsFlush && !a.Worker.NeedsMeet) return;
                if (a.Worker.NeedsFlush) { Apply(new("flush", a.Id)); if (staged is not null && world.Store.Journal.Values.Any(j => j.Entries.Contains("recovery:" + staged + ":", StringComparison.Ordinal))) return; }
                if (a.Worker.NeedsMeet) Apply(new("meet", a.Id));
            }
            throw new InvalidOperationException("R0: regional recovery exceeded bounded drain");
        }
        void Reload() { Apply(new("crash", owner)); Apply(new("tick", 20_000)); owner = 1 - owner; Apply(new("take", Actor().Id)); Drain(); }
        void Release(string id) { Apply(new("add", M: new(id, target, NativeKinds.Resume, Json.Write(new ResumeStimulus("resume-L", Values.From(id))), TargetCreation: 1))); Drain(); }
        Apply(new("take", Actor().Id)); Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec(wf.Id, mode) with { Creation = new(1, 0, "regional-recovery") }))); Drain();
        var parent = State().Frames.Values.Single(f => f.Node == "machine").Id;
        void Signal(string id, string name) { Apply(new("add", M: new(id, target, CompositeSignal.Kind, Json.Write(new CompositeSignal(parent, name)), TargetCreation: 1))); Drain(); }
        if (phase != "entry") Signal("advance", "advance");
        var first = State(); var e = first.Frames.Values.Single(f => f.Node == "e-work"); var l = first.Frames.Values.Single(f => f.Node == "l-work");
        Check(e.Intervention is not null && l.Intervention is null && first.Waits.Values.Any(w => w.Frame == l.Id) && first.Frames[parent].State is not null,
            "R51: a regional fault blocked or cancelled independent same-phase work");
        var initial = Json.Read<MachineState>(Json.Write(first.Frames[parent].State)); var scopes = Json.Write(first.LexicalScopes);
        Check(initial.Progress!.Running.Count == 2 && ChartMark.Calls.Count == 0, "R51: phase barrier advanced while required regional work remained");
        Reload(); Release("fault-L");
        Check(State().Frames.Values.Count(f => f.Intervention is not null) == 2 && RegionalWait.Calls.Count == 2 && Json.Write(State().LexicalScopes) == scopes,
            "R52: stateful regional Resume fault lost sibling/source values or private continuation");
        // This legacy held signal proves phase isolation only; receipt-bearing machine admission belongs to B5b.
        Signal("finish", "finish"); Check(world.Store.Messages.Values.Any(r => r.Message.Id == "finish" && !r.Done), "R52: later chart signal crossed the blocked phase");
        foreach (var name in reverse ? new[] { "l-work", "e-work" } : new[] { "e-work", "l-work" })
        {
            var source = State().Frames.Values.Single(f => f.Node == name); var current = Json.Read<MachineState>(Json.Write(State().Frames[parent].State));
            var id = "repair-" + name; var request = new NativeRetry(id, source.Id, source.Intervention!.Id, "regional repair");
            Apply(new("add", M: new(id, target, NativeRetry.Kind, Json.Write(request), TargetCreation: 1))); Drain(id);
            var next = State().Agenda.Single(w => !w.Resume && w.Node == name); var index = current.Progress!.Running[source.Id];
            current.Progress.Running.Remove(source.Id); current.Progress.Running.Add(next.Frame, index);
            Check(Json.Write(current) == Json.Write(State().Frames[parent].State) && Json.Write(State().LexicalScopes) == scopes && next.LexicalScope == source.LexicalScope,
                "R53: regional retry reset completed flags, sibling obligations, visits or borrowed scopes");
            Reload(); if (name == "l-work")
            {
                Check(RegionalWait.Calls.Last().Phase == "start" && RegionalWait.Calls.Last().Local == 100 && State().Frames.Values.Single(f => f.Node == name).Id == next.Frame,
                    "R54: regional stateful repair replayed a consumed payload instead of a fresh Start");
                Reload(); Release("complete-L");
            }
            if (State().Frames.Values.Any(f => f.Intervention is not null))
                Check(ChartMark.Calls.Count == 0 && Json.Write(State().LexicalScopes) == scopes && State().Frames[parent].State is not null,
                    "R53: a successful regional repair advanced the all-region barrier or retired source values");
        }
        Check(world.Store.Instances[target.Id].Status == InstanceStatus.Finished && State().LexicalScopes is null && RegionalLeaf.Calls.Count == 2 && RegionalLeaf.Calls.All(c => c.Local == 10) &&
            RegionalLeaf.Calls.Select(c => c.Operation).Distinct().Count() == 1 && RegionalLeaf.Calls.Select(c => c.Frame).Distinct().Count() == 2 &&
            RegionalWait.Calls.Count == 4 && RegionalWait.Calls.All(c => c.Local == 100) && RegionalWait.Calls.Select(c => c.Operation).Distinct().Count() == 1 && RegionalWait.Calls.Select(c => c.Frame).Distinct().Count() == 2 &&
            (phase == "entry" ? ChartMark.Calls.Count == 0 : ChartMark.Calls.OrderBy(c => c.Name).SequenceEqual(new[] { ("E", 20), ("L", 200) })),
            "R55: regional recovery repeated work, lost operation/source/target values or failed normal final completion");
        Console.WriteLine($"PASS chart-regional-recovery:{mode},phase={phase},reverse={reverse}");
    }
}
internal sealed class RegionalLeaf : Kernel.Native.Activity<int>
{
    public RegionalLeaf() { }
    [Input] public int Local { get; set; }
    internal static readonly List<(string Frame, string Operation, int Local)> Calls = [];
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext c) { Calls.Add((c.FrameId, c.OperationKey, Local)); return ValueTask.FromResult(Calls.Count == 1 ? Fault("regional", "repair") : Complete(Local)); }
}
internal sealed class RegionalWait : StatefulActivity<int, JsonObject, string>
{
    public RegionalWait() { }
    [Input] public int Local { get; set; }
    internal static int Faults;
    internal static readonly List<(string Phase, string Frame, string Operation, int Local)> Calls = [];
    protected override ValueTask<Transition> ExecuteAsync(StartContext<string> c)
    { Calls.Add(("start", c.FrameId, c.OperationKey, Local)); return ValueTask.FromResult(Suspend(new JsonObject { ["phase"] = "pending" }, new EventTrigger("resume-L"))); }
    protected override ValueTask<Transition> ResumeAsync(ResumeContext<JsonObject, string> c)
    { Calls.Add(("resume", c.FrameId, c.OperationKey, Local)); c.State["phase"] = "mutated"; return ValueTask.FromResult(Faults-- > 0 ? Fault("regional", "repair Resume") : Complete(Local)); }
}
