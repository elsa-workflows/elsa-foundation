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
    internal NativeState State => Json.Read<NativeState>(World.Store.Instances[Target.Id].State);
    internal static void Require(bool ok, string name, string detail)
    { if (!ok) throw new InvalidOperationException(name + ": " + detail); }
    internal void Apply(Act act)
    {
        try { World.Apply(act); }
        catch (NullReferenceException nullError) { throw new InvalidOperationException("T5: missing model state reached a null reducer", nullError); }
        if (World.Check() is { } invariant) throw new InvalidOperationException("Kernel: " + invariant);
    }
    internal void Send(string kind, object? input = null, StartSpec? start = null, bool reply = false)
    {
        var id = "edge-message-" + ++_serial;
        Apply(new("add", M: new Message(id, Target, kind, input is null ? null : Json.Write(input), Start: start,
            ReplyTo: reply ? ReplyTo.Of("outside", id) : null)));
    }
    internal void Drain()
    {
        var actor = World.Of(0, 0);
        for (var i = 0; i < 150; i++)
        {
            Apply(new("poll", actor.Id));
            if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
            if (actor.Worker.Ready.Contains(Target.Id)) Apply(new("turn", actor.Id, S: Target.Id));
            else if (!actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) return;
            if (actor.Worker.NeedsFlush) Apply(new("flush", actor.Id));
            if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
        }
        throw new InvalidOperationException("T5: edge harness did not settle");
    }
    internal Admission Query(string frame, string id)
    {
        var before = World.Deliveries.Count;
        Send(ReceiptQuery.Kind, new ReceiptQuery(frame, id), reply: true); Drain();
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
