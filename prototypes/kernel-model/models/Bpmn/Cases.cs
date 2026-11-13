using System.Text.Json.Nodes;
using Bpmn.Model;

namespace Kernel.Bpmn;

/// <summary>
/// The spike's cases (BPMN.md, section 3): the three gateways, a service task as a job, an interrupting boundary timer
/// that cancels it, a message published before its catch, a call activity, and a loop whose body forks and joins. The
/// smoke test, the cost measurement and the simulation run these processes.
/// </summary>
public static class BpmnCases
{
    public const int Deadline = 300;      // the boundary timers, in milliseconds (the simulation's clock is its own)
    public const int Rounds = 3;          // the loop's rounds

    /// <param name="deadline">The boundary timers: on a real clock, a longer one keeps a race the job should win from going to the timer under load.</param>
    public static ProcessRegistry Registry(int deadline = Deadline)
    {
        var registry = new ProcessRegistry();
        foreach (var process in Processes(deadline)) registry.Add(process);
        return registry
            .Service("a", _ => Set(("a", "A")))
            .Service("b", _ => Set(("b", "B")))
            .Service("big", _ => Set(("size", "big")))
            .Service("medium", _ => Set(("size", "medium")))
            .Service("small", _ => Set(("size", "small")))
            .Service("express", _ => Set(("express-done", true)))
            .Service("insured", _ => Set(("insured-done", true)))
            .Service("standard", _ => Set(("standard-done", true)))
            .Service("slow", _ => Set(("slow-done", true)))
            .Service("escalate", _ => Set(("escalated", true)))
            .Service("prepare", v => Set(("prepared", v["order"]?.DeepClone())))
            .Service("child", v => Set(("child-out", $"child of {v["order"]}")))
            .Service("count", v => Set(("count", (v["count"]?.GetValue<int>() ?? 0) + 1)))
            .Service("note", v => Set(("notes", (v["notes"]?.GetValue<int>() ?? 0) + 1)));
    }

    private static ServiceResult Set(params (string Name, JsonNode? Value)[] values) =>
        new(new JsonObject(values.Select(v => KeyValuePair.Create(v.Name, v.Value))));

    private static IEnumerable<BpmnProcess> Processes(int deadline)
    {
        // Parallel: a fork into two service tasks and a join.
        var p = new ProcessDraft("b-parallel");
        p.B.StartEvent("start").ParallelGateway("fork").ServiceTask("ta", bindingRef: "a").ServiceTask("tb", bindingRef: "b").ParallelGateway("join").EndEvent("end");
        p.Chain("start", "fork", "ta", "join", "end").Chain("fork", "tb", "join");
        yield return new BpmnProcess(p.Build(), Jobs("a", "b"));

        // Exclusive: the first flow whose condition holds (above 100 both do), else the default.
        p = new ProcessDraft("b-exclusive");
        p.B.StartEvent("start").ExclusiveGateway("xor").ServiceTask("big", bindingRef: "big").ServiceTask("medium", bindingRef: "medium").ServiceTask("small", bindingRef: "small")
            .ExclusiveGateway("merge").EndEvent("end");
        p.Flow("start", "xor").Flow("xor", "big", when: "amount > 100").Flow("xor", "medium", when: "amount > 50").Flow("xor", "small", isDefault: true)
            .Chain("big", "merge", "end").Flow("medium", "merge").Flow("small", "merge");
        yield return new BpmnProcess(p.Build(), Jobs("big", "medium", "small"), new JsonObject { ["amount"] = 0 });

        // Inclusive: every flow whose condition holds, else the default; the join waits only for what can still arrive.
        p = new ProcessDraft("b-inclusive");
        p.B.StartEvent("start").InclusiveGateway("or").ServiceTask("express", bindingRef: "express").ServiceTask("insured", bindingRef: "insured")
            .ServiceTask("standard", bindingRef: "standard").InclusiveGateway("or-join").EndEvent("end");
        p.Flow("start", "or").Flow("or", "express", when: "express").Flow("or", "insured", when: "insured").Flow("or", "standard", isDefault: true)
            .Chain("express", "or-join", "end").Flow("insured", "or-join").Flow("standard", "or-join");
        yield return new BpmnProcess(p.Build(), Jobs("express", "insured", "standard"), new JsonObject { ["express"] = false, ["insured"] = false });

        // An interrupting boundary timer on a service task: when it fires first, the job is cancelled and the timeout path runs.
        p = new ProcessDraft("b-boundary");
        p.B.StartEvent("start").ServiceTask("slow", bindingRef: "slow").EndEvent("done")
            .BoundaryEvent("deadline", attachedTo: "slow", new BpmnEventDefinition(BpmnEventDefinitionTypes.Timer), interrupting: true, bindingRef: "deadline")
            .ServiceTask("escalate", bindingRef: "escalate").EndEvent("timed-out");
        p.Chain("start", "slow", "done").Chain("deadline", "escalate", "timed-out");
        yield return new BpmnProcess(p.Build(), With(Jobs("slow", "escalate"), ("deadline", new TimerWork(deadline))));

        // A message catch after a service task: a message published before the catch subscribes is kept and still counts.
        p = new ProcessDraft("b-early");
        p.B.StartEvent("start").ServiceTask("prepare", bindingRef: "prepare")
            .IntermediateCatchEvent("paid", new BpmnEventDefinition(BpmnEventDefinitionTypes.Message), bindingRef: "paid").EndEvent("end");
        p.Chain("start", "prepare", "paid", "end");
        yield return new BpmnProcess(p.Build(), With(Jobs("prepare"), ("paid", new MessageWork("paid", "order"))));

        // A call activity: a child instance of b-child; its variables come back. A boundary timer on it terminates the child.
        p = new ProcessDraft("b-call");
        p.B.StartEvent("start").CallActivity("call", "b-child", bindingRef: "child").EndEvent("end")
            .BoundaryEvent("call-deadline", attachedTo: "call", new BpmnEventDefinition(BpmnEventDefinitionTypes.Timer), interrupting: true, bindingRef: "call-deadline")
            .EndEvent("late");
        p.Chain("start", "call", "end").Chain("call-deadline", "late");
        yield return new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["child"] = new CallWork("b-child"), ["call-deadline"] = new TimerWork(deadline) });
        p = new ProcessDraft("b-child");
        p.B.StartEvent("start").ServiceTask("work", bindingRef: "child-work").EndEvent("end");
        p.Chain("start", "work", "end");
        yield return new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["child-work"] = new JobWork("child") });

        // A loop: a cycle through an exclusive merge, a fork into two service tasks, a join and a decision. The join fires once per round.
        p = new ProcessDraft("b-loop");
        p.B.StartEvent("start").ExclusiveGateway("again").ParallelGateway("fork").ServiceTask("count", bindingRef: "count").ServiceTask("note", bindingRef: "note")
            .ParallelGateway("join").ExclusiveGateway("decide").EndEvent("end");
        p.Chain("start", "again", "fork", "count", "join", "decide").Chain("fork", "note", "join")
            .Flow("decide", "again", when: $"count < {Rounds}").Flow("decide", "end", isDefault: true);
        yield return new BpmnProcess(p.Build(), Jobs("count", "note"), new JsonObject { ["count"] = 0, ["notes"] = 0 });

        // Not in the simulation's mix: an inclusive join inside a loop, one flow taken, beside a parallel branch that waits
        // at a parallel join downstream. Bpmn.Semantics 0.5.0 faults it with bpmn.join.deadlock (BPMN.md, section 7).
        p = new ProcessDraft("b-inclusive-loop");
        p.B.StartEvent("start").ExclusiveGateway("again").ParallelGateway("fork").InclusiveGateway("or").ServiceTask("express", bindingRef: "express")
            .ServiceTask("insured", bindingRef: "insured").InclusiveGateway("or-join").ServiceTask("side", bindingRef: "note").ParallelGateway("join")
            .ServiceTask("count", bindingRef: "count").ExclusiveGateway("decide").EndEvent("end");
        p.Chain("start", "again", "fork", "or").Flow("or", "express", when: "express").Flow("or", "insured", when: "insured")
            .Chain("express", "or-join", "join").Chain("insured", "or-join").Chain("fork", "side", "join").Chain("join", "count", "decide")
            .Flow("decide", "again", when: $"count < {Rounds}").Flow("decide", "end", isDefault: true);
        yield return new BpmnProcess(p.Build(), Jobs("express", "insured", "note", "count"), new JsonObject { ["express"] = true, ["insured"] = false, ["count"] = 0, ["notes"] = 0 });
    }

    private static Dictionary<string, Work> Jobs(params string[] services) => services.ToDictionary(s => s, s => (Work)new JobWork(s), StringComparer.Ordinal);

    private static Dictionary<string, Work> With(Dictionary<string, Work> work, params (string Binding, Work Work)[] more)
    {
        foreach (var (binding, w) in more) work[binding] = w;
        return work;
    }
}
