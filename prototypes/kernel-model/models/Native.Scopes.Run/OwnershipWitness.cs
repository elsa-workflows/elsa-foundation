using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

internal sealed class OwnershipWitness(string mode, bool nested, string kind)
{
    private static string[] Scenarios => ["fault-absorbed", "sibling-cancel", "kernel-terminate", "native-cancel", "borrow-parent"];
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from n in new[] { false, true } from k in Scenarios select $"lexical:ownership,{k},{m},nested={n}";
    internal static void Run()
    {
        foreach (var m in new[] { Modes.Balanced, Modes.Durable }) foreach (var n in new[] { false, true }) foreach (var k in Scenarios)
            new OwnershipWitness(m, n, k).Execute();
        Console.WriteLine("20/20 native lexical ownership/fault/cancellation schedules passed; not incident/retry recovery.");
    }
    private readonly World _world = new(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
    private readonly InstanceRef _target = InstanceRef.In(0, "scope-ownership");
    private int _owner, _serial;
    private Actor Actor => _world.Of(_owner, 0);
    private NativeState Native => Json.Read<NativeState>(_world.Store.Instances[_target.Id].State);
    private static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
    private string Frame(string node) => Native.Frames.Values.Single(f => f.Node == node).Id;
    private string Scope(string node) => Native.LexicalScopes!.Single(p => p.Value.Owner == Frame(node)).Key;
    private void Execute()
    {
        Node root;
        if (kind == "borrow-parent") root = Wf.Node<BorrowingParent>("parent").With(
            Wf.Node<ScopeBorrower>("borrower", ("Ancestor", Binding.Var("ancestor"))).With(Wf.Sequence("borrow-work", Wf.Event("borrow-wait", "borrow"), Wf.Set("borrow-write", "total", Binding.Expr("total + 1")))),
            Wf.Set("read", "observed", Binding.Var("total")));
        else
        {
            Node Owner(string label, bool fail) => Wf.Node<OwningScope>(label, ("Fail", fail)).With(
                Wf.Event("wait-" + label, label), Wf.Node<ExpectedScopeFault>("fault-" + label), Wf.Set("escape-" + label, "escaped", true));
            var a = Owner("A", false); var b = Owner("B", kind == "fault-absorbed");
            root = kind == "fault-absorbed" ? Wf.Parallel("pair", 1, a, b) : Wf.Node<ScopePair>("pair").With(a, b);
        }
        if (nested) root = Wf.Flowchart("outer", [root, Wf.Set("returned", "returned", true)], [new(root.Id, "returned")]);
        NativeWorkflows.Registry.Add(Wf.Workflow("scope-ownership", root));
        Apply(new("take", Actor.Id)); Add(new("start", _target, Kinds.Start, Start: NativeModel.Spec("scope-ownership", mode) with { Creation = new(1, 0, "ownership") })); Drain();
        if (kind is "kernel-terminate" or "native-cancel")
        {
            Check(Native.LexicalScopes is { Count: 2 }, "L9: cancellation did not start with live resources");
            Add(new("cancel", _target, kind == "kernel-terminate" ? Kinds.Terminate : NativeKinds.Cancel, TargetCreation: 1)); Drain();
            Check(_world.Store.Instances[_target.Id].Status == InstanceStatus.Cancelled && Native.LexicalScopes is null &&
                Native.Frames.Values.All(f => f.LexicalScope is null) && Native.Agenda.All(w => w.LexicalScope is null) && !Waits().Any(), "L9: workflow cancellation retained live lexical resources or references");
            Reload(); Add(new("stale", _target, NativeKinds.Resume, Json.Write(new ResumeStimulus("A")), TargetCreation: 1)); Drain();
            Check(Native.LexicalScopes is null && _world.Store.Instances[_target.Id].Status == InstanceStatus.Cancelled, "L9: a stale wake resurrected a cancelled resource");
        }
        else if (kind == "borrow-parent")
        {
            Check(Native.LexicalScopes is { Count: 2 } && Native.LexicalScopes[Scope("borrower")].Parent == Scope("parent"), "L9: a descendant owner could not borrow its ancestor's lexical scope");
            Reload(); Release("borrow"); Drain();
            Check(Native.Variables["observed"]!.ToString() == "11", "L9: borrowed-parent assignment did not use normal lookup");
            Finished();
        }
        else
        {
            var a = Scope("A");
            if (kind == "sibling-cancel")
            {
                var b = Scope("B"); var before = Json.Write(Native);
                Signal(Frame("A"), "foreign", b); Drain();
                Check(Json.Write(Native) == before, "L5: foreign-scope refusal mutated native values, work or allocation");
                Signal(Frame("pair"), "drop-B"); Drain();
                Check(!Native.Frames.Values.Any(f => f.Node == "B") && !Native.LexicalScopes!.ContainsKey(b), "L9: cancelled sibling retained its resources");
            }
            Check(_world.Store.Instances[_target.Id].Status == InstanceStatus.Running && Native.LexicalScopes is { Count: 1 } && Scope("A") == a && !Waits().Any(s => s.Key == "event:B"),
                "L9: absorbed fault/cancel orphaned an owner or removed the surviving sibling");
            Reload(); Check(Scope("A") == a, "L9: surviving sibling changed scope after disposal/reload");
            Release("A"); Drain(); Finished();
        }
        var records = _world.Store.Journal.Values.Where(j => j.Instance == _target.Id).SelectMany(j => Json.Read<string[]>(j.Entries)).ToArray();
        if (kind == "sibling-cancel") foreach (var guard in new[] { "foreign-schedule", "foreign-create", "foreign-release" })
            Check(records.Contains("lexical-refused:" + guard), "L5: foreign ownership guard proof was skipped");
        if (kind == "borrow-parent") Check(records.Contains("lexical-refused:ancestor-release"), "L5: borrowed scope owner guard proof was skipped");
        Console.WriteLine($"PASS lexical:ownership,{kind},{mode},nested={nested}");
    }
    private void Finished()
    {
        Check(_world.Store.Instances[_target.Id].Status == InstanceStatus.Finished && Native.LexicalScopes is null && Native.Variables.GetValueOrDefault("escaped") is null, "L9: completed owner leaked resources or foreign work ran");
        if (nested) Check(Native.Variables["returned"]!.ToString() == "true", "L9: outer workflow did not resume");
    }
    private SubscriptionRow[] Waits() => _world.Store.Subscriptions.Where(s => s.Instance == _target.Id).ToArray();
    private void Release(string name)
    {
        var wait = Waits().Single(s => s.Key == "event:" + name);
        Add(Events.Deliver(new(wait.Key, "release-" + ++_serial, null, _world.Time + 120_000), wait));
    }
    private void Signal(string frame, string name, string? value = null) => Add(new("signal-" + ++_serial, _target, CompositeSignal.Kind, Json.Write(new CompositeSignal(frame, name, value)), TargetCreation: 1));
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
        throw new InvalidOperationException("L0: ownership proof exceeded its bounded drain");
    }
    private void Reload()
    {
        var saved = Json.Write(Native.LexicalScopes);
        Apply(new("crash", _owner)); Apply(new("tick", 20_000)); _owner = 1 - _owner; Apply(new("take", Actor.Id)); Drain();
        Check(Json.Write(Native.LexicalScopes) == saved, "L2: ownership resources changed across reload");
    }
    internal static void Refuse(CompositeContext context, Action action, string expected, string name)
    {
        try { action(); }
        catch (InvalidOperationException e) when (e.Message == expected) { context.Record("lexical-refused:" + name); return; }
        throw new InvalidOperationException("L5: lexical guard did not refuse: " + name);
    }
}

internal sealed class OwningScope : CompositeActivity, ISignalledComposite
{
    public OwningScope() { }
    [Input] public bool Fail { get; set; }
    protected internal override void Execute(CompositeContext context)
    {
        var scope = context.CreateLexicalScope(new Dictionary<string, JsonNode?> { ["local"] = Values.From(context.Node.Id) });
        context.SetState(scope); context.ScheduleInScope(context.Children[0], scope);
        if (Fail) context.Schedule(context.Children[1]);
    }
    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.Complete();
    public bool CanAccept(CompositeContext context) => true;
    public void Accept(CompositeContext context, string messageId, CompositeSignal signal)
    {
        var other = signal.Value!;
        OwnershipWitness.Refuse(context, () => context.ScheduleInScope(context.Children[2], other), "Lexical scope is missing or inaccessible.", "foreign-schedule");
        OwnershipWitness.Refuse(context, () => context.CreateLexicalScope(new Dictionary<string, JsonNode?>(), other), "Lexical scope is missing or inaccessible.", "foreign-create");
        OwnershipWitness.Refuse(context, () => context.ReleaseLexicalScope(other), "Lexical scope is missing or inaccessible.", "foreign-release");
    }
}
internal sealed class ScopePair : CompositeActivity, ISignalledComposite
{
    public ScopePair() { }
    protected internal override void Execute(CompositeContext context) => context.SetState(context.Children.ToDictionary(n => n.Id, n => context.Schedule(n)));
    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        var pending = context.ReadState<Dictionary<string, string>>()!;
        pending.Remove(pending.Single(p => p.Value == child.Frame).Key);
        if (pending.Count == 0) context.Complete(); else context.SetState(pending);
    }
    public bool CanAccept(CompositeContext context) => true;
    public void Accept(CompositeContext context, string messageId, CompositeSignal signal)
    {
        var pending = context.ReadState<Dictionary<string, string>>()!;
        context.Cancel(pending["B"]); pending.Remove("B"); context.SetState(pending);
    }
}
internal sealed class ExpectedScopeFault : Kernel.Native.Activity
{
    public ExpectedScopeFault() { }
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) => ValueTask.FromResult(Fault("expected-scope-fault", "ordinary disposal witness"));
}
internal sealed class BorrowingParent : CompositeActivity
{
    public BorrowingParent() { }
    protected internal override void Execute(CompositeContext context)
    {
        var scope = context.CreateLexicalScope(new Dictionary<string, JsonNode?> { ["total"] = Values.From(10) }); context.SetState(scope);
        context.Schedule(context.Children[0], scope: new Dictionary<string, JsonNode?> { ["ancestor"] = Values.From(scope) });
    }
    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        if (child.Node == "borrower") context.ScheduleInScope(context.Children[1], context.ReadState<string>()!); else context.Complete();
    }
}
internal sealed class ScopeBorrower : CompositeActivity
{
    public ScopeBorrower() { }
    [Input] public string Ancestor { get; set; } = "";
    protected internal override void Execute(CompositeContext context)
    {
        // Test ownership before allocating a child resource: the child-presence guard cannot mask a missing owner guard.
        OwnershipWitness.Refuse(context, () => context.ReleaseLexicalScope(Ancestor), "Only the lexical scope owner can release it.", "ancestor-release");
        var own = context.CreateLexicalScope(new Dictionary<string, JsonNode?>(), Ancestor);
        context.ScheduleInScope(context.Children[0], own);
    }
    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.Complete();
}
