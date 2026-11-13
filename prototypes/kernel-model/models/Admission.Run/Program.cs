using System.Text.Json;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

if (args.SequenceEqual(["--recreate"]))
{
    InvocationRecreation.Run();
    return;
}

if (args.SequenceEqual(["--terminal"]) || args.SequenceEqual(["--list-terminal"]))
{
    TerminalReceipts.Run(args[0] == "--list-terminal");
    return;
}

var scenarios = (from mode in new[] { Modes.Balanced, Modes.Durable }
    from nested in new[] { false, true } from cut in Enum.GetValues<Cut>()
    select new Scenario(mode, nested, cut)).Concat(from mode in new[] { Modes.Balanced, Modes.Durable }
        from nested in new[] { false, true } select new Scenario(mode, nested, Cut.None, true))
    .Concat(from mode in new[] { Modes.Balanced, Modes.Durable } from nested in new[] { false, true }
        select new Scenario(mode, nested, Cut.None, Signals: true)).ToArray();
var controls = new[] { AdmissionFlaw.Hydrate, AdmissionFlaw.ForgetIds, AdmissionFlaw.IgnoreCapacity, AdmissionFlaw.EarlyReply, AdmissionFlaw.BlockedSignal };
if (args.SequenceEqual(["--list"]))
{
    foreach (var scenario in scenarios) Console.WriteLine("schedule:" + scenario);
    foreach (var control in controls) Console.WriteLine("control:" + control);
    return;
}
if (args.Length != 0) throw new ArgumentException("Only --list is supported.");
var passed = 0;
foreach (var scenario in scenarios)
{
    new Witness(scenario.Mode, scenario.Nested, scenario.Cut, blocked: scenario.Blocked, signals: scenario.Signals).Run();
    passed++;
}
var detected = 0;
foreach (var defect in controls)
{
    var name = defect switch { AdmissionFlaw.Hydrate => "A1", AdmissionFlaw.ForgetIds => "A2",
        AdmissionFlaw.IgnoreCapacity => "A3", AdmissionFlaw.EarlyReply => "A4", _ => "A12" };
    try { new Witness(Modes.Balanced, false, Cut.None, defect, signals: defect == AdmissionFlaw.BlockedSignal).Run(); }
    catch (InvalidOperationException error) when (error.Message.StartsWith(name + ":", StringComparison.Ordinal))
    { Console.WriteLine($"Detected {defect}: {error.Message}"); detected++; continue; }
    throw new InvalidOperationException($"Negative control {defect} did not fail its named assertion {name}.");
}
Console.WriteLine($"{passed}/{scenarios.Length} schedules passed; {detected}/{controls.Length} named controls detected. Model reload only, not host/provider qualification.");

enum Cut { None, BeforeCommit, LostAcknowledgement, Reload, StaleOwner }
sealed record Scenario(string Mode, bool Nested, Cut Cut, bool Blocked = false, bool Signals = false);

sealed class Witness(string mode, bool nested, Cut cut, AdmissionFlaw defect = AdmissionFlaw.None, bool blocked = false, bool signals = false)
{
    private readonly World _world = new(new Config(2, 1, Flaw.None, Native: true, AdmissionFlaws: defect)) { Auto = true };
    private readonly InstanceRef _instance = InstanceRef.In(0, "admission");
    private int _owner, _transport;
    private string _frame = "";
    private readonly Dictionary<string, string> _requests = [];
    private Actor Actor => _world.Of(_owner, 0);
    private NativeState State => Json.Read<NativeState>(_world.Store.Instances[_instance.Id].State);
    private InvocationInbox Inbox => State.Inboxes![_frame];
    private Admission[] Replies => _world.Deliveries.Select(x => x.Reply.Item).OfType<Admission>().ToArray();

    public void Run()
    {
        ProbeReceiver.Forbidden = false;
        var receiver = Wf.Node<ProbeReceiver>("receiver").With(
            Wf.Sequence("phase", Wf.Set("exit", "exitDone", true), blocked ? Wf.Node<ProbeFault>("fault") : Wf.Event("gate", "release")),
            Wf.Event("finish", "finish"), Wf.Event("park", "park"), Wf.Node<ProbeNotice>("notice"));
        var root = nested ? Wf.Sequence("parent", receiver, Wf.Set("after", "returned", true)) : receiver;
        NativeWorkflows.Registry.Add(Wf.Workflow("admission-probe", root));
        Apply(new("take", Actor.Id));
        Add(new("start", _instance, Kinds.Start, Start: NativeModel.Spec("admission-probe", mode)));
        Drain();
        _frame = State.Frames.Values.Single(f => f.Node == "receiver").Id;
        Require(State.Variables["exitDone"]!.GetValue<bool>(), "A0: earlier ordinary work did not commit");
        Require((State.Frames[_frame].Parent is not null) == nested, "A0: placement mismatch");
        var births = ProbeReceiver.Births;
        ProbeReceiver.Forbidden = !signals;
        if (signals) Add(new("blocked-signal", _instance, CompositeSignal.Kind,
            Json.Write(new CompositeSignal(_frame, "blocked"))));
        Send("a", "name", null);
        if (cut is Cut.BeforeCommit or Cut.LostAcknowledgement) CommitCut(cut);
        Drain();
        Require(signals || ProbeReceiver.Births == births, "A1: admission constructed the authored receiver");
        Require(Inbox.Pending.Count == 1 && Inbox.Receipts["a"].Order == 1, "A2: first admission lost");
        var malformed = "request" + ++_transport;
        _requests[malformed] = "";
        Add(new(malformed, _instance, InvocationInput.Kind, Payload: null, ReplyTo: ReplyTo.Of("outside", malformed)));
        Drain();
        Require(Replies[^1].Result == "Invalid" && Inbox.Pending.Count == 1 && State.Frames.ContainsKey(_frame),
            "A11: absent payload parked the receiver or changed admitted work");
        if (cut is Cut.Reload or Cut.StaleOwner) ChangeOwner(cut == Cut.StaleOwner);
        var before = Replies.Length;
        Send("a", "name", null); Drain();
        Require(Replies.Length == before + 1 && Replies[^1].Result == "Duplicate" && Inbox.Pending.Count == 1,
            "A2: retry duplicated or lost its semantic receipt");
        Send("a", "name", ""); Drain();
        Require(Replies[^1].Result == "Conflict" && Inbox.Pending.Count == 1, "A2: null and empty contents aliased");
        Send("b", "name", "second");
        Send("c", "name", "third"); Drain();
        Require(Replies[^1].Result == "Full" && Inbox.Pending.Select(i => i.Id).SequenceEqual(["a", "b"]),
            "A3: full admission evicted, reordered or overfilled pending inputs");
        Send("a", "name", null); Drain();
        Require(Replies[^1].Result == "Duplicate" && Replies[^1].Order == 1 && Inbox.Pending.Count == 2,
            "A2: pending capacity hid a retained duplicate");
        Send("a", "name", ""); Drain();
        Require(Replies[^1].Result == "Conflict" && Inbox.Pending.Count == 2, "A2: pending capacity hid an ID conflict");
        for (var i = 0; i < 5; i++) { Send("c", "name", "third"); Drain(); }
        // Reset may send a "lost" end before a later owner's committed replay response. It is uncertainty, not a receipt.
        foreach (var group in _world.Deliveries.Where(x => x.Reply.Channel == Request.Response).GroupBy(x => x.Reply.Request))
            Require(group.Count() == 1 && group.Single().Reply.Item is Admission receipt && receipt.Id == _requests[group.Key],
                "A5: callers received another request's output");
        Require(_world.Deliveries.Where(x => x.Reply.Channel is null).All(x => x.Reply.Error == "lost" && x.Reply.Item is null),
            "A5: uncertain reset was reported as a semantic admission outcome");
        if (blocked)
        {
            Require(State.Frames[_frame].State!.Deserialize<ProbeState>()!.Phase == "AwaitingRecovery" &&
                _world.Store.Instances[_instance.Id].Status == InstanceStatus.Running, "A10: fault did not preserve a blocked receiver");
            Console.WriteLine($"PASS blocked ordinary-child fault,{mode},nested={nested}");
            return;
        }
        ProbeReceiver.Forbidden = false;
        // A normal callback selects a, then waits durably again: a must still occupy capacity.
        Resume("release"); Drain();
        Require(Inbox.Receipts["a"].Status == "Selected", "A6: callback did not select the first input");
        Send("c", "name", "third"); Drain();
        Require(Replies[^1].Result == "Full", "A6: selected input freed its capacity too early");
        // Both mixed orders, with no later outside arrival: held suffixes must wake by their own committed continuation.
        Resume("finish"); var heldAdmission = Send("d", "name", "fourth"); SplitAndReload(heldAdmission); Drain();
        Require(Inbox.Receipts["b"].Status == "Completed" && Inbox.Receipts.ContainsKey("d"), "A7: normal/admission batch stalled");
        Require(!_world.Deliveries.Any(x => x.Reply.Channel == "notice"), "A5: admission callers retained workflow observation");
        Send("e", "name", "fifth"); var heldNormal = Resume("park"); SplitAndReload(heldNormal); Drain();
        Require(Inbox.Receipts.ContainsKey("e") && State.Frames.Values.Any(f => f.Node == "park"), "A7: admission/normal batch stalled");
        // No Event is needed for transport correlation; an obsolete occurrence is refused without another queue entry.
        Send("obsolete", "name", null, _frame + "-old"); Drain();
        Require(Replies[^1].Result == "UnknownInvocation", "A8: stale frame was retargeted");
        // A normal callback settles d/e, exhausting the independently bounded receipt budget.
        Resume("park"); Drain();
        Send("f", "name", "sixth"); Drain();
        Require(Replies[^1].Result == "ReceiptFull" && Inbox.Pending.Count == 0, "A9: retained receipt bound was not enforced");
        Send("a", "name", null); Drain();
        Require(Replies[^1].Result == "Duplicate" && Replies[^1].Order == 1 && Replies[^1].Outcome == "done" && Inbox.Pending.Count == 0,
            "A2: receipt capacity hid the completed duplicate/outcome");
        Send("a", "name", ""); Drain();
        Require(Replies[^1].Result == "Conflict" && Inbox.Pending.Count == 0, "A2: receipt capacity hid an ID conflict");
        Require(Inbox.Receipts.Keys.Order().SequenceEqual(new[] { "a", "b", "d", "e" }.Order()), "A9: accepted receipt disappeared");
        Console.WriteLine($"PASS {mode},nested={nested},cut={cut},blockedSignal={signals}");
    }

    private string Send(string id, string name, string? value, string? frame = null)
    {
        var transport = "request" + ++_transport;
        _requests[transport] = id;
        Add(new(transport, _instance, InvocationInput.Kind, Json.Write(new InvocationInput(frame ?? _frame, id, name, value)),
            ReplyTo: ReplyTo.Of("outside", transport, beforeCommit: true)));
        return transport;
    }
    private string Resume(string name)
    {
        var id = "resume" + ++_transport;
        Add(new(id, _instance, NativeKinds.Resume, Json.Write(new ResumeStimulus(name))));
        return id;
    }
    private void Add(Message message) => Apply(new("add", M: message));
    private void Apply(Act act)
    {
        var receipts = Replies.Length;
        _world.Apply(act);
        Require(!ProbeReceiver.Forbidden || ProbeReceiver.AttemptsWhileForbidden == 0, "A1: admission constructed the authored receiver");
        if (act.Kind == "turn") Require(Replies.Length == receipts, "A4: admission reply escaped before commit");
        if (_world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
    }
    private void Drain()
    {
        for (var i = 0; i < 150; i++)
        {
            Apply(new("poll", Actor.Id));
            if (Actor.Worker.NeedsLoad) Apply(new("load", Actor.Id));
            if (Actor.Worker.Ready.Contains(_instance.Id)) Apply(new("turn", Actor.Id, S: _instance.Id));
            else if (!Actor.Worker.NeedsFlush && !Actor.Worker.NeedsMeet) return;
            if (Actor.Worker.NeedsFlush) Apply(new("flush", Actor.Id));
            if (Actor.Worker.NeedsMeet) Apply(new("meet", Actor.Id));
        }
        throw new InvalidOperationException(signals ? "A12: blocked signal starved metadata admission" : "A7: mixed inbox did not drain within its work bound");
    }
    private void CommitCut(Cut failure)
    {
        PrepareTurn();
        _world.Auto = false;
        var call = Actor.Calls.Count;
        Apply(new("flush", Actor.Id));
        Require(Actor.Calls.Count == call + 1 && Actor.Calls[call].Kind == "commit", "A0: wrong commit cut");
        var replies = Replies.Length;
        Apply(new(failure == Cut.BeforeCommit ? "lose" : "exec", Actor.Id, call));
        Apply(new("answer", Actor.Id, call, Flag: true));
        Require(Replies.Length == replies, "A4: uncertain commit emitted a receipt");
        _world.Auto = true;
        ChangeOwner(false);
    }
    private void PrepareTurn()
    {
        Apply(new("poll", Actor.Id));
        if (Actor.Worker.NeedsLoad) Apply(new("load", Actor.Id));
        if (Actor.Worker.NeedsFlush) Apply(new("flush", Actor.Id));
        Apply(new("turn", Actor.Id, S: _instance.Id));
    }
    private void SplitAndReload(string suffix)
    {
        PrepareTurn();
        Apply(new("flush", Actor.Id));
        Require(_world.Store.Messages.TryGetValue(suffix, out var held) && !held.Done &&
            _world.Store.Messages.Values.Any(m => !m.Done && m.Message.Kind == NativeKinds.Continue),
            "A7: split turn did not retain suffix and its durable wake");
        ChangeOwner(false);
    }
    private void ChangeOwner(bool zombie)
    {
        var old = Actor;
        var call = -1;
        if (zombie)
        {
            Send("a", "name", null);
            PrepareTurn();
            _world.Auto = false;
            call = old.Calls.Count;
            Apply(new("flush", old.Id));
            Require(old.Calls.Count == call + 1 && old.Calls[call].Kind == "commit", "A0: stale admission commit not staged");
            _world.Auto = true;
        }
        Apply(new("crash", _owner, Flag: zombie));
        Apply(new("tick", 20000));
        _owner = 1 - _owner;
        Apply(new("take", Actor.Id));
        Require(Actor.Worker.Owns, "A0: owner did not take partition");
        if (zombie)
        {
            var position = _world.Store.Partitions[0].Position;
            var replies = Replies.Length;
            Apply(new("exec", old.Id, call));
            Apply(new("answer", old.Id, call));
            Require(!old.Worker.Owns && position == _world.Store.Partitions[0].Position && Replies.Length == replies,
                "A0: stale admission commit changed state or released its receipt");
            Drain();
        }
    }
    private static void Require(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); }
}

public sealed record ProbeState(string Phase, string? Selected = null);
public sealed class ProbeFault : Activity
{
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) => new(Fault("probe", "ordinary child failure"));
}
public sealed class ProbeNotice : Activity
{
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) =>
        new(Complete().With(new EmitEffect("notice", Values.From("ordinary output"))));
}
public sealed class ProbeReceiver : CompositeActivity, ISignalledComposite
{
    public static bool Forbidden;
    public static int Births, AttemptsWhileForbidden;
    public ProbeReceiver()
    {
        Births++;
        if (Forbidden) { AttemptsWhileForbidden++; throw new InvalidOperationException("A1: receiver hydration"); }
        AttemptsWhileForbidden = 0;
    }
    protected override void Execute(CompositeContext context)
    {
        context.RegisterInputInbox(2, 4, TimeSpan.FromMinutes(1));
        context.SetState(new ProbeState("Waiting"));
        context.Schedule(context.Children.Single(c => c.Id == "phase"));
    }
    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        var state = context.ReadState<ProbeState>()!;
        if (child.Node == "phase")
        {
            var selected = context.SelectInput()!;
            context.SetState(new ProbeState("Action", selected.Id));
            context.Schedule(context.Children.Single(c => c.Id == "finish"));
            return;
        }
        if (state.Selected is { } id) context.FinishInput(id, "done");
        while (context.SelectInput() is { } pending) context.FinishInput(pending.Id, "done");
        context.SetState(new ProbeState("Settled"));
        context.Schedule(context.Children.Single(c => c.Id == (child.Node == "finish" ? "notice" : "park")));
    }
    protected override void OnChildFaulted(CompositeContext context, ChildFaulted child) =>
        context.SetState(new ProbeState("AwaitingRecovery"));
    public bool CanAccept(CompositeContext context) => false;
    public void Accept(CompositeContext context, string messageId, CompositeSignal signal) =>
        throw new InvalidOperationException("A12: a blocked signal bypassed its evaluation gate");
}
