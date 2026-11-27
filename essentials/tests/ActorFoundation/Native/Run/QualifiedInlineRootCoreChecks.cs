using System.Runtime.CompilerServices;
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
        var store = new InMemoryStore(2, () => now);
        var options = new WorkerOptions { Monotonic = () => now, Partitions = 2, MaxBatch = 2 };
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
        var permittedStarts = new[] { pure, waiting, shortWait }.ToDictionary(
            workflow => WorkflowActorType.Start(NativeExecutionModel.Start(workflow, Modes.Ephemeral)).TypeData,
            StringComparer.Ordinal);

        using var trace = new StringWriter();
        var previous = WriteLine.Out;
        WriteLine.Out = trace;
        try
        {
            var ordinary = new Worker("ordinary", 0, store, new ActorAdapter(new InlineHeaders(), actor), options);
            await ordinary.TakeAsync(token);
            var excluded = Creator(pure);
            Check(ordinary.TryQueueQualifiedInlineStart(excluded, model.CaptureInlineMaterial(pure)) is null,
                "a default qualified owner refuses local Ephemeral starts");
            await ordinary.ReleaseAsync(token);

            var owner = Worker.ForNode("inline", store, new ActorAdapter(new InlineHeaders(MatchesMaterial), actor, self), options);
            await owner.GainSetAsync([0, 1], token);
            var fresh = Creator(pure);
            var differentModel = new NativeExecutionModel(new WorkflowRegistry().Add(pure));
            Check(owner.TryQueueQualifiedInlineStart(fresh, new object()) is null &&
                owner.TryQueueQualifiedInlineStart(fresh, model.CaptureInlineMaterial(waiting)) is null &&
                owner.TryQueueQualifiedInlineStart(fresh, differentModel.CaptureInlineMaterial(pure)) is null &&
                owner.QueuedInlineRootCount == 0 && trace.ToString().Length == 0 && store.Instances.Count == 0,
                "invalid material is refused before queueing, registry fallback, or Park");
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

            var lost = Queue(owner, Creator(pure, partition: 1), pure);
            var retained = Queue(owner, Creator(pure), pure);
            Check(owner.QueuedInlineRootCount == options.MaxBatch &&
                owner.TryQueueQualifiedInlineStart(Creator(pure), model.CaptureInlineMaterial(pure)) is null,
                "the pending inline batch caps all queued captures");
            Check((await owner.LoseAsync([1], token)).SequenceEqual([1]) &&
                await lost.WaitAsync(token) == new InlineRootResult(InlineRootDisposition.OwnershipLost) &&
                !retained.IsCompleted && owner.QueuedInlineRootCount == 1,
                "partial handoff drops that partition's queued capture and receipt");
            Check(await owner.GainAsync(1, token), "the same owner can regain the released partition");
            var gained = Queue(owner, Creator(pure, partition: 1), pure);
            Check(owner.QueuedInlineRootCount == options.MaxBatch &&
                await SettleAsync(owner, retained) == new InlineRootResult(InlineRootDisposition.WriteFree, InstanceStatus.Finished) &&
                await SettleAsync(owner, gained) == new InlineRootResult(InlineRootDisposition.WriteFree, InstanceStatus.Finished),
                "retained and regained partitions can fill and drain the bounded batch");

            var shutdown = Queue(owner, Creator(pure), pure);
            await owner.ReleaseAsync(token);
            Check(await shutdown.WaitAsync(token) == new InlineRootResult(InlineRootDisposition.OwnershipLost),
                "shutdown settles a queued start whose owner was lost");

            await using var bounded = Worker.ForNode("bounded-inline", store, new ActorAdapter(new InlineHeaders(MatchesMaterial), actor, self),
                options with { MaxBatch = 4 });
            await bounded.GainSetAsync([0, 1], token);
            // Retain a full-sized creator without making its waiting actor state large as well.
            var largeWaiting = Creator(shortWait, input: Creator(shortWait).Payload!.PadRight(FoundationMetadataLimits.StartContentBytes));
            var waitingReceipt = Queue(bounded, largeWaiting, shortWait);
            while (!bounded.Unwritten.Contains(largeWaiting.Target.Id) && !waitingReceipt.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                await bounded.RunAsync(token);
            }
            Check(bounded.Unwritten.Contains(largeWaiting.Target.Id) && !waitingReceipt.IsCompleted,
                "the maximum-sized creator is retained through a short in-memory wait");
            var discarded = Queue(bounded, Creator(pure, partition: 1, input: MaximumInput()), pure);
            var replacement = Creator(pure, input: MaximumInput());
            Check(bounded.QueuedInlineRootCount == 1 &&
                bounded.TryQueueQualifiedInlineStart(replacement, model.CaptureInlineMaterial(pure)) is null,
                "a maximum legal input fits, but three such captures do not fit while one waits outside the queue");
            Check((await bounded.LoseAsync([1], token)).SequenceEqual([1]) &&
                await discarded.WaitAsync(token) == new InlineRootResult(InlineRootDisposition.OwnershipLost),
                "handover settles the queued maximum input without giving it a turn");
            var replacementReceipt = Queue(bounded, replacement, pure);
            now += 100;
            Check(await SettleAsync(bounded, replacementReceipt) == new InlineRootResult(InlineRootDisposition.WriteFree, InstanceStatus.Finished) &&
                await SettleAsync(bounded, waitingReceipt) == new InlineRootResult(InlineRootDisposition.WriteFree, InstanceStatus.Finished),
                "handover releases exactly the discarded bytes, and the retained short wait can still finish");

            Check(await bounded.GainAsync(1, token), "both placement partitions are now held");
            var pressure = RootAdmissionPressure.CreateDefault();
            var leftName = ForPartition("left", 0);
            var rightName = ForPartition("right", 1);
            var approved = Creator(pure).Start! with { Creation = null };
            Check(bounded.TryPlaceQualifiedInlineStart("changed-input", approved, model.CaptureInlineMaterial(pure), null,
                    pressure, target => Creator(pure, input: "{}", address: target)) is null &&
                bounded.QueuedInlineRootCount == 0,
                "placement cannot swap the input after the exact creator spec is validated");
            var left = Place(leftName, pure);
            var right = Place(rightName, pure);
            Check(left.Creator.Target.Partition == 0 && right.Creator.Target.Partition == 1 &&
                await SettleAsync(bounded, left.Receipt) == new InlineRootResult(InlineRootDisposition.WriteFree, InstanceStatus.Finished) &&
                await SettleAsync(bounded, right.Receipt) == new InlineRootResult(InlineRootDisposition.WriteFree, InstanceStatus.Finished),
                "the stable actor-name placement spreads over both healthy owned partitions");

            var handingOver = Place(ForPartition("handover", 1), shortWait, largeWaiting.Payload);
            while (!bounded.Unwritten.Contains(handingOver.Creator.Target.Id) && !handingOver.Receipt.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                await bounded.RunAsync(token);
            }
            (Message Creator, Task<InlineRootResult> Receipt)? duringHandover = null;
            store.Locking = _ =>
            {
                store.Locking = null;
                Check(bounded.Holds(1), "handover is committing the old partition's memory before dropping the fence");
                duringHandover = Place(ForPartition("prefer-leaving", 1), pure);
            };
            Check((await bounded.LoseAsync([1], token)).SequenceEqual([1]) &&
                await handingOver.Receipt.WaitAsync(token) == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Running) &&
                duringHandover is { Creator.Target.Partition: 0 } available &&
                await SettleAsync(bounded, available.Receipt) == new InlineRootResult(InlineRootDisposition.WriteFree, InstanceStatus.Finished),
                "a start whose preferred fence is still held but handing over queues on the remaining fence");

            var resetFirst = Queue(bounded, Creator(pure, input: MaximumInput()), pure);
            var resetSecond = Queue(bounded, Creator(pure, input: MaximumInput()), pure);
            Check(bounded.QueuedInlineRootCount == 2, "promotion frees the held maximum-sized capture");
            await bounded.ReleaseAsync(token);
            Check(await resetFirst.WaitAsync(token) == new InlineRootResult(InlineRootDisposition.OwnershipLost) &&
                await resetSecond.WaitAsync(token) == new InlineRootResult(InlineRootDisposition.OwnershipLost) &&
                await bounded.GainAsync(0, token) &&
                await SettleAsync(bounded, Queue(bounded, Creator(pure, input: MaximumInput()), pure)) ==
                    new InlineRootResult(InlineRootDisposition.WriteFree, InstanceStatus.Finished),
                "reset clears all capture reservations before reacquisition");

            var dormant = QueueDormantRoot();
            Check(await SettleAsync(bounded, dormant.Receipt) == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Running) &&
                store.Subscriptions.Any(subscription => subscription.Instance == dormant.Actor) && bounded.InboxCountOf(dormant.Actor) == 0,
                "the promoted actor is dormant with its committed wait still active");
            GC.Collect();
            Check(!dormant.Input.TryGetTarget(out _), "the dormant actor does not retain its original creator body after promotion");
            return 0;

            static string ForPartition(string name, int partition) => InstanceRef.Spread(name, 2) == partition ? name : name + "1";

            [MethodImpl(MethodImplOptions.NoInlining)]
            (string Actor, Task<InlineRootResult> Receipt, WeakReference<string> Input) QueueDormantRoot()
            {
                var input = Creator(waiting).Payload!.PadRight(FoundationMetadataLimits.StartContentBytes);
                var creator = Creator(waiting, input: input);
                return (creator.Target.Id, Queue(bounded, creator, waiting), new WeakReference<string>(input));
            }

            (Message Creator, Task<InlineRootResult> Receipt) Place(string name, Workflow selected, string? input = null)
            {
                var material = model.CaptureInlineMaterial(selected);
                var spec = Creator(selected, input: input).Start! with { Creation = null };
                return bounded.TryPlaceQualifiedInlineStart(name, spec, material, null, pressure,
                    target => Creator(selected, input: spec.Input, address: target)) ??
                    throw new InvalidOperationException("The owner could not place a qualified creator.");
            }
        }
        finally { WriteLine.Out = previous; }

        bool MatchesMaterial(StartSpec start, object material) =>
            start.Model == WorkflowActorType.ActorType && permittedStarts.TryGetValue(start.Definition, out var selected) &&
            model.MatchesInlineMaterial(material, selected.Id, selected.Definition, selected.ExecutableSelection);

        Message Creator(Workflow workflow, int partition = 0, string? input = null, InstanceRef? address = null)
        {
            const string tenant = "ephemeral-fixture";
            var spec = WorkflowActorType.Start(NativeExecutionModel.Start(workflow, Modes.Ephemeral));
            var operation = Guid.NewGuid().ToString("N");
            var target = address ?? InstanceRef.In(partition, tenant + "_" + operation);
            var reference = store.ReferenceFor(tenant, new(target.Id, 1));
            return FoundationRootCreator.Create(target, operation, FoundationRootCreator.OperationDigest(operation),
                new StartSpec(spec.Type, spec.TypeData, input ?? spec.Input, Ephemeral: spec.Ephemeral), null, null)
                with { FoundationRootBinding = (target.Id, tenant, reference) };
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

    internal static string MaximumInput()
    {
        const string prefix = "{\"Mode\":\"Ephemeral\",\"Input\":{\"padding\":\"";
        const string suffix = "\"}}";
        return string.Create(FoundationMetadataLimits.StartContentBytes, (prefix, suffix), (destination, parts) =>
        {
            parts.prefix.CopyTo(destination);
            destination[parts.prefix.Length..^parts.suffix.Length].Fill('a');
            parts.suffix.CopyTo(destination[^parts.suffix.Length..]);
        });
    }

    internal sealed record InlineHeaders(Func<StartSpec, object, bool>? ValidateInlineMaterial = null) : IRootDispatchHeaders
    {
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
