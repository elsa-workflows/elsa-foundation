using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Bpmn.Model.State;

namespace Kernel.Bpmn.Run;

/// <summary>The model on the in-memory store, one owner.</summary>
public static class Smoke
{
    private static int _failures;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
        if (!ok) _failures++;
    }

    /// <summary>Jobs of these services wait until released; every other job runs at once.</summary>
    private sealed class Gates
    {
        public readonly ConcurrentDictionary<string, TaskCompletionSource> Held = new(StringComparer.Ordinal);
        public readonly HashSet<string> Services = new(StringComparer.Ordinal);

        public async ValueTask Gate(IJob job, CancellationToken cancel)
        {
            var service = Json.Read<JobArgs>(job.Arguments!).Service;
            if (Services.Contains(service)) await Held.GetOrAdd(service, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(cancel);
        }

        public void Release(params string[] services)
        {
            foreach (var s in services) Held.GetOrAdd(s, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
        }
    }

    public static ProcessRegistry Registry()
    {
        var registry = new ProcessRegistry();

        var p = new ProcessDraft("smoke-task");
        p.B.StartEvent("start").ServiceTask("task", bindingRef: "quote").EndEvent("end");
        p.Chain("start", "task", "end");
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["quote"] = new JobWork("quote") }));

        // A message catch that comes round again: one message is taken once.
        p = new ProcessDraft("smoke-catch-loop");
        p.B.StartEvent("start").ExclusiveGateway("again").IntermediateCatchEvent("go", new BpmnEventDefinition(BpmnEventDefinitionTypes.Message), bindingRef: "go")
            .ServiceTask("inc", bindingRef: "inc").ExclusiveGateway("decide").EndEvent("end");
        p.Chain("start", "again", "go", "inc", "decide").Flow("decide", "again", when: "n < 2").Flow("decide", "end", isDefault: true);
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["go"] = new MessageWork("go", "order"), ["inc"] = new JobWork("inc") }, new JsonObject { ["n"] = 0 }));

        // A fork whose one branch faults: the instance ends Faulted and the other branch's job goes.
        p = new ProcessDraft("smoke-fault");
        p.B.StartEvent("start").ParallelGateway("fork").ServiceTask("a", bindingRef: "boom").ServiceTask("b", bindingRef: "slow").ParallelGateway("join").EndEvent("end");
        p.Chain("start", "fork", "a", "join", "end").Chain("fork", "b", "join");
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["boom"] = new JobWork("boom"), ["slow"] = new JobWork("slow") }));

        // A call activity whose child ends on an error end event: the coded error boundary catches it.
        var error = new BpmnEventDefinition(BpmnEventDefinitionTypes.Error, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = "E1" });
        p = new ProcessDraft("smoke-call-error");
        p.B.StartEvent("start").CallActivity("call", "smoke-failing", bindingRef: "call").EndEvent("end")
            .BoundaryEvent("caught", attachedTo: "call", error, interrupting: true).ServiceTask("handle", bindingRef: "handle").EndEvent("handled");
        p.Chain("start", "call", "end").Chain("caught", "handle", "handled");
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["call"] = new CallWork("smoke-failing"), ["handle"] = new JobWork("handle") }));
        p = new ProcessDraft("smoke-failing");
        p.B.StartEvent("start").EndEvent("fail", null, error);
        p.Chain("start", "fail");
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work>()));

        return registry
            .Service("quote", v => new ServiceResult(new JsonObject { ["quote"] = $"quote for {v["order"]}" }))
            .Service("inc", v => new ServiceResult(new JsonObject { ["n"] = v["n"]!.GetValue<int>() + 1 }))
            .Service("boom", _ => throw new InvalidOperationException("boom"))
            .Service("slow", _ => new ServiceResult(new JsonObject { ["slow"] = true }))
            .Service("handle", _ => new ServiceResult(new JsonObject { ["handled"] = true }));
    }

    public static async Task<int> RunAsync()
    {
        var gates = new Gates();
        var store = new InMemoryStore(1, () => Environment.TickCount64);
        var owner = new Owner("n1", 0, store, Registry(), options: new BpmnOptions { Gate = gates.Gate });
        await owner.StartAsync();
        List<string> Journal(InstanceRef id) => store.Journal.Values.Where(j => j.Instance == id.Id).OrderBy(j => j.Position).SelectMany(j => Json.Read<List<string>>(j.Entries)).ToList();
        bool Settled() => store.Messages.Values.All(m => m.Done);
        long Keep() => Environment.TickCount64 + 60_000;

        Console.WriteLine("a service task is a job");
        {
            var id = InstanceRef.In(0, "task");
            owner.Worker.Post(new Message("m1", id, Kinds.Start, Start: BpmnModel.Spec("smoke-task", new JsonObject { ["order"] = 7 })), fresh: true);
            await owner.PumpAsync(() => owner.Status(id.Id) == InstanceStatus.Finished);
            var state = owner.State(id.Id)!;
            Check(state.Variables["quote"]?.GetValue<string>() == "quote for 7", $"the job's result is a variable: {state.Variables.GetValueOrDefault("quote")}");
            Check(state.Live.Count == 0 && state.Tokens.Tokens.All(t => t.Status is BpmnTokenStatus.Consumed or BpmnTokenStatus.Canceled), "no live work and no live token left");
            Check(Journal(id) is ["w h1 task job h1", "d h1 task", "end Done"], $"journal: {string.Join(" | ", Journal(id))}");
        }

        Console.WriteLine("a message catch that comes round again takes one message once");
        {
            var key = BpmnModel.MessageKey("go", "o1");
            await Events.PublishAsync(store, new EventRow(key, "go-1", "{\"by\":\"first\"}", Keep()));
            var id = InstanceRef.In(0, "loop");
            owner.Worker.Post(new Message("m2", id, Kinds.Start, Start: BpmnModel.Spec("smoke-catch-loop", new JsonObject { ["order"] = "o1" })), fresh: true);
            await owner.PumpAsync(() => owner.State(id.Id)?.Variables["n"]?.GetValue<int>() == 1 && Settled() && store.Subscriptions.Count == 1);
            await owner.PumpAsync(() => Settled(), poll: true);
            Check(owner.Status(id.Id) == InstanceStatus.Running && owner.State(id.Id)!.Variables["n"]!.GetValue<int>() == 1, "the first message was taken once: the second catch still waits");
            Check(store.Subscriptions.Count == 1, $"one subscription, the second catch's: {store.Subscriptions.Count}");
            await Events.PublishAsync(store, new EventRow(key, "go-2", "{\"by\":\"second\"}", Keep()));
            await owner.PumpAsync(() => owner.Status(id.Id) == InstanceStatus.Finished, poll: true);
            Check(owner.State(id.Id)!.Variables["by"]?.GetValue<string>() == "second" && store.Subscriptions.Count == 0, "the second message ended it; no subscription left");
        }

        Console.WriteLine("a fault in one branch ends the instance and stops the other branch");
        {
            gates.Services.UnionWith(["boom", "slow"]);
            var id = InstanceRef.In(0, "fault");
            owner.Worker.Post(new Message("m3", id, Kinds.Start, Start: BpmnModel.Spec("smoke-fault")), fresh: true);
            await owner.PumpAsync(() => store.Instances.GetValueOrDefault(id.Id)?.Jobs is { } jobs && jobs.Contains("h2"));
            // Both results are in the inbox before the next pass, the fault first (a result goes to the front): one turn takes both.
            gates.Release("slow");
            await Task.Delay(30);
            gates.Release("boom");
            await Task.Delay(30);
            await owner.PumpAsync(() => owner.Status(id.Id) == InstanceStatus.Faulted && Settled());
            var state = owner.State(id.Id)!;
            Check(state.Live.Count == 0 && store.Instances[id.Id].Jobs is null && Journal(id).IndexOf("d h1 a") < Journal(id).IndexOf("x h2 b"), $"faulted, the later result fed to nobody, no work left: {string.Join(" | ", Journal(id))}");
            gates.Services.Clear();
        }

        Console.WriteLine("a call activity's child error keeps its code");
        {
            var id = InstanceRef.In(0, "call");
            owner.Worker.Post(new Message("m4", id, Kinds.Start, Start: BpmnModel.Spec("smoke-call-error")), fresh: true);
            await owner.PumpAsync(() => owner.Status(id.Id) is InstanceStatus.Finished or InstanceStatus.Faulted && Settled());
            Check(owner.Status(id.Id) == InstanceStatus.Finished && owner.State(id.Id)!.Variables.GetValueOrDefault("handled")?.GetValue<bool>() == true,
                $"the error boundary with code E1 caught the child's error: {owner.Status(id.Id)}, {string.Join(" | ", Journal(id))}");
        }

        Check(Settled() && store.Subscriptions.Count == 0, "no message and no subscription left");
        Console.WriteLine(_failures == 0 ? "smoke: all passed" : $"smoke: {_failures} FAILED");
        return _failures;
    }
}
