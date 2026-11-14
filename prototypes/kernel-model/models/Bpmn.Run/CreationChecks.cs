using Bpmn.Model;

namespace Kernel.Bpmn.Run;

internal static class CreationChecks
{
    internal static IEnumerable<string> Names => from qualified in new[] { false, true }
        from history in Enum.GetValues<History>() from recover in new[] { false, true }
        select $"bpmn-creation:qualified={qualified},history={history},recover={recover}";

    internal static async Task Run()
    {
        foreach (var qualified in new[] { false, true })
        foreach (var history in Enum.GetValues<History>())
        foreach (var recover in new[] { false, true })
        {
            var registry = new ProcessRegistry();
            var escalation = new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation,
                new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = "X" });
            var message = new BpmnEventDefinition(BpmnEventDefinitionTypes.Message);
            var p = new ProcessDraft("identity-parent");
            p.B.StartEvent("start").CallActivity("call", "identity-child", bindingRef: "call").EndEvent("normal")
                .BoundaryEvent("caught", "call", escalation, interrupting: true)
                .IntermediateCatchEvent("hold", message, bindingRef: "hold").EndEvent("end");
            p.Chain("start", "call", "normal").Chain("caught", "hold", "end");
            registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work>
            { ["call"] = new CallWork("identity-child"), ["hold"] = new MessageWork("release", "order") }));
            p = new ProcessDraft("identity-child");
            p.B.StartEvent("start").IntermediateThrowEvent("raise", escalation)
                .IntermediateCatchEvent("hold", message, bindingRef: "hold").EndEvent("end");
            if (recover) p.B.IntermediateCatchEvent("gate", message, bindingRef: "gate");
            if (recover) p.Chain("start", "gate", "raise", "hold", "end");
            else p.Chain("start", "raise", "hold", "end");
            registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work>
            { ["hold"] = new MessageWork("child-release", "order"), ["gate"] = new MessageWork("child-go", "order") }));
            p = new ProcessDraft("identity-wait");
            p.B.StartEvent("start").IntermediateCatchEvent("hold", message, bindingRef: "hold").EndEvent("end");
            p.Chain("start", "hold", "end");
            registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work>
            { ["hold"] = new MessageWork("replacement-release", "order") }));
            var store = new InMemoryStore(1, () => 100);
            var capture = new CaptureStore(store);
            var owner = new Owner("identity-owner", 0, capture, registry, () => 100);
            await owner.StartAsync();
            var parent = InstanceRef.In(0, "identity-parent");
            var spec = BpmnModel.Spec("identity-parent", history: history) with
                { Creation = qualified ? new(1, 0, "parent") : null };
            owner.Worker.Post(new("identity-start", parent, Kinds.Start, Start: spec), fresh: true);
            async Task Drain()
            {
                await owner.Worker.WokenAsync(null);
                for (var i = 0; i < 20; i++) await owner.Worker.RunAsync();
            }
            await Drain();
            if (recover)
            {
                Check(capture.Sent.All(m => m.Kind != BpmnKinds.Signal), "BC0: recovery missed the waiting child");
                await owner.Worker.DisposeAsync();
                owner = new Owner("identity-before-signal", 0, capture, registry, () => 100);
                await owner.StartAsync();
                await Events.PublishAsync(store, new(BpmnModel.MessageKey("child-go", ""), "child-go", null, 1000));
                await Drain();
            }
            var signal = capture.Sent.Single(m => m.Kind == BpmnKinds.Signal);
            Check(signal.TargetCreation == (qualified ? 1 : null), "BC1: scope signal lost its parent creation");
            var child = signal.Read<ScopeSignal>().Child;
            var cancel = capture.Sent.Single(m => m.Target.Id == child && m.Kind == Kinds.Terminate);
            Check(cancel.TargetCreation == (qualified ? 1 : null), "BC2: call cancellation lost its child creation");
            Check(store.Instances[child].Status == InstanceStatus.Cancelled && store.Instances[parent.Id].Status == InstanceStatus.Running,
                "BC3: called child did not cancel while its parent remained waiting");
            if (qualified)
            {
                // Replay the real earlier cancellation against a replacement at the same actor address.
                await owner.Worker.DisposeAsync();
                owner = new Owner("identity-reload", 0, capture, registry, () => 100);
                await owner.StartAsync();
                await store.AddAsync(new("identity-delete", new(child), Kinds.Delete, TargetCreation: 1));
                await Drain();
                await store.AddAsync(new("identity-recreate", new(child), Kinds.Start,
                    Start: BpmnModel.Spec("identity-wait", history: history) with { Creation = new(2, 1, "replacement") }));
                await Drain();
                var before = store.Instances[child];
                await store.AddAsync(cancel with { Id = "identity-late-cancel" });
                await Drain();
                Check(store.Instances[child] == before && before.Creation == 2 && before.Status == InstanceStatus.Running,
                    "BC4: old call cancellation changed the replacement after reload");
            }
            await Events.PublishAsync(store, new(BpmnModel.MessageKey("release", ""), "release", null, 1000));
            await Drain();
            Check(store.Instances[parent.Id].Status == InstanceStatus.Finished, "BC5: parent did not finish its ordinary message wait");
            await owner.Worker.DisposeAsync();
            Console.WriteLine($"PASS bpmn-creation:qualified={qualified},history={history},recover={recover}");
        }
    }

    private static void Check(bool ok, string detail)
    { if (!ok) throw new InvalidOperationException(detail); }
}
