using Kernel.Foundation.InMemory;
using Kernel.Native;
using Kernel.Native.Preview;
using Contract = Kernel.Foundation.Contracts;
using WorkflowActorType = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

internal static class DirectApprovalPreviewChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long now = 1_792_000_000_000;
        var store = new InMemoryStore(1, () => now);
        var writes = new List<TurnCommit>();
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed) writes.AddRange(batch.Turns.Where(turn => turn.Row is not null));
        });
        var scope = ContractScopes.Bind("preview");
        var ingress = new InMemoryIngress(store, new Requests("direct-preview"), "direct-preview");
        var reads = new ActorList(store, 32);
        var runtime = new Kernel.WorkflowActor.WorkflowRuntime(ingress, reads);
        await using var first = await Activate("first");
        var admitted = await runtime.StartAsync(scope, "birth", NativeExecutionModel.Start(DirectApprovalWorkflow.Definition));
        var reference = admitted.Actor!.Value;
        await Pump(first);
        var journal = await reads.TailAsync(scope, reference);
        var wait = journal.Items.Select(entry => Json.Read<string>(entry.Entry))
            .Single(entry => entry.StartsWith(DirectApprovalWorkflow.WaitEntry, StringComparison.Ordinal));
        var frame = wait[DirectApprovalWorkflow.WaitEntry.Length..];
        check(writes.Count == 1 && writes[0] is { Expected: null, Row.Revision: 1, Row.StateVersion: 1, Row.Status: InstanceStatus.Running } &&
            writes[0].Consumed.Count == 1 && writes[0].FoundationHeader?.TypeData.Contains("\"Version\":14", StringComparison.Ordinal) == true &&
            writes[0].FoundationHeader?.Labels.GetValueOrDefault("definition") == DirectApprovalWorkflow.Definition,
            "public Native start consumes its exact creator and commits the open frame, workflow header and journal");
        check(frame.Length > 0 && journal.Items.All(entry => entry.Revision == 1) && store.Subscriptions.Count == 0,
            "the caller obtains the direct signal's frame from the public journal without an event registration");

        var firstApproval = await Send("approval-1", "Ada");
        await Pump(first);
        var afterFirst = await reads.GetAsync(scope, reference);
        check(firstApproval.Outcome == Contract.AdmissionOutcome.Accepted && firstApproval.Actor == reference &&
            afterFirst?.Header.Status == Contract.ActorStatus.Running &&
            writes.Count == 2 && writes[1] is { Expected: 1, Row.Revision: 2 } && writes[1].Done.Count == 1 &&
            State().Frames[frame].State!.AsArray().Single()!.GetValue<string>() == "Ada",
            "first approval stores one value and marks the retained exact input done at revision two");
        var before = (await reads.TailAsync(scope, reference)).Items.ToArray();
        await first.DisposeAsync();

        await using var next = await Activate("successor");
        ingress = new InMemoryIngress(store, new Requests("direct-preview-successor"), "direct-preview-successor");
        var duplicate = await Send("approval-1", "replacement");
        await Pump(next);
        check(duplicate.Outcome == Contract.AdmissionOutcome.Duplicate && duplicate.Actor == reference && writes.Count == 2 &&
            (await reads.TailAsync(scope, reference)).Items.SequenceEqual(before) &&
            State().Frames[frame].State!.AsArray().Single()!.GetValue<string>() == "Ada",
            "a cold owner ignores replay of the original operation and preserves its first value and journal");

        var secondApproval = await Send("approval-2", "Grace");
        await Pump(next);
        var ended = await reads.GetAsync(scope, reference);
        var result = State().Results["approve"]!.AsArray().Select(v => v!.GetValue<string>());
        check(secondApproval.Outcome == Contract.AdmissionOutcome.Accepted && secondApproval.Actor == reference &&
            ended?.Header is { Status: Contract.ActorStatus.Finished, EndedAt: now } &&
            result.SequenceEqual(["Ada", "Grace"]) && writes.Count == 3 && writes[2] is { Expected: 2, Row.Revision: 3, Unsubscribe: true } &&
            writes[2].Done.Count == 1, "second approval finishes once, marks its retained input done and records its terminal timestamp");
        check(writes.All(t => t.Sent.Count == 0 && t.Subscribed.Count == 0 && t.Unsubscribed.Count == 0 && t.Held is null or { Count: 0 } &&
            t.FoundationCascade is null) && store.Subscriptions.Count == 0,
            "the three authored turns emit no timers, jobs, child starts, subscriptions, broadcasts or held inputs");
        var terminalJournal = await reads.TailAsync(scope, reference);
        var history = terminalJournal.Items.Select(entry => Json.Read<string>(entry.Entry)).ToList();
        check(history.Count(entry => entry == "approved:approval-1") == 1 && history.Count(entry => entry == "approved:approval-2") == 1 &&
            history.Last() == "end" && history.Count(entry => entry == "end") == 1 && writes[2].Journal is not null, "the public journal records each accepted approval and one ending");

        NativeState State() => Json.Read<NativeState>(store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row.State);

        ValueTask<Contract.Admission> Send(string operation, string value) => ingress.SendAsync(scope, new Contract.Target.Ref(reference),
            operation, CompositeSignal.Kind, Json.Write(new CompositeSignal(frame, "approve", value)));

        async Task<IScopeActivation> Activate(string node)
        {
            var registry = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build());
            var actor = new WorkflowActorType([new NativeExecutionModel(registry)]);
            var worker = new Worker(node, 0, wire, new ActorAdapter(store, actor), new WorkerOptions { Monotonic = () => 0 });
            return (await new DurableHost([worker]).ActivateAsync(0))!;
        }

        static async Task Pump(IScopeActivation activation)
        {
            for (var pass = 0; pass < 10; pass++)
            {
                await activation.WokenAsync(null);
                await activation.RunAsync();
            }
        }
    }
}
