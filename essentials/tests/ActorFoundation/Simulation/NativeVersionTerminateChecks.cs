using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Contract = Kernel.Foundation.Contracts;
using PublicModel = Kernel.Model.Contracts;
using Workflow = Kernel.WorkflowActor.WorkflowActor;
using WorkflowRuntime = Kernel.WorkflowActor.WorkflowRuntime;

namespace Kernel.Sim;

internal static class NativeVersionTerminateChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await RunCase(Kind.NativeUpgrade, check);
        await RunCase(Kind.ExplicitFallback, check);
        await RunCase(Kind.WithoutMetadata, check);
    }

    private enum Kind { NativeUpgrade, ExplicitFallback, WithoutMetadata }

    private static async Task RunCase(Kind kind, Action<bool, string> check)
    {
        const long now = 1_702_312_000_000;
        const string receiptId = "pending-receipt";
        const int publicHeaderReserve = 16_384 + 8_192;
        var title = $"C1 {kind}";
        var fallback = kind == Kind.ExplicitFallback;
        var withoutMetadata = kind == Kind.WithoutMetadata;
        var expands = kind != Kind.NativeUpgrade;
        var store = new InMemoryStore(1, () => now);
        var bound = ContractScopes.Bind("native-version-terminate");
        var requests = new Requests("version-terminate");
        var ingress = new InMemoryIngress(store, requests, "successor",
            (_, command) => command is Contract.Kinds.Terminate or Contract.Kinds.Delete or Contract.Kinds.Suspend);
        var reads = new ActorList(store, 16);
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("root", Wf.Parallel("parallel", null,
                Wf.Node<NativeTerminalEnvelopeChecks.OpenInbox>("receipt", (nameof(NativeTerminalEnvelopeChecks.OpenInbox.Protected), true)),
                Wf.Node<NativeTerminalEnvelopeChecks.Padding>("padding"),
                Wf.Dispatch("child", "descendant", wait: true))))
            .Add(Wf.Workflow("descendant", Wf.Event("wait", "never-delivered")));
        var native = new NativeExecutionModel(registry);
        // The fixture's v13 writer uses the shared canonical Native shape without the v14 background job recipe.
        var writer = new ObservedNative(native, 13);
        var reader = new ObservedNative(native, 14, expands ? 65_536 : 0);
        var firstActor = new Workflow([writer]);
        var nextActor = new Workflow(withoutMetadata ? [new StateOnlyNative(reader)] : [reader]);
        var opened = await new WorkflowRuntime(ingress, reads).StartAsync(bound, "start", NativeExecutionModel.Start("root"));
        var reference = opened.Actor ?? throw new InvalidOperationException("The workflow start was refused.");
        var target = new Contract.Target.Ref(reference);
        var root = store.FoundationTarget(bound.Tenant, reference.Id)!.Id;
        var terminal = new List<TurnCommit>();
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed) return;
            terminal.AddRange(batch.Turns.Where(turn => turn.Instance == root && turn.Row?.Status == InstanceStatus.Cancelled &&
                turn.Sent.Any(message => message.Kind == Kinds.Terminate)));
        });

        string child;
        string frame;
        InstanceRow saved;
        Contract.ActorHeader savedHeader;
        long originalGrowth;
        await using (var first = await Own("writer", firstActor))
        {
            await DriveUntil(first, () => Row(root) is { Children: { } children } &&
                ChildRegistry.Read(children).SingleOrDefault() is { } child && Row(child.Id)?.Status == InstanceStatus.Running);
            var staged = Row(root)!;
            child = ChildRegistry.Read(staged.Children).Single().Id;
            var state = State(staged);
            frame = state.Frames.Values.Single(item => item.Node == "receipt").Id;
            var padding = state.Frames.Values.Single(item => item.Node == "padding").Id;
            await Send("retain", InvocationInput.Kind, new InvocationInput(frame, receiptId, "protect"));
            await DriveUntil(first, () => State(Row(root)!).Inboxes![frame].Receipts.ContainsKey(receiptId));
            var retained = Row(root)!;
            var header = (await reads.GetAsync(bound, reference))!.Header;
            originalGrowth = firstActor.TerminalGrowth(header, retained.State);
            var padSize = checked((int)(NativeTerminalEnvelopeChecks.Envelope - originalGrowth -
                NativeTerminalEnvelopeChecks.RowBytes(retained, publicHeaderReserve)));
            await Send("grow", CompositeSignal.Kind, new CompositeSignal(padding, "grow", padSize.ToString(CultureInfo.InvariantCulture)));
            await DriveUntil(first, () => State(Row(root)!).Frames[padding].State?.GetValue<string>().Length == padSize);
            saved = Row(root)!;
            savedHeader = (await reads.GetAsync(bound, reference))!.Header;
            Console.WriteLine($"{title} v{System.Text.Json.Nodes.JsonNode.Parse(savedHeader.TypeData)?["Version"]} revision {saved.Revision}, " +
                $"status {saved.Status}, state {Encoding.UTF8.GetByteCount(saved.State)}, terminal growth {originalGrowth}");
            Verify(saved.Status == InstanceStatus.Running && HasNativeHeader(savedHeader, 13) &&
                State(saved).Inboxes?[frame].Receipts[receiptId] is { Status: "Queued", Retention: 3_600_000 } &&
                originalGrowth == 30 && NativeTerminalEnvelopeChecks.RowBytes(saved, publicHeaderReserve) + originalGrowth ==
                    NativeTerminalEnvelopeChecks.Envelope,
                "the preceding owner commits v13 and an exact-envelope live checkpoint with a protected invocation");
        }

        var upgraded = reader.UpgradeState(saved.State, 13);
        var upgradedJson = JsonSerializer.Serialize(upgraded, native.StateType);
        var delta = Encoding.UTF8.GetByteCount(upgradedJson) - Encoding.UTF8.GetByteCount(saved.State);
        var terminalGrowth = expands ? 37 : 30;
        var projected = nextActor.TerminalGrowth(savedHeader, saved.State);
        Verify((expands ? delta >= 65_536 : upgradedJson == saved.State) &&
            projected == (withoutMetadata ? 0 : terminalGrowth + delta),
            withoutMetadata ? "the projection preserves model growth when termination does not rewrite state"
                : "the projection reserves the UTF-8 difference plus the post-upgrade metadata growth");
        if (fallback)
        {
            reader.TerminateMetadata(upgraded);
            var expandedTerminal = saved with { State = JsonSerializer.Serialize(upgraded, native.StateType), Children = null };
            Verify(NativeTerminalEnvelopeChecks.RowBytes(expandedTerminal, HeaderBytes(savedHeader)) > NativeTerminalEnvelopeChecks.Envelope,
                "the deliberately expanded model would exceed the ending envelope on the preceding checkpoint");
        }
        reader.ResetObservations();

        // Select the existing version-unavailable route explicitly before taking ownership of an incompatible row.
        var successorActor = fallback ? new Workflow([]) : nextActor;
        var priorRevision = saved.Revision;
        var retainedHeader = savedHeader;
        Task<Contract.Reply>? terminationReply = null;
        if (fallback)
            await Send("ordinary", "before-terminate", "pending");
        if (kind == Kind.NativeUpgrade)
            await Terminate();
        await using var successor = await Own("successor", successorActor);
        if (fallback)
        {
            await successor.WokenAsync(null);
            await successor.RunAsync();
            var checkpoint = Row(root)!;
            var inbox = await reads.InboxAsync(bound, reference);
            Verify(inbox.Items is [{ Id: "ordinary", State: Contract.DeliveryState.Pending }] &&
                checkpoint.Status == InstanceStatus.Running && checkpoint.State == saved.State && checkpoint.Revision == saved.Revision,
                "the unavailable model defers the earlier ordinary input and preserves the committed checkpoint");
            priorRevision = checkpoint.Revision;
            await Terminate();
        }
        if (withoutMetadata)
        {
            var selected = (await reads.GetAsync(bound, reference))!.Selection;
            await Send("suspend", Contract.Kinds.Suspend, new Contract.OperatorCommand(selected));
            await DriveUntil(successor, () => Row(root)!.Revision > saved.Revision);
            var suspended = Row(root)!;
            retainedHeader = (await reads.GetAsync(bound, reference))!.Header;
            Verify(suspended.Status == InstanceStatus.Running && suspended.State == saved.State &&
                HasNativeHeader(retainedHeader, 13, suspended: true) && reader.UpgradeVersions.Count > 0,
                "a header-only Suspend commits on the expanding older checkpoint when the model has no metadata hook");
            priorRevision = suspended.Revision;
            reader.ResetObservations();
            await Terminate();
        }
        await DriveUntil(successor, () => Row(child)?.Status == InstanceStatus.Cancelled);
        if (terminationReply is not null)
            Verify((await terminationReply.WaitAsync(TimeSpan.FromSeconds(5))) is { Error: null, Item: "terminated and kept" },
                "the committed operator reply confirms protected ThenDelete");

        var ended = Row(root)!;
        var final = (await reads.GetAsync(bound, reference))!.Header;
        Console.WriteLine($"{title} ended {ended.Status}/{ended.Revision}, child {Row(child)?.Status}, " +
            $"ordinary turns {reader.OrdinaryRootTurns}, upgrade calls {string.Join(",", reader.UpgradeVersions)}, " +
            $"ending writes {string.Join(",", terminal.Select(t => $"{t.Expected}->{t.Row?.Revision} / sends {t.Sent.Count}"))}");
        Verify(ended is { Status: InstanceStatus.Cancelled, Retired: false } && terminal is [var turn] && turn.Expected == priorRevision && turn.Row?.Revision == priorRevision + 1 &&
            turn.Sent.Where(message => message.Kind == Kinds.Terminate).ToArray() is [var terminate] &&
            terminate.CascadeOrigin && terminate.Sender == root && terminate.SenderCreation == saved.Creation &&
            terminate.Target.Id == child && terminate.TargetCreation == Row(child)?.Creation &&
            Row(child)?.Status == InstanceStatus.Cancelled && reader.OrdinaryRootTurns == 0,
            "the successor commits cancellation and the bound descendant terminate before any ordinary root turn");

        var originalReceipt = State(saved).Inboxes![frame].Receipts[receiptId];
        var result = State(ended).Inboxes![frame];
        if (expands)
            Verify(reader.UpgradeVersions.Count == 0 && reader.MetadataTerminations == 0 &&
                ended.State == saved.State && final.TypeData == retainedHeader.TypeData &&
                HasNativeHeader(final, 13, suspended: withoutMetadata) &&
                result.Closed is null && result.Receipts[receiptId] == originalReceipt,
                "termination preserves the opaque state, v13 header and original receipt");
        else
            Verify(reader.UpgradeVersions.Count > 0 && reader.UpgradeVersions.All(version => version == 13) &&
                reader.MetadataTerminations == 1 && reader.TerminatedAnUpgradedState &&
                HasNativeHeader(final, 14) && result.Closed == "cancelled" && result.Pending.Count == 0 &&
                result.Receipts[receiptId] == (originalReceipt with { Status = "NotProcessed", Outcome = "cancelled", Clock = "workflow:end" }),
                "real Native v14 termination upgrades v13 and closes the protected invocation with its terminal clock");

        var terminalBytes = NativeTerminalEnvelopeChecks.RowBytes(ended, HeaderBytes(final));
        Console.WriteLine($"{title} terminal bytes {terminalBytes} of {NativeTerminalEnvelopeChecks.Envelope}");
        Verify(terminalBytes <= NativeTerminalEnvelopeChecks.Envelope, "the actual terminal projection stays inside the in-memory envelope");

        InstanceRow? Row(string id) => store.Instances.GetValueOrDefault(id);
        void Verify(bool condition, string description) => check(condition, $"{title}: {description}");
        async Task Terminate()
        {
            var selected = (await reads.GetAsync(bound, reference))!.Selection;
            if (withoutMetadata)
                await Send("terminate", Contract.Kinds.Terminate, new Contract.OperatorCommand(selected));
            else
                terminationReply = ingress.RequestAsync(bound, target, "terminate", Contract.Kinds.Terminate,
                    JsonSerializer.Serialize(new Contract.OperatorCommand(selected, ThenDelete: true, ExpectedTarget: reference)),
                    null, new Contract.RequestOptions(null, now + 1_000)).AsTask();
        }
        async Task Send(string id, string kind, object payload)
        {
            var result = await ingress.SendAsync(bound, target, id, kind, JsonSerializer.Serialize(payload));
            if (result.Outcome != Contract.AdmissionOutcome.Accepted)
                throw new InvalidOperationException($"The fixture delivery '{id}' was refused: {result.Reason}.");
        }
        async Task<IScopeActivation> Own(string node, Workflow actor) =>
            await new DurableHost([new Worker(node, 0, wire, new ActorAdapter(store, actor, (requests, node)),
                new WorkerOptions { Monotonic = () => 0, Transport = new Replies(requests) })]).ActivateAsync(0)
            ?? throw new InvalidOperationException("The fixture owner failed to acquire its partition.");
    }

    private static async Task DriveUntil(IScopeActivation owner, Func<bool> finished)
    {
        await owner.WokenAsync(null);
        for (var pass = 0; pass < 32 && !finished(); pass++) await owner.RunAsync();
    }

    private static NativeState State(InstanceRow row) => JsonSerializer.Deserialize<NativeState>(row.State)!;
    private static bool HasNativeHeader(Contract.ActorHeader header, int version, bool suspended = false)
    {
        using var document = JsonDocument.Parse(header.TypeData);
        var value = document.RootElement;
        return value.GetProperty("Model").GetString() == "native" && value.GetProperty("Definition").GetString() == "root" &&
            value.GetProperty("Version").GetInt32() == version && value.GetProperty("Suspended").GetBoolean() == suspended &&
            !header.Labels.ContainsKey("version");
    }

    private static int HeaderBytes(Contract.ActorHeader header) => Encoding.UTF8.GetByteCount(header.TypeData) +
        header.Labels.Sum(pair => Encoding.UTF8.GetByteCount(pair.Key) + Encoding.UTF8.GetByteCount(pair.Value));

    private sealed class Replies(Requests requests) : IReplyTransport
    {
        public void Send(string node, Reply reply) => requests.Receive(reply);
    }

    private sealed class StateOnlyNative(ObservedNative model) : PublicModel.IExecutionModel
    {
        public string Key => model.Key;
        public int StateVersion => model.StateVersion;
        public Type StateType => model.StateType;
        public object CreateState(PublicModel.WorkflowStart start) => model.CreateState(start);
        public object UpgradeState(string state, int fromVersion) => model.UpgradeState(state, fromVersion);
        public ValueTask TurnAsync(PublicModel.ITurn turn, CancellationToken token) => model.TurnAsync(turn, token);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => model.ExecuteJobAsync(job, token);
        public Contract.RestorePlan RestoreWaits(PublicModel.InstanceInfo instance, object state) => model.RestoreWaits(instance, state);
        public long TerminalGrowth(PublicModel.InstanceInfo instance, object state) => 0;
    }

    private sealed class ObservedNative(NativeExecutionModel model, int version, int padding = 0) : PublicModel.IExecutionModel, PublicModel.IModelMetadata
    {
        internal readonly List<int> UpgradeVersions = [];
        private readonly HashSet<object> _upgraded = new(ReferenceEqualityComparer.Instance);
        internal bool TerminatedAnUpgradedState { get; private set; }
        internal int MetadataTerminations { get; private set; }
        internal int OrdinaryRootTurns { get; private set; }
        public string Key => model.Key;
        public int StateVersion => version;
        public Type StateType => model.StateType;
        public object CreateState(PublicModel.WorkflowStart start) => model.CreateState(start);
        public object UpgradeState(string state, int fromVersion)
        {
            UpgradeVersions.Add(fromVersion);
            var upgraded = (NativeState)model.UpgradeState(state, fromVersion);
            if (padding != 0)
            {
                const string addedFrame = "expanded-shape-probe";
                upgraded.Variables.Add(addedFrame, JsonValue.Create(new string('x', padding)));
                upgraded.Frames.Add(addedFrame, new Frame { Id = addedFrame, Node = "receipt" });
                upgraded.Inboxes!.Add(addedFrame, new InvocationInbox { Capacity = 1, ReceiptCapacity = 1 });
            }
            _upgraded.Add(upgraded);
            return upgraded;
        }
        public ValueTask TurnAsync(PublicModel.ITurn turn, CancellationToken cancellationToken)
        {
            if (turn.Instance.Definition == "root") OrdinaryRootTurns++;
            return model.TurnAsync(turn, cancellationToken);
        }
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => model.ExecuteJobAsync(job, token);
        public Contract.RestorePlan RestoreWaits(PublicModel.InstanceInfo instance, object state) => model.RestoreWaits(instance, state);
        public long TerminalGrowth(PublicModel.InstanceInfo instance, object state) => model.TerminalGrowth(instance, state);
        public bool HandlesMetadata(Contract.Message message) => model.HandlesMetadata(message);
        public ValueTask MetadataAsync(PublicModel.ITurn turn, Contract.ActorStatus status, long? endedAt, CancellationToken token) =>
            model.MetadataAsync(turn, status, endedAt, token);
        public bool ProtectsDeletion(object state, long? endedAt, long now) => model.ProtectsDeletion(state, endedAt, now);
        public void TerminateMetadata(object state)
        {
            if (((NativeState)state).Inboxes is not null)
            {
                MetadataTerminations++;
                TerminatedAnUpgradedState = _upgraded.Contains(state);
            }
            model.TerminateMetadata(state);
        }
        internal void ResetObservations()
        {
            UpgradeVersions.Clear();
            _upgraded.Clear();
            TerminatedAnUpgradedState = false;
            MetadataTerminations = 0;
        }
    }
}
