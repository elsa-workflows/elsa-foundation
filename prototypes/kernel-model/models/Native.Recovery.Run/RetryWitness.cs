using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

internal static class RetryWitness
{
    internal static void Run()
    {
        RetryLeaf.Invocations.Clear();
        var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
        var target = InstanceRef.In(0, "successor"); var actor = world.Of(0, 0);
        var workflow = Wf.Workflow("successor", Wf.Node<AbsorbRecoveryFault>("outer").With(Wf.Parallel("parallel", null,
            Wf.Sequence("sequence", Wf.Set("before", "before", 1), Wf.Node<RetryLeaf>("leaf", ("Value", Binding.Literal(17))), Wf.Set("after", "after", 1)),
            Wf.Event("sibling", "finish"))));
        workflow.InterventionRequested = true; NativeWorkflows.Registry.Add(workflow);
        void Apply(Act a) { world.Apply(a); if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error); }
        void Drain(bool cut = false)
        {
            for (var i = 0; i < 100; i++)
            {
                Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
                if (actor.Worker.Ready.Contains(target.Id)) Apply(new("turn", actor.Id, S: target.Id));
                else if (!actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) return;
                if (actor.Worker.NeedsFlush) { Apply(new("flush", actor.Id)); if (cut) return; }
                if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
            }
            throw new InvalidOperationException("R0: successor witness exceeded bounded drain");
        }
        NativeState State() => Json.Read<NativeState>(world.Store.Instances[target.Id].State);
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        Apply(new("take", actor.Id)); Apply(new("add", M: new("start", target, Kinds.Start,
            Start: NativeModel.Spec(workflow.Id) with { Creation = new(1, 0, "successor-proof") }))); Drain();
        var source = State().Frames.Values.Single(f => f.Node == "leaf");
        Check(source.Intervention is not null && RetryLeaf.Invocations.Count == 1, "R12: retry witness did not begin with an ordinary intervention");
        Apply(new("add", M: new("retry", target, NativeRetry.Kind, Json.Write(new NativeRetry("operator-1", source.Id, source.Intervention!.Id, "dependency repaired")), TargetCreation: 1))); Drain(cut: true);
        var checkpoint = State();
        Check(checkpoint.Agenda.Any(w => w.Node == "leaf" && w.Frame != source.Id && w.Parent == source.Parent) && !checkpoint.Frames.ContainsKey(source.Id) && RetryLeaf.Invocations.Count == 1,
            "R13: retry did not durably stage a fresh successor before executing it");
        Drain(); var next = State();
        Check(RetryLeaf.Invocations.Count == 2 && RetryLeaf.Invocations[1].Frame != source.Id && RetryLeaf.Invocations[1].Operation == source.OperationKey && RetryLeaf.Invocations[1].Value == 17 &&
            next.Variables["before"]!.ToString() == "1" && next.Variables["after"]!.ToString() == "1" && world.Store.Instances[target.Id].Status == InstanceStatus.Running,
            "R14: successor lost pinned identity/input or ordinary parent completion routing");
        Console.WriteLine("PASS recovery:fresh-successor-minimal");
    }
}
internal sealed class RetryLeaf : Kernel.Native.Activity<int>
{
    public RetryLeaf() { }
    [Input] public int Value { get; set; }
    internal static readonly List<(string Frame, string Operation, int Value)> Invocations = [];
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        Invocations.Add((context.Frame, context.OperationKey, Value));
        return ValueTask.FromResult(Invocations.Count == 1 ? Fault("dependency", "repair required") : Complete(Value));
    }
}
