using System.Text;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class EventIdentityChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var fill in new[] { '\u0001', '<' })
            await AtNameLimits(fill, check);
        await PublicationReuse(StringComparer.OrdinalIgnoreCase, check);
        await PublicationReuse(StringComparer.Ordinal, check);
        await PublicationReuse(StringComparer.Ordinal, check, slowConsumer: true);
        await UnsettledTicket(check);
        await FoldedRegistration(check);
        await LegacyTicket(doneDelivery: false, check);
        await LegacyTicket(doneDelivery: true, check);
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
        var ticket = store.Messages.Values.Single(row => row.Message.Kind == Kinds.Publish).Message;
        var publication = FoundationEventTicket.Decode(ticket);
        if (doneDelivery)
            store.Messages[ticket.InboxKey].Message = ticket with { Payload = Json.Write(publication) };
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
        var ticket = store.Messages.Values.Single(row => row.Message.Kind == Kinds.Publish).Message.InboxKey;
        await worker.MeetAsync();
        var original = store.FoundationEvents.Single();
        now += 21;
        await store.SweepAsync();
        check(store.Messages.GetValueOrDefault(ticket) is { Done: false } &&
            store.FoundationEvents.GetValueOrDefault(original.Key)?.FoundationIncarnation == original.Value.FoundationIncarnation,
            "an expired turn publication keeps its incarnation while its owning ticket is still pending");

        await worker.FlushAsync();
        await store.SweepAsync();
        check(!store.Messages.ContainsKey(ticket) && !store.FoundationEvents.ContainsKey(original.Key),
            "a sweep can remove an expired turn publication after its ticket settles");
        await Pump(active);
        check(Observed(store, scope, actor) is [{ Payload: "from turn" }],
            "the delivery remains available after the settled publication is swept");
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

    private sealed class Probe((string Key, string Wait)[] waits, string? limitPayload = null) : Contract.IActorType
    {
        public string Type => "event-identity";
        public bool Withholds(Contract.ActorHeader header) => false;
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var observed = turn.State is { } previous ? Json.Read<List<Observation>>(previous) : [];
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == "publish") turn.Publish("Order-7", "from turn", TimeSpan.FromMilliseconds(20));
                if (message.Kind == "unsubscribe") turn.Unsubscribe("order-7", "WAIT");
                if (message.Kind == Contract.Kinds.Start)
                    foreach (var (key, wait) in waits) turn.Subscribe(key, wait, keep: true);
                if (message.Event is { } delivery)
                    observed.Add(new(delivery.Key, delivery.Id, delivery.Wait,
                        limitPayload is not null && message.Payload == limitPayload ? "equal to limit payload" : message.Payload));
            }
            turn.State = Json.Write(observed);
            return default;
        }
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
