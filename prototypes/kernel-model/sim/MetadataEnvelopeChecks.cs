using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class MetadataEnvelopeChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long now = 1_701_400_030_000;
        const int messageBytes = 16 * 1_024 * 1_024 - 48 * 1_024;
        const int startBytes = messageBytes - 15 * 1_024;
        const int eventBytes = messageBytes - 104 * 1_024;
        var fullMessage = new string('é', messageBytes / 2);
        var fullStart = new string('s', startBytes);
        var fullEvent = new string('<', eventBytes);
        var model = new Probe(fullEvent);
        var store = new InMemoryStore(1, () => now) { ObserveLifecycles = true };
        var scope = new Contract.BoundScope("metadata");
        var worker = new Worker("metadata", 0, store, new ActorAdapter(store, model),
            new WorkerOptions { Monotonic = () => 0, Lifecycle = store.Lifecycle });
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node);
        await using var activation = (await new DurableHost([worker]).ActivateAsync(0))!;
        var normal = new Contract.StartSpec(model.Type, "test");
        var live = await Start("live", normal);
        var publisher = await Start("publisher", normal with { Input = "publish" });
        var overPublisher = await Start("over-publisher", normal with { Input = "publish-over" });
        var badJob = await Start("over-job", normal with { Input = "job-over" });
        var handledJob = await Start("handled-job", normal with { Input = "job-handled" });
        var handledStart = await Start("handled-start", normal with { Input = "start-handled" });
        store.AdmitGeneratedFoundationStart(scope.Tenant, "old-type-data",
            new StartSpec(model.Type, new string('t', 16 * 1_024 + 1), "previously admitted"), null, null);
        await Pump();
        check(Checkpoint(publisher).Status == InstanceStatus.Running &&
            store.FoundationEvents.Values.Any(e => e.Key == "metadata_signal" && e.Payload == fullEvent),
            "a real typed publish preserves an escape-heavy payload at the source limit");
        check(Checkpoint(overPublisher).Status == InstanceStatus.Faulted &&
            !store.FoundationEvents.Values.Any(e => e.Key == "metadata_refused"),
            "a turn publish one byte above the source limit parks without recording an event");
        check(Checkpoint(badJob) is { Status: InstanceStatus.Faulted, Jobs: null } &&
            !store.Messages.Values.Any(row => row.Message.Target.Id == Checkpoint(badJob).Id &&
                row.Message.Kind is Kinds.Job or Kinds.JobDeadline),
            "an over-limit encoded job ticket parks before creating a job or its deadline");
        var handled = Checkpoint(handledJob);
        check(handled is { Status: InstanceStatus.Running, Jobs: null, State: "used fallback" } &&
            !store.Messages.Values.Any(row => row.Message.Target.Id == handled.Id &&
                row.Message.Kind is Kinds.Job or Kinds.JobDeadline),
            "an actor that handles a job limit can commit its fallback without retaining a ticket or timer");
        check(Checkpoint(handledStart) is { Status: InstanceStatus.Running, Children: null, State: "used fallback" },
            "an actor can catch the typed TypeData limit before staging an oversized child");
        check(store.Lifecycle!.Drain().Any(change => change.Refused?.Any(message => message.Id == "old-type-data") == true) &&
            store.Messages.Values.All(item => item.Message.Id != "old-type-data"),
            "lifecycle observation records the durable refusal of a projected typed start");

        var target = new Contract.Target.Ref(live);
        var oversized = await ingress.SendAsync(scope, target, "body", "ordinary", fullMessage + "x");
        var body = await ingress.SendAsync(scope, target, "body", "ordinary", fullMessage);
        check(oversized.Outcome == Contract.AdmissionOutcome.Refused && body.Outcome == Contract.AdmissionOutcome.Accepted &&
            store.Messages.Values.Count(r => r.Message.Id == "body" && r.Message.Payload == fullMessage) == 1,
            "ordinary content fits at its byte limit and an oversized refusal leaves the same ID reusable");

        var overStart = await ingress.SendAsync(scope, new Contract.Target.Start(normal with { Input = fullStart + "x" }),
            "wide-start", Contract.Kinds.Start);
        var fitStart = await ingress.SendAsync(scope, new Contract.Target.Start(normal with { Input = fullStart }),
            "wide-start", Contract.Kinds.Start);
        check(overStart.Outcome == Contract.AdmissionOutcome.Refused && fitStart.Outcome == Contract.AdmissionOutcome.Accepted &&
            store.Messages.Values.Any(r => r.Message is { Id: "wide-start", Payload: null, Start.Input: var input } && input == fullStart),
            "a typed start reserves its header and stores its accepted input once");

        var outsideEvent = new Contract.Target.Event("outside");
        var overEvent = await ingress.SendAsync(scope, outsideEvent, "event", Contract.Kinds.Event, fullEvent + "x");
        var fitEvent = await ingress.SendAsync(scope, outsideEvent, "event", Contract.Kinds.Event, fullEvent);
        check(overEvent.Outcome == Contract.AdmissionOutcome.Refused && fitEvent.Outcome == Contract.AdmissionOutcome.Accepted &&
            store.FoundationEvents[("metadata_outside", "event")].Payload == fullEvent,
            "outside event oversize is refused before admitting a publication or claiming its ID");
        var startTooWide = false;
        try { await ingress.SetStartAsync(scope, "new-life", "wait", normal with { Input = new string('a', 64 * 1_024 + 1) }); }
        catch (ArgumentException) { startTooWide = true; }
        await ingress.SetStartAsync(scope, "new-life", "wait", normal with { Input = new string('a', 64 * 1_024) });
        check(startTooWide && store.Subscriptions.Single(s => s.Key == "metadata_new-life").Start?.Input?.Length == 64 * 1_024,
            "event start registration fits exactly within its reserved input and refuses the next byte");

        store.SetFoundationEventStart(scope.Tenant, "old-start", Probe.LegacyWait, new(model.Type, "old-start"));
        var existing = Checkpoint(live);
        await store.SubscribeAsync(new SubscriptionRow("metadata_old-wait", existing.Id, Probe.LegacyWait, Creation: existing.Creation)
            { FoundationTenant = scope.Tenant });
        await store.SubscribeAsync(new SubscriptionRow("metadata_keep-wait", existing.Id, "keep", Creation: existing.Creation)
            { FoundationTenant = scope.Tenant });
        var oldStartRemoved = true;
        try { await ingress.SetStartAsync(scope, "old-start", Probe.LegacyWait, null); }
        catch (Contract.MetadataLimitExceeded) { oldStartRemoved = false; }
        await ingress.SendAsync(scope, target, "drop-old-wait", "update", "unsubscribe-legacy");
        await Pump();
        check(oldStartRemoved && Checkpoint(live).Status == InstanceStatus.Running &&
            store.Subscriptions.Any(s => s.Key == "metadata_keep-wait") && store.Subscriptions.All(s => s.Wait != Probe.LegacyWait),
            "both public unsubscribe paths can remove legacy waits above the new registration limit");

        var prior = Checkpoint(live);
        var fence = worker.Fences.Single();
        var composedTurn = new DurableTurn(new TurnInput(worker, new Slot(), prior.Id, [], 0, new("probe", "definition"), now));
        var senderAtLimit = new string('x', 1_024 + 160);
        var overSender = false;
        try { composedTurn.SendFromReceivedAddress(senderAtLimit + "x", new(prior.Id), "ok", null); }
        catch (MetadataLimitExceeded error)
        {
            overSender = error.ParamName == "receivedAt" && error.LimitBytes == senderAtLimit.Length;
        }
        var issued = composedTurn.SendFromReceivedAddress(senderAtLimit, new(prior.Id), "ok", null);
        check(overSender && composedTurn.SentCount == 1 && issued.EndsWith(".0000", StringComparison.Ordinal),
            "a send from a received address rejects excess sender bytes before staging or assigning an ordinal");

        foreach (var size in new[] { 4 * 1_024 * 1_024 - 1, 4 * 1_024 * 1_024, 4 * 1_024 * 1_024 + 1 })
        {
            var invalid = new Message("wide-kind", new(prior.Id), new string('k', size), TargetCreation: prior.Creation);
            var good = new Message("sibling", new(prior.Id), "ok", TargetCreation: prior.Creation);
            var turn = new TurnCommit(prior.Id, prior with { Revision = prior.Revision + 1 }, prior.Revision, [],
                [], [good, invalid], [], [], "[]");
            var refused = false;
            try { await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [turn], [], [])); }
            catch (StoreException error) when (error.Failure == Failure.Definite) { refused = true; }
            var unchanged = Checkpoint(live) == prior && store.Partitions[fence.Partition].Position == fence.Position &&
                !store.Messages.ContainsKey(good.InboxKey) && !store.Messages.ContainsKey(invalid.InboxKey);
            check(refused && unchanged, $"a composed kind of {size} bytes refuses the checkpoint and every sibling write");
            if (!refused || !unchanged) break;
        }

        const string oldPayload = "before-frame";
        var old = new EventRow("metadata_signal", "old", oldPayload, now) { FoundationTenant = "metadata" };
        var encoded = new Message("old", new(prior.Id), Kinds.Publish, Json.Write(old));
        check(FoundationEventTicket.Decode(encoded) == old &&
            new string?[] { null, "", "x|:\u0001" }.All(payload =>
            {
                var current = old with { Payload = payload };
                return FoundationEventTicket.Decode(encoded with { Payload = FoundationEventTicket.Encode(current) }) == current;
            }),
            "publication frames preserve legacy tickets, absent content, empty content and delimiters");

        async Task<Contract.ActorRef> Start(string id, Contract.StartSpec spec) =>
            (await ingress.SendAsync(scope, new Contract.Target.Start(spec), id, Contract.Kinds.Start)).Actor!.Value;
        InstanceRow Checkpoint(Contract.ActorRef reference) => store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row;

        async Task Pump()
        {
            for (var pass = 0; pass < 10; pass++)
            {
                await activation.WokenAsync(null);
                await activation.RunAsync();
            }
        }
    }

    private sealed class Probe(string fullEvent) : Contract.IActorType
    {
        internal static readonly string LegacyWait = new('w', 1_025);
        private readonly string _overJob = new('<', 3 * 1_024 * 1_024);

        public string Type => "metadata";
        public bool Withholds(Contract.ActorHeader header) => false;
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            foreach (var delivery in turn.Inbox)
                switch (delivery.Payload)
                {
                    case "publish": turn.Publish("signal", fullEvent, TimeSpan.FromMinutes(1)); break;
                    case "publish-over": turn.Publish("refused", fullEvent + "x", TimeSpan.FromMinutes(1)); break;
                    case "job-over":
                        turn.StartJob("pending", _overJob, TimeSpan.FromMinutes(1));
                        break;
                    case "job-handled":
                        try { turn.StartJob("pending", _overJob, TimeSpan.FromMinutes(1)); }
                        catch (Contract.MetadataLimitExceeded)
                        {
                            turn.State = "used fallback";
                            return default;
                        }
                        break;
                    case "start-handled":
                        try { turn.Start(new Contract.StartSpec(Type, new string('t', 16 * 1_024 + 1))); }
                        catch (Contract.MetadataLimitExceeded)
                        {
                            turn.State = "used fallback";
                            return default;
                        }
                        break;
                    case "unsubscribe-legacy": turn.Unsubscribe("old-wait", LegacyWait); break;
                }
            turn.State = "saved";
            return default;
        }
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
