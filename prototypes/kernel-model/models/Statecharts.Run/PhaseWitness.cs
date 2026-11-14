using System.Text.Json;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

internal sealed class PhaseWitness(string mode, bool nested, string phase, string first, bool both)
{
    internal static IEnumerable<string> Names => Cases().Select(c => $"phase:{c.mode},nested={c.nested},{c.phase},first={c.first},both={c.both}");
    private static IEnumerable<(string mode, bool nested, string phase, string first, bool both)> Cases() =>
        from mode in new[] { Modes.Balanced, Modes.Durable } from nested in new[] { false, true }
        from phase in new[] { "exit", "transition", "entry" } from first in new[] { "E", "L" }
        from both in new[] { false, true } select (mode, nested, phase, first, both);
    internal static void RunAll()
    {
        foreach (var c in Cases()) new PhaseWitness(c.mode, c.nested, c.phase, c.first, c.both).Run();
        Console.WriteLine("48/48 phase schedules passed; fault recovery and publication closure remain separate.");
    }

    private readonly World _world = new(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
    private readonly InstanceRef _target = InstanceRef.In(0, "phase-machine");
    private int _owner, _serial;
    private string _frame = "";
    private Actor Actor => _world.Of(_owner, 0);
    private NativeState State => Json.Read<NativeState>(_world.Store.Instances[_target.Id].State);
    private string Other => first == "E" ? "L" : "E";
    private bool Has(string variable) => State.Variables.GetValueOrDefault(variable)?.ToString() == "yes";
    private string? Value(string variable) => State.Variables.GetValueOrDefault(variable)?.ToString();
    private bool Waiting(string name) => _world.Store.Subscriptions.Any(s => s.Instance == _target.Id && s.Key == "event:" + name);
    private static void Check(bool ok, string detail) { if (!ok) throw new InvalidOperationException(detail); }

    private void Run()
    {
        var children = new List<Node>();
        string Mark(string id)
        {
            children.Add(Wf.Set(id, id, "yes"));
            return id;
        }
        string Parent(string id, string region, string when)
        {
            if (phase != when || region != first) return Mark(id);
            children.Add(Wf.Sequence(id, Wf.Event(id + "-wait", "parent-" + region), Wf.Set(id + "-set", id, "yes")));
            return id;
        }
        foreach (var region in new[] { "E", "L" })
        {
            var work = new List<Node>();
            if (both || region == first) work.Add(Wf.Event(region + "-wait", "release-" + region));
            work.Add(Wf.Set(region + "-write", "shared", region));
            work.Add(Wf.Set(region + "-read", region + "-read", Binding.Var("shared")));
            work.Add(Wf.Set(region + "-done", region + "-done", "yes"));
            children.Add(Wf.Sequence("work-" + region, work.ToArray()));
        }
        var review = new State("Review",
            [new("Editorial", [new("Editorial", [new("EditLeaf", [new("EditLeaf",
                Entry: phase == "entry" ? "work-E" : null, Exit: phase == "exit" ? "work-E" : Mark("E-child-exit"))])],
                Entry: phase == "entry" ? Parent("E-parent-entry", "E", "entry") : null,
                Exit: phase == "exit" ? Parent("E-parent-exit", "E", "exit") : Mark("E-parent-exit")),
                new("EditorDone", Entry: Mark("E-target-entry"))]),
             new("Legal", [new("Legal", [new("LegalInner", [new("LegalInner", [new("LegalLeaf", [new("LegalLeaf",
                Entry: phase == "entry" ? "work-L" : null, Exit: phase == "exit" ? "work-L" : Mark("L-child-exit"))])],
                Entry: phase == "entry" ? Mark("L-inner-entry") : null, Exit: Mark("L-inner-exit"))])],
                Entry: phase == "entry" ? Parent("L-parent-entry", "L", "entry") : null,
                Exit: phase == "exit" ? Parent("L-parent-exit", "L", "exit") : Mark("L-parent-exit")),
                new("LegalDone", Entry: Mark("L-target-entry"))])],
            Entry: phase == "entry" ? Mark("review-entry") : null, Exit: Mark("review-exit"));
        var definition = new Definition(new(phase == "entry" ? "Before" : "Review",
            [new("Before", Exit: Mark("before-exit")), review, new("After", Entry: Mark("after-entry")), new("Done", Final: true)]),
            [new("Before", "begin", "Review", Mark("before-action")), new("Review", "move", "After", Mark("move-action")),
             new("Editorial", "advance", "EditorDone", "work-E"), new("Legal", "advance", "LegalDone", "work-L"),
             new("Review", "finish", "Done"), new("After", "finish", "Done")]);
        var machine = Wf.Node<StateMachine>("machine", ("ChartDefinition", Binding.Literal(definition))).With(children.ToArray());
        var root = nested ? Wf.Flowchart("outer", [machine, Wf.Set("returned", "returned", "yes")], [new("machine", "returned")]) : machine;
        NativeWorkflows.Registry.Add(Wf.Workflow("phase-proof", root, ("shared", "initial")));
        Apply(new("take", Actor.Id));
        Add(new("start", _target, Kinds.Start, Start: NativeModel.Spec("phase-proof", mode) with { Creation = new(1, 0, "phase") }));
        Drain();
        _frame = State.Frames.Values.Single(f => f.Node == "machine").Id;
        Signal("selected", phase == "entry" ? "begin" : phase == "exit" ? "move" : "advance");
        Drain();
        if (phase == "entry")
        {
            Check(Waiting("parent-" + first) && !Waiting("release-" + first), "P4: child entry ran before its waiting parent");
            Check(both ? Waiting("release-" + Other) : Has(Other + "-done"), "P1: waiting parent blocked an independent entry branch");
            Reload(); Release("parent-" + first); Drain();
            Check(Has(first + "-parent-entry") && Has("review-entry"), "P4: entry lost parent completion");
        }
        Check(Waiting("release-" + first), "P1: selected regional work did not reach its ordinary wait");
        Check(both ? Waiting("release-" + Other) : Has(Other + "-done"), "P1: independent regional work was serialized");
        if (!both && phase == "exit")
            Check(Has(Other + "-parent-exit"), "P1: deep wait blocked an unrelated ancestor exit");
        CheckBarrier();
        Signal("later", "finish"); Drain();
        Check(!Trace().Any(p => p[1] == "later" && p[2] == "evaluated"), "P2: later business input crossed pending phase work");
        Reload();
        Check(Waiting("release-" + first) && (!both || Waiting("release-" + Other)), "P5: reload lost one of the independent child waits");
        Release("release-" + first); Drain();
        if (both)
        {
            Check(Has(first + "-done") && Waiting("release-" + Other) && !Has(Other + "-done"), "P5: one callback settled the other pending child");
            Check(Value("shared") == first && Value(first + "-read") == first, "P6: native shared values did not survive the first regional completion");
            CheckBarrier(); Reload(); Release("release-" + Other); Drain();
        }
        if (phase == "exit")
        {
            Check(Waiting("parent-" + first) && !Has("review-exit") && !Has("move-action"), "P4: enclosing exit/action crossed a waiting descendant exit");
            Reload(); Release("parent-" + first); Drain();
        }
        Check(_world.Store.Instances[_target.Id].Status == InstanceStatus.Finished && (!nested || Has("returned")), "P3: selected phases did not finish and return to their caller");
        var last = both ? Other : first;
        Check(Value("shared") == last && Value("E-read") == "E" && Value("L-read") == "L", "P6: ordinary shared-value behavior changed across order/reload");
        var events = Trace().Where(p => p[1] is "selected" or "later" && p[2] is "evaluated" or "handled").Select(p => p[1] + ":" + p[2]);
        Check(events.SequenceEqual(["selected:evaluated", "selected:handled", "later:evaluated", "later:handled"]), "P2: chart inputs were re-evaluated or reordered across restart");
        foreach (var region in new[] { "E", "L" })
            Check(Entries().Count(e => e.StartsWith("c:", StringComparison.Ordinal) && e.EndsWith(":" + region + "-write", StringComparison.Ordinal)) == 1,
                "P5: completed regional work was repeated after reload");
        Console.WriteLine($"PASS phase:{mode},nested={nested},{phase},first={first},both={both}");
    }

    private void CheckBarrier()
    {
        if (phase == "exit") Check(!Has("move-action") && !Has("after-entry"), "P3: action/entry crossed the all-exit barrier");
        if (phase == "transition") Check(!Has("E-target-entry") && !Has("L-target-entry"), "P3: entry crossed the all-action barrier");
        Check(!Trace().Any(p => p[1] == "selected" && p[2] == "handled"), "P3: reaction completed with required phase work pending");
    }
    private IEnumerable<string> Entries() => _world.Store.Journal.Values.OrderBy(j => j.Position).Where(j => j.Instance == _target.Id).SelectMany(j => Json.Read<string[]>(j.Entries));
    private IEnumerable<string?[]> Trace() => Entries().Where(e => e.StartsWith("statechart:", StringComparison.Ordinal)).Select(e => JsonSerializer.Deserialize<string?[]>(e["statechart:".Length..])!);
    private void Signal(string id, string name) => Add(new(id, _target, CompositeSignal.Kind, Json.Write(new CompositeSignal(_frame, name)), TargetCreation: 1));
    private void Release(string name)
    {
        var wait = _world.Store.Subscriptions.Single(s => s.Instance == _target.Id && s.Key == "event:" + name);
        Add(Events.Deliver(new("event:" + name, "release-" + ++_serial, null, _world.Time + 120_000), wait));
    }
    private void Add(Message message) => Apply(new("add", M: message));
    private void Apply(Act action) { _world.Apply(action); if (_world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error); }
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
        throw new InvalidOperationException("P0: phase witness exceeded its bounded drain");
    }
    private void Reload()
    {
        Apply(new("crash", _owner)); Apply(new("tick", 20_000)); _owner = 1 - _owner; Apply(new("take", Actor.Id));
        Drain();
    }
}
