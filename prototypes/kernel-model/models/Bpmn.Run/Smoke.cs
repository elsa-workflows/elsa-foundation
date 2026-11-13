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

        // A called child that escalates: the non-interrupting escalation boundary on the call runs while the child goes on.
        var escalation = new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = "X1" });
        p = new ProcessDraft("smoke-call-escalation");
        p.B.StartEvent("start").CallActivity("call", "smoke-escalating", bindingRef: "call").EndEvent("end")
            .BoundaryEvent("noticed", attachedTo: "call", escalation, interrupting: false).ServiceTask("note", bindingRef: "note").EndEvent("noted");
        p.Chain("start", "call", "end").Chain("noticed", "note", "noted");
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["call"] = new CallWork("smoke-escalating"), ["note"] = new JobWork("note") }));
        p = new ProcessDraft("smoke-escalating");
        p.B.StartEvent("start").IntermediateThrowEvent("raise", escalation).ServiceTask("go-on", bindingRef: "go-on").EndEvent("end");
        p.Chain("start", "raise", "go-on", "end");
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["go-on"] = new JobWork("go-on") }));

        // A fork with a call activity beside a service task: the child gives back only what it set.
        p = new ProcessDraft("smoke-call-fork");
        p.B.StartEvent("start").ParallelGateway("fork").CallActivity("call", "smoke-child", bindingRef: "call").ServiceTask("inc", bindingRef: "inc").ParallelGateway("join").EndEvent("end");
        p.Chain("start", "fork", "call", "join", "end").Chain("fork", "inc", "join");
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["call"] = new CallWork("smoke-child"), ["inc"] = new JobWork("inc") }, new JsonObject { ["n"] = 0 }));
        p = new ProcessDraft("smoke-child");
        p.B.StartEvent("start").ServiceTask("work", bindingRef: "child-work").EndEvent("end");
        p.Chain("start", "work", "end");
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["child-work"] = new JobWork("child-work") }));

        foreach (var call in new[] { false, true })
        {
            var binding = call ? "child" : "service";
            p = new ProcessDraft("smoke-iterations-" + binding);
            var loop = new BpmnLoopCharacteristics(collectionVariable: "items", itemVariable: "item");
            p.B.Variable("items", "array").StartEvent("start").Task(call ? "callActivity" : "serviceTask", "work", null, binding, loop).EndEvent("end");
            p.Chain("start", "work", "end");
            registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work>
            {
                [binding] = call ? new CallWork("smoke-item-child") : new JobWork("observe"),
            }));
        }
        p = new ProcessDraft("smoke-item-child");
        p.B.StartEvent("start").ServiceTask("observe", bindingRef: "observe").EndEvent("end");
        p.Chain("start", "observe", "end");
        registry.Add(new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["observe"] = new JobWork("observe") }));

        return registry
            .Service("observe", v => new ServiceResult(new JsonObject
            {
                ["observed-" + v["loopIndex"]] = v["item"]?.DeepClone(),
            }))
            .Service("child-work", _ => new ServiceResult(new JsonObject { ["child-out"] = "done" }))
            .Service("note", _ => new ServiceResult(new JsonObject { ["noted"] = true }))
            .Service("go-on", _ => new ServiceResult(new JsonObject { ["went-on"] = true }))
            .Service("quote", v => new ServiceResult(new JsonObject { ["quote"] = $"quote for {v["order"]}" }))
            .Service("inc", v => new ServiceResult(new JsonObject { ["n"] = v["n"]!.GetValue<int>() + 1 }))
            .Service("boom", _ => throw new InvalidOperationException("boom"))
            .Service("slow", _ => new ServiceResult(new JsonObject { ["slow"] = true }))
            .Service("handle", _ => new ServiceResult(new JsonObject { ["handled"] = true }));
    }

    public static async Task<int> RunAsync()
    {
        var schedulers = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, maxConcurrencyLevel: 1);
        try
        {
            return await Task.Factory.StartNew(RunOnOwnerAsync, CancellationToken.None,
                TaskCreationOptions.DenyChildAttach, schedulers.ExclusiveScheduler).Unwrap();
        }
        finally { schedulers.Complete(); await schedulers.Completion; }
    }

    private static async Task<int> RunOnOwnerAsync()
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
            async Task ReleaseAndWait(string service, string handle)
            {
                gates.Release(service);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (!owner.Worker.JobResultReady(id.Id, handle)) await Task.Delay(1, timeout.Token);
            }
            await ReleaseAndWait("slow", "h2");
            await ReleaseAndWait("boom", "h1");
            await owner.PumpAsync(() => owner.Status(id.Id) == InstanceStatus.Faulted && Settled());
            var state = owner.State(id.Id)!;
            var entries = Journal(id);
            var fault = entries.IndexOf("d h1 a");
            var cancelled = entries.IndexOf("x h2 b");
            Check(state.Live.Count == 0 && store.Instances[id.Id].Jobs is null && fault >= 0 && cancelled > fault, $"faulted, the later result fed to nobody, no work left: {string.Join(" | ", Journal(id))}");
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

        Console.WriteLine("a called child's escalation reaches the call's escalation boundary");
        {
            var id = InstanceRef.In(0, "escalation");
            owner.Worker.Post(new Message("m5", id, Kinds.Start, Start: BpmnModel.Spec("smoke-call-escalation")), fresh: true);
            await owner.PumpAsync(() => owner.Status(id.Id) is InstanceStatus.Finished or InstanceStatus.Faulted && Settled());
            var vars = owner.State(id.Id)!.Variables;
            Check(owner.Status(id.Id) == InstanceStatus.Finished && vars.ContainsKey("noted") && vars.ContainsKey("went-on"),
                $"the boundary ran and the child went on: {owner.Status(id.Id)}, {string.Join(" | ", Journal(id))}");
        }

        Console.WriteLine("a called child gives back only the variables it set");
        {
            gates.Services.Add("child-work");
            var id = InstanceRef.In(0, "call-fork");
            owner.Worker.Post(new Message("m6", id, Kinds.Start, Start: BpmnModel.Spec("smoke-call-fork")), fresh: true);
            await owner.PumpAsync(() => owner.State(id.Id)?.Variables["n"]?.GetValue<int>() == 1);     // the branch beside the call has set n
            gates.Release("child-work");
            await owner.PumpAsync(() => owner.Status(id.Id) == InstanceStatus.Finished && Settled());
            var vars = owner.State(id.Id)!.Variables;
            Check(vars["n"]!.GetValue<int>() == 1 && vars["child-out"]?.GetValue<string>() == "done", $"n is {vars["n"]} (the child's stale copy is 0), child-out is {vars.GetValueOrDefault("child-out")}");
            gates.Services.Clear();
            gates.Held.Clear();
        }

        foreach (var binding in new[] { "service", "child" })
        {
            var context = new JsonObject { ["items"] = new JsonArray("alpha", "beta"), ["item"] = "parent", ["loopIndex"] = -1 };
            var id = InstanceRef.In(0, "iterations-" + binding);
            owner.Worker.Post(new Message("iterations-" + binding, id, Kinds.Start, Start: BpmnModel.Spec("smoke-iterations-" + binding, context)), fresh: true);
            await owner.PumpAsync(() => owner.Status(id.Id) is InstanceStatus.Finished or InstanceStatus.Faulted && Settled());
            var variables = owner.State(id.Id)!.Variables;
            Check(owner.Status(id.Id) == InstanceStatus.Finished &&
                variables.GetValueOrDefault("observed-0")?.ToString() == "alpha" && variables.GetValueOrDefault("observed-1")?.ToString() == "beta" &&
                variables["item"]?.ToString() == "parent" && variables["loopIndex"]?.GetValue<int>() == -1,
                $"parallel {binding} work sees distinct item/index pairs and leaves parent inputs intact");
        }
        Check(BpmnModel.MessageKey("a:b", "c") != BpmnModel.MessageKey("a", "b:c")
            && BpmnModel.MessageKey("", ":") != BpmnModel.MessageKey(":", "")
            && BpmnModel.MessageKey("a:b", "") != BpmnModel.MessageKey("a%3Ab", "")
            && BpmnModel.MessageKey("paid", "o1") == "msg:paid:o1", "message correlation separates component boundaries");
        var precise = new Dictionary<string, JsonNode?> { ["n"] = 9007199254740993L };
        foreach (var (expression, expected) in new[] { ("n == 9007199254740992", false), ("n > 9007199254740992", true),
            ("n == 9.007199254740993e15", true), ("n < 9007199254740993.5", true),
            ("n >= +09007199254740993.00", true), ("n < 1e-400", false) })
            Check(Conditions.Holds(expression, precise) == expected, $"{expression} is {expected}");

        foreach (var (json, expression, expected) in new[] { ("-1.01", "n < -1", true), ("-0", "n == 0.0e12", true),
            ("1.0001", "n <= 1.0000", false), ("1e400", "n > 9.99e399", true), ("1e-400", "n != 0", true) })
            Check(Conditions.Holds(expression, new Dictionary<string, JsonNode?> { ["n"] = JsonNode.Parse(json) }) == expected,
                $"{json} in {expression} is {expected}");

        Console.WriteLine("conditions");
        foreach (var (body, holds) in new[] { ("s != 'a==b'", true), ("s == 'x<y'", true), ("n >= 2", true), ("n < 2", false), ("flag", true), ("missing", false) })
        {
            var vars = new Dictionary<string, JsonNode?> { ["s"] = body.Contains("x<y") ? "x<y" : "q", ["n"] = 2, ["flag"] = true };
            Check(Conditions.Holds(body, vars) == holds, $"{body} is {holds}");
        }

        Check(Settled() && store.Subscriptions.Count == 0, "no message and no subscription left");
        Console.WriteLine(_failures == 0 ? "smoke: all passed" : $"smoke: {_failures} FAILED");
        return _failures;
    }
}
