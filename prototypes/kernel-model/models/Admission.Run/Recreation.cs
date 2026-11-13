using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

// Expected counterexample, separate from the passing admission schedules. No kernel defect is fixed here.
internal static class InvocationRecreation
{
    public static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        foreach (var nested in new[] { false, true }) Reproduce(mode, nested);
        Console.WriteLine("4/4 delete/recreate counterexamples observed; I22 rejects recreation. Shared identity gate remains OPEN.");
    }

    private static void Reproduce(string mode, bool nested)
    {
        var world = new World(new Config(1, 1, Flaw.None, Native: true)) { Auto = true };
        var actor = world.Of(0, 0);
        var target = InstanceRef.In(0, "reused-invocation");
        var receiver = Wf.Node<RecreationReceiver>("receiver").With(Wf.Event("finish", "finish"));
        var root = nested ? Wf.Sequence("parent", receiver, Wf.Set("after", "returned", true)) : receiver;
        NativeWorkflows.Registry.Add(Wf.Workflow("recreation-probe", root));
        var recreated = false;
        var sawI22 = false;
        var serial = 0;
        void Require(bool ok, string message)
        { if (!ok) throw new InvalidOperationException("R1: " + message); }
        void Apply(Act act)
        {
            world.Apply(act);
            if (world.Check() is not { } error) return;
            // The existing oracle deliberately forbids reused IDs. Preserve this failure; do not call the run green.
            if (!recreated || error != $"I22 {target.Id} was deleted and has a row again")
                throw new InvalidOperationException("Kernel: " + error);
            sawI22 = true;
        }
        void Add(string kind, string? payload = null, StartSpec? start = null, bool reply = false)
        {
            var id = "recreation-message-" + ++serial;
            Apply(new("add", M: new Message(id, target, kind, payload, Start: start,
                ReplyTo: reply ? ReplyTo.Of("outside", id) : null)));
        }
        void Drain()
        {
            for (var i = 0; i < 150; i++)
            {
                Apply(new("poll", actor.Id));
                if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
                if (actor.Worker.Ready.Contains(target.Id)) Apply(new("turn", actor.Id, S: target.Id));
                else if (!actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) return;
                if (actor.Worker.NeedsFlush) Apply(new("flush", actor.Id));
                if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
            }
            throw new InvalidOperationException("R1: drain exceeded its work bound");
        }
        NativeState State() => Json.Read<NativeState>(world.Store.Instances[target.Id].State);
        Admission Last() => world.Deliveries.Select(x => x.Reply.Item).OfType<Admission>().Last();
        Apply(new("take", actor.Id));
        Add(Kinds.Start, start: NativeModel.Spec("recreation-probe", mode)); Drain();
        var oldFrame = State().Frames.Values.Single(f => f.Node == "receiver").Id;
        var oldInput = new InvocationInput(oldFrame, "sender-input", "late-event", "old-creation-payload");
        Add(InvocationInput.Kind, Json.Write(oldInput), reply: true); Drain();
        Require(Last().Result == "Accepted" && Last().Order == 1, "old creation did not accept its input");
        Add(NativeKinds.Resume, Json.Write(new ResumeStimulus("finish"))); Drain();
        Require(world.Store.Instances[target.Id].Status == InstanceStatus.Finished, "ordinary child did not complete workflow");
        if (nested) Require(State().Variables["returned"]!.GetValue<bool>(), "caller did not continue");
        Add(Kinds.Delete); Drain();
        Require(!world.Store.Instances.ContainsKey(target.Id), "normal delete did not remove ended row");
        recreated = true;
        Add(Kinds.Start, start: NativeModel.Spec("recreation-probe", mode)); Drain();
        var newFrame = State().Frames.Values.Single(f => f.Node == "receiver").Id;
        Require(newFrame == oldFrame && State().Inboxes![newFrame].Receipts.Count == 0,
            "fixture no longer recreates the same local address; reassess the counterexample");
        Add(InvocationInput.Kind, Json.Write(oldInput), reply: true); Drain();
        var inbox = State().Inboxes![newFrame];
        Require(Last().Result == "Accepted" && Last().Order == 1 && inbox.Pending.Single() == oldInput,
            "old invocation input no longer aliases the new creation; replace this expected-counterexample probe with a regression");
        Require(sawI22, "existing non-recreation oracle did not report its coverage boundary");
        Console.WriteLine($"COUNTEREXAMPLE {mode},nested={nested}: {target.Id}/{oldFrame} reused; old input accepted fresh into new creation; I22 observed");
    }
}

public sealed class RecreationReceiver : CompositeActivity
{
    protected override void Execute(CompositeContext context)
    {
        context.RegisterInputInbox(1, 2);
        context.Schedule(context.Children.Single());
    }
    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.Complete();
}
