using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

// Real source fault and control processing, with explicitly seeded malformed parent/scope contexts.
internal static class RecoveryRefusals
{
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from c in new[] { "unknown-state", "legacy-scope", "wrong-handle" }
        select $"chart-recovery-seeded-parent-refusal:{m},case={c}";
    internal static void Run()
    {
        foreach (var m in new[] { Modes.Balanced, Modes.Durable }) foreach (var c in new[] { "unknown-state", "legacy-scope", "wrong-handle" }) Run(m, c);
    }
    private static void Run(string mode, string kind)
    {
        var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true }; var target = InstanceRef.In(0, "chart-refusal"); var owner = 0; var refusing = false;
        Actor Actor() => world.Of(owner, 0); NativeState State() => Json.Read<NativeState>(world.Store.Instances[target.Id].State);
        var def = new StructuralDefinition([new("root", NodeKind.Root, Initial: "A"), new("A", NodeKind.Atomic, "root", Exit: "fail"), new("B", NodeKind.Final, "root")], [new("go", 0, "A", "B", VisitMode.Reenter, "go")]);
        var machine = Wf.Node<StateMachine>("machine", ("StructuralDefinition", Binding.Literal(def))).With(Wf.Node<RefusalDependency>("fail"));
        var wf = Wf.Workflow("chart-refusal", machine); wf.InterventionRequested = true; NativeWorkflows.Registry.Add(wf);
        void Apply(Act a)
        {
            world.Apply(a);
            if (refusing && world.Store.Instances[target.Id].Status != InstanceStatus.Running)
                throw new InvalidOperationException("R48: invalid parent retry parked instead of refusing without mutation");
            if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
        }
        void Drain()
        {
            for (var i = 0; i < 100; i++)
            {
                var a = Actor(); Apply(new("poll", a.Id)); if (a.Worker.NeedsLoad) Apply(new("load", a.Id));
                if (a.Worker.Ready.Contains(target.Id)) Apply(new("turn", a.Id, S: target.Id)); else if (!a.Worker.NeedsFlush && !a.Worker.NeedsMeet) return;
                if (a.Worker.NeedsFlush) Apply(new("flush", a.Id)); if (a.Worker.NeedsMeet) Apply(new("meet", a.Id));
            }
            throw new InvalidOperationException("R0: parent refusal exceeded bounded drain");
        }
        Apply(new("take", Actor().Id)); Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec(wf.Id, mode) with { Creation = new(1, 0, "chart-refusal") }))); Drain();
        var parent = State().Frames.Values.Single(f => f.Node == "machine");
        Apply(new("add", M: new("go", target, CompositeSignal.Kind, Json.Write(new CompositeSignal(parent.Id, "go")), TargetCreation: 1))); Drain();
        var seed = State(); var source = seed.Frames.Values.Single(f => f.Node == "fail");
        if (source.Intervention is null) throw new InvalidOperationException("R48: refusal fixture did not reach an actual phase fault");
        var pending = Json.Read<MachineState>(Json.Write(seed.Frames[parent.Id].State));
        if (kind == "unknown-state") pending = pending with { Active = ["unknown"], Visits = pending.Visits!.Select(v => v with { State = "unknown" }).ToArray() };
        if (kind == "legacy-scope") pending = pending with { Scopes = null, LegacyScopes = true };
        if (kind == "wrong-handle") source.LexicalScope = pending.Scopes!.Machine; // valid accessible handle, wrong visit affiliation
        seed.Frames[parent.Id].State = Values.From(pending);
        world.Store.Instances[target.Id] = world.Store.Instances[target.Id] with { State = Json.Write(seed) };
        Apply(new("crash", owner)); Apply(new("tick", 20_000)); owner = 1; Apply(new("take", Actor().Id)); Drain();
        var before = Json.Write(State()); refusing = true;
        var retry = new NativeRetry("invalid-parent", source.Id, source.Intervention.Id, "probe refusal");
        Apply(new("add", M: new("retry", target, NativeRetry.Kind, Json.Write(retry), ReplyTo: ReplyTo.Of("outside", "retry", world.Time + 120_000), TargetCreation: 1))); Drain();
        if (Json.Write(State()) != before || world.Deliveries.Select(d => d.Reply.Item).OfType<NativeRetryResult>().LastOrDefault()?.Result != "ParentObligationUnavailable")
            throw new InvalidOperationException("R49: invalid parent retry changed source/parent/identity/resources or lost its explicit refusal");
        Console.WriteLine($"PASS chart-recovery-seeded-parent-refusal:{mode},case={kind}");
    }
}
internal sealed class RefusalDependency : Kernel.Native.Activity
{
    public RefusalDependency() { }
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext c) => ValueTask.FromResult(Fault("dependency", "refusal proof"));
}
