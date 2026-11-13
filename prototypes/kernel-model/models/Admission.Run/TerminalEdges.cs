using Kernel.Workflows;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

internal sealed class EdgeHarness(AdmissionFlaw defect)
{
    internal readonly World World = new(new Config(1, 1, Flaw.None, Native: true, AdmissionFlaws: defect)) { Auto = true };
    internal readonly InstanceRef Target = InstanceRef.In(0, "terminal-edge");
    private int _serial;
    internal readonly HashSet<string> FinishedNotices = [];
    internal NativeState State => Json.Read<NativeState>(World.Store.Instances[Target.Id].State);
    internal static void Require(bool ok, string name, string detail)
    { if (!ok) throw new InvalidOperationException(name + ": " + detail); }
    internal void Apply(Act act)
    {
        try { World.Apply(act); }
        catch (NullReferenceException nullError) { throw new InvalidOperationException("T5: missing model state reached a null reducer", nullError); }
        foreach (var message in World.Store.Messages.Values.Select(x => x.Message))
            if (message.Kind == WorkflowKinds.ChildFinished && message.Target.Id == Target.Id) FinishedNotices.Add(message.Id);
        if (World.Check() is { } invariant) throw new InvalidOperationException("Kernel: " + invariant);
    }
    internal void Send(string kind, object? input = null, StartSpec? start = null, bool reply = false, InstanceRef? target = null)
    {
        var id = "edge-message-" + ++_serial;
        Apply(new("add", M: new Message(id, target ?? Target, kind, input is null ? null : Json.Write(input), Start: start,
            ReplyTo: reply ? ReplyTo.Of("outside", id) : null)));
    }
    internal void Drain()
    {
        var actor = World.Of(0, 0);
        for (var i = 0; i < 150; i++)
        {
            Apply(new("poll", actor.Id));
            if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
            var ready = actor.Worker.Ready.ToArray();
            foreach (var instance in ready) Apply(new("turn", actor.Id, S: instance));
            if (ready.Length == 0 && !actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) return;
            if (actor.Worker.NeedsFlush) Apply(new("flush", actor.Id));
            if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
        }
        throw new InvalidOperationException("T5: edge harness did not settle");
    }
    internal Admission Query(string frame, string id, InstanceRef? target = null)
    {
        var before = World.Deliveries.Count;
        Send(ReceiptQuery.Kind, new ReceiptQuery(frame, id), reply: true, target: target); Drain();
        Require(World.Deliveries.Count == before + 1 && World.Deliveries[^1].Reply.Item is Admission,
            "T5", "missing or duplicate metadata response");
        return (Admission)World.Deliveries[^1].Reply.Item!;
    }
}

internal static class TerminalEdges
{
    internal static void NullState(AdmissionFlaw defect = AdmissionFlaw.None)
    {
        var h = new EdgeHarness(defect);
        h.World.Oracle.Fragiles[h.Target.Id] = true; // expectation only: the normal missing-definition start must Park
        h.Apply(new("take", h.World.Of(0, 0).Id));
        h.Send(Kinds.Start, start: NativeModel.Spec("missing-terminal-definition")); h.Drain();
        var row = h.World.Store.Instances[h.Target.Id];
        EdgeHarness.Require(row.Status == InstanceStatus.Faulted && row.State == "null", "T5", "normal failed start did not persist absent model state");
        var reply = h.Query("never-registered", "sender-id");
        EdgeHarness.Require(reply.Result == "UnknownInvocation" && reply.Actor == h.Target.Id, "T5", "absent model state had no defined lookup result");
        h.Send(Kinds.Delete); h.Drain();
        EdgeHarness.Require(!h.World.Store.Instances.ContainsKey(h.Target.Id), "T5", "normal failed-start deletion wedged");
        Console.WriteLine("PASS terminal:null-state");
    }

    internal static void ParentNotice()
    {
        var h = new EdgeHarness(AdmissionFlaw.None);
        ReturnCounter.Calls = 0;
        TerminalReceiver.Forbidden = false;
        var child = Wf.Workflow("receipt-child", Wf.Node<TerminalReceiver>("receiver")
            .With(Wf.Event("step", "step"), Wf.Event("waiting", "waiting")));
        NativeWorkflows.Registry.Add(child).Add(Wf.Workflow("receipt-caller",
            Wf.Sequence("caller", Wf.Dispatch("call", child.Id, true), Wf.Node<ReturnCounter>("after"))));
        h.Apply(new("take", h.World.Of(0, 0).Id)); h.Send(Kinds.Start, start: NativeModel.Spec("receipt-caller")); h.Drain();
        var childRow = h.World.Store.Instances.Values.Single(row => row.Parent == h.Target.Id);
        var target = new InstanceRef(childRow.Id);
        var frame = Json.Read<NativeState>(childRow.State).Frames.Values.Single(f => f.Node == "receiver").Id;
        h.Send(InvocationInput.Kind, new InvocationInput(frame, "a", "business"), target: target); h.Drain();
        h.Send(NativeKinds.Resume, new ResumeStimulus("step"), target: target); h.Drain();
        EdgeHarness.Require(ReturnCounter.Calls == 1 && h.World.Store.Instances[h.Target.Id].Status == InstanceStatus.Finished &&
            h.FinishedNotices.Count == 1, "T8", "ordinary child/caller completion did not send one committed parent notice");
        TerminalReceiver.Forbidden = true;
        NativeWorkflows.Registry.Remove(child.Id);
        for (var i = 0; i < 3; i++)
        {
            var receipt = h.Query(frame, "a", target);
            EdgeHarness.Require(receipt.Result == "Found" && receipt.Outcome == "handled" && h.FinishedNotices.Count == 1 && ReturnCounter.Calls == 1,
                "T8", "retained metadata query sent another workflow-finished notice or repeated the caller");
        }
        NativeWorkflows.Registry.Add(child);
        TerminalReceiver.Forbidden = false;
        Console.WriteLine("PASS terminal:parent-notice");
    }

    internal static void SharedClock(AdmissionFlaw defect = AdmissionFlaw.None)
    {
        var h = new EdgeHarness(defect);
        Node Receiver(string id, int retention) => Wf.Node<SharedReceiptReceiver>(id, ("Retention", retention)).With(Wf.Event(id + "-step", "close"));
        var workflow = Wf.Workflow("shared-terminal-clock", Wf.Parallel("all", null, Receiver("fast", 1000), Receiver("slow", 2000)));
        NativeWorkflows.Registry.Add(workflow);
        h.Apply(new("take", h.World.Of(0, 0).Id)); h.Send(Kinds.Start, start: NativeModel.Spec(workflow.Id)); h.Drain();
        var frames = h.State.Frames.Values.Where(f => f.Node is "fast" or "slow").ToDictionary(f => f.Node, f => f.Id);
        foreach (var frame in frames.Values) { h.Send(InvocationInput.Kind, new InvocationInput(frame, "same-id", "business")); h.Drain(); }
        h.Apply(new("publish", B: 1, S: "event:close")); h.Drain();
        var state = h.State;
        var first = state.Inboxes![frames["fast"]].Receipts["same-id"];
        var second = state.Inboxes[frames["slow"]].Receipts["same-id"];
        EdgeHarness.Require(first.Clock is not null && first.Clock == second.Clock, "T6", "ordinary parallel completion did not share its commit fact");
        var origin = h.World.Time;
        h.Apply(new("tick", 1000));
        EdgeHarness.Require(h.Query(frames["fast"], "same-id").Result == "MissingOrExpired", "T6", "first receipt did not expire");
        var slow = h.Query(frames["slow"], "same-id");
        EdgeHarness.Require(slow.Result == "Found" && slow.ValidUntil == origin + 2000 && slow.FinalizedAt == origin,
            "T6", "cleanup released a fact still referenced by another invocation");
        h.Apply(new("tick", 1000)); h.Query(frames["slow"], "same-id");
        var facts = CommitClockFacts.Read(h.World.Store.Instances[h.Target.Id])!;
        EdgeHarness.Require(!facts.ContainsKey(first.Clock!) && facts.ContainsKey(WorkflowCommitClock.Terminal),
            "T6", "final cleanup leaked model clock or released workflow-owned fact");
        Console.WriteLine("PASS terminal:shared-clock");
    }
}

public sealed class SharedReceiptReceiver : CompositeActivity
{
    [Input] public int Retention { get; set; }
    protected override void Execute(CompositeContext context)
    {
        context.RegisterInputInbox(1, 1, TimeSpan.FromMilliseconds(Retention));
        context.Schedule(context.Children.Single());
    }
    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        var input = context.SelectInput()!;
        context.FinishInput(input.Id, "handled"); context.Complete();
    }
}

public sealed class ReturnCounter : Activity
{
    public static int Calls;
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
    { Calls++; return new(Complete()); }
}
