using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

sealed partial class CreationWitness
{
    internal void HeldSingleton()
    {
        HeldReceiver.Accepts = 0;
        NativeWorkflows.Registry.Add(Wf.Workflow("held-child", Wf.Event("release", "release")))
            .Add(Wf.Workflow("creation-probe", Wf.Node<HeldReceiver>("receiver").With(Wf.Dispatch("call", "held-child", true, mode: mode))));
        Apply(new("take", Actor.Id)); Send(Kinds.Start, 1, start: Spec(1)); Drain();
        var child = _world.Store.Messages.Values.Single(m => m.Message.Start?.Parent == _target.Id).Message.Target; Drain(child);
        Send(CompositeSignal.Kind, 1, new CompositeSignal(Frame, "deferred")); Drain();
        var held = _world.Store.Messages.Values.Single(m => m.Message.Kind == CompositeSignal.Kind).Message.Id;
        Require(Actor.Worker.Messages.Any(m => m.Id == held) && Row.Status == InstanceStatus.Running && HeldReceiver.Accepts == 0,
            "C9", "ordinary composite input was not actually deferred");
        var before = Row;
        Send(InvocationInput.Kind, 2, new InvocationInput(Frame, "stale", "event"), reply: true); Drain();
        Require(_world.Deliveries.Any(d => d.Reply.Item is CreationResult { Result: "WrongCreation" }) && Row == before && HeldReceiver.Accepts == 0,
            "C9", "held prefix blocked identity refusal or activated authored work");
        Send(Kinds.Start, 1, start: Spec(1), reply: true); Drain();
        Require(Last is CreationResult { Result: "ExistingCreation" } && Row == before, "C9", "held prefix blocked lifecycle retry");
        Send(NativeKinds.Resume, 1, new ResumeStimulus("release"), target: child); Drain(child); Drain();
        Require(Row.Status == InstanceStatus.Finished && HeldReceiver.Accepts == 1 && !_world.Store.Messages.ContainsKey(held),
            "C9", "held prefix blocked child completion or its deferred-input wake");
        Console.WriteLine($"PASS creation:held-singleton,{mode}");
    }

    internal void NativeChildCancel()
    {
        NativeWorkflows.Registry.Add(Wf.Workflow("cancel-child", Wf.Event("waiting", "waiting")))
            .Add(Wf.Workflow("creation-probe", Wf.Node<FirstWait>("receiver")
                .With(Wf.Dispatch("call", "cancel-child", true, mode: mode), Wf.Event("winner", "winner"))));
        Apply(new("take", Actor.Id)); Send(Kinds.Start, 1, start: Spec(1)); Drain();
        var child = _world.Store.Messages.Values.Single(m => m.Message.Start?.Parent == _target.Id).Message.Target; Drain(child);
        Send(NativeKinds.Resume, 1, new ResumeStimulus("winner")); Drain();
        var cancel = _world.Store.Messages.Values.Single(m => m.Message.Target == child && m.Message.Kind == NativeKinds.Cancel).Message;
        Require(Row.Status == InstanceStatus.Finished && cancel.TargetCreation == 1, "C10", "ordinary native branch cancellation lost its child creation");
        Drain(child); Drain();
        Require(_world.Store.Instances[child.Id].Status == InstanceStatus.Cancelled, "C10", "ordinary cancelled child remained active");
        Send(Kinds.Delete, 1, target: child); Drain(child);
        Send(Kinds.Start, 2, start: NativeModel.Spec("cancel-child", mode) with { Creation = new(2, 1, "replacement-child") }, target: child); Drain(child);
        var replacement = _world.Store.Instances[child.Id];
        Apply(new("add", M: cancel)); Drain(child);
        Require(_world.Store.Instances[child.Id] == replacement && replacement.Status == InstanceStatus.Running,
            "C10", "old native branch cancellation reached a replacement child");
        Console.WriteLine($"PASS creation:native-child-cancel,{mode}");
    }
}

public sealed class HeldReceiver : CompositeActivity, ISignalledComposite
{
    public static int Accepts;
    protected override void Execute(CompositeContext context)
    { context.SetState(false); context.Schedule(context.Children.Single()); }
    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.SetState(true);
    public bool CanAccept(CompositeContext context) => context.GetState<bool>();
    public void Accept(CompositeContext context, string messageId, CompositeSignal signal)
    { Accepts++; context.Complete(); }
}
