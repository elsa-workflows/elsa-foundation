using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

sealed partial class CreationWitness
{
    internal void VisitIsolation()
    {
        NativeWorkflows.Registry.Add(Wf.Workflow("creation-probe", Wf.ForEach("each", Wf.Expr("[1,2]"),
            Wf.Node<VisitReceiver>("receiver").With(Wf.Event("step", "step")))));
        var other = InstanceRef.In(0, "other-creation-probe");
        Apply(new("take", Actor.Id));
        Send(Kinds.Start, 1, start: Spec(1)); Drain();
        Send(Kinds.Start, 1, start: Spec(1), target: other); Drain(other);
        var first = Frame;
        var otherState = Json.Read<NativeState>(_world.Store.Instances[other.Id].State);
        Require(otherState.Frames.Values.Single(f => f.Node == "receiver").Id == first, "C7", "two actors did not actually have equal local frames");
        void Input(string frame, string id, InstanceRef? target = null)
        { Send(InvocationInput.Kind, 1, new InvocationInput(frame, id, "event", target?.Id ?? "primary"), reply: true, target: target); Drain(target); }
        Input(first, "same"); Require(Last is Admission { Result: "Accepted", Order: 1 }, "C7", "primary first input refused");
        Input(first, "same", other); Require(Last is Admission { Result: "Accepted", Order: 1 }, "C7", "equal frame in another actor shared receipt identity");
        Send(NativeKinds.Resume, 1, new ResumeStimulus("step")); Drain();
        Require(Frame == first && State.Inboxes![first].Receipts["same"].Status == "Completed", "C7", "normal action did not finish input while its receiver remained active");
        Input(first, "next"); Require(Last is Admission { Result: "ReceiptFull" }, "C7", "active receipt budget released before expiry");
        Apply(new("tick", 1100)); Input(first, "next");
        Require(Last is Admission { Result: "Accepted", Order: 2 }, "C7", "active receipt budget not reusable after exact captured expiry");
        Send(NativeKinds.Resume, 1, new ResumeStimulus("step")); Drain();
        var second = Frame;
        Require(first != second && State.Inboxes![first].Closed is not null, "C7", "ordinary same-node re-entry reused a visit frame");
        Input(first, "late"); Require(Last is Admission { Result: "Ended" } && State.Inboxes![second].Receipts.Count == 0,
            "C7", "old visit input reached the new visit");
        Input(second, "same"); Require(Last is Admission { Result: "Accepted", Order: 1 }, "C7", "new visit could not independently accept same sender ID");
        otherState = Json.Read<NativeState>(_world.Store.Instances[other.Id].State);
        Require(otherState.Inboxes![first].Receipts["same"].Status == "Queued" && otherState.Frames.ContainsKey(first),
            "C7", "primary completion or expiry changed the equal frame in another actor");
        Console.WriteLine($"PASS creation:visit-isolation,{mode}");
    }
}

public sealed class VisitReceiver : CompositeActivity
{
    protected override void Execute(CompositeContext context)
    { context.RegisterInputInbox(1, 1, TimeSpan.FromSeconds(1)); context.SetState(0); context.Schedule(context.Children.Single()); }
    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        while (context.SelectInput() is { } input) context.FinishInput(input.Id, "handled");
        var count = context.GetState<int>() + 1; context.SetState(count);
        if (count == 2) context.Complete(); else context.Schedule(context.Children.Single());
    }
}
