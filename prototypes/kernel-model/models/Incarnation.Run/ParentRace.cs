using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;
using Kernel.Workflows;

sealed partial class CreationWitness
{
    internal void ParentRace(string cut)
    {
        NativeWorkflows.Registry.Add(Wf.Workflow("creation-child", Wf.Event("gate", "gate")))
            .Add(Wf.Workflow("creation-probe", Wf.Sequence("parent", Wf.Dispatch("call", "creation-child", true),
                Wf.Set("after", "returned", true), Wf.Event("tail", "tail"))));
        Apply(new("take", Actor.Id)); Send(Kinds.Start, 1, start: Spec(1)); Drain();
        var start = _world.Store.Messages.Values.Single(m => m.Message.Start?.Parent == _target.Id).Message;
        var child = start.Target;
        Require(start.TargetCreation == 1 && start.Start?.ParentCreation == 1 && start.Start.Creation?.Generation == 1 &&
            Json.Read<List<CreationAddress>>(Row.Children!).Single() == new CreationAddress(child.Id, 1),
            "C4", "parent did not atomically retain its exact pending child start");
        Require(!_world.Store.Instances.ContainsKey(child.Id), "C4", "child already committed before the cancellation race");
        Send(Kinds.Terminate, 1); Drain();
        var terminate = _world.Store.Messages.Values.Single(m => m.Message.Target == child && m.Message.Kind == Kinds.Terminate).Message;
        Require(terminate.TargetCreation == 1 && Row.Status == InstanceStatus.Cancelled, "C4", "pending-child cancellation was not pinned");
        Prepare(child);
        if (cut != "pending")
        {
            _world.Auto = false; var old = Actor; var call = old.Calls.Count; Apply(new("flush", old.Id));
            Require(old.Calls.Count == call + 1 && old.Calls[call].Kind == "commit", "C4", "child creation cut missed its actual commit");
            Apply(new(cut == "lost-ack" ? "exec" : "lose", old.Id, call));
            Require(_world.Store.Instances.ContainsKey(child.Id) == (cut == "lost-ack"), "C4", "wrong physical child-creation boundary");
            Apply(new("answer", old.Id, call, Flag: true)); _world.Auto = true; Takeover();
        }
        else Apply(new("flush", Actor.Id));
        Drain(child);
        Require(_world.Store.Instances[child.Id] is { Creation: 1, Status: InstanceStatus.Cancelled }, "C4", "cancel did not follow the original pending child start");
        var finished = _world.Store.Messages.Values.Single(m => m.Message.Target == _target && m.Message.Kind == WorkflowKinds.ChildFinished).Message;
        Require(finished.TargetCreation == 1 && finished.Read<ChildFinished>().Creation == 1, "C4", "child completion did not pin its original parent");
        Drain();

        Send(Kinds.Delete, 1, target: child); Drain(child);
        Require(_world.Store.Instances[child.Id].Retired, "C4", "first child was not retired normally");
        var replacement = NativeModel.Spec("creation-child", mode) with { Creation = new(2, 1, "child-replacement") };
        Send(Kinds.Start, 2, start: replacement, target: child); Drain(child);
        var newChild = _world.Store.Instances[child.Id];
        Apply(new("add", M: terminate)); Drain(child); // actual original carrier, replayed after its first copy was consumed
        Require(_world.Store.Instances[child.Id] == newChild && newChild.Status == InstanceStatus.Running,
            "C4", "old parent termination retargeted a replacement child");

        Send(Kinds.Delete, 1); Drain(); Require(Row.Retired, "C4", "original parent was not retired");
        Send(Kinds.Start, 2, start: Spec(2)); Drain();
        var newStart = _world.Store.Messages.Values.Single(m => m.Message.Start?.Parent == _target.Id).Message;
        Require(newStart.Target != child && newStart.Start?.ParentCreation == 2, "C4", "child ID reused a different parent creation's operation");
        Drain(newStart.Target);
        var parent = Row;
        Apply(new("add", M: finished)); Drain();
        Require(Row == parent && !State.Variables.ContainsKey("returned"), "C4", "old child completion advanced a replacement parent");
        Send(NativeKinds.Resume, 1, new ResumeStimulus("gate"), target: newStart.Target); Drain(newStart.Target);
        var newFinished = _world.Store.Messages.Values.Single(m => m.Message.Target == _target && m.Message.Kind == WorkflowKinds.ChildFinished).Message;
        Drain(); Require(State.Variables["returned"]!.GetValue<bool>(), "C4", "correct child's completion did not continue the parent");
        parent = Row; Apply(new("add", M: newFinished)); Drain();
        Require(Row == parent, "C4", "already consumed child completion re-entered the model");
        Console.WriteLine($"PASS creation:parent-race,{mode},cut={cut}");
    }
}
