using Kernel.Foundation.InMemory;
using Kernel.Native;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;

namespace Kernel.Sim;

internal static class ContractNativeChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var direct = typeof(NativeExecutionModel).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();
        var graph = typeof(Kernel.Graph.Models.FlowchartConnection).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name);
        check(direct.Contains("Kernel.Foundation.Contracts") && direct.Contains("Kernel.Model.Contracts") &&
            direct.All(name => name is "Kernel.Foundation.Contracts" or "Kernel.Model.Contracts" or "Kernel.Graph" or "Kernel.Native.Activities" ||
                name?.StartsWith("System.", StringComparison.Ordinal) == true) &&
            graph.All(name => name?.StartsWith("System.", StringComparison.Ordinal) == true),
            "the compiled Native execution model uses the public contracts and its layer-three libraries");
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
            await Run((ok, label) => check(ok, $"{mode}: {label}"), mode);
    }

    private static async Task Run(Action<bool, string> check, string mode)
    {
        const long initial = 1_701_800_000_000;
        const long due = initial + 100;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests("native-contract"), "native-contract");
        Contract.IActorReads reads = new ActorList(store, 32);
        var runtime = new PublicRuntime(ingress, reads);
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("nested", Wf.Set("nested-output", "output", "child value")))
            .Add(Wf.Workflow("main", Wf.Sequence("sequence",
                Wf.Delay("delay", 100),
                Wf.Call("lookup", "pricing"),
                Wf.Dispatch("child", "nested", wait: true),
                Wf.Event("signal", "approval"),
                Wf.Http("send", "https://partner.invalid/notify"))));
        var sent = new List<NativeOutbound>();
        var model = new NativeExecutionModel(registry, outbound: sent.Add);
        var reference = (await runtime.StartAsync(scope, "start-native", NativeExecutionModel.Start("main", mode))).Actor!.Value;
        var target = new Contract.Target.Ref(reference);
        var owner = await Activate("native-first");
        await Pump(owner);

        var saved = State();
        var (wait, timer) = saved.Waits.Single(item => item.Value.Kind == "timer");
        var inputs = await runtime.InboxAsync(scope, reference);
        var restore = model.RestoreWaits(new(reference, scope.Tenant, model.Key, "main", null), saved);
        check(timer.Key.StartsWith('~') && timer.Due == due && inputs.Items is [{ Id: var id, Due: due }] && id == timer.Key &&
            restore.Timers is [{ Id: var restored, Kind: NativeKinds.Timer, Due: due, Payload: var payload }] &&
            restored == timer.Key && Json.Read<TimerFired>(payload!).Wait == wait,
            "Native commits the original timer recipe and the same opaque handle returned to the model");

        await ingress.SendAsync(scope, target, "forged-timer", NativeKinds.Timer, Json.Write(new TimerFired(wait)));
        await Pump(owner);
        check(State().Waits.ContainsKey(wait) && sent.Count == 0,
            "an outside timer-shaped message cannot release the Native wait");
        await owner.DisposeAsync();

        now = due - 1;
        await using var successor = await Activate("native-next");
        await Pump(successor);
        check(State().Waits.ContainsKey(wait), "the Native timer remains dormant on the successor before its due reading");

        now = due;
        await Pump(successor);
        var after = State();
        check(after.Results.GetValueOrDefault("lookup")?.GetValue<string>() == "answer of pricing" &&
            after.Results.GetValueOrDefault("child")?["Output"]?.GetValue<string>() == "child value" &&
            after.Waits.Values.Any(item => item is { Kind: "event", Key: "event:approval" }),
            "the same Native engine runs the timed resume, background activity and exact child completion through the contract");

        await ingress.SendAsync(scope, new Contract.Target.Event("event:approval"), "approved", "published", "yes");
        await Pump(successor);
        var key = Models.EffectKey.For(reference.Id, "send", Models.EffectKey.RootPath, mode == Modes.Durable ? 1 : 0);
        check(await reads.GetAsync(scope, reference) is { Header.Status: Contract.ActorStatus.Finished } &&
            State().Results.GetValueOrDefault("signal")?["Payload"]?.GetValue<string>() == "yes" &&
            sent is [{ Actor: var sender, EffectKey: var effect, Effect: HttpSendEffect http }] &&
            sender == reference && effect == key && http.Request.Headers[SendHttpRequest.IdempotencyKeyHeader] == key,
            "Native finishes from the contract event and its HTTP activity derives the effect key from the opaque actor ref");

        async Task<IScopeActivation> Activate(string node)
        {
            var worker = new Worker(node, 0, store, new ActorAdapter(store, new PublicWorkflow([model])),
                new WorkerOptions { Monotonic = () => now - initial });
            return (await new DurableHost([worker]).ActivateAsync(0))!;
        }

        NativeState State() => Json.Read<NativeState>(store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row.State);
    }

    private static async Task Pump(IScopeActivation owner)
    {
        for (var step = 0; step < 25; step++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
            await Task.Yield();
        }
    }
}
