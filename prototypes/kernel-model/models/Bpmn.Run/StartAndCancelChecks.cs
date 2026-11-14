using System.Text.Json.Nodes;
using Bpmn.Model;

namespace Kernel.Bpmn.Run;

internal static class StartAndCancelChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var registry = new ProcessRegistry();
        var p = new ProcessDraft("order-start");
        p.B.StartEvent("by-payment", null, new BpmnEventDefinition(BpmnEventDefinitionTypes.Message))
            .StartEvent("by-shipment", null, new BpmnEventDefinition(BpmnEventDefinitionTypes.Message))
            .ServiceTask("record-payment", bindingRef: "pay").ServiceTask("record-shipment", bindingRef: "ship")
            .EndEvent("paid").EndEvent("shipped");
        p.Chain("by-payment", "record-payment", "paid").Chain("by-shipment", "record-shipment", "shipped");
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["pay"] = new JobWork("pay"), ["ship"] = new JobWork("ship") }));
        registry.Service("pay", v => new ServiceResult(new JsonObject { ["route"] = "payment", ["seen"] = v["value"]?.DeepClone() }));
        registry.Service("ship", _ => new ServiceResult(new JsonObject { ["route"] = "shipment" }));

        p = new ProcessDraft("transaction");
        p.B.Transaction().StartEvent("start").ExclusiveGateway("choose").EndEvent("normal")
            .EndEvent("cancelled", null, new BpmnEventDefinition(BpmnEventDefinitionTypes.Cancel));
        p.Chain("start", "choose").Flow("choose", "cancelled", when: "cancel").Flow("choose", "normal", isDefault: true);
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work>()));
        p = new ProcessDraft("call-transaction");
        p.B.StartEvent("start").CallActivity("call", "transaction", bindingRef: "call").ServiceTask("success", bindingRef: "success")
            .EndEvent("normal").BoundaryEvent("caught", "call", new BpmnEventDefinition(BpmnEventDefinitionTypes.Error), interrupting: true)
            .ServiceTask("handler", bindingRef: "handler").EndEvent("handled");
        p.Chain("start", "call", "success", "normal").Chain("caught", "handler", "handled");
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work>
        { ["call"] = new CallWork("transaction"), ["success"] = new JobWork("success"), ["handler"] = new JobWork("handler") }));
        registry.Service("success", _ => new ServiceResult(new JsonObject { ["route"] = "success" }));
        registry.Service("handler", _ => new ServiceResult(new JsonObject { ["route"] = "boundary" }));

        var store = new InMemoryStore(1, () => 100);
        var owner = new Owner("initial", 0, store, registry);
        await owner.StartAsync();
        async Task Recover(string name)
        {
            await owner.Worker.DisposeAsync();
            owner = new Owner(name, 0, store, registry);
            await owner.StartAsync();
        }
        async Task Drain()
        {
            await owner.Worker.WokenAsync(null);
            for (int pass = 0; pass < 12; pass++) await owner.Worker.RunAsync();
        }
        var subscription = BpmnModel.MessageStart("order-start", "by-payment", "paid", "order", new JsonObject { ["value"] = "configured" });
        await Events.SubscribeAsync(store, subscription, 1);
        var paid = new EventRow(subscription.Key, "42", """{"value":"published"}""", 1000);
        await Events.PublishAsync(store, paid);
        var target = Events.Deliver(paid, subscription).Target.Id;
        await Recover("start-recovery");
        await Drain();
        // Registrar catch-up and publisher redelivery both target the one original instance after takeover.
        await Events.SubscribeAsync(store, subscription, 1);
        await Events.PublishAsync(store, paid);
        await Drain();
        var row = store.Instances[target];
        var state = Json.Read<BpmnState>(row.State);
        var starts = store.Journal.Values.Where(j => j.Instance == target).SelectMany(j => Json.Read<string[]>(j.Entries))
            .Count(entry => entry.StartsWith("w ", StringComparison.Ordinal));
        check(row.Status == InstanceStatus.Finished && state.Started && state.Variables.GetValueOrDefault("route")?.ToString() == "payment" &&
              state.Variables.GetValueOrDefault("seen")?.ToString() == "published" && starts == 1,
            "published event starts only its named BPMN branch with its payload, once across recovery and redelivery");

        foreach (var cancel in new[] { true, false })
        {
            var parent = InstanceRef.In(0, "parent-" + cancel);
            owner.Worker.Post(new("start-" + cancel, parent, Kinds.Start, Start: BpmnModel.Spec("call-transaction", new JsonObject { ["cancel"] = cancel })), fresh: true);
            await owner.Worker.RunAsync(); // parent publishes child start
            await owner.Worker.RunAsync(); // child publishes its terminal notice, before the parent runs again
            var notice = store.Messages.Values.Select(r => r.Message).Single(m => m.Kind == WorkflowKinds.ChildFinished && m.Target == parent).Read<ChildFinished>();
            var expectedStatus = cancel ? InstanceStatus.Cancelled : InstanceStatus.Finished;
            check(notice.Status == expectedStatus && store.Instances[notice.Child].Status == expectedStatus,
                $"committed child outcome {(cancel ? "Cancelled" : "Done")} survives in the parent notice");
            await Recover("parent-recovery-" + cancel);
            await Drain();
            var result = store.Instances[parent.Id];
            var vars = Json.Read<BpmnState>(result.State).Variables;
            check(result.Status == InstanceStatus.Finished && vars.GetValueOrDefault("route")?.ToString() == (cancel ? "boundary" : "success"),
                $"recovered parent takes the {(cancel ? "error boundary" : "success flow")} for its called transaction");
        }
    }
}
