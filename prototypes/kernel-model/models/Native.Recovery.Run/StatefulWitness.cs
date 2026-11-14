using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

internal static class StatefulWitness
{
    private static string[] Parents => ["root", "sequence", "parallel", "foreach"];
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from p in Parents from f in new[] { false, true }
        select $"recovery:stateful,{m},parent={p},startFault={f}";
    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var parent in Parents) foreach (var startFault in new[] { false, true }) Run(mode, parent, startFault);
    }
    private static void Run(string mode, string parent, bool startFault)
    {
        StatefulProbe.Calls.Clear(); StatefulProbe.StartFault = startFault;
        var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true }; var target = InstanceRef.In(0, "stateful-proof"); var owner = 0;
        Actor Actor() => world.Of(owner, 0);
        var leaf = Wf.Node<StatefulProbe>("leaf", ("Value", Binding.Var(parent == "foreach" ? "CurrentItem" : "value")), ("Data", Binding.Literal(new JsonObject { ["kept"] = 9 })));
        var body = parent == "foreach" ? Wf.ForEach("parent", Binding.Literal(new[] { 17, 19 }), leaf) : parent == "parallel"
            ? Wf.Parallel("parent", null, leaf, Wf.Sequence("sibling", Wf.Event("change", "change"), Wf.Set("changed", "value", -1)))
            : Wf.Sequence("parent", leaf, Wf.Set("after", "after", 1));
        var wf = Wf.Workflow("stateful-proof", parent == "root" ? leaf : Wf.Node<AbsorbRecoveryFault>("outer").With(body), ("value", 17));
        wf.InterventionRequested = true; NativeWorkflows.Registry.Add(wf);
        NativeState State() => Json.Read<NativeState>(world.Store.Instances[target.Id].State);
        void Check(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }
        void Apply(Act a)
        {
            world.Apply(a);
            if (world.Store.Instances.TryGetValue(target.Id, out var row) && row.Status == InstanceStatus.Faulted)
                throw new InvalidOperationException("R32: stateful reported fault ended instead of retaining its invocation");
            if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
        }
        void Drain(string? staged = null)
        {
            for (var i = 0; i < 100; i++)
            {
                var actor = Actor(); Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
                if (actor.Worker.Ready.Contains(target.Id)) Apply(new("turn", actor.Id, S: target.Id));
                else if (!actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) return;
                if (actor.Worker.NeedsFlush)
                {
                    Apply(new("flush", actor.Id));
                    if (staged is not null && world.Store.Journal.Values.Any(j => j.Entries.Contains("recovery:" + staged + ":", StringComparison.Ordinal))) return;
                }
                if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
            }
            throw new InvalidOperationException("R0: stateful witness exceeded bounded drain");
        }
        void Reload() { Apply(new("crash", owner)); Apply(new("tick", 20_000)); owner = 1 - owner; Apply(new("take", Actor().Id)); Drain(); }
        void Name(string id, string name, string payload) => Apply(new("add", M: new(id, target, NativeKinds.Resume, Json.Write(new ResumeStimulus(name, Values.From(payload))), TargetCreation: 1)));
        Apply(new("take", Actor().Id)); Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec(wf.Id, mode) with { Creation = new(1, 0, "stateful-proof") }))); Drain();
        SubscriptionRow? oldWait = null;
        if (!startFault)
        {
            oldWait = world.Store.Subscriptions.Single(s => s.Instance == target.Id && s.Key == "event:go"); Reload();
            if (parent == "parallel") { Name("change", "change", "change"); Drain(); }
            // Two actual competing wait-addressed deliveries queue two resumes before either is processed.
            Apply(new("add", M: Events.Deliver(new("event:go", "old-1", Values.From("old-payload").ToJsonString(), world.Time + 120_000), oldWait)));
            Apply(new("add", M: Events.Deliver(new("event:go", "old-2", Values.From("duplicate-payload").ToJsonString(), world.Time + 120_000), oldWait))); Drain();
        }
        var source = State().Frames.Values.SingleOrDefault(f => f.Node == "leaf");
        Check(source?.Intervention is not null && StatefulProbe.Calls.Count == (startFault ? 1 : 2), "R32: stateful reported fault lost its continuing invocation or replayed a resume");
        Check(source!.RecoveryInputs?["Value"]?.ToString() == "17" && source.RecoveryInputs["Data"]?["kept"]?.ToString() == "9" && !State().Waits.Values.Any(w => w.Frame == source.Id) && !State().Agenda.Any(w => w.Frame == source.Id),
            "R33: stateful fault lost detached pins or retained runnable consumed waits");
        if (!startFault) Check(source.State?["Marker"]?.ToString() == "first" && StatefulProbe.Calls[1].Value == 17 && StatefulProbe.Calls[1].Kept == 9,
            "R34: Resume reevaluated changed bindings or lost its retained private state");
        var archive = Json.Write(source); Reload();
        var retry = new NativeRetry("stateful-retry", source.Id, source.Intervention!.Id, "repair dependency");
        Apply(new("add", M: new("retry", target, NativeRetry.Kind, Json.Write(retry), TargetCreation: 1))); Drain(retry.Id);
        var work = State().Agenda.Single(w => !w.Resume && w.Node == "leaf");
        Check(work.Frame != source.Id && work.Attempt == 0 && work.Payload is null && work.Wait is null && work.OperationKey == source.OperationKey && StatefulProbe.Calls.Count == (startFault ? 1 : 2),
            "R35: stateful retry did not stage a fresh non-resume invocation");
        Reload();
        var fresh = State().Frames.Values.Single(f => f.Node == "leaf");
        Check(fresh.Id == work.Frame && fresh.State?["Marker"]?.ToString() == "fresh" && Json.Write(State().Retries![retry.Id].Source) == archive && StatefulProbe.Calls.Last().Phase == "start" && StatefulProbe.Calls.Last().Value == 17,
            "R36: successor replayed private state/payload, changed pins or mutated forensic source");
        var currentWait = world.Store.Subscriptions.Single(s => s.Instance == target.Id && s.Key == "event:go");
        if (oldWait is not null)
        {
            Check(oldWait.Wait != currentWait.Wait, "R37: successor reused an old wait identity"); var count = StatefulProbe.Calls.Count;
            Apply(new("add", M: Events.Deliver(new("event:go", "late-old", Values.From("late-old").ToJsonString(), world.Time + 120_000), oldWait))); Drain();
            Check(StatefulProbe.Calls.Count == count && State().Frames.ContainsKey(fresh.Id), "R37: old occurrence-addressed delivery resumed the successor");
        }
        // Name lookup has no occurrence address. A late name-only stimulus intentionally resolves the NEW wait.
        Name("late-name", "go", "newly-accepted-name"); Drain();
        if (parent == "foreach") { Name("next-item", "go", "next-item"); Drain(); }
        if (parent == "parallel" && startFault) { Name("sibling", "change", "change"); Drain(); }
        Check(world.Store.Instances[target.Id].Status == InstanceStatus.Finished && StatefulProbe.Calls.Last().Payload == (parent == "foreach" ? "next-item" : "newly-accepted-name") &&
            StatefulProbe.Calls.Take(startFault ? 3 : 4).Select(c => c.Operation).Distinct().Count() == 1 && StatefulProbe.Calls.All(c => c.Kept == 9),
            "R38: fresh name-based delivery failed, operation changed or mutable JSON escaped its snapshot");
        Console.WriteLine($"PASS recovery:stateful,{mode},parent={parent},startFault={startFault}");
    }
}
internal sealed record ProbePrivate(string Marker);
internal sealed class StatefulProbe : StatefulActivity<int, ProbePrivate, string>
{
    public StatefulProbe() { }
    [Input] public int Value { get; set; }
    [Input] public JsonObject Data { get; set; } = new();
    internal static bool StartFault;
    internal static readonly List<(string Phase, string Frame, string Operation, int Value, int Kept, string? Payload)> Calls = [];
    protected override ValueTask<Transition> ExecuteAsync(StartContext<string> context)
    {
        Calls.Add(("start", context.FrameId, context.OperationKey, Value, Data["kept"]!.GetValue<int>(), null)); Data["kept"] = -1;
        return ValueTask.FromResult(Calls.Count == 1 && StartFault ? Fault("stateful-dependency", "repair") : Suspend(new ProbePrivate(Calls.Count == 1 ? "first" : "fresh"), new EventTrigger("go"), new EventTrigger("other")));
    }
    protected override ValueTask<Transition> ResumeAsync(ResumeContext<ProbePrivate, string> context)
    {
        Calls.Add(("resume", context.FrameId, context.OperationKey, Value, Data["kept"]!.GetValue<int>(), context.Trigger)); Data["kept"] = -2;
        return ValueTask.FromResult(Calls.Count == 2 && !StartFault ? Fault("stateful-dependency", "repair") : Complete(Value));
    }
}
