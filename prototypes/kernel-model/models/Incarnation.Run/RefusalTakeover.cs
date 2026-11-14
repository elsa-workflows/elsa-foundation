using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

sealed partial class CreationWitness
{
    internal void RefusalTakeover(bool missing, string control)
    {
        var trace = new List<string>(); _world.Log = trace.Add;
        NativeWorkflows.Registry.Add(Wf.Workflow("creation-probe", Wf.Node<IdentityReceiver>("receiver").With(Wf.Event("finish", "finish"))));
        Apply(new("take", Actor.Id)); Send(Kinds.Start, 1, start: Spec(1)); Drain();
        Send(Kinds.Suspend, 1); Drain();
        Require(Row.Suspended, "C8", "normal suspend did not reach its durable boundary");
        var births = IdentityReceiver.Births;
        Send(InvocationInput.Kind, missing ? null : 2, new InvocationInput(Frame, "stale", "event"), reply: true);
        var stale = _world.Store.Messages.Values.Single(m => m.Message.Kind == InvocationInput.Kind).Message.Id;
        Apply(new("poll", Actor.Id)); Apply(new("flush", Actor.Id)); // mark it seen, but do not run its refusal
        Require(_world.Store.Messages[stale].Seen > 0 && Row.Suspended, "C8", "old owner did not actually mark the pending stale message");
        Send(control, 1); Takeover(); Drain();
        Require(control == Kinds.Resume ? !Row.Suspended && Row.Status == InstanceStatus.Running : Row.Status == InstanceStatus.Cancelled,
            "C8", "inherited identity refusal blocked a valid control behind it");
        Require(Last is CreationResult result && result.Result == (missing ? "IdentityRequired" : "WrongCreation") &&
            !_world.Store.Messages.ContainsKey(stale) && trace.Any(s => s.Contains("on trial [" + stale + "]", StringComparison.Ordinal)) &&
            IdentityReceiver.Births == births, "C8", "inherited refusal skipped its trial/fence or activated authored work");
        Console.WriteLine($"PASS creation:refusal-takeover,{mode},missing={missing},control={control}");
    }
}
