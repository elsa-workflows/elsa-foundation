using System.Text.Json.Nodes;
using Bpmn.Model;

namespace Kernel.Bpmn.Run;

internal static class MessageStartChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var registry = new ProcessRegistry();
        foreach (var name in new[] { "payment", "shipment", "payment-copy" })
        {
            var p = new ProcessDraft(name);
            p.B.StartEvent("start", null, new BpmnEventDefinition(BpmnEventDefinitionTypes.Message));
            var work = new Dictionary<string, Work>();
            if (name == "payment")
            {
                p.B.ReceiveTask("again", bindingRef: "again");
                work["again"] = new MessageWork("payment", "account");
                p.Chain("start", "again");
            }
            if (name == "shipment")
            {
                p.B.StartEvent("signal-only", null, new BpmnEventDefinition(BpmnEventDefinitionTypes.Signal));
                p.B.EndEvent("signal-end");
                p.Chain("signal-only", "signal-end");
            }
            p.B.EndEvent("end");
            p.Chain(name == "payment" ? "again" : "start", "end");
            registry.Add(new BpmnProcess(p.Build(), work));
        }
        var store = new InMemoryStore(1, () => 100);
        var owner = new Owner("old", 0, store, registry);
        await owner.StartAsync();

        SubscriptionRow Register(string process, string message) =>
            BpmnModel.MessageStart(process, "start", message, "A", new JsonObject { ["account"] = "A" });
        var pay = Register("payment", "payment");
        var ship = Register("shipment", "shipment");
        var copy = Register("payment-copy", "payment");
        foreach (var s in new[] { pay, ship, copy }) await Events.SubscribeAsync(store, s, 1);
        // Identical event ID and local BPMN start name for all. Two registrations also share the same event key.
        var paid = new EventRow(pay.Key, "42", null, 1000);
        var shipped = new EventRow(ship.Key, "42", null, 1000);
        var firstTarget = Events.Deliver(paid, pay).Target.Id;
        await Events.PublishAsync(store, paid);
        await Events.PublishAsync(store, shipped);
        await owner.Worker.DisposeAsync();
        owner = new Owner("new", 0, store, registry);
        await owner.StartAsync();
        async Task Drain()
        {
            await owner.Worker.WokenAsync(null);
            for (int i = 0; i < 12; i++) await owner.Worker.RunAsync();
        }
        for (int i = 0; i < 12 && !store.Instances.ContainsKey(firstTarget); i++) await owner.Worker.RunAsync();
        check(store.Instances.TryGetValue(firstTarget, out var first) && first.Revision == 1 &&
            Json.Read<BpmnState>(first.State).TakenEvents.Contains(new ConsumedEvent(pay.Key, paid.Id)),
            "the first committed revision contains the triggering event receipt");
        await owner.Worker.DisposeAsync();
        owner = new Owner("after-receipt", 0, store, registry);
        await owner.StartAsync();
        await Drain();
        var runs = store.Instances.Values.ToArray();
        check(runs.Length == 3 && runs.Select(r => r.Definition).ToHashSet().SetEquals(["payment", "shipment", "payment-copy"]),
            "one event ID reaches three distinct definitions sharing the same local start ID");
        check(store.Instances.TryGetValue(firstTarget, out var row) && row.Status == InstanceStatus.Running &&
            Json.Read<BpmnState>(row.State).TakenEvents.Contains(new ConsumedEvent(pay.Key, paid.Id)) &&
            store.Subscriptions.Any(s => s.Instance == firstTarget && s.Key == pay.Key),
            "the start commit records its trigger and the following catch still waits on that key");
        check(pay == Register("payment", "payment") && pay.Wait != BpmnModel.MessageStart("payment", "other-start", "payment", "A").Wait &&
            pay.Wait != BpmnModel.MessageStart("payment", "start", "payment", "B").Wait, "registration identity includes the local start and correlation");
        await Events.SubscribeAsync(store, Register("payment", "payment"), 1);
        await Events.PublishAsync(store, paid);
        await Drain();
        check(store.Instances[firstTarget].Status == InstanceStatus.Running && store.Instances.Count == runs.Length,
            "redelivery of the triggering event cannot consume the next catch or start the original process twice");
        await Events.PublishAsync(store, paid with { Id = "fresh", Payload = """{"accepted":"fresh"}""" });
        await Drain();
        var original = store.Instances[firstTarget];
        check(original.Status == InstanceStatus.Finished && Json.Read<BpmnState>(original.State).Variables.GetValueOrDefault("accepted")?.ToString() == "fresh",
            "a fresh publication completes the original instance's waiting catch");

        var shipment = store.Instances[Events.Deliver(shipped, ship).Target.Id];
        check(shipment.Status == InstanceStatus.Finished && Json.Read<BpmnState>(shipment.State).TakenEvents.Contains(new ConsumedEvent(ship.Key, shipped.Id)),
            "a process with message and signal start events accepts its registered message start");
        foreach (var element in new[] { "signal-only", "signal-end" })
        {
            var wrong = BpmnModel.MessageStart("shipment", element, "misconfigured", element);
            await Events.SubscribeAsync(store, wrong, 1);
            var publication = new EventRow(wrong.Key, element, null, 1000);
            await Events.PublishAsync(store, publication);
            await Drain();
            var rejected = store.Instances[Events.Deliver(publication, wrong).Target.Id];
            check(rejected.Status == InstanceStatus.Faulted && rejected.Fault?.Contains($"'{element}' is not a message start event", StringComparison.Ordinal) == true,
                $"message registration targeting '{element}' faults with the invalid start element");
        }

        var incomplete = new SubscriptionRow("raw", "", "start", BpmnModel.Spec("shipment"));
        await Events.SubscribeAsync(store, incomplete, 1);
        var invalid = new EventRow("raw", "bad", null, 1000);
        await Events.PublishAsync(store, invalid);
        await Drain();
        var faulted = store.Instances[Events.Deliver(invalid, incomplete).Target.Id];
        check(faulted.Status == InstanceStatus.Faulted && faulted.Fault?.Contains("BpmnModel.MessageStart", StringComparison.Ordinal) == true,
            "an event start registered with a plain start spec faults with the missing registration contract");
    }
}
