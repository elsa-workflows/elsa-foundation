using System.Text.Json.Nodes;
using Bpmn.Model.State;

namespace Kernel.Bpmn.Run;

/// <summary>The BPMN cases on the in-memory store, one owner.</summary>
public static class Smoke
{
    private static int _failures;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
        if (!ok) _failures++;
    }

    public static ProcessRegistry Registry()
    {
        var p = new ProcessDraft("smoke-task");
        p.B.StartEvent("start").ServiceTask("task", bindingRef: "quote").EndEvent("end");
        p.Chain("start", "task", "end");
        return new ProcessRegistry()
            .Add(new BpmnProcess(p.Build(), new Dictionary<string, Work> { ["quote"] = new JobWork("quote") }))
            .Service("quote", v => new ServiceResult(new JsonObject { ["quote"] = $"quote for {v["order"]}" }));
    }

    public static async Task<int> RunAsync()
    {
        var store = new InMemoryStore(1, () => Environment.TickCount64);
        var owner = new Owner("n1", 0, store, Registry());
        await owner.StartAsync();

        Console.WriteLine("a service task is a job");
        var id = InstanceRef.In(0, "task");
        owner.Worker.Post(new Message("m1", id, Kinds.Start, Start: BpmnModel.Spec("smoke-task", new JsonObject { ["order"] = 7 })), fresh: true);
        await owner.PumpAsync(() => owner.Status(id.Id) == InstanceStatus.Finished);
        var state = owner.State(id.Id)!;
        Check(state.Variables["quote"]?.GetValue<string>() == "quote for 7", $"the job's result is a variable: {state.Variables.GetValueOrDefault("quote")}");
        Check(state.Live.Count == 0 && state.Tokens.Tokens.All(t => t.Status is BpmnTokenStatus.Consumed or BpmnTokenStatus.Canceled), "no live work and no live token left");
        Check(store.Messages.Values.All(m => m.Done) && store.Subscriptions.Count == 0, "no message and no subscription left");
        var journal = store.Journal.Values.Where(j => j.Instance == id.Id).OrderBy(j => j.Position).SelectMany(j => Json.Read<List<string>>(j.Entries)).ToList();
        Check(journal.Any(e => e.StartsWith("w:", StringComparison.Ordinal) && e.Contains(":job:")) && journal[^1] == "end:Done", $"journal: {string.Join(" ", journal)}");

        Console.WriteLine(_failures == 0 ? "smoke: all passed" : $"smoke: {_failures} FAILED");
        return _failures;
    }
}
