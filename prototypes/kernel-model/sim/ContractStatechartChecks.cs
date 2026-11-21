using System.Text.Json;
using Kernel.Foundation.InMemory;
using Kernel.Statecharts;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

internal static class ContractStatechartChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long startAt = 1_700_600_000_000;
        var now = startAt;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        var ingress = new OrdinaryIngress(store);
        Contract.IActorReads reader = new ActorList(store, 32);
        var definition = new Definition(new Region("draft",
            [new("draft", Entry: "mark", Exit: "mark"), new("review", Entry: "mark"), new("completed", Entry: "mark", Final: true)]),
            [new("draft", "submit", "review", "mark", When: "approved"), new("review", "complete", "completed")]);
        StatechartModel NewModel() => new(
            new Dictionary<string, Definition> { ["approval"] = definition },
            new Dictionary<string, Action<Models.ITurn, Step>> { ["mark"] = Mark });

        async Task<IScopeActivation> Owner(string node)
        {
            var adapter = new ActorAdapter(store, new PublicWorkflow([NewModel()]));
            var worker = new Worker(node, 0, store, adapter, new WorkerOptions { Monotonic = () => now - startAt });
            return (await new DurableHost([worker]).ActivateAsync(0))!;
        }

        var modelReferences = typeof(StatechartModel).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();
        check(modelReferences.Contains("Kernel.Model.Contracts") &&
            modelReferences.All(name => name is "Kernel.Foundation.Contracts" or "Kernel.Model.Contracts" or "netstandard" ||
                name?.StartsWith("System.", StringComparison.Ordinal) == true),
            "the compiled statechart model depends only on the public contracts and framework");

        var start = PublicWorkflow.Start(new(NewModel().Key, "approval"));
        var admitted = await ingress.SendAsync(scope, new Contract.Target.Start(start), "chart-start");
        var reference = admitted.Actor!.Value;
        var exact = new Contract.Target.Ref(reference);
        var owner = await Owner("chart-owner");
        await Pump(owner);
        check(await reader.GetAsync(scope, reference) is { Header: { Type: PublicWorkflow.ActorType, Status: Contract.ActorStatus.Running } } view &&
            view.Header.Labels["definition"] == "approval" && Active() == "draft",
            "the public workflow actor and compiled chart open the actor through the real Worker");

        await ingress.SendAsync(scope, exact, "wrong-guard", "submit", "denied");
        await Pump(owner);
        check(Active() == "draft", "the statechart's transition guard blocks an ineligible message through the adapter");

        await ingress.SendAsync(scope, exact, "submit", "submit", "approved");
        var duplicate = await ingress.SendAsync(scope, exact, "submit", "submit", "approved");
        await Pump(owner);
        check(duplicate.Outcome == Contract.AdmissionOutcome.Duplicate && Active() == "review",
            "the exact admitted message advances the compiled chart once");
        await owner.DisposeAsync();

        now++;
        await using var next = await Owner("chart-successor");
        await ingress.SendAsync(scope, exact, "complete", "complete");
        await Pump(next);
        var tail = await reader.TailAsync(scope, reference);
        var steps = tail.Items.Where(entry => entry.Entry.Contains("\"activity\"", StringComparison.Ordinal))
            .Select(entry => Json.Read<ActivityEntry>(entry.Entry)).ToArray();
        check(Active() == "completed" &&
            await reader.GetAsync(scope, reference) is { Header: { Status: Contract.ActorStatus.Finished, EndedAt: var at } } && at == now &&
            steps.Select(step => $"{step.Phase}:{step.State}").SequenceEqual(
                ["entry:draft", "exit:draft", "transition:draft", "entry:review", "entry:completed"]) &&
            steps.All(step => step.Ref == reference.Id),
            "the replacement owner resumes the chart and commits one ordered history of activity calls using only the exact ref");

        string Active()
        {
            using var json = JsonDocument.Parse(store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row.State);
            return json.RootElement.GetProperty("Active")[0].GetString()!;
        }
    }

    private sealed record ActivityEntry(string Kind, string Phase, string State, string Ref);

    private static void Mark(Models.ITurn turn, Step step) =>
        turn.Record(new ActivityEntry("activity", step.Phase, step.State, turn.Instance.Ref.Id));

    private static async Task Pump(IScopeActivation activation)
    {
        for (var step = 0; step < 20; step++)
        {
            await activation.WokenAsync(null);
            await activation.RunAsync();
            await Task.Yield();
        }
    }
}
