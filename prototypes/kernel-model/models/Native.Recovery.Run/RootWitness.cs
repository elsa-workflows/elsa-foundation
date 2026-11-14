using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

internal static class RootWitness
{
    private static string[] Cuts => ["steady", "reload-fault", "reload-staged", "lost-before-apply", "lost-answer", "cancel-fault", "terminate-staged"];
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from r in new[] { true, false } from e in new[] { false, true } from c in Cuts
        select $"recovery:root-iteration,{m},root={r},external={e},cut={c}";
    internal static void Run(bool? onlyRoot = null, bool full = false)
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var root in new[] { true, false }.Where(r => onlyRoot is null || r == onlyRoot))
        foreach (var external in new[] { false, true }) foreach (var cut in full ? Cuts : ["steady"]) Run(mode, root, external, cut);
    }
    private static void Run(string mode, bool root, bool external, string cut)
    {
        RootLeaf.Calls.Clear();
        var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
        var target = InstanceRef.In(0, "root-next"); var owner = 0; Actor Actor() => world.Of(owner, 0);
        var leaf = new Node("leaf", external ? typeof(ExternalRootLeaf) : typeof(RootLeaf));
        leaf.Inputs.Add("Value", root ? Binding.Literal(7) : Binding.Var("CurrentItem")); leaf.Inputs.Add("Index", root ? Binding.Literal(0) : Binding.Var("CurrentIndex"));
        var workflow = Wf.Workflow("root-next", root ? leaf : Wf.Node<AbsorbRecoveryFault>("outer").With(Wf.ForEach("loop", Binding.Literal(new[] { 7, 9 }), leaf)));
        workflow.InterventionRequested = true; NativeWorkflows.Registry.Add(workflow);
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        void Apply(Act action)
        {
            world.Apply(action);
            // The independent expected-root checkpoint assertion must precede the general fault diagnostic.
            if (root && world.Store.Instances.TryGetValue(target.Id, out var committed) && committed.Status == InstanceStatus.Faulted)
                throw new InvalidOperationException("R22: opted-in root fault ended the run instead of retaining intervention");
            if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
        }
        void Drain(string? stopControl = null)
        {
            for (var i = 0; i < 100; i++)
            {
                var actor = Actor();
                Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
                if (actor.Worker.Ready.Contains(target.Id)) Apply(new("turn", actor.Id, S: target.Id));
                else if (!actor.Worker.NeedsFlush && !actor.Worker.NeedsMeet) return;
                if (actor.Worker.NeedsFlush)
                {
                    Apply(new("flush", actor.Id));
                    if (stopControl is not null && world.Store.Journal.Values.Any(j => j.Entries.Contains("recovery:" + stopControl + ":", StringComparison.Ordinal))) return;
                }
                if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
            }
            throw new InvalidOperationException("R0: root/iteration witness exceeded bounded drain");
        }
        NativeState State() => Json.Read<NativeState>(world.Store.Instances[target.Id].State);
        void Reload() { Apply(new("crash", owner)); Apply(new("tick", 20_000)); owner = 1 - owner; Apply(new("take", Actor().Id)); }
        void Send(string id, NativeRetry request) => Apply(new("add", M: new(id, target, NativeRetry.Kind, Json.Write(request), ReplyTo: ReplyTo.Of("outside", id, world.Time + 120_000), TargetCreation: 1)));
        void Stage(NativeRetry request, int calls, bool uncertain = false)
        {
            var id = "delivery-" + request.Id; Send(id, request);
            if (!uncertain) Drain(request.Id);
            else
            {
                var prepared = false;
                for (var i = 0; i < 100 && !prepared; i++)
                {
                    var actor = Actor(); Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
                    if (actor.Worker.Ready.Contains(target.Id)) Apply(new("turn", actor.Id, S: target.Id));
                    prepared = actor.Worker.Queued > 0 && actor.Worker.Messages.Any(m => m.Id == id && m.Taken);
                    if (!prepared && actor.Worker.NeedsFlush) Apply(new("flush", actor.Id));
                    if (!prepared && actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
                }
                Check(prepared, "R27: root/iteration uncertainty missed its actual retry turn");
                var row = world.Store.Instances[target.Id]; var replies = world.Deliveries.Count(d => d.Reply.Item is NativeRetryResult);
                world.Auto = false; var old = Actor(); var call = old.Calls.Count; Apply(new("flush", old.Id));
                Check(old.Calls.Count == call + 1 && old.Calls[call].Kind == "commit", "R27: root/iteration uncertainty missed its actual commit");
                Apply(new(cut == "lost-answer" ? "exec" : "lose", old.Id, call));
                Check(cut == "lost-answer" ? State().Retries?.Count == 1 : world.Store.Instances[target.Id] == row, "R27: root/iteration uncertainty crossed the wrong physical boundary");
                Apply(new("answer", old.Id, call, Flag: true));
                Check(RootLeaf.Calls.Count == calls && world.Deliveries.Count(d => d.Reply.Item is NativeRetryResult) == replies, "R27: unknown root/iteration acknowledgement released a receipt or executed the successor");
                world.Auto = true; Reload(); Drain(); return;
            }
            var current = State(); var work = current.Agenda.SingleOrDefault(w => w.Node == "leaf");
            var source = current.Retries![request.Id].Source;
            Check(work is not null && work.Frame != source.Id && work.Parent == source.Parent && work.Attempt == 0 && RootLeaf.Calls.Count == calls,
                "R24: root/iteration retry did not commit its exact fresh obligation before execution");
            if (!root) Check(Values.To<ForEachState>(current.Frames[source.Parent!].State)!.Index == 0 && work!.Scope is { } scheduling && scheduling.GetValueOrDefault("CurrentItem")?.ToString() == "7" && scheduling.GetValueOrDefault("CurrentIndex")?.ToString() == "0",
                "R25: retry advanced or changed the failed iteration before success");
        }
        void Cancel(bool staged)
        {
            // The already ordered wake precedes this control. Durable external work first commits its new attempt;
            // other leaves may fault once more before termination. Neither path may execute again after it ends.
            var permitted = staged && !(external && mode == Modes.Durable) ? 2 : 1;
            Apply(new("add", M: new("cancel", target, staged ? Kinds.Terminate : NativeKinds.Cancel, TargetCreation: 1))); Drain();
            Check(world.Store.Instances[target.Id].Status == InstanceStatus.Cancelled && RootLeaf.Calls.Count == permitted,
                "R31: root/iteration cancellation executed or reopened failed work");
            var history = Json.Write(State().Retries); NativeWorkflows.Registry.Remove(workflow.Id);
            Apply(new("add", M: new("query", target, ReceiptQuery.Kind, Json.Write(new ReceiptQuery("unknown", "probe")), ReplyTo: ReplyTo.Of("outside", "query", world.Time + 120_000), TargetCreation: 1))); Drain();
            Apply(new("add", M: new("stale", target, NativeKinds.Continue, TargetCreation: 1))); Drain();
            Check(world.Store.Instances[target.Id].Status == InstanceStatus.Cancelled && RootLeaf.Calls.Count == permitted && Json.Write(State().Retries) == history &&
                world.Deliveries.Select(d => d.Reply.Item).OfType<Admission>().LastOrDefault()?.Result == "UnknownInvocation",
                "R31: cancelled root/iteration metadata hydrated authored material, changed history or resumed work");
        }
        Apply(new("take", Actor().Id)); Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec(workflow.Id, mode) with { Creation = new(1, 0, "root-proof") }))); Drain();
        var state = State(); var source = state.Frames.Values.SingleOrDefault(f => f.Node == "leaf");
        Check(world.Store.Instances[target.Id].Status == InstanceStatus.Running && source?.Intervention is not null && RootLeaf.Calls.Count == 1,
            root ? "R22: opted-in root fault ended the run instead of retaining intervention" : "R23: direct ForEach fault lost its continuing iteration");
        if (cut == "cancel-fault") { Cancel(false); Console.WriteLine($"PASS recovery:root-iteration,{mode},root={root},external={external},cut={cut}"); return; }
        if (cut == "reload-fault") { workflow.InterventionRequested = false; Reload(); Drain(); }
        var first = new NativeRetry("root-retry-1", source!.Id, source.Intervention!.Id, "first repair");
        Stage(first, 1, cut.StartsWith("lost-", StringComparison.Ordinal));
        if (cut == "terminate-staged") { Cancel(true); Console.WriteLine($"PASS recovery:root-iteration,{mode},root={root},external={external},cut={cut}"); return; }
        if (cut == "reload-staged") Reload();
        Drain();
        var again = State().Frames.Values.Single(f => f.Node == "leaf");
        Check(RootLeaf.Calls.Count == 2 && again.Intervention is not null && again.Id != source.Id && State().InterventionPolicy == 2,
            "R28: repeated root/iteration fault lost its fresh occurrence, intervention or frozen policy");
        foreach (var retry in new[] { first, first with { Reason = "conflict" }, first with { Id = "stale-source" } })
        { Send("replay-" + retry.Id + retry.Reason, retry); Drain(); }
        Check(world.Deliveries.Select(d => d.Reply.Item).OfType<NativeRetryResult>().TakeLast(3).Select(r => r.Result).SequenceEqual(new[] { "Duplicate", "Conflict", "NotEligible" }) && RootLeaf.Calls.Count == 2,
            "R28: root/iteration replay changed its old receipt or re-executed failed work");
        Stage(new("root-retry-2", again.Id, again.Intervention!.Id, "second repair"), 2); Drain();
        Check(world.Store.Instances[target.Id].Status == InstanceStatus.Finished && RootLeaf.Calls.Count == (root ? 3 : 4) && RootLeaf.Calls.Take(3).Select(c => c.Frame).Distinct().Count() == 3 && RootLeaf.Calls.Take(3).Select(c => c.Operation).Distinct().Count() == 1 &&
            RootLeaf.Calls.Select(c => c.Value).SequenceEqual(root ? new[] { 7, 7, 7 } : new[] { 7, 7, 7, 9 }) && RootLeaf.Calls.Select(c => c.Index).SequenceEqual(root ? new[] { 0, 0, 0 } : new[] { 0, 0, 0, 1 }) &&
            RootLeaf.Calls.All(c => c.Attempt == (external && mode == Modes.Durable ? 1 : 0)) && (root || RootLeaf.Calls[3].Operation != RootLeaf.Calls[2].Operation), "R26: recovery repeated, skipped or misrouted a root/iteration obligation");
        Console.WriteLine($"PASS recovery:root-iteration,{mode},root={root},external={external},cut={cut}");
    }
}
internal class RootLeaf : Kernel.Native.Activity<int>
{
    public RootLeaf() { }
    [Input] public int Value { get; set; }
    [Input] public int Index { get; set; }
    internal static readonly List<(string Frame, string Operation, int Value, int Index, int Attempt)> Calls = [];
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        Calls.Add((context.FrameId, context.OperationKey, Value, Index, context.Attempt));
        return ValueTask.FromResult(Calls.Count <= 2 ? Fault("dependency", "repair required") : Complete(Value));
    }
}
[External]
internal sealed class ExternalRootLeaf : RootLeaf { public ExternalRootLeaf() { } }
