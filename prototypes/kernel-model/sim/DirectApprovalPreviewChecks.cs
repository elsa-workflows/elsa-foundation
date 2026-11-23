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
        await Run(check, false);
        await Run((ok, name) => check(ok, "ordinary-root dispatch: " + name), true);
    }

    private static async Task Run(Action<bool, string> check, bool useRootHeaders)
    {
        const long now = 1_792_000_000_000;
        var store = new InMemoryStore(1, () => now);
        var headers = new RootHeaders(store);
        var writes = new List<TurnCommit>();
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed) writes.AddRange(batch.Turns.Where(turn => turn.Row is not null));
        });
        var scope = new Contract.BoundScope("preview");
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
            writes[0].Consumed.Count == 1 && writes[0].FoundationHeader?.Labels.GetValueOrDefault("version") == "14" &&
            writes[0].FoundationHeader?.Labels.GetValueOrDefault("definition") == DirectApprovalWorkflow.Definition,
            "public Native start consumes its exact creator and commits the open frame, workflow header and journal");
        check(frame.Length > 0 && journal.Items.All(entry => entry.Revision == 1) && store.Subscriptions.Count == 0,
            "the caller obtains the direct signal's frame from the public journal without an event registration");

        var firstApproval = await Send("approval-1", "Ada");
        await Pump(first);
        var afterFirst = await reads.GetAsync(scope, reference);
        check(firstApproval.Outcome == Contract.AdmissionOutcome.Accepted && afterFirst?.Header.Status == Contract.ActorStatus.Running &&
            writes.Count == 2 && writes[1] is { Expected: 1, Row.Revision: 2 } && writes[1].Done.Count == 1 &&
            State().Frames[frame].State!.AsArray().Single()!.GetValue<string>() == "Ada",
            "first approval stores one value and marks the retained exact input done at revision two");
        var before = (await reads.TailAsync(scope, reference)).Items.ToArray();
        await first.DisposeAsync();

        if (useRootHeaders)
        {
            // A provider loads its persisted header together with the checkpoint. Drop the owner's
            // transient projection so the cold dispatch has to ask for the actual stored revision.
            var checkpoint = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row;
            store.Instances[checkpoint.Id] = checkpoint with { FoundationDispatchHeader = null };
        }
        await using var next = await Activate("successor");
        var duplicate = await Send("approval-1", "replacement");
        await Pump(next);
        check(duplicate.Outcome == Contract.AdmissionOutcome.Duplicate && writes.Count == 2 &&
            (await reads.TailAsync(scope, reference)).Items.SequenceEqual(before) &&
            State().Frames[frame].State!.AsArray().Single()!.GetValue<string>() == "Ada",
            "a cold owner ignores replay of the original operation and preserves its first value and journal");

        var secondApproval = await Send("approval-2", "Grace");
        await Pump(next);
        var ended = await reads.GetAsync(scope, reference);
        var result = State().Results["approve"]!.AsArray().Select(v => v!.GetValue<string>());
        check(secondApproval.Outcome == Contract.AdmissionOutcome.Accepted && ended?.Header is { Status: Contract.ActorStatus.Finished, EndedAt: now } &&
            result.SequenceEqual(["Ada", "Grace"]) && writes.Count == 3 && writes[2] is { Expected: 2, Row.Revision: 3, Unsubscribe: true } &&
            writes[2].Done.Count == 1, "second approval finishes once, marks its retained input done and records its terminal timestamp");
        check(writes.All(t => t.Sent.Count == 0 && t.Subscribed.Count == 0 && t.Unsubscribed.Count == 0 && t.Held is null or { Count: 0 } &&
            t.FoundationCascade is null) && store.Subscriptions.Count == 0,
            "the three authored turns emit no timers, jobs, child starts, subscriptions, broadcasts or held inputs");
        var terminalJournal = await reads.TailAsync(scope, reference);
        var history = terminalJournal.Items.Select(entry => Json.Read<string>(entry.Entry)).ToList();
        check(history.Count(entry => entry == "approved:approval-1") == 1 && history.Count(entry => entry == "approved:approval-2") == 1 &&
            history.Last() == "end" && history.Count(entry => entry == "end") == 1 && writes[2].Journal is not null, "the public journal records each accepted approval and one ending");

        if (useRootHeaders)
        {
            check(headers.Starts == 1 && headers.LoadedRevisions.Contains(2),
                "the first turn uses the admitted public reference and the cold owner loads its durable revision-two header");

            var unsupported = await runtime.StartAsync(scope, "needs-event", NativeExecutionModel.Start("event-wait"));
            await Pump(next);
            var rejected = await reads.GetAsync(scope, unsupported.Actor!.Value);
            check(rejected?.Header is { Status: Contract.ActorStatus.Faulted, Fault: { } fault } &&
                fault.Contains("ordinary roots", StringComparison.Ordinal) && store.Subscriptions.Count == 0 &&
                writes[^1] is { OpensPark: true, Row.Status: InstanceStatus.Faulted, Subscribed.Count: 0 },
                "a real event wait yields a Park outcome without committing a partial authored checkpoint or subscription");
        }

        NativeState State() => Json.Read<NativeState>(store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row.State);

        ValueTask<Contract.Admission> Send(string operation, string value) => ingress.SendAsync(scope, new Contract.Target.Ref(reference),
            operation, CompositeSignal.Kind, Json.Write(new CompositeSignal(frame, "approve", value)));

        async Task<IScopeActivation> Activate(string node)
        {
            var registry = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build())
                .Add(Wf.Workflow("event-wait", Wf.Event("wait", "event")));
            var actor = new WorkflowActorType([new NativeExecutionModel(registry)]);
            var worker = new Worker(node, 0, wire, (useRootHeaders ? new ActorAdapter(headers, actor) : new ActorAdapter(store, actor)), new WorkerOptions { Monotonic = () => 0 });
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

    private sealed class RootHeaders(InMemoryStore store) : IRootDispatchHeaders
    {
        internal int Starts;
        internal readonly List<long> LoadedRevisions = [];

        public (string Tenant, string Reference) Creator(Message creator)
        {
            Starts++;
            return store.FoundationIdentity(creator.Target.Id, creator.TargetCreation!.Value);
        }

        public FoundationDispatchHeader Checkpoint(InstanceRow row)
        {
            var (tenant, reference) = store.FoundationIdentity(row.Id, row.Creation);
            var checkpoint = store.ReadFoundationActor(tenant, reference).Actor!;
            if (checkpoint.Row.Revision != row.Revision) throw new InvalidOperationException("Unexpected checkpoint revision.");
            LoadedRevisions.Add(row.Revision);
            return new(tenant, reference, checkpoint.Header);
        }
    }
}
