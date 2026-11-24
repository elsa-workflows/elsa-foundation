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
    private const long Envelope = 16_457_728;

    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
        {
            await RunCase("TG-1(c1)", mode, oversize: true, thenDelete: false, check);
            await RunCase("TG-1(c2)", mode, oversize: false, thenDelete: false, check);
            await RunCase("TG-2(3)", mode, oversize: false, thenDelete: true, check);
        }
    }

    private static async Task RunCase(string scenario, string mode, bool oversize, bool thenDelete, Action<bool, string> check)
    {
        const long now = 1_702_310_000_000;
        var name = $"{scenario} {mode}";
        var store = new InMemoryStore(1, () => now);
        var scope = ContractScopes.Bind("native-terminal-envelope");
        var ingress = new InMemoryIngress(store, new Requests(name), name, (_, kind) => kind is Contract.Kinds.Terminate or Contract.Kinds.Delete);
        var reads = new ActorList(store, 16);
        var runtime = new PublicRuntime(ingress, reads);
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("detached", Wf.Set("done", "done", true)))
            .Add(Wf.Workflow("grandchild", Wf.Delay("wait", 100_000)))
            .Add(Wf.Workflow("candidate", Wf.Parallel("branches", null, Branches())))
            .Add(Wf.Workflow("parent", Wf.Dispatch("child", "candidate", wait: true)));
        var model = new NativeExecutionModel(registry, new() { MaxActivitiesPerTurn = 10_000 });
        var actor = new PublicWorkflow([model]);
        var start = await runtime.StartAsync(scope, "start", NativeExecutionModel.Start("parent", mode));
        var parent = start.Actor ?? throw new InvalidOperationException("The Native parent was not admitted.");
        var parentId = store.FoundationTarget(scope.Tenant, parent.Id)!.Id;
        var candidateId = "";
        var creatorBatches = new List<string[]>();
        var childTerminates = new List<Message>();
        var parentNotices = new List<Message>();
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed) return;
            foreach (var turn in batch.Turns)
            {
                if (turn.Instance == parentId && turn.Row is { Children: { } children } && candidateId.Length == 0)
                    candidateId = ChildRegistry.Read(children).Single().Id;
                if (turn.Instance != candidateId) continue;
                var creators = turn.Sent.Where(message => message.InboxScope == InboxKey.ChildCreatorScope &&
                    message.Start is { Parent: null }).Select(message => message.Id).ToArray();
                if (creators.Length != 0) creatorBatches.Add(creators);
                childTerminates.AddRange(turn.Sent.Where(message => message.Kind == Kinds.Terminate));
                parentNotices.AddRange(turn.Sent.Where(message => message.Kind == Contract.Kinds.ChildFinished &&
                    message.Target.Id == parentId));
            }
        });

        await using (var initial = await Own("original"))
        {
            await DriveUntil(initial, () => creatorBatches.Count >= 1);
            await Publish("batch-two");
            await DriveUntil(initial, () => creatorBatches.Count >= 2);
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
            initialState.Inboxes?.Count == InboxCount && initialState.Inboxes.Values.All(inbox => inbox.Closed is null),
            "three authored commits issue 1,500 distinct detached creators and leave 3,500 invocation inboxes open");

        // Import the old checkpoint shape with the real emitted IDs after the first owner fully stops.
        var oldHandles = DurableKeyJson.WriteKeys(Handles(beforeRestore).Concat(detachedHandles));
        store.Instances[candidateId] = beforeRestore with { CustomHandles = oldHandles };
        await using var successor = await Own("successor");

        if (thenDelete)
        {
            var receive = initialState.Frames.Values.Single(frame => frame.Node == "inbox-0").Id;
            var input = new InvocationInput(receive, "retained", "protect");
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
        var padding = atLimit + (oversize ? Bytes(oldHandles) : 0);
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
            oversize ? "the handle-sized excess parks and preserves the previous checkpoint" : "the authored growth commits at the exact projected envelope");
        Console.WriteLine($"{name} state bytes {Bytes(afterGrowth.State)}, retained handle bytes {Bytes(oldHandles)}, terminal growth {terminalGrowth}");

        var target = thenDelete ? reference : parent;
        var view = await reads.GetAsync(scope, target) ?? throw new InvalidOperationException("The terminate target is missing.");
        var command = new Contract.OperatorCommand(view.Selection, ThenDelete: thenDelete, ExpectedTarget: thenDelete ? target : null);
        var end = await ingress.SendAsync(scope, new Contract.Target.Ref(target), "terminate", Contract.Kinds.Terminate, JsonSerializer.Serialize(command));
        await DriveUntil(successor, () => Row(grandchild)?.Status == InstanceStatus.Cancelled && parentNotices.Count != 0);
        var finished = Row(candidateId);
        var childRow = Row(grandchild);
        var expectedStatus = oversize ? InstanceStatus.Faulted : InstanceStatus.Cancelled;
        Console.WriteLine($"{name} termination {end.Outcome}, A {finished?.Status}/{finished?.Revision}, " +
            $"P {Row(parentId)?.Status}, G {childRow?.Status}, child terminates {childTerminates.Count}, parent notices {parentNotices.Count}");
        Report(end.Outcome == Contract.AdmissionOutcome.Accepted && finished is { Retired: false } && finished.Status == expectedStatus &&
            childTerminates is [var child] && child.Target.Id == grandchild && child.TargetCreation == childRow?.Creation &&
            childRow?.Status == InstanceStatus.Cancelled && parentNotices is [var notice] && notice.SenderCreation == finished.Creation,
            "one landed bound child terminate cancels the child and one notice returns to the parent");
        if (!oversize && finished is not null)
        {
            var terminalView = (await reads.GetAsync(scope, reference))!;
            var headerBytes = Bytes(terminalView.Header.TypeData)
                + terminalView.Header.Labels.Sum(pair => Bytes(pair.Key) + Bytes(pair.Value));
            var size = RowBytes(finished, headerBytes);
            Console.WriteLine($"{name} terminal envelope bytes {size}");
            Report(finished.CustomHandles == oldHandles && State(finished).Inboxes?.Values.All(inbox => inbox.Closed == "cancelled") == true &&
                size <= Envelope, "the terminal row retains the predecessor handles, closes every inbox and fits");
        }

        InstanceRow? Row(string id) => store.Instances.GetValueOrDefault(id);
        void Report(bool condition, string description) => check(condition, $"{name}: {description}");
        async Task<IScopeActivation> Own(string suffix)
        {
            var worker = new Worker(name + suffix, 0, wire, new ActorAdapter(store, actor), new WorkerOptions { Monotonic = () => 0 });
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

    private static long RowBytes(InstanceRow row, int variableHeader) =>
        16_384L + variableHeader
        + Math.Max(Bytes(row.Children), 384L * ChildRegistry.Read(row.Children).Count)
        + Math.Max(Bytes(row.Jobs), 12_416L * JobRegistry.Read(row.Jobs).Count)
        + (row.Cascade?.Entries.StorageBytes ?? 0)
        + Bytes(row.State) + Bytes(row.CustomHandles) + Bytes(row.CommitTimes) + Bytes(row.Model) + Bytes(row.Definition);

    private static Node[] Branches() => Enumerable.Range(0, InboxCount)
        .Select(index => Wf.Node<OpenInbox>($"inbox-{index}", (nameof(OpenInbox.Protected), index == 0)))
        .Concat([
            Wf.Node<Padding>("padding"),
            Wf.Dispatch("attached", "grandchild", wait: true),
            Wf.Sequence("old-creators", Spawn(1), Wf.Event("await-two", "batch-two"), Spawn(2),
                Wf.Event("await-three", "batch-three"), Spawn(3)),
        ]).ToArray();

    private static Node Spawn(int batch) => Wf.Parallel($"spawns-{batch}", null, Enumerable.Range(0, CreatorBatch)
        .Select(index => Wf.Dispatch($"detached-{batch}-{index}", "detached", wait: false)).ToArray());

    private sealed class OpenInbox : CompositeActivity
    {
        [Input] public bool Protected { get; set; }
        protected override void Execute(CompositeContext context) =>
            context.RegisterInputInbox(1, 1, Protected ? TimeSpan.FromHours(1) : null);
        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
            throw new InvalidOperationException("An invocation inbox has no authored children.");
    }

    private sealed class Padding : CompositeActivity, ISignalledComposite
    {
        protected override void Execute(CompositeContext context) => context.SetState("");
        public bool CanAccept(CompositeContext context) => true;
        public void Accept(CompositeContext context, string messageId, CompositeSignal signal) =>
            context.SetState(new string('x', int.Parse(signal.Value!, CultureInfo.InvariantCulture)));
        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
            throw new InvalidOperationException("Padding has no authored children.");
    }
}
