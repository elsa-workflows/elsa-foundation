using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

// Qualified regression replacing the historical #4394 counterexample. Historical source and failed evidence remain unchanged.
internal static class InvocationRecreation
{
    public static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        foreach (var nested in new[] { false, true }) Reproduce(mode, nested);
        Console.WriteLine("4/4 qualified delete/recreate regressions passed. Wider shared propagation and provider gates remain separate.");
    }

    private static void Reproduce(string mode, bool nested)
    {
        var world = new World(new Config(1, 1, Flaw.None, Native: true)) { Auto = true };
        var actor = world.Of(0, 0);
        var target = InstanceRef.In(0, "reused-invocation");
        var receiver = Wf.Node<RecreationReceiver>("receiver").With(Wf.Event("finish", "finish"));
        var root = nested ? Wf.Sequence("parent", receiver, Wf.Set("after", "returned", true)) : receiver;
        NativeWorkflows.Registry.Add(Wf.Workflow("recreation-probe", root));
        var serial = 0;
        void Require(bool ok, string message)
        { if (!ok) throw new InvalidOperationException("R1: " + message); }
        void Apply(Act act)
        {
            world.Apply(act);
            if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
        }
        void Add(string kind, string? payload = null, StartSpec? start = null, bool reply = false, long generation = 1)
        {
            var id = "recreation-message-" + ++serial;
            Apply(new("add", M: CreationFixture.Pin(new Message(id, target, kind, payload, Start: start,
                ReplyTo: reply ? ReplyTo.Of("outside", id, world.Time + 30_000) : null, TargetCreation: generation))));
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
        // Let the receipt promise expire while renewing the real owner lease in bounded steps.
        for (var elapsed = 0; elapsed < 60_000; elapsed += 5000)
        { Apply(new("tick", 5000)); Apply(new("beat", actor.Id)); }
        Add(Kinds.Delete); Drain();
        Require(world.Store.Instances[target.Id].Retired, "normal delete did not retire ended creation");
        Add(Kinds.Start, start: NativeModel.Spec("recreation-probe", mode) with { Creation = new(2, 1, "recreation-next") }, generation: 2); Drain();
        var newFrame = State().Frames.Values.Single(f => f.Node == "receiver").Id;
        Require(newFrame == oldFrame && State().Inboxes![newFrame].Receipts.Count == 0,
            "fixture no longer recreates the same local address; reassess the counterexample");
        Add(InvocationInput.Kind, Json.Write(oldInput), reply: true); Drain();
        var response = world.Deliveries.Last().Reply.Item;
        Require(response is CreationResult { Result: "WrongCreation" } && State().Inboxes![newFrame].Receipts.Count == 0,
            "old invocation reached the replacement creation");
        Add(InvocationInput.Kind, Json.Write(oldInput), reply: true, generation: 2); Drain();
        Require(Last().Result == "Accepted" && Last().Order == 1, "correct replacement invocation input failed");
        Console.WriteLine($"PASS recreation:{mode},nested={nested}: same local frame, distinct shared creation; old input refused");
    }
}

public sealed class RecreationReceiver : CompositeActivity
{
    protected override void Execute(CompositeContext context)
    {
        context.RegisterInputInbox(1, 2, TimeSpan.FromMinutes(1));
        context.Schedule(context.Children.Single());
    }
    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.Complete();
}
