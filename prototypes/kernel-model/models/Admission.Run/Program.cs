using System.Text.Json;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
foreach (var nested in new[] { false, true })
foreach (var cut in Enum.GetValues<Cut>()) new Witness(mode, nested, cut).Run();
foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
foreach (var nested in new[] { false, true }) new Witness(mode, nested, Cut.None, blocked: true).Run();
foreach (var defect in new[] { AdmissionFlaw.Hydrate, AdmissionFlaw.ForgetIds, AdmissionFlaw.IgnoreCapacity, AdmissionFlaw.EarlyReply })
{
    var name = defect switch { AdmissionFlaw.Hydrate => "A1", AdmissionFlaw.ForgetIds => "A2",
        AdmissionFlaw.IgnoreCapacity => "A3", _ => "A4" };
    try { new Witness(Modes.Balanced, false, Cut.None, defect).Run(); }
    catch (InvalidOperationException error) when (error.Message.StartsWith(name + ":", StringComparison.Ordinal))
    { Console.WriteLine($"Detected {defect}: {error.Message}"); continue; }
    throw new InvalidOperationException($"Negative control {defect} did not fail its named assertion {name}.");
}
Console.WriteLine("24 schedules passed; 4 named negative controls detected. Model reload only, not host/provider qualification.");

enum Cut { None, BeforeCommit, LostAcknowledgement, Reload, StaleOwner }

sealed class Witness(string mode, bool nested, Cut cut, AdmissionFlaw defect = AdmissionFlaw.None, bool blocked = false)
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
            Wf.Event("finish", "finish"), Wf.Event("park", "park"));
        var root = nested ? Wf.Sequence("parent", receiver, Wf.Set("after", "returned", true)) : receiver;
        NativeWorkflows.Registry.Add(Wf.Workflow("admission-probe", root));
        Apply(new("take", Actor.Id));
        Add(new("start", _instance, Kinds.Start, Start: NativeModel.Spec("admission-probe", mode)));
        Drain();
        _frame = State.Frames.Values.Single(f => f.Node == "receiver").Id;
        Require(State.Variables["exitDone"]!.GetValue<bool>(), "A0: earlier ordinary work did not commit");
        Require((State.Frames[_frame].Parent is not null) == nested, "A0: placement mismatch");
        var births = ProbeReceiver.Births;
        ProbeReceiver.Forbidden = true;
        Send("a", "name", null);
        if (cut is Cut.BeforeCommit or Cut.LostAcknowledgement) CommitCut(cut);
        Drain();
        Require(ProbeReceiver.Births == births, "A1: admission constructed the authored receiver");
        Require(Inbox.Pending.Count == 1 && Inbox.Receipts["a"].Order == 1, "A2: first admission lost");
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
        for (var i = 0; i < 5; i++) { Send("c", "name", "third"); Drain(); }
        foreach (var group in _world.Deliveries.GroupBy(x => x.Reply.Request))
            Require(group.Count() == 1 && group.Single().Reply.Item is Admission receipt && receipt.Id == _requests[group.Key],
                "A5: callers received another request's output");
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
        Resume("finish"); Send("d", "name", "fourth"); Drain();
        Require(Inbox.Receipts["b"].Status == "Completed" && Inbox.Receipts.ContainsKey("d"), "A7: normal/admission batch stalled");
        Send("e", "name", "fifth"); Resume("park"); Drain();
        Require(Inbox.Receipts.ContainsKey("e") && State.Frames.Values.Any(f => f.Node == "park"), "A7: admission/normal batch stalled");
        // No Event is needed for transport correlation; an obsolete occurrence is refused without another queue entry.
        Send("obsolete", "name", null, _frame + "-old"); Drain();
        Require(Replies[^1].Result == "UnknownInvocation", "A8: stale frame was retargeted");
        // A normal callback settles d/e, exhausting the independently bounded receipt budget.
        Resume("park"); Drain();
        Send("f", "name", "sixth"); Drain();
        Require(Replies[^1].Result == "ReceiptFull" && Inbox.Pending.Count == 0, "A9: retained receipt bound was not enforced");
        Require(Inbox.Receipts.Keys.Order().SequenceEqual(new[] { "a", "b", "d", "e" }.Order()), "A9: accepted receipt disappeared");
        Console.WriteLine($"PASS {mode},nested={nested},cut={cut}");
    }

    private void Send(string id, string name, string? value, string? frame = null)
    {
        var transport = "request" + ++_transport;
        _requests[transport] = id;
        Add(new(transport, _instance, InvocationInput.Kind, Json.Write(new InvocationInput(frame ?? _frame, id, name, value)),
            ReplyTo: ReplyTo.Of("outside", transport, beforeCommit: true)));
    }
    private void Resume(string name) => Add(new("resume" + ++_transport, _instance, NativeKinds.Resume,
        Json.Write(new ResumeStimulus(name))));
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
        throw new InvalidOperationException("A7: mixed inbox did not drain within its work bound");
    }
    private void CommitCut(Cut failure)
    {
        Apply(new("poll", Actor.Id));
        if (Actor.Worker.NeedsLoad) Apply(new("load", Actor.Id));
        if (Actor.Worker.NeedsFlush) Apply(new("flush", Actor.Id));
        Apply(new("turn", Actor.Id, S: _instance.Id));
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
    private void ChangeOwner(bool zombie)
    {
        var old = Actor;
        Apply(new("crash", _owner, Flag: zombie));
        Apply(new("tick", 20000));
        _owner = 1 - _owner;
        Apply(new("take", Actor.Id));
        Require(Actor.Worker.Owns, "A0: owner did not take partition");
        if (zombie)
        {
            var position = _world.Store.Partitions[0].Position;
            Apply(new("beat", old.Id));
            Require(!old.Worker.Owns && position == _world.Store.Partitions[0].Position, "A0: stale fence advanced");
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
public sealed class ProbeReceiver : CompositeActivity
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
        context.RegisterInputInbox(2, 4);
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
        context.Schedule(context.Children.Single(c => c.Id == "park"));
    }
    protected override void OnChildFaulted(CompositeContext context, ChildFaulted child) =>
        context.SetState(new ProbeState("AwaitingRecovery"));
}
