using Kernel.Durable;
using Kernel.Native;
using Kernel.Sim;
using Kernel.Sim.Native;

sealed partial class CreationWitness
{
    internal void TransportRace(string kind)
    {
        Node Root(int delay) => kind == "event" ? Wf.Event("event", "creation-event")
            : Wf.Node<FirstWait>("receiver").With(Wf.Event("finish", "finish"), Wf.Delay("timer", delay));
        NativeWorkflows.Registry.Add(Wf.Workflow("creation-old", Root(1000))).Add(Wf.Workflow("creation-new", Root(10_000)));
        Apply(new("take", Actor.Id)); Send(Kinds.Start, 1, start: Spec(1) with { Definition = "creation-old" }); Drain();
        Message old;
        if (kind == "event")
        {
            Events.PublishAsync(_world.Store, new EventRow("event:creation-event", "old-publication", Json.Write("old"), _world.Time + 100)).GetAwaiter().GetResult();
            old = _world.Store.Messages.Values.Single(m => m.Message.Kind == Kinds.Event).Message;
            Require(Events.Delivered(old)?.Event == "old-publication", "C5", "qualified delivery lost its original logical event identity");
        }
        else
        {
            old = _world.Store.Messages.Values.Single(m => m.Message.Kind == NativeKinds.Timer).Message;
            Send(NativeKinds.Resume, 1, new ResumeStimulus("finish"));
        }
        Drain(); Require(Row.Status == InstanceStatus.Finished && old.TargetCreation == 1, "C5", "original wait did not complete normally");
        Send(Kinds.Delete, 1); Drain(); Require(Row.Retired, "C5", "original wait owner did not retire");
        if (kind == "event") { Apply(new("tick", 200)); Apply(new("sweep")); }
        Require(!_world.Store.Messages.ContainsKey(old.Id), "C5", "old carrier is still deduplicated; this would not exercise target dispatch");
        Send(Kinds.Start, 2, start: Spec(2) with { Definition = "creation-new" }); Drain();
        var before = Row;
        Apply(new("add", M: old));
        if (kind == "timer") Apply(new("tick", 1000));
        Drain(); Require(Row == before && Row.Status == InstanceStatus.Running, "C5", "old pinned wait completed a replacement creation");
        if (kind == "event")
            Events.PublishAsync(_world.Store, new EventRow("event:creation-event", "new-publication", Json.Write("new"), _world.Time + 100)).GetAwaiter().GetResult();
        else Apply(new("tick", 9000));
        Drain(); Require(Row.Status == InstanceStatus.Finished, "C5", "correct new event/deadline did not complete");
        if (kind == "event") Require(State.Results["event"]!.GetValue<string>() == "new", "C5", "replacement wait received the old payload");
        Console.WriteLine($"PASS creation:transport-race,{mode},kind={kind}");
    }
}

public sealed class FirstWait : CompositeActivity
{
    protected override void Execute(CompositeContext context)
    { foreach (var child in context.Children) context.Schedule(child); }
    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.Complete();
}
