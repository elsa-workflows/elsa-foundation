using System.Text.Json.Nodes;
using Bpmn.Model;
using Bpmn.Semantics;

namespace Kernel.Bpmn.Run;

internal static class ScopeChecks
{
    private static readonly JsonObject Inputs = new() { ["items"] = new JsonArray("alpha", "beta"), ["item"] = "parent", ["loopIndex"] = -1 };

    public static void Register(ProcessRegistry registry)
    {
        var p = new ProcessDraft("scoped-receive");
        var loop = new BpmnLoopCharacteristics(collectionVariable: "items", itemVariable: "item");
        p.B.Variable("items", "array").StartEvent("start").Task("receiveTask", "receive", null, "receive", loop).EndEvent("end");
        p.Chain("start", "receive", "end");
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["receive"] = new MessageWork("payment", "item") }));

        p = new ProcessDraft("two-messages");
        p.B.StartEvent("start").ReceiveTask("pay", bindingRef: "pay").ReceiveTask("ship", bindingRef: "ship").EndEvent("end");
        p.Chain("start", "pay", "ship", "end");
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work>
        {
            ["pay"] = new MessageWork("payment", "item"), ["ship"] = new MessageWork("shipment", "item")
        }));

        p = new ProcessDraft("scoped-call");
        p.B.Variable("items", "array").StartEvent("start").Task("callActivity", "invoke", null, "invoke", loop).EndEvent("end");
        p.Chain("start", "invoke", "end");
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["invoke"] = new CallWork("choose") }));
        p = new ProcessDraft("choose");
        p.B.StartEvent("start").ExclusiveGateway("choice").ServiceTask("alpha", bindingRef: "alpha")
            .ServiceTask("other", bindingRef: "other").EndEvent("end-a").EndEvent("end-b");
        p.Chain("start", "choice").Flow("choice", "alpha", when: "item == 'alpha'").Flow("choice", "other", isDefault: true)
            .Chain("alpha", "end-a").Chain("other", "end-b");
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["alpha"] = new JobWork("alpha"), ["other"] = new JobWork("other") }));
        registry.Service("alpha", v => new ServiceResult(new JsonObject { ["alpha-route"] = v["loopIndex"]?.DeepClone() }));
        registry.Service("other", v => new ServiceResult(new JsonObject { ["other-route"] = v["loopIndex"]?.DeepClone() }));
    }

    public static async Task Run(ProcessRegistry registry, Action<bool, string> check)
    {
        var store = new InMemoryStore(1, () => 100);
        var owner = new Owner("first", 0, store, registry);
        await owner.StartAsync();
        async Task Pump()
        {
            await owner.Worker.WokenAsync(null);
            for (int i = 0; i < 12; i++) await owner.Worker.RunAsync();
        }
        async Task Start(string process)
        {
            var target = InstanceRef.In(0, process);
            owner.Worker.Post(new("start-" + process, target, Kinds.Start, Start: BpmnModel.Spec(process, Inputs)), fresh: true);
            await Pump();
        }
        async Task Recover(string name)
        {
            await owner.Worker.DisposeAsync();
            owner = new Owner(name, 0, store, registry);
            await owner.StartAsync();
        }
        async Task Publish(string channel, string correlation, string eventId, JsonObject? payload = null)
        {
            await Events.PublishAsync(store, new(BpmnModel.MessageKey(channel, correlation), eventId, payload?.ToJsonString(), 1000));
            await Pump();
        }
        await Start("scoped-receive");
        const string receiver = "0/scoped-receive";
        var keys = store.Subscriptions.Where(s => s.Instance == receiver).Select(s => s.Key).Order(StringComparer.Ordinal).ToArray();
        check(keys.SequenceEqual(new[] { "alpha", "beta" }.Select(i => BpmnModel.MessageKey("payment", i))), "parallel receive subscriptions bind their iteration items");
        await Recover("receive-recovery");
        var state = Json.Read<BpmnState>((await store.LoadAsync([receiver])).Single().State);
        var conditions = new Conditions(state);
        var frames = state.Live.Values.OrderBy(w => w.Key, StringComparer.Ordinal).ToArray();
        // Version 0.5.0 routes an ordinary MI element's outgoing flows once at its coordinator (Iteration=null).
        // Exercise the condition port's non-null contract with the real persisted IDs independently of that route.
        BpmnConditionRequest Request(string body, string? iteration) => new("receive", "choice", BpmnConditionSites.SequenceFlow, body, null, iteration);
        var values = frames.Select(w => conditions.Evaluate(Request("item == 'alpha'", w.Iteration)).Kind).ToArray();
        check(frames.All(w => w.Iteration is not null) && values.SequenceEqual([BpmnConditionResultKind.True, BpmnConditionResultKind.False]),
            "condition port uses two interpreter-issued iteration IDs after snapshot reload");
        // Done removes its entry before the interpreter sees the completion. Keep that single frame for this evaluation.
        var finishing = state.Live.Single(entry => entry.Value == frames[0]);
        state.Live.Remove(finishing.Key);
        var completing = new Conditions(state, finishing.Value);
        check(completing.Evaluate(Request("loopIndex == 0", finishing.Value.Iteration)).Kind == BpmnConditionResultKind.True &&
            completing.Evaluate(Request("loopIndex == -1", null)).Kind == BpmnConditionResultKind.True &&
            completing.Evaluate(Request("loopIndex == -1", "missing-frame")).Kind == BpmnConditionResultKind.Failed,
            "a completing frame shadows the parent until its report returns; a missing frame fails closed");
        await Publish("payment", "alpha", "same", new JsonObject { ["first"] = 1 });
        await Publish("payment", "beta", "same", new JsonObject { ["second"] = 2 });
        var received = Json.Read<BpmnState>(store.Instances[receiver].State);
        check(store.Instances[receiver].Status == InstanceStatus.Finished && received.Variables.ContainsKey("first") &&
            received.Variables.ContainsKey("second") && received.Variables["item"]?.ToString() == "parent", "two correlated events with the same ID finish the recovered receiver");

        await Start("two-messages");
        await Publish("payment", "parent", "42");
        await Recover("shipment-recovery");
        await Publish("shipment", "parent", "42");
        check(store.Instances["0/two-messages"].Status == InstanceStatus.Finished, "two distinct event names with ID 42 are consumed across recovery");
        await Start("scoped-call");
        var choice = Json.Read<BpmnState>(store.Instances["0/scoped-call"].State).Variables;
        check(store.Instances["0/scoped-call"].Status == InstanceStatus.Finished && choice.GetValueOrDefault("alpha-route")?.GetValue<int>() == 0 &&
            choice.GetValueOrDefault("other-route")?.GetValue<int>() == 1 && choice["item"]?.ToString() == "parent",
            "the two multi-instance children choose distinct conditional flows");
    }
}
