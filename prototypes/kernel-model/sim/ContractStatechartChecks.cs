using System.Text.Json;
using Kernel.Foundation.InMemory;
using Kernel.Statecharts;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;

namespace Kernel.Sim;

internal static class ContractStatechartChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long startAt = 1_700_600_000_000;
        var now = startAt;
        var store = new InMemoryStore(1, () => now);
        var scope = ContractScopes.Bind("acme");
        var ingress = new OrdinaryIngress(store);
        Contract.IActorReads reader = new ActorList(store, 32);
        var edge = new InMemoryIngress(store, new Requests("chart-runtime"), "chart-runtime",
            (bound, kind) => bound == scope && kind == Contract.Kinds.Terminate);
        var runtime = new PublicRuntime(edge, new ActorList(store, 1));
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

        var activityAssembly = typeof(Kernel.Native.Delay).Assembly;
        var activityReferences = activityAssembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();
        check(typeof(Kernel.Native.WriteLine).Assembly == activityAssembly &&
            typeof(Kernel.Native.CallService).Assembly == activityAssembly &&
            typeof(Kernel.Native.SendHttpRequest).Assembly == activityAssembly &&
            typeof(Kernel.Native.SendEmail).Assembly == activityAssembly &&
            typeof(Kernel.Native.ActivityContext).GetProperty(nameof(Kernel.Native.ActivityContext.Instance))?.PropertyType == typeof(Contract.ActorRef) &&
            activityReferences.Contains("Kernel.Foundation.Contracts") &&
            activityReferences.All(name => name == "Kernel.Foundation.Contracts" || name?.StartsWith("System.", StringComparison.Ordinal) == true),
            "the compiled Native activities depend only on the foundation contract and framework and receive an opaque ref");

        var modelReferences = typeof(StatechartModel).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();
        check(modelReferences.Contains("Kernel.Model.Contracts") &&
            modelReferences.All(name => name is "Kernel.Foundation.Contracts" or "Kernel.Model.Contracts" or "netstandard" ||
                name?.StartsWith("System.", StringComparison.Ordinal) == true),
            "the compiled statechart model depends only on the public contracts and framework");

        var start = new Models.WorkflowStart(NewModel().Key, "approval");
        var admitted = await runtime.StartAsync(scope, "chart-start", start);
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

        var another = (await runtime.StartAsync(scope, "chart-list", start, callerKey: "review")).Actor!.Value;
        await Pump(next);
        var first = await runtime.ListAsync(scope, new());
        var later = first.Next is { } bookmark ? await runtime.ListAsync(scope, new(), bookmark) : first;
        check(first.Items.Count == 1 && later.Items.Count == 1 && later.Next is null &&
            first.Items.Concat(later.Items).Select(item => item.Header.Ref).ToHashSet().SetEquals([reference, another]),
            "the contract-only runtime pages through workflow refs using an opaque bookmark");

        var selected = first.Items.Concat(later.Items).Single(view => view.Header.Ref == another);
        var inbox = await runtime.InboxAsync(scope, selected.Header.Ref);
        check(inbox.Items is [{ Kind: "operator-timer", Handle.Length: > 0, Due: var timerDue }] && timerDue > now,
            "the runtime sees the opaque handle for a pending timer through the public inbox read");

        var cross = await runtime.TerminateAsync(ContractScopes.Bind("other"), "foreign-terminate", selected);
        var own = await runtime.TerminateAsync(scope, "terminate", selected);
        await Pump(next);
        check(cross.Outcome == Contract.AdmissionOutcome.Refused && own.Outcome == Contract.AdmissionOutcome.Accepted &&
            await reader.GetAsync(scope, another) is { Header.Status: Contract.ActorStatus.Cancelled },
            "the contract-only operator API sends the selected workflow's opaque ref and selection under the bound scope");

        string Active()
        {
            using var json = JsonDocument.Parse(store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row.State);
            return json.RootElement.GetProperty("Active")[0].GetString()!;
        }
    }

    private sealed record ActivityEntry(string Kind, string Phase, string State, string Ref);

    private static void Mark(Models.ITurn turn, Step step)
    {
        turn.Record(new ActivityEntry("activity", step.Phase, step.State, turn.Instance.Ref.Id));
        if (step is { Phase: "entry", State: "draft" })
            turn.Send(turn.Instance.Ref, "operator-timer", dueAt: turn.Now + 10_000);
    }

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
