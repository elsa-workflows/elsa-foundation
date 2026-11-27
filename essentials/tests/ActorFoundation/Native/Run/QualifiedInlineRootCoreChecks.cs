using Kernel.Durable;
using Kernel.Runtime;
using Contract = Kernel.Foundation.Contracts;
using WorkflowActorType = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class QualifiedInlineRootCoreChecks
{
    internal static async Task<int> RunAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        long now = 10_000;
        var store = new InMemoryStore(1, () => now);
        var options = new WorkerOptions { Monotonic = () => now };
        var key = new byte[32];
        var self = new FoundationSelfIssuer(seed => FoundationSelfSend.Handle(key, seed), SupportsTimers: true);
        var pure = Wf.Workflow("inline-finish", Wf.WriteLine("write", "selected body"));
        pure = new Workflow(pure.Id, pure.Root) { ExecutableSelection = "exact-material" };
        var alternate = Wf.Workflow(pure.Id, Wf.WriteLine("write", "wrong registry body"));
        alternate = new Workflow(alternate.Id, alternate.Root) { ExecutableSelection = pure.ExecutableSelection };
        var waiting = Wf.Workflow("inline-wait", Wf.Sequence("sequence", Wf.WriteLine("write", "waiting once"), Wf.Event("event", "finish")));
        waiting = new Workflow(waiting.Id, waiting.Root) { ExecutableSelection = "exact-wait-material" };
        var shortWait = Wf.Workflow("inline-short", Wf.Sequence("sequence", Wf.Delay("delay", 100), Wf.WriteLine("write", "short wait finished")));
        shortWait = new Workflow(shortWait.Id, shortWait.Root) { ExecutableSelection = "exact-short-wait" };
        var model = new NativeExecutionModel(new WorkflowRegistry().Add(alternate).Add(waiting).Add(shortWait));
        var actor = new WorkflowActorType([model]);

        using var trace = new StringWriter();
        var previous = WriteLine.Out;
        WriteLine.Out = trace;
        try
        {
            var ordinary = new Worker("ordinary", 0, store, new ActorAdapter(new InlineHeaders(Enabled: false), actor), options);
            await ordinary.TakeAsync(token);
            var excluded = Creator(pure);
            Check(ordinary.TryQueueQualifiedInlineStart(excluded, model.CaptureInlineMaterial(pure)) is null,
                "a default qualified owner refuses local Ephemeral starts");
            await ordinary.ReleaseAsync(token);

            var owner = new Worker("inline", 0, store, new ActorAdapter(new InlineHeaders(Enabled: true), actor, self), options);
            await owner.TakeAsync(token);
            var fresh = Creator(pure);
            var completed = Queue(owner, fresh, pure);
            Check(!completed.IsCompleted, "queue acknowledgement does not claim a committed start");
            Check(owner.TryQueueQualifiedInlineStart(fresh, model.CaptureInlineMaterial(pure)) is null,
                "an exact pending creator cannot be queued twice");
            Check(await SettleAsync(owner, completed) == new InlineRootResult(InlineRootDisposition.WriteFree, InstanceStatus.Finished),
                "the selected first turn reports a write-free end");
            Check(store.Instances.Count == 0 && store.Messages.Count == 0 && store.Journal.Count == 0 &&
                store.FoundationTarget("ephemeral-fixture", fresh.FoundationRootBinding!.Value.Reference) is null,
                "a write-free run leaves no actor identity, inbox, checkpoint or journal");
            Check(trace.ToString().Contains("selected body", StringComparison.Ordinal) &&
                !trace.ToString().Contains("wrong registry body", StringComparison.Ordinal),
                "the admitted local material wins over the registry");

            var delayed = Creator(shortWait);
            var shortReceipt = Queue(owner, delayed, shortWait);
            while (!owner.Unwritten.Contains(delayed.Target.Id) && !shortReceipt.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                await owner.RunAsync(token);
            }
            Check(!shortReceipt.IsCompleted && store.Instances.Count == 0 && store.Messages.Count == 0,
                "a one-way short timer waits in memory without completing its receipt");
            now += 100;
            Check(await SettleAsync(owner, shortReceipt) == new InlineRootResult(InlineRootDisposition.WriteFree, InstanceStatus.Finished) &&
                store.Instances.Count == 0 && store.Messages.Count == 0 && store.Journal.Count == 0,
                "the authenticated short timer continues and ends without storage");

            var yielding = Creator(waiting);
            Check(await SettleAsync(owner, Queue(owner, yielding, waiting)) == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Running),
                "a subscription reports a durable continuation only after its first commit");
            var exact = yielding.FoundationRootBinding!.Value.Reference;
            Check(store.FoundationTarget("ephemeral-fixture", exact)?.Id == yielding.Target.Id &&
                store.FoundationTarget("other-tenant", exact) is null &&
                store.Instances[yielding.Target.Id].Status == InstanceStatus.Running &&
                store.Subscriptions.SingleOrDefault() is { } wait && wait.Instance == yielding.Target.Id &&
                wait.FoundationTenant == "ephemeral-fixture" &&
                trace.ToString().Split("waiting once", StringSplitOptions.None).Length == 2,
                "promotion retains the original actor, tenant, subscription and completed work");

            var lost = Queue(owner, Creator(pure), pure);
            await owner.ReleaseAsync(token);
            Check(await lost.WaitAsync(token) == new InlineRootResult(InlineRootDisposition.OwnershipLost),
                "shutdown settles a queued start whose owner was lost");
            return 0;
        }
        finally { WriteLine.Out = previous; }

        Message Creator(Workflow workflow)
        {
            const string tenant = "ephemeral-fixture";
            var spec = WorkflowActorType.Start(NativeExecutionModel.Start(workflow, Modes.Ephemeral));
            var operation = Guid.NewGuid().ToString("N");
            var address = InstanceRef.In(0, tenant + "_" + operation);
            var reference = store.ReferenceFor(tenant, new(address.Id, 1));
            return FoundationRootCreator.Create(address, operation, FoundationRootCreator.OperationDigest(operation),
                new StartSpec(spec.Type, spec.TypeData, spec.Input, Ephemeral: spec.Ephemeral), null, null)
                with { FoundationRootBinding = (address.Id, tenant, reference) };
        }

        async Task<InlineRootResult> SettleAsync(Worker worker, Task<InlineRootResult> completion)
        {
            while (!completion.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                await worker.RunAsync(token);
                if (!completion.IsCompleted) await Task.Delay(5, token);
            }
            return await completion;
        }
        Task<InlineRootResult> Queue(Worker owner, Message creator, Workflow material) =>
            owner.TryQueueQualifiedInlineStart(creator, model.CaptureInlineMaterial(material))
            ?? throw new InvalidOperationException("The test owner could not queue its qualified creator.");
    }

    private sealed record InlineHeaders(bool Enabled) : IRootDispatchHeaders
    {
        public bool SupportsInlineStarts => Enabled;
        public bool RequiresCommittedTerminal => false;
        public bool KeepsParkInputs => false;
        public bool KeepsParkArrivals => false;
        public bool KeepsPrivateParkInputs => false;
        public bool RecoversPark => false;
        public bool TakesLiveEventWaits => true;
        public bool RefusesParkedOperators => false;
        public bool SettlesEndedMailbox => true;

        public (string Tenant, string Reference) Creator(Message creator) => creator.FoundationRootBinding is { } binding
            ? (binding.Tenant, binding.Reference) : throw new InvalidOperationException("Missing qualified creator.");
        public FoundationDispatchHeader Checkpoint(InstanceRow row) =>
            row.FoundationDispatchHeader ?? throw new InvalidOperationException("Missing qualified checkpoint header.");
    }

    private static void Check(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }
}
