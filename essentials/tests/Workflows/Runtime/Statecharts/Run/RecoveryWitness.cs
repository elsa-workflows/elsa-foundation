using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

internal static class RecoveryWitness
{
    private static string[] Cuts => ["steady", "lost-before-apply", "lost-answer", "cancel-fault", "terminate-staged"];
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from n in new[] { false, true } from p in new[] { "entry", "exit", "action" } from cut in Cuts
        select $"chart-recovery:{m},nested={n},phase={p},cut={cut}";
    internal static void Run(bool baseline = false)
    {
        if (baseline) { Run(Modes.Balanced, true, "entry", true, "steady"); return; }
        foreach (var m in new[] { Modes.Balanced, Modes.Durable }) foreach (var n in new[] { false, true }) foreach (var p in new[] { "entry", "exit", "action" }) foreach (var cut in Cuts) Run(m, n, p, false, cut);
    }
    private static void Run(string mode, bool nested, string phase, bool baseline, string cut)
    {
        ChartDependency.Calls.Clear(); ChartMark.Calls.Clear();
        var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true }; var target = InstanceRef.In(0, "chart-recovery"); var owner = 0;
        Actor Actor() => world.Of(owner, 0);
        NativeState State() => Json.Read<NativeState>(world.Store.Instances[target.Id].State);
        var leaf = Wf.Node<ChartDependency>("dependency", ("Local", Binding.Var("local")));
        Node Mark(string name) => Wf.Node<ChartMark>(name, ("Name", Binding.Literal(name)), ("Local", Binding.Var("local")));
        var def = new StructuralDefinition([new("root", NodeKind.Root, Initial: "A"),
            new("A", NodeKind.Atomic, "root", Entry: phase == "entry" ? "dependency" : "entry", Exit: phase == "exit" ? "dependency" : "exit", Defaults: new Dictionary<string, JsonNode?> { ["local"] = Values.From(17) }),
            new("B", NodeKind.Final, "root", Entry: "final", Defaults: new Dictionary<string, JsonNode?> { ["local"] = Values.From(99) })],
            [new("move", 0, "A", "B", VisitMode.Reenter, "go", phase == "action" ? "dependency" : "action")]);
        var children = new List<Node> { leaf, Mark("final") };
        foreach (var name in new[] { "entry", "exit", "action" }.Where(p => p != phase)) children.Add(Mark(name));
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(def))).With(children.ToArray());
        var root = nested ? Wf.Flowchart("flow", [machine, Wf.Set("returned", "returned", true)], [new("machine", "returned")]) : machine;
        var wf = Wf.Workflow("chart-recovery", baseline ? Wf.Node<ChartCatch>("catch").With(root) : root); wf.InterventionRequested = true; NativeWorkflows.Registry.Add(wf);
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        void Apply(Act a)
        {
            world.Apply(a);
            if (world.Store.Instances.TryGetValue(target.Id, out var row) && row.Status == InstanceStatus.Faulted) throw new InvalidOperationException("R44: phase fault ended instead of preserving the selected reaction");
            if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
        }
        void Drain(string? staged = null)
        {
            for (var i = 0; i < 100; i++)
            {
                var a = Actor(); Apply(new("poll", a.Id)); if (a.Worker.NeedsLoad) Apply(new("load", a.Id));
                if (a.Worker.Ready.Contains(target.Id)) Apply(new("turn", a.Id, S: target.Id)); else if (!a.Worker.NeedsFlush && !a.Worker.NeedsMeet) return;
                if (a.Worker.NeedsFlush) { Apply(new("flush", a.Id)); if (staged is not null && world.Store.Journal.Values.Any(j => j.Entries.Contains("recovery:" + staged + ":", StringComparison.Ordinal))) return; }
                if (a.Worker.NeedsMeet) Apply(new("meet", a.Id));
            }
            throw new InvalidOperationException("R0: chart recovery exceeded bounded drain");
        }
        void Reload() { Apply(new("crash", owner)); Apply(new("tick", 20_000)); owner = 1 - owner; Apply(new("take", Actor().Id)); Drain(); }
        Apply(new("take", Actor().Id)); Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec(wf.Id, mode) with { Creation = new(1, 0, "chart-recovery") }))); Drain();
        var machineFrame = State().Frames.Values.SingleOrDefault(f => f.Node == "machine");
        Check(machineFrame is not null, "R44: phase fault lost its selected reaction and ordinary parent");
        void Signal() { Apply(new("add", M: new("move", target, CompositeSignal.Kind, Json.Write(new CompositeSignal(machineFrame!.Id, "go")), TargetCreation: 1))); Drain(); }
        if (phase != "entry") Signal();
        var state = State(); var source = state.Frames.Values.SingleOrDefault(f => f.Node == "dependency");
        Check(source?.Intervention is not null && source.Parent == machineFrame!.Id && ChartDependency.Calls.Count == 1,
            "R44: phase fault lost its selected reaction and ordinary parent");
        var before = Json.Read<MachineState>(Json.Write(state.Frames[machineFrame!.Id].State)); var scope = Json.Write(state.LexicalScopes); var marks = ChartMark.Calls.ToArray();
        Check(before.Pending is not null && before.Progress!.Running.ContainsKey(source!.Id) && source.RecoveryInputs?["Local"]?.ToString() == "17" && source.LexicalScope is not null,
            "R45: phase intervention lost its exact child, source values or pinned inputs");
        void Cancel(bool staged)
        {
            Apply(new("add", M: new("cancel", target, staged ? Kinds.Terminate : NativeKinds.Cancel, TargetCreation: 1))); Drain();
            Check(world.Store.Instances[target.Id].Status == InstanceStatus.Cancelled && ChartDependency.Calls.Count == (staged ? 2 : 1),
                "R56: phase cancellation changed normal wake order or resurrected failed work");
            var history = Json.Write(State().Retries); NativeWorkflows.Registry.Remove(wf.Id);
            Apply(new("add", M: new("query", target, ReceiptQuery.Kind, Json.Write(new ReceiptQuery(machineFrame.Id, "unknown")), ReplyTo: ReplyTo.Of("outside", "query", world.Time + 120_000), TargetCreation: 1))); Drain();
            Check(Json.Write(State().Retries) == history && world.Deliveries.Select(d => d.Reply.Item).OfType<Admission>().LastOrDefault()?.Result == "UnknownInvocation",
                "R56: phase terminal metadata hydrated authored material or rewrote history");
            Console.WriteLine($"PASS chart-recovery:{mode},nested={nested},phase={phase},cut={cut}");
        }
        if (cut == "cancel-fault") { Cancel(false); return; }
        wf.InterventionRequested = false; Reload();
        var retry = new NativeRetry("repair", source!.Id, source.Intervention!.Id, "repair phase dependency");
        Apply(new("add", M: new("repair", target, NativeRetry.Kind, Json.Write(retry), TargetCreation: 1)));
        if (cut.StartsWith("lost-", StringComparison.Ordinal))
        {
            var prepared = false;
            for (var i = 0; i < 100 && !prepared; i++)
            {
                var a = Actor(); Apply(new("poll", a.Id)); if (a.Worker.NeedsLoad) Apply(new("load", a.Id));
                if (a.Worker.Ready.Contains(target.Id)) Apply(new("turn", a.Id, S: target.Id));
                prepared = a.Worker.Queued > 0 && a.Worker.Messages.Any(m => m.Id == "repair" && m.Taken);
                if (!prepared && a.Worker.NeedsFlush) Apply(new("flush", a.Id)); if (!prepared && a.Worker.NeedsMeet) Apply(new("meet", a.Id));
            }
            Check(prepared, "R57: phase uncertainty missed control preparation"); var row = world.Store.Instances[target.Id];
            world.Auto = false; var actorAtCut = Actor(); var call = actorAtCut.Calls.Count; Apply(new("flush", actorAtCut.Id));
            Check(actorAtCut.Calls.Count == call + 1 && actorAtCut.Calls[call].Kind == "commit", "R57: phase uncertainty missed physical commit");
            Apply(new(cut == "lost-answer" ? "exec" : "lose", actorAtCut.Id, call));
            Check(cut == "lost-answer" ? State().Retries?.Count == 1 : world.Store.Instances[target.Id] == row, "R57: phase uncertainty crossed wrong physical boundary");
            Apply(new("answer", actorAtCut.Id, call, Flag: true)); Check(ChartDependency.Calls.Count == 1, "R57: unknown commit answer executed fresh phase work");
            world.Auto = true; Reload();
        }
        else Drain(retry.Id);
        var work = State().Agenda.SingleOrDefault(w => !w.Resume && w.Node == "dependency");
        Check(State().Retries?.ContainsKey(retry.Id) == true, "R46: phase repair did not stage its fresh child obligation");
        var successor = State().Retries![retry.Id].Successor;
        Check(cut.StartsWith("lost-", StringComparison.Ordinal) || work is not null && work.Frame == successor && work.LexicalScope == source.LexicalScope && work.OperationKey == source.OperationKey && work.Attempt == 0 && ChartDependency.Calls.Count == 1,
            "R46: phase repair did not stage its fresh child obligation");
        var expected = Json.Read<MachineState>(Json.Write(before)); var index = expected.Progress!.Running[source.Id]; expected.Progress.Running.Remove(source.Id); expected.Progress.Running.Add(successor, index);
        Check(Json.Write(expected) == Json.Write(State().Frames[machineFrame.Id].State) && Json.Write(State().LexicalScopes) == scope && marks.SequenceEqual(ChartMark.Calls),
            "R46: phase retry changed completed work, plan, visits or scope instead of only rebinding its child");
        if (cut == "terminate-staged") { Cancel(true); return; }
        Reload(); var repeated = State().Frames.Values.SingleOrDefault(f => f.Node == "dependency");
        Check(repeated?.Id == successor && repeated.Intervention is not null && ChartDependency.Calls.Count == 2,
            "R58: repeated phase fault lost its continuing physical obligation");
        var second = new NativeRetry("repair-2", repeated!.Id, repeated.Intervention!.Id, "second phase repair");
        Apply(new("add", M: new("repair-2", target, NativeRetry.Kind, Json.Write(second), TargetCreation: 1))); Drain(second.Id);
        var next = State().Agenda.Single(w => !w.Resume && w.Node == "dependency");
        expected.Progress.Running.Remove(successor); expected.Progress.Running.Add(next.Frame, index);
        Check(Json.Write(expected) == Json.Write(State().Frames[machineFrame.Id].State) && Json.Write(State().LexicalScopes) == scope && marks.SequenceEqual(ChartMark.Calls) && next.LexicalScope == repeated.LexicalScope,
            "R58: repeated repair reset completed phases, visits or source resources");
        Reload(); if (phase == "entry") Signal();
        Check(world.Store.Instances[target.Id].Status == InstanceStatus.Finished && (!nested || State().Variables.GetValueOrDefault("returned")?.ToString() == "true") && State().LexicalScopes is null &&
            ChartDependency.Calls.Count == 3 && ChartDependency.Calls.Select(c => c.Frame).Distinct().Count() == 3 && ChartDependency.Calls.Select(c => c.Operation).Distinct().Count() == 1 && ChartDependency.Calls.All(c => c.Local == 17) &&
            ChartMark.Calls.Where(c => c.Name != "final").All(c => c.Local == 17) && ChartMark.Calls.Single(c => c.Name == "final").Local == 99 && ChartMark.Calls.GroupBy(c => c.Name).All(g => g.Count() == 1),
            "R47: phase recovery repeated completed work, lost source/target values or failed normal completion");
        Console.WriteLine($"PASS chart-recovery:{mode},nested={nested},phase={phase},cut={cut}");
    }
}
internal sealed class ChartDependency : Kernel.Native.Activity<int>
{
    public ChartDependency() { }
    [Input] public int Local { get; set; }
    internal static readonly List<(string Frame, string Operation, int Local)> Calls = [];
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext c)
    { Calls.Add((c.FrameId, c.OperationKey, Local)); return ValueTask.FromResult(Calls.Count <= 2 ? Fault("dependency", "repair") : Complete(Local)); }
}
internal sealed class ChartMark : Kernel.Native.Activity<int>
{
    public ChartMark() { }
    [Input] public string Name { get; set; } = "";
    [Input] public int Local { get; set; }
    internal static readonly List<(string Name, int Local)> Calls = [];
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext c) { Calls.Add((Name, Local)); return ValueTask.FromResult(Complete(Local)); }
}
internal sealed class ChartCatch : CompositeActivity
{
    public ChartCatch() { }
    protected internal override void Execute(CompositeContext c) => c.Schedule(c.Children.Single());
    protected internal override void OnChildCompleted(CompositeContext c, ChildCompleted child) => c.Complete();
    protected internal override void OnChildFaulted(CompositeContext c, ChildFaulted child) => c.Complete();
}
