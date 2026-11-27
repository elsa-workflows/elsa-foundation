using System.Globalization;
using System.Text;
using System.Text.Json;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Contract = Kernel.Foundation.Contracts;
using PublicRuntime = Kernel.WorkflowActor.WorkflowRuntime;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

internal static class NativeTerminalEnvelopeChecks
{
    private const int InboxCount = 3_500;
    private const int CreatorBatch = 500;
    internal const long Envelope = 16_457_728;

    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        {
            await RunCase("TG-1(c1)", mode, Boundary.WithoutHandleCharge, thenDelete: false, check);
            await RunCase("TG-1(c2)", mode, Boundary.Exact, thenDelete: false, check);
            await RunCase("TG-2(3)", mode, Boundary.Exact, thenDelete: true, check);
            await RunCase("TG-1 cap+1", mode, Boundary.ExcessByte, thenDelete: false, check);
            await RunCase("TG-2 protected cap+1", mode, Boundary.ExcessByte, thenDelete: true, check);
        }
    }

    private enum Boundary { Exact, ExcessByte, WithoutHandleCharge }

    private static async Task RunCase(string scenario, string mode, Boundary boundary, bool thenDelete, Action<bool, string> check)
    {
        const long now = 1_702_310_000_000;
        var name = $"{scenario} {mode}";
        var oversize = boundary != Boundary.Exact;
        var store = new InMemoryStore(1, () => now);
        var scope = ContractScopes.Bind("native-terminal-envelope");
        var requests = new Requests(name);
        var ingress = new InMemoryIngress(store, requests, name + "successor", (_, kind) => kind is Contract.Kinds.Terminate or Contract.Kinds.Delete);
        var reads = new ActorList(store, 16);
        var runtime = new PublicRuntime(ingress, reads);
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("detached", Wf.Set("done", "done", true)))
            .Add(Wf.Workflow("grandchild", Wf.Delay("wait", 100_000)))
            .Add(Wf.Workflow("candidate", Wf.Parallel("branches", null, Branches(mode))))
            .Add(Wf.Workflow("parent", Wf.Dispatch("child", "candidate", wait: true, mode: mode)));
        var model = new NativeExecutionModel(registry, new() { MaxActivitiesPerTurn = 10_000 });
        var actor = new PublicWorkflow([model]);
        var start = await runtime.StartAsync(scope, "start", NativeExecutionModel.Start("parent", mode));
        var parent = start.Actor ?? throw new InvalidOperationException("The Native parent was not admitted.");
        var parentId = store.FoundationTarget(scope.Tenant, parent.Id)!.Id;
        var candidateId = "";
        var creatorBatches = new List<string[]>();
        var candidateTurns = new List<TurnCommit>();
        var parentTerminates = new List<Message>();
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed) return;
            foreach (var turn in batch.Turns)
            {
                if (turn.Instance == parentId && turn.Row is { Children: { } children } && candidateId.Length == 0)
                    candidateId = ChildRegistry.Read(children).Single().Id;
                if (turn.Instance == parentId)
                    parentTerminates.AddRange(turn.Sent.Where(message => message.Kind == Kinds.Terminate && message.Target.Id == candidateId));
                if (turn.Instance != candidateId) continue;
                if (turn.Sent.Any(message => message.Kind is Kinds.Terminate or Contract.Kinds.ChildFinished))
                    candidateTurns.Add(turn);
                var creators = turn.Sent.Where(message => message.InboxScope == InboxKey.ChildCreatorScope &&
                    message.Start is { Parent: null }).Select(message => message.Id).ToArray();
                if (creators.Length != 0) creatorBatches.Add(creators);
            }
        });

        await using (var initial = await Own("original"))
        {
            await DriveUntil(initial, () => creatorBatches.Count >= 1 && Awaiting("batch-two"), maxPasses: 5_000);
            await Publish("batch-two");
            await DriveUntil(initial, () => creatorBatches.Count >= 2 && Awaiting("batch-three"));
            await Publish("batch-three");
            await DriveUntil(initial, () => creatorBatches.Count >= 3 &&
                Row(candidateId) is { } row && ChildRegistry.Read(row.Children).SingleOrDefault() is { } child &&
                Row(child.Id) is { Status: InstanceStatus.Running });
        }
        var beforeRestore = Row(candidateId) ?? throw new InvalidOperationException("The authored candidate did not start.");
        var grandchild = ChildRegistry.Read(beforeRestore.Children).Single().Id;
        var reference = new Contract.ActorRef(store.FoundationReferenceOf(scope.Tenant, candidateId, beforeRestore.Creation)!);
        var initialState = State(beforeRestore);
        var detachedHandles = creatorBatches.SelectMany(batch => batch).ToArray();
        Report(creatorBatches.Select(batch => batch.Length).SequenceEqual([CreatorBatch, CreatorBatch, CreatorBatch]) &&
            detachedHandles.Distinct(StringComparer.Ordinal).Count() == CreatorBatch * 3 &&
            Handles(beforeRestore).Count == 1 && !Handles(beforeRestore).Intersect(detachedHandles).Any() &&
            initialState.Mode == mode && initialState.Inboxes?.Count == InboxCount &&
            initialState.Inboxes.Values.All(inbox => inbox.Closed is null),
            "three authored commits issue 1,500 distinct detached creators and leave 3,500 invocation inboxes open");

        // Import the old checkpoint shape with the real emitted IDs after the first owner fully stops.
        var oldHandles = DurableKeyJson.WriteKeys(Handles(beforeRestore).Concat(detachedHandles));
        store.Instances[candidateId] = beforeRestore with { CustomHandles = oldHandles };
        await using var successor = await Own("successor");

        var receivingFrame = initialState.Frames.Values.Single(frame => frame.Node == "inbox-0").Id;
        if (thenDelete)
        {
            var input = new InvocationInput(receivingFrame, "retained", "protect");
            var admission = await ingress.SendAsync(scope, new Contract.Target.Ref(reference), "retain", InvocationInput.Kind, JsonSerializer.Serialize(input));
            await DriveUntil(successor, () => Row(candidateId)!.Revision > beforeRestore.Revision);
            var protectedRead = await reads.GetAsync(scope, reference);
            Report(admission.Outcome == Contract.AdmissionOutcome.Accepted &&
                actor.ProtectsDeletion(protectedRead!.Header, Row(candidateId)!.State, now),
                "a real retained invocation input protects this workflow from deletion");
        }

        var beforeGrowth = Row(candidateId)!;
        var state = State(beforeGrowth);
        var frame = state.Frames.Values.Single(candidate => candidate.Node == "padding");
        // C1 projects the UTF-8 state, metadata, child reserve, maximum public header and Native termination growth.
        var terminalGrowth = InboxCount * 7 + (thenDelete ? 23 : 0);
        var atLimit = checked((int)(Envelope - RowBytes(beforeGrowth, 16_384 + 8_192) - terminalGrowth));
        var padding = atLimit + (boundary switch
        {
            Boundary.WithoutHandleCharge => Bytes(oldHandles),
            Boundary.ExcessByte => 1,
            _ => 0
        });
        var signal = new CompositeSignal(frame.Id, "grow", padding.ToString(CultureInfo.InvariantCulture));
        var growing = await ingress.SendAsync(scope, new Contract.Target.Ref(reference), "grow", CompositeSignal.Kind, JsonSerializer.Serialize(signal));
        await DriveUntil(successor, () => Row(candidateId)!.Revision > beforeGrowth.Revision);
        var afterGrowth = Row(candidateId)!;
        var growthView = await reads.GetAsync(scope, reference);
        Report(growing.Outcome == Contract.AdmissionOutcome.Accepted &&
            (oversize ? afterGrowth.State == beforeGrowth.State && growthView?.Park is not null &&
                    afterGrowth.Fault?.Contains("terminal growth exceed", StringComparison.Ordinal) == true
                : afterGrowth.Status == InstanceStatus.Running && afterGrowth.CustomHandles == oldHandles &&
                    RowBytes(afterGrowth, 16_384 + 8_192) + terminalGrowth == Envelope),
            oversize ? "the excess parks and preserves the previous checkpoint" : "the authored growth commits at the exact projected envelope");
        Console.WriteLine($"{name} state bytes {Bytes(afterGrowth.State)}, retained handle bytes {Bytes(oldHandles)}, terminal growth {terminalGrowth}");

        if (boundary == Boundary.ExcessByte) return;

        var target = thenDelete ? reference : parent;
        var view = await reads.GetAsync(scope, target) ?? throw new InvalidOperationException("The terminate target is missing.");
        var command = new Contract.OperatorCommand(view.Selection, ThenDelete: thenDelete, ExpectedTarget: thenDelete ? target : null);
        var deliveryTarget = new Contract.Target.Ref(target);
        var payload = JsonSerializer.Serialize(command);
        var reply = thenDelete ? ingress.RequestAsync(scope, deliveryTarget, "terminate", Contract.Kinds.Terminate, payload,
            null, new Contract.RequestOptions(null, now + 1_000)).AsTask() : null;
        var end = reply is null ? await ingress.SendAsync(scope, deliveryTarget, "terminate", Contract.Kinds.Terminate, payload) : null;
        await DriveUntil(successor, () => Row(grandchild)?.Status == InstanceStatus.Cancelled);
        var kept = reply is not null &&
            (await reply.WaitAsync(TimeSpan.FromSeconds(5))) is { Error: null, Item: "terminated and kept" };
        if (thenDelete)
            Report(kept, "the committed operator reply confirms the protected ThenDelete branch");

        var finished = Row(candidateId);
        var childRow = Row(grandchild);
        var cascading = candidateTurns.Where(turn => turn.Sent.Any(message => message.Kind == Kinds.Terminate)).ToArray();
        var noticeTurns = candidateTurns.SelectMany(turn => turn.Sent.Where(message => message.Kind == Contract.Kinds.ChildFinished)
            .Select(message => (Turn: turn, Message: message))).ToArray();
        var expectedStatus = oversize ? InstanceStatus.Faulted : InstanceStatus.Cancelled;
        var parentCreation = Row(parentId)!.Creation;
        Console.WriteLine($"{name} A {finished?.Status}/{finished?.Revision}, G {childRow?.Status}, " +
            $"cascade commits {cascading.Length}, parent notices {noticeTurns.Length}");
        Report((thenDelete ? kept : end?.Outcome == Contract.AdmissionOutcome.Accepted) &&
            finished is { Retired: false } && finished.Status == expectedStatus &&
            cascading is [var cascade] && cascade.Row is { } cascadeRow &&
            cascade.Expected == afterGrowth.Revision && cascadeRow.Status == expectedStatus &&
            cascade.Sent.Where(message => message.Kind == Kinds.Terminate).ToArray() is [var child] &&
            child.CascadeOrigin && child.Sender == candidateId && child.SenderCreation == finished.Creation &&
            child.Target.Id == grandchild && child.TargetCreation == childRow?.Creation &&
            childRow?.Status == InstanceStatus.Cancelled &&
            (thenDelete || parentTerminates is [var sent] && sent.CascadeOrigin && sent.SenderCreation == parentCreation &&
                sent.TargetCreation == finished.Creation && cascade.Consumed.Contains(sent.InboxKey)) &&
            noticeTurns is [{ Turn.Row.Revision: var noticeRevision, Message: var notice }] &&
            noticeRevision == (oversize ? afterGrowth.Revision : cascadeRow.Revision) &&
            notice.Sender == candidateId && notice.SenderCreation == finished.Creation &&
            notice.Target.Id == parentId && notice.TargetCreation == parentCreation &&
            notice.Read<FoundationChildNotice>() == new FoundationChildNotice(candidateId, expectedStatus, null, finished.Creation, !oversize),
            "the qualified cascade commits one bound terminate and the correct parent notice lands at its required boundary");

        if (oversize)
            Report(cascading is [{ Row: { } checkpoint }] && checkpoint.State == afterGrowth.State &&
                checkpoint.CustomHandles == oldHandles && finished?.State == afterGrowth.State && finished.CustomHandles == oldHandles,
                "the parked cascade preserves the saved faulted state and the predecessor handles");

        if (!oversize && finished is not null)
        {
            var terminalView = (await reads.GetAsync(scope, reference))!;
            var headerBytes = Bytes(terminalView.Header.TypeData)
                + terminalView.Header.Labels.Sum(pair => Bytes(pair.Key) + Bytes(pair.Value));
            var size = RowBytes(finished, headerBytes);
            var priorInboxes = State(afterGrowth).Inboxes!;
            var endedInboxes = State(finished).Inboxes;
            Console.WriteLine($"{name} terminal envelope bytes {size}");
            Report(finished.CustomHandles == oldHandles && endedInboxes?.Count == InboxCount &&
                priorInboxes.Keys.All(key => endedInboxes.GetValueOrDefault(key)?.Closed == "cancelled") && size <= Envelope,
                "the terminal row keeps all 3,500 inbox identities and predecessor handles, closes the inboxes and fits");
            if (thenDelete)
            {
                var queued = priorInboxes[receivingFrame].Receipts["retained"];
                var retained = endedInboxes?[receivingFrame].Receipts.GetValueOrDefault("retained");
                Report(queued is { Status: "Queued", Retention: 3_600_000 } &&
                    retained == (queued with { Status = "NotProcessed", Outcome = "cancelled", Clock = "workflow:end" }) &&
                    retained is not null && endedInboxes![receivingFrame].Pending.Count == 0,
                    "the original invocation receipt retains its identity and window and ends NotProcessed");
            }
        }

        InstanceRow? Row(string id) => store.Instances.GetValueOrDefault(id);
        bool Awaiting(string name) => Row(candidateId) is { } row && State(row).Waits.Values
            .Any(wait => wait.Kind == "event" && wait.Key == "event:" + name);
        void Report(bool condition, string description) => check(condition, $"{name}: {description}");
        async Task<IScopeActivation> Own(string suffix)
        {
            var node = name + suffix;
            var worker = new Worker(node, 0, wire, new ActorAdapter(store, actor, (requests, node)),
                new WorkerOptions { Monotonic = () => 0, Transport = new Replies(requests) });
            return await new DurableHost([worker]).ActivateAsync(0) ?? throw new InvalidOperationException("The partition is already owned.");
        }
        async Task Publish(string id)
        {
            var result = await ingress.SendAsync(scope, new Contract.Target.Event("event:" + id), id, "published");
            if (result.Outcome != Contract.AdmissionOutcome.Accepted)
                throw new InvalidOperationException($"The Native gate '{id}' was not accepted.");
        }
    }

    private static async Task DriveUntil(IScopeActivation owner, Func<bool> finished, int maxPasses = 48)
    {
        await owner.WokenAsync(null);
        for (var pass = 0; pass < maxPasses && !finished(); pass++) await owner.RunAsync();
    }

    private static NativeState State(InstanceRow row) => JsonSerializer.Deserialize<NativeState>(row.State)!;
    private static IReadOnlyList<string> Handles(InstanceRow row) => row.CustomHandles is { } encoded ? DurableKeyJson.ReadKeys(encoded) : [];
    private static int Bytes(string? text) => text is null ? 0 : Encoding.UTF8.GetByteCount(text);

    internal static long RowBytes(InstanceRow row, int variableHeader) =>
        16_384L + variableHeader
        + Math.Max(Bytes(row.Children), 384L * ChildRegistry.Read(row.Children).Count)
        + Math.Max(Bytes(row.Jobs), 12_416L * JobRegistry.Read(row.Jobs).Count)
        + (row.Cascade?.Entries.StorageBytes ?? 0)
        + Bytes(row.State) + Bytes(row.CustomHandles) + Bytes(row.CommitTimes) + Bytes(row.Model) + Bytes(row.Definition);

    private static Node[] Branches(string mode) => Enumerable.Range(0, InboxCount)
        .Select(index => Wf.Node<OpenInbox>($"inbox-{index}", (nameof(OpenInbox.Protected), index == 0)))
        .Concat([
            Wf.Node<Padding>("padding"),
            Wf.Dispatch("attached", "grandchild", wait: true, mode: mode),
            Wf.Sequence("old-creators", Spawn(1, mode), Wf.Event("await-two", "batch-two"), Spawn(2, mode),
                Wf.Event("await-three", "batch-three"), Spawn(3, mode)),
        ]).ToArray();

    private static Node Spawn(int batch, string mode) => Wf.Node<DetachBatch>($"spawns-{batch}", (nameof(DetachBatch.Mode), mode));

    private sealed class DetachBatch : Activity
    {
        [Input] public string Mode { get; set; } = Modes.Balanced;

        protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) =>
            new(Complete().With(Enumerable.Range(0, CreatorBatch)
                .Select(_ => (Effect)new DispatchEffect("detached", null, Mode)).ToArray()));
    }

    private sealed class Replies(Requests requests) : IReplyTransport
    {
        public void Send(string node, Reply reply) => requests.Receive(reply);
    }

    internal sealed class OpenInbox : CompositeActivity
    {
        [Input] public bool Protected { get; set; }
        protected override void Execute(CompositeContext context) =>
            context.RegisterInputInbox(1, 1, Protected ? TimeSpan.FromHours(1) : null);
        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
            throw new InvalidOperationException("An invocation inbox has no authored children.");
    }

    internal sealed class Padding : CompositeActivity, ISignalledComposite
    {
        protected override void Execute(CompositeContext context) => context.SetState("");
        public bool CanAccept(CompositeContext context) => true;
        public void Accept(CompositeContext context, string messageId, CompositeSignal signal) =>
            context.SetState(new string('x', int.Parse(signal.Value!, CultureInfo.InvariantCulture)));
        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
            throw new InvalidOperationException("Padding has no authored children.");
    }
}
