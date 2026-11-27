using Kernel.Foundation.InMemory;
using Kernel.Native;
using Contract = Kernel.Foundation.Contracts;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

internal static class NativeDetachedHandleChecks
{
    private sealed record Creator(Message Message, bool WasPending, IReadOnlyList<string> ParentHandles);

    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
            await CheckAsync(mode, (passed, description) => check(passed, $"{mode}: {description}"));
    }

    private static async Task CheckAsync(string mode, Action<bool, string> check)
    {
        const long now = 1_702_310_000_000;
        var store = new InMemoryStore(1, () => now);
        var scope = ContractScopes.Bind("native-detached-handles");
        var ingress = new InMemoryIngress(store, new Requests("native-detached"), "native-detached",
            (bound, kind) => bound == scope && kind == Contract.Kinds.Terminate);
        var reads = new ActorList(store, 16);
        var runtime = new PublicRuntime(ingress, reads);
        var workflows = new WorkflowRegistry()
            .Add(Wf.Workflow("detached", Wf.Set("complete", "result", "independent")))
            .Add(Wf.Workflow("attached", Wf.Delay("waiting", 100_000)))
            .Add(Wf.Workflow("parent", Wf.Sequence("sequence",
                Wf.Parallel("first", null,
                    Wf.Dispatch("detach-one", "detached", wait: false),
                    Wf.Event("advance", "advance")),
                Wf.Parallel("second", null,
                    Wf.Dispatch("detach-two", "detached", wait: false),
                    Wf.Dispatch("attach", "attached", wait: true)))));
        var accepted = await runtime.StartAsync(scope, "start", NativeExecutionModel.Start("parent", mode));
        var parent = accepted.Actor!.Value;
        var address = store.FoundationTarget(scope.Tenant, parent.Id)!.Id;
        var creators = new List<Creator>();
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed)
                foreach (var message in batch.Turns.Where(turn => turn.Instance == address)
                    .SelectMany(turn => turn.Sent).Where(message => message.InboxScope == InboxKey.ChildCreatorScope))
                    creators.Add(new(message, store.Messages.ContainsKey(message.InboxKey), Handles()));
        });
        var actor = new PublicWorkflow([new NativeExecutionModel(workflows)]);
        await using (var original = await Own("detached-original"))
        {
            await original.WokenAsync(null);
            for (var i = 0; i < 25 && creators.Count == 0; i++) await original.RunAsync();
            check(creators is [{ Message.Start.Parent: null, WasPending: true }] &&
                creators[0].ParentHandles.Count == 0, "the first pending detached creator needs no parent checkpoint handle");
            check(creators.Count == 1 && store.Messages.ContainsKey(creators[0].Message.InboxKey) &&
                !store.Instances.ContainsKey(creators[0].Message.Target.Id), "the original owner leaves a durable pending creator");
        }

        await using var successor = await Own("detached-successor");
        await Drive(successor);
        check(creators.Count == 1 && store.Instances.GetValueOrDefault(creators[0].Message.Target.Id)?.Status == InstanceStatus.Finished &&
            !store.Messages.ContainsKey(creators[0].Message.InboxKey),
            "the successor consumes the pending creator and the detached child finishes independently");
        var resumed = await ingress.SendAsync(scope, new Contract.Target.Event("event:advance"), "advance", "published", "yes");
        check(resumed.Outcome == Contract.AdmissionOutcome.Accepted, "the retained parent wait accepts its next input after takeover");
        await Drive(successor);
        var detached = creators.Where(creator => creator.Message.Start?.Parent is null).ToArray();
        var boundChildren = creators.Where(creator => creator.Message.Start?.Parent == address).ToArray();
        check(detached.Length == 2 && detached.All(creator => creator.WasPending &&
                !store.Messages.ContainsKey(creator.Message.InboxKey) &&
                store.Instances.GetValueOrDefault(creator.Message.Target.Id) is { Status: InstanceStatus.Finished, Revision: 1 }),
            "after restart, both detached children have completed their creator turn once");
        check(detached.Length == 2 && detached.All(creator => !creator.ParentHandles.Contains(creator.Message.Id)) &&
                detached.All(creator => !Handles().Contains(creator.Message.Id)),
            "detached creators leave no parent handle either pending or after consumption");
        check(boundChildren.Length == 1 && boundChildren[0].WasPending &&
                boundChildren[0].ParentHandles.Contains(boundChildren[0].Message.Id) &&
                Handles().Contains(boundChildren[0].Message.Id) &&
                ChildRegistry.Read(store.Instances[address].Children).Select(entry => entry.Id)
                    .SequenceEqual([boundChildren[0].Message.Target.Id]),
            "the pending attached creator and registry keep their existing semantics");

        var selected = await reads.GetAsync(scope, parent);
        if (selected is null) throw new InvalidOperationException("The parent never landed.");
        var terminate = await runtime.TerminateAsync(scope, "terminate", selected);
        await Drive(successor);
        check(terminate.Outcome == Contract.AdmissionOutcome.Accepted &&
                store.Instances[address].Status == InstanceStatus.Cancelled &&
                boundChildren.Length == 1 && store.Instances.GetValueOrDefault(boundChildren[0].Message.Target.Id)?.Status == InstanceStatus.Cancelled &&
                detached.Length == 2 && detached.All(creator => store.Instances[creator.Message.Target.Id].Status == InstanceStatus.Finished),
            "qualified termination still cancels the attached subtree and leaves detached results intact");

        IReadOnlyList<string> Handles() => store.Instances.GetValueOrDefault(address)?.CustomHandles is { } json
            ? DurableKeyJson.ReadKeys(json) : [];

        async Task<IScopeActivation> Own(string node)
        {
            var worker = new Worker(node, 0, wire, new ActorAdapter(store, actor),
                new WorkerOptions { Monotonic = () => 0, PassTurnBudget = 1 });
            return await new DurableHost([worker]).ActivateAsync(0) ?? throw new InvalidOperationException("The partition is already owned.");
        }

        static async Task Drive(IScopeActivation owner)
        {
            await owner.WokenAsync(null);
            for (var i = 0; i < 25; i++) await owner.RunAsync();
        }
    }
}
