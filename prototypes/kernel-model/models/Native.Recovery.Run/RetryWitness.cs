using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

internal static class RetryWitness
{
    private static string[] Cuts => ["steady", "reload-before", "reload-staged", "lost-before-apply", "lost-answer", "sibling-before"];
    private static string[] Refusals => ["legacy-v5", "replace-leaf", "replace-binding", "replace-capture", "replace-parent"];
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } from d in new[] { false, true } from c in Cuts select $"recovery:successor,{m},parallel-parent={d},cut={c}";
    internal static IEnumerable<string> RefusalNames => from m in new[] { Modes.Balanced, Modes.Durable } from d in new[] { false, true } from c in Refusals select $"recovery:{(c == "legacy-v5" ? "seeded-context" : "material-refusal")},{m},parallel-parent={d},case={c}";
    internal static void Run(bool full = false, bool refusals = false)
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable }) foreach (var direct in new[] { false, true }) foreach (var cut in refusals ? Refusals : full ? Cuts : ["steady"]) Run(mode, direct, cut);
    }
    private static void Run(string mode, bool direct, string cut)
    {
        RetryLeaf.Invocations.Clear();
        var world = new World(new Config(2, 1, Flaw.None, Native: true)) { Auto = true };
        var target = InstanceRef.In(0, "successor"); var owner = 0; Actor Actor() => world.Of(owner, 0);
        Workflow Build(bool replacement = false)
        {
            var leaf = new Node("leaf", replacement ? typeof(ReplacementRetryLeaf) : typeof(RetryLeaf));
            leaf.Inputs.Add("Value", Binding.Var("sourceValue")); leaf.Inputs.Add("Iteration", Binding.Var("CurrentItem")); leaf.Inputs.Add("Local", Binding.Var("local"));
            return Wf.Workflow("successor", Wf.Node<RetryScope>("outer").With(Wf.ForEach("iterations", Binding.Literal(new[] { 7 }), Wf.Parallel("parallel", null,
                direct ? leaf : Wf.Sequence("sequence", Wf.Set("before", "before", 1), leaf, Wf.Set("after", "after", 1)),
                Wf.Sequence("sibling", Wf.Event("change", "change"), Wf.Set("mutate", "sourceValue", 99), Wf.Event("finish", "finish")), Wf.Event("tail", "tail")))), ("sourceValue", 17));
        }
        var workflow = Build();
        workflow.InterventionRequested = true; NativeWorkflows.Registry.Add(workflow);
        void Apply(Act a) { world.Apply(a); if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error); }
        void Drain(bool cut = false)
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
                    if (cut && world.Store.Journal.Values.Any(j => j.Entries.Contains("recovery:operator-1:", StringComparison.Ordinal))) return;
                }
                if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
            }
            throw new InvalidOperationException("R0: successor witness exceeded bounded drain");
        }
        NativeState State() => Json.Read<NativeState>(world.Store.Instances[target.Id].State);
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        void Reload() { Apply(new("crash", owner)); Apply(new("tick", 20_000)); owner = 1 - owner; Apply(new("take", Actor().Id)); }
        Apply(new("take", Actor().Id)); Apply(new("add", M: new("start", target, Kinds.Start,
            Start: NativeModel.Spec(workflow.Id, mode) with { Creation = new(1, 0, "successor-proof") }))); Drain();
        var source = State().Frames.Values.Single(f => f.Node == "leaf");
        var originalParent = State().Frames[source.Parent!];
        var scopes = Json.Write(State().LexicalScopes);
        Check(source.Intervention is not null && RetryLeaf.Invocations.Count == 1, "R12: retry witness did not begin with an ordinary intervention");
        var wait = world.Store.Subscriptions.Single(s => s.Instance == target.Id && s.Key == "event:change");
        Apply(new("add", M: Events.Deliver(new("event:change", "change", null, world.Time + 120_000), wait))); Drain();
        Check(State().Variables["sourceValue"]!.ToString() == "99", "R15: pinned-input witness did not change its source value");
        if (cut == "reload-before") { Reload(); Drain(); }
        var request = new NativeRetry("operator-1", source.Id, source.Intervention!.Id, "dependency repaired");
        var serial = State().Next;
        if (cut == "legacy-v5")
        {
            // Deliberately seeded old-schema context, separately counted; not a historical writer/binary claim.
            var old = State(); old.Frames[source.Id].RecoveryMaterial = null;
            var upgraded = new NativeModel(new()).UpgradeState(Json.Write(old), 5);
            world.Store.Instances[target.Id] = world.Store.Instances[target.Id] with { State = Json.Write(upgraded) };
            Reload(); Drain();
        }
        if (cut.StartsWith("replace-", StringComparison.Ordinal))
        {
            var replacement = Build(cut == "replace-leaf");
            if (cut == "replace-binding") replacement.Nodes["leaf"].Inputs["Value"] = Binding.Literal(22);
            if (cut == "replace-capture") replacement.Nodes["leaf"].Captures.Add("new-capture", "");
            if (cut == "replace-parent") replacement.Nodes[direct ? "parallel" : "sequence"].Children.Reverse();
            NativeWorkflows.Registry.Add(replacement); Reload(); Drain();
        }
        if (cut == "sibling-before")
        {
            var sibling = world.Store.Subscriptions.Single(s => s.Instance == target.Id && s.Key == "event:finish");
            Apply(new("add", M: Events.Deliver(new("event:finish", "finish-early", null, world.Time + 120_000), sibling)));
        }
        Apply(new("add", M: new("retry", target, NativeRetry.Kind, Json.Write(request), ReplyTo: ReplyTo.Of("outside", "retry", world.Time + 120_000), TargetCreation: 1)));
        if (Refusals.Contains(cut))
        {
            Drain(); var refused = State();
            Check(world.Deliveries.Select(d => d.Reply.Item).OfType<NativeRetryResult>().LastOrDefault()?.Result == (cut == "legacy-v5" ? "MaterialIdentityUnavailable" : "MaterialUnavailable") &&
                RetryLeaf.Invocations.Count == 1 && refused.Retries is null && refused.Next == serial && refused.Frames[source.Id].Intervention == source.Intervention,
                "R18: unverifiable or replaced material was retried or refusal mutated its source obligation");
            Console.WriteLine($"PASS recovery:{(cut == "legacy-v5" ? "seeded-context" : "material-refusal")},{mode},parallel-parent={direct},case={cut}"); return;
        }
        NativeState checkpoint;
        if (cut.StartsWith("lost-", StringComparison.Ordinal))
        {
            var prepared = false;
            for (var i = 0; i < 100 && !prepared; i++)
            {
                var actor = Actor(); Apply(new("poll", actor.Id)); if (actor.Worker.NeedsLoad) Apply(new("load", actor.Id));
                if (actor.Worker.Ready.Contains(target.Id)) Apply(new("turn", actor.Id, S: target.Id));
                prepared = actor.Worker.Queued > 0 && actor.Worker.Messages.Any(m => m.Id == "retry" && m.Taken);
                if (!prepared && actor.Worker.NeedsFlush) Apply(new("flush", actor.Id));
                if (!prepared && actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
            }
            Check(prepared, "R16: uncertainty witness missed its actual retry turn");
            var row = world.Store.Instances[target.Id]; var replies = world.Deliveries.Count(d => d.Reply.Item is NativeRetryResult);
            world.Auto = false; var old = Actor(); var call = old.Calls.Count; Apply(new("flush", old.Id));
            Check(old.Calls.Count == call + 1 && old.Calls[call].Kind == "commit", "R16: uncertainty witness missed its actual retry commit");
            Apply(new(cut == "lost-answer" ? "exec" : "lose", old.Id, call));
            Check(cut == "lost-answer" ? State().Retries?.Count == 1 : world.Store.Instances[target.Id] == row, "R16: retry cut crossed the wrong physical application boundary");
            if (cut == "lost-answer") Check(State().Agenda.Any(w => w.Node == "leaf" && w.Frame != source.Id) && !State().Frames.ContainsKey(source.Id),
                "R13: retry did not durably stage a fresh successor before executing it");
            Apply(new("answer", old.Id, call, Flag: true));
            Check(world.Deliveries.Count(d => d.Reply.Item is NativeRetryResult) == replies && RetryLeaf.Invocations.Count == 1, "R16: unknown retry acknowledgement released a receipt or ran a successor");
            checkpoint = State();
            world.Auto = true; Reload(); Drain();
        }
        else { Drain(cut: true); checkpoint = State(); }
        if (checkpoint.Retries?.ContainsKey(request.Id) == true && direct)
        {
            var successor = checkpoint.Agenda.Single(w => w.Node == "leaf");
            var prior = Values.To<ParallelState>(originalParent.State)!; var rebound = Values.To<ParallelState>(checkpoint.Frames[source.Parent!].State)!;
            Check(rebound.Pending.SequenceEqual(prior.Pending.Select(id => id == source.Id ? successor.Frame : id)) && rebound.Done == prior.Done && rebound.Faulted == prior.Faulted && rebound.Joined == prior.Joined,
                "R20: committed retry did not replace the exact outstanding parallel child obligation");
        }
        Check(cut.StartsWith("lost-", StringComparison.Ordinal) || checkpoint.Agenda.Any(w => w.Node == "leaf" && w.Frame != source.Id && w.Parent == source.Parent) && !checkpoint.Frames.ContainsKey(source.Id) && RetryLeaf.Invocations.Count == 1,
            "R13: retry did not durably stage a fresh successor before executing it; " + world.Deliveries.Select(d => d.Reply.Item).OfType<NativeRetryResult>().LastOrDefault()?.Result);
        if (cut is "reload-staged" or "sibling-before") Reload();
        Drain(); var next = State();
        var completions = world.Store.Journal.Values.Where(j => j.Instance == target.Id).SelectMany(j => Json.Read<string[]>(j.Entries)).Where(e => e.StartsWith("c:", StringComparison.Ordinal)).ToArray();
        Check(completions.Count(e => e.EndsWith(":before", StringComparison.Ordinal)) == (direct ? 0 : 1) && completions.Count(e => e.EndsWith(":after", StringComparison.Ordinal)) == (direct ? 0 : 1),
            "R21: recovery replayed previously completed ordinary work");
        if (cut == "sibling-before")
        {
            var siblingAt = Array.FindIndex(completions, e => e.EndsWith(":sibling", StringComparison.Ordinal));
            Check(siblingAt >= 0 && siblingAt < Array.FindIndex(completions, e => e.EndsWith(":leaf", StringComparison.Ordinal)),
                "R20: sibling callback did not cross the staged retry and reload boundary");
        }
        Check(RetryLeaf.Invocations.Count == 2 && RetryLeaf.Invocations[1].Frame != source.Id && RetryLeaf.Invocations[1].Operation == source.OperationKey && RetryLeaf.Invocations[1].Value == 17 &&
            RetryLeaf.Invocations[1].Iteration == 7 && RetryLeaf.Invocations[1].Local == 23 && Json.Write(next.LexicalScopes) == scopes &&
            (direct || next.Variables["before"]!.ToString() == "1" && next.Variables["after"]!.ToString() == "1") && world.Store.Instances[target.Id].Status == InstanceStatus.Running,
            "R14: successor lost pinned identity/input or ordinary parent completion routing");
        var accepted = next.Retries![request.Id]; var count = RetryLeaf.Invocations.Count;
        foreach (var retry in new[] { request, request with { Reason = "different content" }, request with { Id = "stale-source" } })
        { Apply(new("add", M: new("delivery-" + retry.Id + retry.Reason, target, NativeRetry.Kind, Json.Write(retry), ReplyTo: ReplyTo.Of("outside", retry.Id + retry.Reason, world.Time + 120_000), TargetCreation: 1))); Drain(); }
        Check(RetryLeaf.Invocations.Count == count && State().Retries!.Count == 1 && State().Retries![request.Id].Successor == accepted.Successor &&
            world.Deliveries.Select(d => d.Reply.Item).OfType<NativeRetryResult>().TakeLast(3).Select(r => r.Result).SequenceEqual(new[] { "Duplicate", "Conflict", "NotEligible" }),
            "R17: replay, conflict or stale source created another successor or changed its receipt");
        if (world.Store.Subscriptions.SingleOrDefault(s => s.Instance == target.Id && s.Key == "event:finish") is { } finish)
        { Apply(new("add", M: Events.Deliver(new("event:finish", "finish", null, world.Time + 120_000), finish))); Drain(); }
        var tail = world.Store.Subscriptions.Single(s => s.Instance == target.Id && s.Key == "event:tail");
        Apply(new("add", M: Events.Deliver(new("event:tail", "tail", null, world.Time + 120_000), tail))); Drain();
        Check(world.Store.Instances[target.Id].Status == InstanceStatus.Finished && State().LexicalScopes is null && State().Frames.Count == 0 && RetryLeaf.Invocations.Count == 2,
            "R19: successful recovery repeated its iteration or leaked completed ancestor resources");
        Console.WriteLine($"PASS recovery:successor,{mode},parallel-parent={direct},cut={cut}");
    }
}
internal sealed class RetryScope : CompositeActivity
{
    public RetryScope() { }
    protected internal override void Execute(CompositeContext context) => context.ScheduleInScope(context.Children.Single(), context.CreateLexicalScope(
        new Dictionary<string, System.Text.Json.Nodes.JsonNode?> { ["local"] = Values.From(23) }));
    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.Complete();
    protected internal override void OnChildFaulted(CompositeContext context, ChildFaulted child) => context.Complete();
}
internal sealed class ReplacementRetryLeaf : RetryLeaf
{
    public ReplacementRetryLeaf() { }
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
    { Invocations.Add((context.FrameId, context.OperationKey, 999, Iteration, Local)); return ValueTask.FromResult(Complete(999)); }
}
internal class RetryLeaf : Kernel.Native.Activity<int>
{
    public RetryLeaf() { }
    [Input] public int Value { get; set; }
    [Input] public int Iteration { get; set; }
    [Input] public int Local { get; set; }
    internal static readonly List<(string Frame, string Operation, int Value, int Iteration, int Local)> Invocations = [];
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        Invocations.Add((context.FrameId, context.OperationKey, Value, Iteration, Local));
        return ValueTask.FromResult(Invocations.Count == 1 ? Fault("dependency", "repair required") : Complete(Value));
    }
}
