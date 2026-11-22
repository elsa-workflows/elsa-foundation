using System.Text;
using System.Text.Json.Nodes;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class EventIdentityChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var fill in new[] { '\u0001', '<' })
            await AtNameLimits(fill, check);
        await NativePublicationBoundary(check);
        await PublicationReuse(StringComparer.OrdinalIgnoreCase, check);
        await PublicationReuse(StringComparer.Ordinal, check);
        await PublicationReuse(StringComparer.Ordinal, check, slowConsumer: true);
        await UnsettledTicket(check);
        await CheckAuthoredStep(loseAfter: false, check);
        await CheckAuthoredStep(loseAfter: true, check);
        await CheckAuthoredStep(loseAfter: false, check, paged: true);
        await CheckAuthoredStep(loseAfter: true, check, paged: true);
        await CheckAuthoredStep(loseAfter: null, check);
        await CheckAuthoredStep(loseAfter: null, check, endPublisher: true);
        await CheckPartialPublicationRecovery(Contract.RecoverMode.Resume, check);
        await CheckPartialPublicationRecovery(Contract.RecoverMode.Resume, check, takeover: true);
        await CheckPartialPublicationRecovery(Contract.RecoverMode.RetryAll, check);
        await CheckPartialPublicationRecovery(Contract.RecoverMode.Retry, check, selectPublication: true);
        await CheckPartialPublicationRecovery(Contract.RecoverMode.Retry, check);
        await CheckPartialPublicationRecovery(Contract.RecoverMode.Resume, check, endBeforeTicket: true);
        foreach (var resolution in Enum.GetValues<ParkClearKind>())
            await CheckPartialPublicationRecovery(Contract.RecoverMode.Resume, check, clear: resolution);
        await CheckRestoredWait(RestoreAt.BeforeAuthor, check);
        await CheckRestoredWait(RestoreAt.BeforeMatch, check);
        await CheckRestoredWait(RestoreAt.AfterMatch, check);
        await CheckRestoredWait(RestoreAt.BeforeInsert, check, keep: true);
        await CheckRestoredWait(RestoreAt.BeforeMatch, check, keep: true);
        await CheckRestoredWait(RestoreAt.AfterMatch, check, keep: true);
        await FoundationEventSendChecks.KeepingSubscription(check);
        await FoundationEventSendChecks.KeepingSubscriptionBatch(check);
        await FoundationEventSendChecks.StartRegistrationCatchUp(check);
        await FoldedRegistration(check);
        await LegacyTicket(doneDelivery: false, check);
        await LegacyTicket(doneDelivery: true, check);
    }

    private static async Task NativePublicationBoundary(Action<bool, string> check)
    {
        const long now = 1_701_400_041_000;
        var budget = FoundationMetadataLimits.EventContentBytes - 2;
        var escaped = new string('<', budget / 6) + new string('x', budget % 6);
        var exact = JsonValue.Create(escaped);
        var wire = exact.ToJsonString();
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("exact", Wf.Publish("publish", "Order-7", Binding.Literal(exact))))
            .Add(Wf.Workflow("excess", Wf.Publish("publish", "Order-7", Binding.Literal(JsonValue.Create(escaped + "x")))))
            .Add(Wf.Workflow("absent", Wf.Publish("publish", "Order-7")))
            .Add(Wf.Workflow("empty", Wf.Publish("publish", "Order-7", "")));
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("native-publication");
        var probe = new Probe([("event:Order-7", "wait")], wire);
        var adapter = new ActorAdapter(store, probe);
        var native = new Kernel.WorkflowActor.WorkflowActor([new NativeExecutionModel(registry)]);
        var router = new ActorTypeRouter(adapter, new Dictionary<string, IActorType>
        {
            [native.Type] = new ActorAdapter(store, native)
        });
        var worker = new Worker("native-publication", 0, store, router, new WorkerOptions { Monotonic = () => 0 });
        var edge = new InMemoryIngress(store, new Requests(worker.Node), worker.Node);
        var reads = new ActorList(store, 10);
        var runtime = new Kernel.WorkflowActor.WorkflowRuntime(edge, reads);
        var subscriber = (await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "wait")), "wait", Contract.Kinds.Start)).Actor!.Value;
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump(owner);

        var actor = await Publish("exact");
        check(Encoding.UTF8.GetByteCount(wire) == FoundationMetadataLimits.EventContentBytes &&
            store.ReadFoundationActor(scope.Tenant, actor.Id).Actor is { Row.Status: InstanceStatus.Finished } &&
            Observed(store, scope, subscriber) is [{ Payload: "equal to limit payload" }],
            "a real Native Publish effect matches the first attainable maximum serialized payload once");
        var excessive = await Publish("excess");
        check(store.ReadFoundationActor(scope.Tenant, excessive.Id).Actor is { Park: not null } &&
            store.FoundationEvents.Count == 1 && store.Messages.Values.All(row => row.Message.Kind != Kinds.Publish),
            "a Native serialized payload one byte above the limit parks without a ticket or event row");
        await Publish("absent");
        await Publish("empty");
        var observations = Observed(store, scope, subscriber);
        check(observations.Count == 3 && observations[1].Payload is null && observations[2].Payload == "\"\"",
            "Native publication preserves a missing payload and a serialized empty string distinctly");

        async Task<Contract.ActorRef> Publish(string definition)
        {
            var started = await runtime.StartAsync(scope, definition, NativeExecutionModel.Start(definition));
            await Pump(owner);
            return started.Actor!.Value;
        }
    }

    private static async Task AtNameLimits(char fill, Action<bool, string> check)
    {
        const long now = 1_701_400_040_000;
        var scope = new Contract.BoundScope(new string('t', 64));
        var name = new string(fill, 1_024);
        var otherWait = name[..^1] + 'w';
        var secondKey = name[..^1] + 'k';
        var store = new InMemoryStore(1, () => now);
        var nearLimit = fill == '<' ? new string('<', 16_621_568) : null;
        var probe = new Probe([(name, name), (name, otherWait), (secondKey, name)], nearLimit);
        var worker = Owner("at-limits", store, probe);
        var edge = new InMemoryIngress(store, new Requests(worker.Node), worker.Node);
        var start = await edge.SendAsync(scope, new Contract.Target.Key(name, new(probe.Type, "")), "start", Contract.Kinds.Start);
        var actor = start.Actor!.Value;
        await using var active = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump(active);

        var payload = nearLimit ?? "\"<\\\u0001:";
        var payloadBytes = Encoding.UTF8.GetByteCount(payload);
        var accepted = await edge.SendAsync(scope, new Contract.Target.Event(name), name, Contract.Kinds.Event, payload,
            new(KeepFor: TimeSpan.FromMinutes(1)));
        var acceptedOther = await edge.SendAsync(scope, new Contract.Target.Event(secondKey), name, Contract.Kinds.Event, "",
            new(KeepFor: TimeSpan.FromMinutes(1)));
        await MatchPending(worker);
        var deliveries = store.Messages.Values.Where(row => row.Message.Kind == Kinds.Event).Select(row => row.Message).ToArray();
        var first = deliveries.Where(message => Events.MatchFor(message)?.Key == scope.Tenant + "_" + name).ToArray();
        var label = $"U+{(int)fill:x4}";
        check(accepted.Outcome == Contract.AdmissionOutcome.Accepted && acceptedOther.Outcome == Contract.AdmissionOutcome.Accepted &&
            first.Length == 2 && deliveries.Length == 3 && deliveries.Select(message => message.Id).Distinct().Count() == 3 &&
            first[0].EventMatch == first[1].EventMatch && deliveries.Select(message => message.EventMatch).Distinct().Count() == 2 &&
            deliveries.All(message => message.Id.Length == 67 && message.EventMatch?.Length == 64 &&
                Encoding.UTF8.GetByteCount(message.Payload!) - (first.Contains(message) ? payloadBytes : 0) < 40 * 1_024),
            $"{label}: maximum names yield fixed-size ids, one match per publication and distinct delivery keys per wait");

        var matches = await store.KeptMatchesAsync(store.Subscriptions.ToArray());
        var duplicates = new List<EventAcceptance>();
        foreach (var route in matches) duplicates.Add(await store.AddEventAsync(route.Delivery()));
        check(duplicates.Count == 3 && duplicates.All(receipt => !receipt.Added),
            $"{label}: catch-up uses the same persisted delivery keys as publication");
        await Pump(active);
        var observed = Observed(store, scope, actor);
        var expected = nearLimit is null ? payload : "equal to limit payload";
        check(observed.Count == 3 && observed.All(item => item.Id == name) &&
            observed.Any(item => item.Key == name && item.Wait == otherWait && item.Payload == expected) &&
            observed.Any(item => item.Key == name && item.Wait == name && item.Payload == expected) &&
            observed.Any(item => item.Key == secondKey && item.Wait == name && item.Payload == ""),
            $"{label}: the real actor receives exact event ids, waits and original payloads");
        if (nearLimit is null) return;

        var target = new Contract.Target.Ref(actor);
        await edge.SendAsync(scope, target, "author-exact", "publish-sized");
        await worker.PollAsync();
        await worker.LoadAsync();
        await worker.FlushAsync();
        worker.RunTurn(store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row.Id);
        await worker.FlushAsync();
        var ticket = store.Messages.Values.Single(row => row.Message.FoundationPublish?.Authored is not null).Message;
        var framed = FoundationEventTicket.Decode(ticket);
        check(framed.Payload == nearLimit && framed.Id == "~" + ticket.Id &&
            Encoding.UTF8.GetByteCount(ticket.Payload!) <= FoundationMetadataLimits.MessageContentBytes,
            "a real authored turn stores the exact maximum escaped-looking payload in a bounded frame");
        await Pump(active);
        check(Observed(store, scope, actor).Count(item => item.Id == framed.Id && item.Payload == "equal to limit payload") == 2,
            "both waits receive the maximum authored payload unchanged and once");
        await edge.SendAsync(scope, target, "author-over", "publish-sized", "one more");
        await Pump(active);
        check(store.ReadFoundationActor(scope.Tenant, actor.Id).Actor is { Park: not null } &&
            store.FoundationEvents.Values.Count(publication => publication.Id.StartsWith('~')) == 1 &&
            store.Messages.Values.All(row => row.Message.FoundationPublish?.Authored is null),
            "one byte above the authored payload limit parks the producer without committing a publication ticket");
    }

    private static async Task PublicationReuse(StringComparer equality, Action<bool, string> check, bool slowConsumer = false)
    {
        var now = 1_701_400_050_000L;
        var folds = equality == StringComparer.OrdinalIgnoreCase;
        var label = slowConsumer ? "slow consumer" : folds ? "folded" : "ordinal";
        var store = new InMemoryStore(1, () => now, Flaw.None, equality)
            { IngressDeduplicationWindow = TimeSpan.FromMilliseconds(20) };
        var scope = new Contract.BoundScope("events");
        var probe = new Probe(folds ? [("Order-7", "wait"), ("ORDER-7", "WAIT")] : [("Order-7", "wait")]);
        var worker = Owner("first", store, probe);
        var edge = new InMemoryIngress(store, new Requests(worker.Node), worker.Node);
        var start = await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "")), "start", Contract.Kinds.Start);
        var actor = start.Actor!.Value;
        await using var active = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump(active);

        var options = new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(20));
        if (!folds)
        {
            await edge.SendAsync(scope, new Contract.Target.Event("order-7"), "E", Contract.Kinds.Event, "unmatched", options);
            await MatchPending(worker);
            check(!store.Messages.Values.Any(row => row.Message.Kind == Kinds.Event),
                $"{label}: equivalent-looking correlation spellings remain distinct");
        }

        var firstKey = folds ? "order-7" : "Order-7";
        var first = await edge.SendAsync(scope, new Contract.Target.Event(firstKey), "E", Contract.Kinds.Event, null, options);
        var retry = await edge.SendAsync(scope, new Contract.Target.Event("Order-7"), folds ? "e" : "E", Contract.Kinds.Event, "retry", options);
        await MatchPending(worker);
        var original = store.FoundationEvents[("events_" + firstKey, "E")];
        var rows = store.Messages.Values.Where(row => row.Message.Kind == Kinds.Event).ToArray();
        check(first.Outcome == Contract.AdmissionOutcome.Accepted && retry.Outcome == Contract.AdmissionOutcome.Duplicate &&
            rows is [{ }] && original is { FoundationIncarnation: not null, Payload: null },
            $"{label}: an in-window retry retains the stored spelling, incarnation and payload");
        if (rows.Length != 1) return;

        var oldKey = rows[0].Message.InboxKey;
        var waits = store.Subscriptions.ToArray();
        if (folds)
        {
            check(waits is [{ Key: "events_Order-7", Wait: "wait" }],
                "folded: equivalent actor subscriptions keep the first stored wait");
            waits = waits.Select(wait => wait with { Key = "events_ORDER-7", Wait = "WAIT" }).ToArray();
        }

        var catchUp = await store.KeptMatchesAsync(waits);
        check(catchUp is [var route] && route.Delivery().Message.InboxKey == oldKey && !(await store.AddEventAsync(route.Delivery())).Added,
            $"{label}: catch-up uses the publication's stored spelling when deriving its digest");
        if (!slowConsumer) await Pump(active);
        now += 21;
        var beforeSweep = await edge.SendAsync(scope, new Contract.Target.Event("Order-7"), folds ? "e" : "E",
            Contract.Kinds.Event, "premature replacement", options);
        check(beforeSweep.Outcome == Contract.AdmissionOutcome.Duplicate &&
            store.FoundationEvents[("events_" + firstKey, "E")] == original &&
            store.Messages.Values.Count(row => row.Message.Kind == Kinds.Event) == 1,
            $"{label}: an unswept publication remains the receipt after the keep window");
        await store.SweepAsync();
        check(!store.FoundationEvents.ContainsKey(("events_" + firstKey, "E")) &&
            store.Messages.GetValueOrDefault(oldKey) is { } oldRow && oldRow.Done == !slowConsumer,
            $"{label}: the sweep preserves the old delivery while it removes its expired publication");

        var nextKey = folds ? "Order-7" : firstKey;
        var next = await edge.SendAsync(scope, new Contract.Target.Event(nextKey), "E", Contract.Kinds.Event, "", options);
        await MatchPending(worker);
        var renewed = store.FoundationEvents[("events_" + nextKey, "E")];
        var pending = store.Messages.Values.Where(row => row.Message.Kind == Kinds.Event && !row.Done && row.Message.InboxKey != oldKey).ToArray();
        check(next.Outcome == Contract.AdmissionOutcome.Accepted && renewed.FoundationIncarnation != original.FoundationIncarnation &&
            pending is [var row] && row.Message.EventMatch != oldKey.Match && row.Message.Id != oldKey.Id,
            $"{label}: a new publication has a fresh delivery key while the old delivery remains");
        await store.SweepAsync();
        check(slowConsumer ? store.Messages.ContainsKey(oldKey) : !store.Messages.ContainsKey(oldKey),
            $"{label}: the next sweep removes a Done marker and leaves a pending delivery");

        await active.DisposeAsync();
        var successor = Owner("second", store, probe);
        await using var replacement = (await new DurableHost([successor]).ActivateAsync(0))!;
        await Pump(replacement);
        var observed = Observed(store, scope, actor);
        check(observed is [{ Id: "E", Payload: null }, { Id: "E", Payload: "" }] &&
            observed.All(item => item.Key == "Order-7" && item.Wait == "wait"),
            $"{label}: a successor sees the continuing wait apply each incarnation once and preserves null versus empty");
        if (folds)
        {
            await edge.SendAsync(scope, new Contract.Target.Ref(actor), "remove", "unsubscribe");
            await Pump(replacement);
            check(store.Subscriptions.Count == 0 &&
                store.ReadFoundationActor(scope.Tenant, actor.Id).Actor?.Row.Status == InstanceStatus.Running,
                "folded: an actor can unsubscribe its stored key and wait through equivalent spellings");
        }
    }

    private static async Task FoldedRegistration(Action<bool, string> check)
    {
        var now = 1_701_400_070_000L;
        var store = new InMemoryStore(1, () => now, Flaw.None, StringComparer.OrdinalIgnoreCase);
        var scope = new Contract.BoundScope("events");
        var probe = new Probe([]);
        var worker = Owner("registration", store, probe);
        var edge = new InMemoryIngress(store, new Requests(worker.Node), worker.Node);
        await using var active = (await new DurableHost([worker]).ActivateAsync(0))!;
        await edge.SetStartAsync(scope, "Order-7", "wait", new(probe.Type, "first"));
        await edge.SetStartAsync(scope, "order-7", "WAIT", new(probe.Type, "replacement"));
        await edge.SendAsync(scope, new Contract.Target.Event("ORDER-7"), "E1", Contract.Kinds.Event, "content");
        await Pump(active);
        check(store.Subscriptions.ToArray() is [{ Key: "events_Order-7", Wait: "wait", Start.Definition: "replacement" }] &&
            store.ReadFoundationActors(scope.Tenant).Actors is [{ Header.TypeData: "replacement" }],
            "folded start registration replaces one binding, keeps its identity and starts the replacement");

        await edge.SetStartAsync(scope, "ORDER-7", "Wait", null);
        await edge.SendAsync(scope, new Contract.Target.Event("order-7"), "E2", Contract.Kinds.Event, "later");
        await Pump(active);
        check(store.Subscriptions.Count == 0 && store.ReadFoundationActors(scope.Tenant).Actors.Count == 1,
            "folded start removal takes effect for subsequent publications with an equivalent spelling");

        now++;
        await edge.SetStartAsync(scope, "Order-7", "wait", new(probe.Type, "before shadow"));
        var raw = new SubscriptionRow("events_ORDER-7", "", "wait", new(probe.Type, "raw"));
        await store.SubscribeAsync(raw);
        await edge.SetStartAsync(scope, "ORDER-7", "wait", new(probe.Type, "after shadow"));
        check(store.Subscriptions.Count == 2 &&
            store.Subscriptions.Single(wait => wait.FoundationTenant == scope.Tenant).Start?.Definition == "after shadow",
            "a raw exact alias cannot shadow replacement of an equivalent scoped wait");
        await edge.SetStartAsync(scope, "ORDER-7", "wait", null);
        await edge.SendAsync(scope, new Contract.Target.Event("order-7"), "E3", Contract.Kinds.Event, "after removal");
        await Pump(active);
        check(store.Subscriptions.ToArray() is [var remaining] && remaining == raw &&
            store.ReadFoundationActors(scope.Tenant).Actors.Count == 1,
            "a raw exact alias survives removal of the equivalent scoped wait without starting a second Foundation actor");
    }

    private static async Task MatchPending(Worker worker)
    {
        for (var step = 0; step < 6; step++)
        {
            await worker.PollAsync();
            await worker.MeetAsync();
        }
    }

    private static async Task LegacyTicket(bool doneDelivery, Action<bool, string> check)
    {
        var now = 1_701_400_080_000L;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("events");
        var probe = new Probe([("Order-7", "wait")]);
        var worker = Owner("legacy-first", store, probe);
        var edge = new InMemoryIngress(store, new Requests(worker.Node), worker.Node);
        var start = await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "")), "start", Contract.Kinds.Start);
        var actor = start.Actor!.Value;
        await using var active = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump(active);
        var id = store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row.Id;
        await edge.SendAsync(scope, new Contract.Target.Ref(actor), "emit", "publish");
        await SelectAndFlush(worker);

        // This is the old durable intermediate state after matching and before the ticket was consumed.
        var stored = store.Messages.Values.Single(row => row.Message.Kind == Kinds.Publish);
        var first = stored.Message;
        var framed = FoundationEventTicket.Decode(first);
        var publication = framed with { KeepUntil = now + framed.KeepUntil };
        var ticket = (first with { FoundationPublish = null,
            Payload = doneDelivery ? Json.Write(publication) : FoundationEventTicket.Encode(publication) }).AsKernelTicket();
        stored.Message = ticket;
        var key = (publication.Key, publication.Id);
        store.RecordFoundationPublication(publication);
        var legacy = Events.Route(publication, store.Subscriptions.Single());
        await store.AddEventAsync(legacy);
        if (doneDelivery) await SelectAndFlush(worker);

        now += (long)Defaults.Lease.TotalMilliseconds + 1;
        await store.SweepAsync();
        var label = doneDelivery ? "legacy Done delivery" : "legacy pending delivery";
        check(store.FoundationEvents.ContainsKey(key) && store.Messages.GetValueOrDefault(legacy.Message.InboxKey) is { } row &&
            row.Done == doneDelivery, $"{label}: sweep retains the stored publication and its delivery while the old ticket is active");

        var successor = Owner("legacy-successor", store, probe);
        await using var replacement = (await new DurableHost([successor]).ActivateAsync(0))!;
        await successor.PollAsync();
        await successor.LoadAsync();
        await successor.FlushAsync();
        await successor.MeetAsync();
        check(store.FoundationEvents.GetValueOrDefault(key) is { FoundationIncarnation: null, FoundationPublishTicket: var linked } &&
            linked == ticket.InboxKey, $"{label}: the retry links the old ticket without minting a new incarnation");
        await successor.FlushAsync();
        await Pump(replacement);
        check(Observed(store, scope, actor) is [{ Payload: "from turn" }],
            $"{label}: the real successor applies the original publication once");

        async Task SelectAndFlush(Worker owner)
        {
            await owner.PollAsync();
            await owner.LoadAsync();
            await owner.FlushAsync();
            owner.RunTurn(id);
            await owner.FlushAsync();
        }
    }

    private static async Task UnsettledTicket(Action<bool, string> check)
    {
        var now = 1_701_400_060_000L;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("events");
        var probe = new Probe([("Order-7", "wait")]);
        var worker = Owner("publisher", store, probe);
        var edge = new InMemoryIngress(store, new Requests(worker.Node), worker.Node);
        var start = await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "")), "start", Contract.Kinds.Start);
        var actor = start.Actor!.Value;
        await using var active = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump(active);

        await edge.SendAsync(scope, new Contract.Target.Ref(actor), "emit", "publish");
        await worker.PollAsync();
        await worker.LoadAsync();
        await worker.FlushAsync();
        worker.RunTurn(store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row.Id);
        await worker.FlushAsync();
        var before = store.Messages.Values.Single(row => row.Message.Kind == Kinds.Publish);
        var ticket = before.Message.InboxKey;
        await worker.MeetAsync();
        var original = store.FoundationEvents.Single();
        check(original.Value.FoundationAcceptedAt == now && original.Value.KeepUntil == now + 20 &&
            original.Value.Id == "~" + ticket.Id && store.Messages[ticket] is { Message.Payload: null } after &&
            after.Seq != before.Seq && !store.Messages.Values.Any(row => row.Message.Kind == Kinds.Event),
            "the authored insert step commits the event and rewrites its ticket without matching or copying its payload");
        now += 21;
        await store.SweepAsync();
        check(store.Messages.GetValueOrDefault(ticket) is { Done: false } &&
            store.FoundationEvents.GetValueOrDefault(original.Key)?.FoundationIncarnation == original.Value.FoundationIncarnation,
            "an expired turn publication keeps its incarnation while its owning ticket is still pending");

        await worker.PollAsync();
        await worker.MeetAsync();
        await store.SweepAsync();
        check(!store.Messages.ContainsKey(ticket) && !store.FoundationEvents.ContainsKey(original.Key),
            "a sweep can remove an expired turn publication after its ticket settles");
        await Pump(active);
        check(Observed(store, scope, actor) is [{ Payload: "from turn" }],
            "the delivery remains available after the settled publication is swept");
    }

    private static async Task CheckAuthoredStep(bool? loseAfter, Action<bool, string> check, bool paged = false, bool endPublisher = false)
    {
        const long initial = 1_701_400_090_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("authored-step");
        var count = paged ? InMemoryStore.ParkPageLimit + 1 : 1;
        var probe = new Probe(Enumerable.Range(0, count).Select(index => ("Order-7", $"wait-{index:D3}")).ToArray(), restoreKeep: true);
        var lost = false;
        var failSteps = loseAfter is null;
        CommitBatch? lostCommit = null;
        var initialSequence = 0L;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (batch.Turns is not [{ FoundationPublicationSequence: { } sequence, FoundationCatchUpStep: not null }] ||
                paged && sequence == initialSequence) return;
            if (failSteps && !landed) throw new StoreException(Failure.Definite, "authored page rejected by fixture");
            if (landed == loseAfter && !lost)
            {
                lost = true;
                lostCommit = batch;
                throw new StoreException(Failure.Unknown, "authored insert response lost");
            }
        });
        Worker NewOwner() => new("authored-step", 0, wire, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - initial, MaxAttempts = 2 });
        var first = NewOwner();
        var edge = new InMemoryIngress(store, new Requests(first.Node), first.Node, (_, kind) => kind == Contract.Kinds.Recover);
        var admitted = await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "")), "start", Contract.Kinds.Start);
        var actor = admitted.Actor!.Value;
        await using var active = (await new DurableHost([first]).ActivateAsync(0))!;
        await Pump(active);
        var address = store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row.Id;
        await edge.SendAsync(scope, new Contract.Target.Ref(actor), "emit", endPublisher ? "publish-and-end" : "publish");
        await first.PollAsync();
        await first.LoadAsync();
        await first.FlushAsync();
        first.RunTurn(address);
        now += 5;
        await first.FlushAsync();
        var original = store.Messages.Values.Single(row => row.Message.Kind == Kinds.Publish);
        var key = original.Message.InboxKey;
        initialSequence = original.Seq;
        check(original.Message.FoundationPublish is { Authored.AcceptedAt: initial + 5 } && store.FoundationEvents.Count == 0,
            "the author commit stamps the framed ticket while the event row is still absent");
        var outside = await edge.SendAsync(scope, new Contract.Target.Event("Order-7"), "~" + key.Id, Contract.Kinds.Event);
        check(outside is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.InvalidEventId, Publication: null } &&
            store.FoundationEvents.Count == 0 && store.Messages[key] == original,
            "outside admission refuses the authored event id before the insert-only phase");
        now += 7;

        if (loseAfter is null)
        {
            await Pump(active);
            var parked = store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!;
            if (endPublisher)
            {
                check(parked is { Park: null, Row.Status: InstanceStatus.Finished } && store.FoundationEvents.Count == 0 &&
                    store.Messages[key] is { Done: true, KeptBy: null, ParkDeadLetter: true, Message.Payload: not null },
                    "an exhausted insert for an ended publisher keeps its replayable payload as a dead letter");
                return;
            }
            check(parked is { Park: not null, Row.Status: InstanceStatus.Faulted } && store.FoundationEvents.Count == 0 &&
                store.Messages[key] is { Done: true, KeptBy: ParkKeepReason.Consumed, ParkDeadLetter: false, Message.Payload: not null },
                "an exhausted authored insert parks the exact actor and keeps its original payload for retry");
            if (parked.Park is null) return;
            var selection = (await new ActorList(store, 10).GetAsync(scope, actor))!.Selection!;
            failSteps = false;
            await edge.SendAsync(scope, new Contract.Target.Ref(actor), "recover-publish", Contract.Kinds.Recover,
                Json.Write(new Contract.OperatorCommand(selection, Mode: Contract.RecoverMode.RetryAll)));
            await Pump(active);
            check(store.ReadFoundationActor(scope.Tenant, actor.Id).Actor is { Park: null } &&
                !store.Messages.ContainsKey(key) && Observed(store, scope, actor) is [{ Payload: "from turn" }],
                "recovery restores the kept authored ticket and completes its original publication once");
            return;
        }

        await first.PollAsync();
        await first.MeetAsync();
        if (paged)
        {
            await first.PollAsync();
            await first.MeetAsync();
            check(lost && !first.Owns && store.FoundationEvents.Count == 1 &&
                (store.Messages[key].Message.FoundationPublish?.After is not null) == loseAfter.Value &&
                store.Messages.Values.Count(row => row.Message.Kind == Kinds.Event) == (loseAfter.Value ? InMemoryStore.ParkPageLimit : 0),
                "loss at a nonfinal match page preserves the original cursor or the exact committed delivery prefix");
        }
        else check(lost && !first.Owns && (store.FoundationEvents.Count == 1) == loseAfter.Value &&
                (store.Messages[key].Message.Payload is null) == loseAfter.Value,
                "a lost insert answer leaves either both durable phase changes or the original framed ticket");
        await active.DisposeAsync();
        var successor = NewOwner();
        await using var replacement = (await new DurableHost([successor]).ActivateAsync(0))!;
        check(lostCommit is not null && !(await store.CommitAsync(lostCommit)).Landed,
            "the previous owner's delayed authored step cannot land after the successor takes ownership");
        await Pump(replacement);
        var observed = Observed(store, scope, actor);
        check(store.FoundationEvents.Values.Single() is { FoundationAcceptedAt: initial + 5, KeepUntil: initial + 25 } &&
            !store.Messages.ContainsKey(key) && observed.Count == count && observed.All(item => item.Payload == "from turn") &&
            observed.Select(item => item.Wait).Distinct().Count() == count,
            "the successor finishes matching once and keeps the original author commit's retention deadline");
    }

    private static async Task CheckPartialPublicationRecovery(Contract.RecoverMode mode, Action<bool, string> check,
        bool takeover = false, bool selectPublication = false, bool endBeforeTicket = false, ParkClearKind? clear = null)
    {
        const long initial = 1_701_400_097_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now)
        {
            ParkedRetentionWindow = clear == ParkClearKind.Expiry ? TimeSpan.FromMilliseconds(1) : null
        };
        var scope = new Contract.BoundScope("partial-publication");
        var count = 2 * InMemoryStore.ParkPageLimit + 1;
        var waits = Enumerable.Range(0, count).Select(index => ("Order-7", $"wait-{index:D3}")).ToArray();
        var probe = new Probe(waits, restoreKeep: false, separatePublisher: true);
        var fail = false;
        var rejected = 0;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed && fail && batch.Turns is [{ FoundationPublicationSequence: not null, FoundationCatchUpStep: not null }])
            {
                rejected++;
                throw new StoreException(Failure.Definite, "Refuse the publication step after the first match page.");
            }
        });
        var worker = Owner("partial-publication-first");
        var edge = new InMemoryIngress(store, new Requests(worker.Node), worker.Node, (_, kind) => kind is Contract.Kinds.Recover or Contract.Kinds.Discard or Contract.Kinds.Terminate or Contract.Kinds.Delete);
        await using var first = await Activate(worker);
        var subscriber = await Start("subscriber");
        var publisher = await Start("publisher");
        await Pump(first);
        var address = store.FoundationTarget(scope.Tenant, publisher.Id)!.Id;
        var target = new Contract.Target.Ref(publisher);
        await edge.SendAsync(scope, target, "emit", "publish");
        await Step(worker);
        if (endBeforeTicket)
        {
            // Admit the first recovery page before the publication advances to its new stored sequence.
            await edge.SendAsync(scope, target, "end-first", "end-model");
            for (var index = 1; index < InMemoryStore.ParkPageLimit; index++)
                await edge.SendAsync(scope, target, $"earlier-{index:D3}", "ordinary");
        }
        var key = store.Messages.Values.Single(row => row.Message.Kind == Kinds.Publish).Message.InboxKey;
        await RunMatch(worker);
        await RunMatch(worker);
        var page = store.Messages[key].Message.FoundationPublish;
        var prefix = store.Messages.Values.Count(message => message.Message.Kind == Kinds.Event);
        var label = clear?.ToString() ?? $"{mode}, takeover={takeover}, select-ticket={selectPublication}, earlier-end={endBeforeTicket}";
        check(page?.After is not null && store.FoundationEvents.Count == 1 && prefix == InMemoryStore.ParkPageLimit &&
            store.Subscriptions.Count == count,
            $"KP-1 {label}: first page commits before a separate publisher parks");
        if (page?.After is null) return;

        fail = true;
        for (var pass = 0; pass < 12 && store.Instances[address].Status == InstanceStatus.Running; pass++) await RunMatch(worker);
        var park = store.ReadFoundationActor(scope.Tenant, publisher.Id).Actor!;
        check(rejected == 2 && park.Park is not null &&
            store.Messages[key] is { Done: true, KeptBy: ParkKeepReason.Consumed, ParkDeadLetter: false } &&
            store.Messages[key].Message.FoundationPublish == page,
            $"KP-1 {label}: exhausting the later step keeps the already-landed cursor on the original publication");
        if (park.Park is null) return;
        if (clear is { } disposition)
        {
            var selected = (await new ActorList(store, 5).GetAsync(scope, publisher))!.Selection!;
            var commandKind = disposition switch
            {
                ParkClearKind.Discard => Contract.Kinds.Discard,
                ParkClearKind.Terminate => Contract.Kinds.Terminate,
                ParkClearKind.Delete => Contract.Kinds.Delete,
                _ => null
            };
            if (commandKind is null) now++;
            else await edge.SendAsync(scope, target, "clear-park", commandKind, Json.Write(new Contract.OperatorCommand(selected)));

            fail = false;
            await Pump(first);
            var cleared = store.Instances[address];
            check(cleared.ParkDiscard is null && (cleared.Retired || cleared.ParkDiscarded) &&
                store.Messages.GetValueOrDefault(key) is { Done: true, KeptBy: null, ParkDeadLetter: true } letter &&
                letter.Message.FoundationPublish == page && Observed(store, scope, subscriber).Count == prefix,
                $"KP-1 {label}: clearing the Park leaves the original cursor in a dead letter and never repeats the delivered prefix");
            return;
        }
        if (endBeforeTicket) await Step(worker);

        IReadOnlyList<string>? handles = null;
        if (mode == Contract.RecoverMode.Retry)
        {
            var selected = key;
            if (!selectPublication)
            {
                await edge.SendAsync(scope, target, "other", "other-consumed");
                var other = store.Messages.Values.Single(message => message.Message.Id == "other");
                other.Attempts = 2;
                await Step(worker);
                selected = other.Message.InboxKey;
            }
            var handle = store.SealFoundationKeptHandle(scope.Tenant, publisher.Id, selected);
            check(handle is not null, $"KP-1 {label}: the operator can select the intended consumed envelope");
            if (handle is null) return;
            handles = [handle];
        }
        var selection = (await new ActorList(store, 5).GetAsync(scope, publisher))!.Selection!;
        var accepted = await edge.SendAsync(scope, target, "remove-park", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(selection, mode, handles)));
        fail = false;
        var reachedEndFirst = false;
        for (var pass = 0; pass < 18 && store.Messages[key].Done; pass++)
        {
            if (endBeforeTicket && pass != 0 && pass % 3 == 0) now += 11;
            await Step(worker);
            reachedEndFirst |= store.Messages[key].Done && store.Instances[address] is
                { Status: InstanceStatus.Finished, ParkRecovery: not null };
        }
        if (endBeforeTicket)
            check(reachedEndFirst, "KP-1 the restored first page ends the publisher before the retained ticket opens");
        var restored = (!endBeforeTicket || reachedEndFirst) && accepted.Outcome == Contract.AdmissionOutcome.Accepted && !store.Messages[key].Done &&
            store.Messages[key].KeptBy is null && store.Messages[key].Message.FoundationPublish == page;
        check(restored, $"KP-1 {label}: the fenced Recover page restores the exact publish ticket without resetting its match cursor");
        if (!restored) return;
        if (takeover)
        {
            await first.DisposeAsync();
            now += 11;
            await using var cold = await Activate(Owner("partial-publication-cold"));
            // The first page's inherited inputs may each need a fenced trial before their subscriber runs.
            for (var pass = 0; pass < count / 10 + 10 && Observed(store, scope, subscriber).Count < count; pass++)
                await Pump(cold);
        }
        else
        {
            for (var pass = 0; pass < 6 && (store.Instances[address].ParkRecovery is not null ||
                Observed(store, scope, subscriber).Count < count); pass++)
            {
                await Pump(first);
                now += 11;
            }
        }
        var delivered = Observed(store, scope, subscriber);
        var resolved = store.Instances[address];
        var finished = resolved.ParkRecovery is null &&
            resolved.Status == (endBeforeTicket ? InstanceStatus.Finished : InstanceStatus.Running) &&
            !store.Messages.ContainsKey(key) && delivered.Count == count &&
            delivered.Select(item => item.Wait).Distinct(StringComparer.Ordinal).Count() == count &&
            delivered.All(item => item.Payload == "from turn");
        check(finished, $"KP-1 {label}: all 341 waits receive the original publication once through the real subscriber" +
            (finished ? "" : $" (observed {delivered.Count}, publisher {resolved.Status}, drain {resolved.ParkRecovery}, ticket {store.Messages.ContainsKey(key)})"));

        async Task<Contract.ActorRef> Start(string definition)
        {
            var start = await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, definition)), definition, Contract.Kinds.Start);
            return start.Actor!.Value;
        }
        async Task Step(Worker owner)
        {
            await owner.PollAsync();
            await owner.LoadAsync();
            await owner.FlushAsync();
            owner.RunTurn(address);
            await owner.FlushAsync();
        }
        static async Task RunMatch(Worker owner)
        {
            await owner.PollAsync();
            await owner.LoadAsync();
            await owner.MeetAsync();
            await owner.FlushAsync();
        }
        Worker Owner(string node) => new(node, 0, wire, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - initial, MaxAttempts = 2 });
        static async Task<IScopeActivation> Activate(Worker owner) => (await new DurableHost([owner]).ActivateAsync(0))!;
    }

    private enum RestoreAt { BeforeAuthor, BeforeInsert, BeforeMatch, AfterMatch }

    private static async Task CheckRestoredWait(RestoreAt point, Action<bool, string> check, bool keep = false)
    {
        var now = 1_701_400_091_000L;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("authored-restored-wait");
        var probe = new Probe([("Order-7", "wait")], restoreKeep: keep);
        var publicationSteps = 0;
        var catchUpSteps = 0;
        var observedStore = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed) return;
            publicationSteps += batch.Turns.Count(turn => turn.FoundationPublicationSequence is not null && turn.FoundationCatchUpFailed is null);
            catchUpSteps += batch.Turns.Count(turn => turn.FoundationCatchUpStep is not null && turn.FoundationPublicationSequence is null);
        });
        var worker = new Worker("restored-wait", 0, observedStore, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => 0 });
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node, (_, kind) => kind == Contract.Kinds.Recover);
        var publisher = await Begin("publisher");
        var restored = await Begin("restored");
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump(owner);
        await ingress.SendAsync(scope, new Contract.Target.Ref(restored), "fault", "fault");
        await Pump(owner);
        var target = new Contract.Target.Ref(restored);
        var selection = (await new ActorList(store, 10).GetAsync(scope, restored))!.Selection!;

        if (point == RestoreAt.BeforeAuthor)
        {
            await Restore();
            now -= 30_000;
        }
        await ingress.SendAsync(scope, new Contract.Target.Ref(publisher), "authored-publish", "publish");
        await Step(publisher);
        if (point == RestoreAt.BeforeInsert) await Restore();
        await Match();
        check(publicationSteps == 1 && store.FoundationEvents.Count == 1 && !store.Messages.Values.Any(row => row.Message.Kind == Kinds.Event),
            "the first authored step commits before any live or restored wait is matched");

        var catchUpBeforeRestore = catchUpSteps;
        if (point == RestoreAt.AfterMatch) await Match();
        if (point is RestoreAt.BeforeMatch or RestoreAt.AfterMatch) await Restore();
        if (point != RestoreAt.AfterMatch) await Match();
        await Pump(owner);
        if (point == RestoreAt.BeforeMatch && keep)
            check(publicationSteps == 2 && catchUpSteps == catchUpBeforeRestore + 1,
                "both the publication page and keeping catch-up commit after the insert");
        var expected = point != RestoreAt.AfterMatch || keep ? 1 : 0;
        var shift = point == RestoreAt.BeforeAuthor ? " followed by a 30-second clock backstep" : "";
        check(Observed(store, scope, publisher) is [{ Payload: "from turn" }] &&
            Observed(store, scope, restored).Count == expected,
            $"restoring a {(keep ? "keeping" : "non-keeping")} wait at {point}{shift} gets {expected} authored deliveries");

        async Task<Contract.ActorRef> Begin(string id) =>
            (await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, id)), id, Contract.Kinds.Start)).Actor!.Value;

        async Task Step(Contract.ActorRef actor)
        {
            var id = store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row.Id;
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(id);
            await worker.FlushAsync();
        }

        async Task Match()
        {
            await worker.PollAsync();
            await worker.MeetAsync();
        }

        async Task Restore()
        {
            await ingress.SendAsync(scope, target, "recover", Contract.Kinds.Recover, Json.Write(new Contract.OperatorCommand(selection)));
            await Step(restored);
        }
    }

    private static Worker Owner(string node, InMemoryStore store, Probe probe) =>
        new(node, 0, store, new ActorAdapter(store, probe), new WorkerOptions { Monotonic = () => 0 });

    private static List<Observation> Observed(InMemoryStore store, Contract.BoundScope scope, Contract.ActorRef actor) =>
        Json.Read<List<Observation>>(store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row.State);

    private static async Task Pump(IScopeActivation activation)
    {
        for (var count = 0; count < 10; count++)
        {
            await activation.WokenAsync(null);
            await activation.RunAsync();
        }
    }

    private sealed record Observation(string Key, string Id, string Wait, string? Payload);

    private sealed class Probe((string Key, string Wait)[] waits, string? limitPayload = null, bool? restoreKeep = null,
        bool separatePublisher = false) : Contract.IActorType
    {
        public string Type => "event-identity";
        public bool Withholds(Contract.ActorHeader header) => false;
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var observed = turn.State is { } previous ? Json.Read<List<Observation>>(previous) : [];
            foreach (var message in turn.Inbox)
            {
                if (message.Kind is "publish" or "publish-and-end")
                {
                    turn.Publish("Order-7", "from turn", TimeSpan.FromMilliseconds(20));
                    if (message.Kind == "publish-and-end") turn.End(Contract.ActorStatus.Finished);
                }
                if (message.Kind == "publish-sized")
                    turn.Publish(waits[0].Key, message.Payload is null ? limitPayload : limitPayload + "<", TimeSpan.FromMinutes(1));
                if (message.Kind == "fault") throw new InvalidOperationException("The fixture parks the wait.");
                if (message.Kind == "end-model") turn.End(Contract.ActorStatus.Finished);
                if (message.Kind == "unsubscribe") turn.Unsubscribe("order-7", "WAIT");
                if (message.Kind == Contract.Kinds.Start && (!separatePublisher || turn.Header.TypeData == "subscriber"))
                    foreach (var (key, wait) in waits) turn.Subscribe(key, wait, keep: restoreKeep ?? true);
                if (message.Event is { } delivery)
                    observed.Add(new(delivery.Key, delivery.Id, delivery.Wait,
                        limitPayload is not null && message.Payload == limitPayload ? "equal to limit payload" : message.Payload));
            }
            turn.State = Json.Write(observed);
            return default;
        }
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) =>
            new([], [], (!separatePublisher || header.TypeData == "subscriber") && restoreKeep is { } keep ? waits.Select(wait => new Contract.SubscriptionSpec(wait.Key, wait.Wait, keep)).ToArray() : []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
