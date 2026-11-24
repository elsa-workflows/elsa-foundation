using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Sim.Native;

/// <summary>
/// Round 5 C: a retried activity is a new activation, and so is its second retry. The leaf faults twice and completes the
/// third time; the operator retries it twice (the root witness's flow). The three activations must carry three keys with
/// the paths <c>-</c>, <c>leaf=retry1</c>, <c>leaf=retry2</c>, the generation walked along the committed retry lineage
/// (receipt by receipt, successor to source). With the FlatRetryGeneration mutation the second retry repeats the first's
/// path and key, which this witness shows as the mutation biting. The simulation's N7 runs underneath (the world is the
/// simulation's).
/// </summary>
internal static class EffectKeyRetryWitness
{
    internal static IEnumerable<string> Names => from m in new[] { Modes.Balanced, Modes.Durable } select $"effect-key:double-retry,{m}";

    internal static void Run()
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        {
            var sound = Run(mode, NativeFlaw.None);
            Check(sound.Count == 3 && sound.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() == 3, $"EK5 {mode}: three activations, three keys ({string.Join(", ", sound.Select(c => c.Key))})");
            Check(sound.Select(c => c.Path).SequenceEqual(["-", "leaf=retry1", "leaf=retry2"]), $"EK6 {mode}: the retry generations are 0, 1, 2 ({string.Join(", ", sound.Select(c => c.Path))})");
            var flat = Run(mode, NativeFlaw.FlatRetryGeneration);
            Check(flat.Count == 3 && flat[1].Key == flat[2].Key && flat.Select(c => c.Path).SequenceEqual(["-", "leaf=retry1", "leaf=retry1"]),
                $"EK7 {mode}: FlatRetryGeneration makes the second retry share the first's key ({string.Join(", ", flat.Select(c => c.Key))})");
            Console.WriteLine($"PASS effect-key:double-retry,{mode}: {string.Join(", ", sound.Select(c => c.Key))}; FlatRetryGeneration collides on {flat[1].Key}");
        }
    }

    private static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }

    private static List<(string Frame, string Path, string Key)> Run(string mode, NativeFlaw flaws)
    {
        EffectKeyLeaf.Calls.Clear();
        var world = new World(new Config(2, 1, Flaw.None, Native: true, NativeFlaws: flaws)) { Auto = true };
        var target = InstanceRef.In(0, "retry-keys"); var owner = 0; Actor Actor() => world.Of(owner, 0);
        var leaf = new Node("leaf", typeof(EffectKeyLeaf));
        var workflow = Wf.Workflow("retry-keys", leaf);
        workflow.InterventionRequested = true; NativeWorkflows.Registry.Add(workflow);
        try
        {
            void Apply(Act action)
            {
                world.Apply(action);
                if (world.Check() is { } error) throw new InvalidOperationException("Kernel: " + error);
            }
            void Drain(string? staged = null)
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
                        if (staged is not null && world.Store.Journal.Values.Any(j => j.Entries.Contains("recovery:" + staged + ":", StringComparison.Ordinal))) return;
                    }
                    if (actor.Worker.NeedsMeet) Apply(new("meet", actor.Id));
                }
                throw new InvalidOperationException("EK0: the double-retry witness exceeded its bounded drain");
            }
            NativeState State() => Json.Read<NativeState>(world.Store.Instances[target.Id].State);
            void Retry(string id, Frame source)
            {
                var request = new NativeRetry(id, source.Id, source.Intervention!.Id, "repair");
                Apply(new("add", M: new("delivery-" + id, target, NativeRetry.Kind, Json.Write(request), ReplyTo: ReplyTo.Of("outside", id, world.Time + 120_000), TargetCreation: 1)));
                Drain(id); Drain();
            }
            Apply(new("take", Actor().Id));
            Apply(new("add", M: new("start", target, Kinds.Start, Start: NativeModel.Spec(workflow.Id, mode) with { Creation = new(1, 0, "retry-keys") }))); Drain();
            var first = State().Frames.Values.Single(f => f.Node == "leaf");
            Check(first.Intervention is not null && EffectKeyLeaf.Calls.Count == 1, $"EK1 {mode}: the first fault did not wait for intervention");
            Retry("retry-1", first);
            var second = State().Frames.Values.Single(f => f.Node == "leaf");
            Check(second.Id != first.Id && second.Intervention is not null && EffectKeyLeaf.Calls.Count == 2, $"EK2 {mode}: the first retry did not run a fresh activation that faulted again");
            Retry("retry-2", second);
            Check(world.Store.Instances[target.Id].Status == InstanceStatus.Finished && EffectKeyLeaf.Calls.Count == 3, $"EK3 {mode}: the second retry did not complete the run");
            return EffectKeyLeaf.Calls.ToList();
        }
        finally { NativeWorkflows.Registry.Remove(workflow.Id); }
    }
}

/// <summary>Faults twice, sends the third time; records the key and path of every activation.</summary>
[External]
internal sealed class EffectKeyLeaf : Kernel.Native.Activity
{
    public EffectKeyLeaf() { }
    internal static readonly List<(string Frame, string Path, string Key)> Calls = [];
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        Calls.Add((context.FrameId, context.IterationPath, context.EffectKey));
        return ValueTask.FromResult(Calls.Count <= 2 ? Fault("dependency", "repair required")
            : Complete().With(new HttpSendEffect(new OutboundHttpRequest("POST", "https://partner.example/retried", null, new Dictionary<string, string> { [SendHttpRequest.IdempotencyKeyHeader] = context.EffectKey }))));
    }
}
