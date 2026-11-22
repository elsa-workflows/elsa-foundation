using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class FoundationEventSendChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long initial = 1_700_800_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now) { IngressDeduplicationWindow = TimeSpan.FromMilliseconds(40) };
        var scope = new Contract.BoundScope("acme");
        var ingress = new EventIngress(store, new Requests("event-one-way"), "event-owner");
        var worker = new Worker("event-owner", 0, store, new ActorAdapter(store, new Probe()), new WorkerOptions { Monotonic = () => now - initial });
        await using var activation = (await new DurableHost([worker]).ActivateAsync(0))!;
        var start = new Contract.StartSpec("event-send", "one-way");
        var quiet = new Contract.Target.Event("quiet");
        var keep = new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(20));

        var refused = await ingress.SendAsync(scope, quiet, "~issued", "forged", keep);
        check(refused is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.InvalidEventId } &&
            store.FoundationEvents.Count == 0, "outside event admission reserves the turn-issued id domain");
        var ephemeral = await ingress.SendAsync(scope, quiet, "quiet", "unkept", null);
        await Pump(activation);
        now++;
        await ingress.SetStartAsync(scope, quiet.CorrelationKey, "future", start);
        check(ephemeral.Outcome == Contract.AdmissionOutcome.Accepted && store.Messages.Values.All(row => row.Done),
            "a one-way send without waiters is admitted and stays unavailable to future waits unless kept");

        var retained = new Contract.Target.Event("retained");
        var kept = await ingress.SendAsync(scope, retained, "kept", "caught-up", keep);
        var duplicate = await ingress.SendAsync(scope, retained, "kept", "changed", keep);
        await ingress.SetStartAsync(scope, retained.CorrelationKey, "late-start", start);
        check(kept.Outcome == Contract.AdmissionOutcome.Accepted && duplicate.Outcome == Contract.AdmissionOutcome.Duplicate &&
            store.Messages.Values.Count(row => row.Message.Kind is Kinds.Start or Kinds.Event) == 2,
            "a kept event without waiters catches up one later start, with its original payload");

        var live = InstanceRef.In(0, "event-live-root");
        var liveRef = store.AdmitFoundationStart(scope.Tenant, new Message("arm", live, Kinds.Start,
            Start: new StartSpec("event-send", "live", "arm", Creation: new(1, 0, "arm")), TargetCreation: 1))!;
        await Pump(activation);
        check(store.ReadFoundationActors(scope.Tenant).Actors.Any(item => item.Header.TypeData == "one-way" &&
                Json.Read<List<string>>(item.Row.State).SequenceEqual(["caught-up"])),
            "the late start processes the retained payload through the real actor adapter");

        var topic = new Contract.Target.Event("live");
        var first = await ingress.SendAsync(scope, topic, "reusable", "first", keep);
        await Pump(activation);
        now += 20;
        var onBoundary = await ingress.SendAsync(scope, topic, "reusable", "duplicate", keep);
        now++;
        await store.SweepAsync();
        var afterKeep = await ingress.SendAsync(scope, topic, "reusable", "after keep", keep);
        now += 20;
        var beforeSweep = await ingress.SendAsync(scope, topic, "reusable", "premature", keep);
        await store.SweepAsync();
        var second = await ingress.SendAsync(scope, topic, "reusable", "second", keep);
        await Pump(activation);
        check(first.Outcome == Contract.AdmissionOutcome.Accepted && second.Outcome == Contract.AdmissionOutcome.Accepted &&
            onBoundary.Outcome == Contract.AdmissionOutcome.Duplicate && beforeSweep.Outcome == Contract.AdmissionOutcome.Duplicate &&
            afterKeep is { Outcome: Contract.AdmissionOutcome.Duplicate, Publication.Status: Contract.PublicationStatus.Matched } &&
            afterKeep.Publication.Incarnation == first.Publication?.Incarnation && second.Publication!.Incarnation != first.Publication?.Incarnation &&
            Json.Read<List<string>>(store.ReadFoundationActor(scope.Tenant, liveRef).Actor!.Row.State).SequenceEqual(["first", "second"]),
            "an expired publication stays the receipt until sweep and its old match cannot suppress the new delivery");
    }

    public static async Task SubscriptionLifecycle(Action<bool, string> check)
    {
        const long now = 1_701_500_000_000;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        Contract.IIngress edge = new InMemoryIngress(store, new Requests("subscription-check"), "subscription-check");
        var actor = new Probe();
        var worker = new Worker("subscription-check", 0, store, new ActorAdapter(store, actor), new WorkerOptions { Monotonic = () => 0 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var scenarios = new[] { ("remove", InstanceStatus.Running), ("park", InstanceStatus.Faulted),
            ("end", InstanceStatus.Finished) };
        var subjects = new Dictionary<string, Contract.ActorRef>();
        foreach (var (action, _) in scenarios)
        {
            await edge.SendAsync(scope, new Contract.Target.Event(action), "earlier", "ignored", "earlier",
                new(KeepFor: TimeSpan.FromMinutes(1)));
            await Pump(owner);
            var spec = new Contract.StartSpec(actor.Type, action, "twice");
            subjects.Add(action, (await edge.SendAsync(scope, new Contract.Target.Start(spec), action, Contract.Kinds.Start)).Actor!.Value);
        }
        await Pump(owner);
        check(store.Subscriptions.Count == 3 && subjects.Values.All(subject => Results(subject).Count == 0),
            "same-wait registration is idempotent and ordinary waits skip events kept before registration");

        foreach (var (action, _) in scenarios)
            await edge.SendAsync(scope, new Contract.Target.Event(action), "before", "ignored", "before");
        await Pump(owner);
        check(subjects.Values.All(subject => Results(subject).SequenceEqual(["before"])),
            "an event committed after registration reaches each live wait once");

        foreach (var (action, _) in scenarios)
            await edge.SendAsync(scope, new Contract.Target.Ref(subjects[action]), action, "wait-" + action);
        await Pump(owner);
        foreach (var (action, status) in scenarios)
        {
            var subject = subjects[action];
            var row = Row(subject);
            await edge.SendAsync(scope, new Contract.Target.Event(action), "after", "ignored", "after");
            await Pump(owner);
            check(row.Status == status && Results(subject).SequenceEqual(["before"]) &&
                store.Subscriptions.All(wait => wait.Instance != row.Id) &&
                store.Messages.Values.All(item => item.Message.Target.Id != row.Id || item.Message.Payload != "after"),
                $"wait-{action} removes the subscription at commit and later publication cannot queue a delivery");
        }

        var topic = new Contract.Target.Event("start");
        var start = new Contract.StartSpec(actor.Type, "started-by-event");
        await edge.SetStartAsync(scope, topic.CorrelationKey, "only", start);
        await edge.SendAsync(scope, topic, "on", "ignored", "on");
        await Pump(owner);
        await edge.SetStartAsync(scope, topic.CorrelationKey, "only", null);
        await edge.SendAsync(scope, topic, "off", "ignored", "off");
        await Pump(owner);
        var spawned = store.ReadFoundationActors(scope.Tenant).Actors.Where(item => item.Header.TypeData == "started-by-event").ToArray();
        check(spawned is [var only] && Json.Read<List<string>>(only.Row.State).SequenceEqual(["on"]),
            "only the event published while the start subscription exists starts an actor");

        InstanceRow Row(Contract.ActorRef subject) => store.ReadFoundationActor(scope.Tenant, subject.Id).Actor!.Row;
        List<string> Results(Contract.ActorRef subject) => Json.Read<List<string>>(Row(subject).State);
    }

    internal static async Task CheckPagedPublication(Action<bool, string> check)
    {
        await CheckConcurrentAdmission(check);
        await CheckMatchOrder(check);
        await CheckPage(check, byteCut: false, loseAfter: null);
        await CheckPage(check, byteCut: true, loseAfter: null);
        await CheckPage(check, byteCut: false, loseAfter: null, largeStart: true);
        await CheckPage(check, byteCut: false, loseAfter: false);
        await CheckPage(check, byteCut: false, loseAfter: true);
        await CheckDeadLetter(check);
        await CheckAlreadyDeliveredPage(check);
    }

    internal static async Task KeepingSubscription(Action<bool, string> check)
    {
        const long initial = 1_701_510_000_000;
        var now = initial;
        const string key = "keeping";
        var store = new InMemoryStore(1, () => now, Flaw.None, StringComparer.OrdinalIgnoreCase)
        {
            IngressDeduplicationWindow = TimeSpan.FromMilliseconds(10)
        };
        var scope = new Contract.BoundScope("keeping-scope");
        var actor = new Probe();
        var edge = new InMemoryIngress(store, new Requests("keeping-wait"), "keeping-wait");
        Worker Owner(string node) => new(node, 0, store, new ActorAdapter(store, actor), new WorkerOptions { Monotonic = () => 0 });
        var worker = Owner("keeping-wait");
        await using var first = (await new DurableHost([worker]).ActivateAsync(0))!;
        var started = await edge.SendAsync(scope, new Contract.Target.Start(new(actor.Type, key)), "actor", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(started.Actor!.Value);
        await Pump(first);

        await Publish(key, "owed", 100);
        await Publish("uncovered", "later-horizon", 110);
        await Pump(first);
        now++;
        await Turn("register", "wait-keep");
        var registration = store.Subscriptions.SingleOrDefault();
        var ticket = store.Messages.Values.SingleOrDefault(row => row.Message.ParkCatchUp is not null);
        var opened = registration is { FoundationKeepsEvents: true, FoundationRegisteredAt: initial + 1, FoundationIncarnation: not null } &&
            ticket is { Done: false, Message: { HasIssuedTicket: true, ParkCatchUp.Entitlement: initial + 1 } };
        check(opened, "a keeping subscription stamps its first registration and commits one entitled meet ticket with it");
        if (!opened) return;

        now++;
        await Turn("duplicate", "wait-keep");
        check(store.Subscriptions.Single() == registration &&
            store.Messages.Values.Single(row => row.Message.ParkCatchUp is not null) == ticket,
            "same-turn and later equivalent registrations keep one incarnation, its stamp and its meet ticket");

        now = initial + 111;
        await store.SweepAsync();
        check(store.FoundationEvents.Values.Select(row => row.Id).SequenceEqual(["owed"]),
            "coverage holds the owed event while the sweep removes an uncovered event and advances the horizon");
        await first.DisposeAsync();
        worker = Owner("keeping-successor");
        await using var successor = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump(successor);
        check(Observed().SequenceEqual(["owed"]) && store.Messages.Values.All(row => row.Message.ParkCatchUp is null),
            "the successor finishes the committed catch-up and delivers the expired owed event once");

        await Turn("remove", "wait-remove");
        check(store.Subscriptions.Count == 0, "unsubscribing removes the covered incarnation");
        now = initial + 50;
        await successor.DisposeAsync();
        worker = Owner("keeping-backstep");
        await using var latest = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Publish(key, "below-horizon", 30);
        await Pump(latest);
        await Turn("register-again", "wait-keep");
        check(store.Subscriptions.SingleOrDefault() is { FoundationRegisteredAt: initial + 50 } replacement &&
            replacement.FoundationIncarnation != registration!.FoundationIncarnation &&
            store.FoundationEvents.Values.Any(row => row.Id == "below-horizon" && row.KeepUntil == initial + 80) &&
            store.Messages.Values.SingleOrDefault(row => row.Message.ParkCatchUp is not null) is
                { Message.ParkCatchUp.Entitlement: initial + 111 },
            "a new incarnation after clock rollback cannot claim events at or below the recorded horizon");
        await Pump(latest);
        check(Observed().SequenceEqual(["owed"]) && store.Messages.Values.All(row => row.Message.ParkCatchUp is null),
            "the later incarnation completes without replaying the old publication or claiming an event behind the horizon");

        async Task Publish(string topic, string id, long keep) =>
            _ = await edge.SendAsync(scope, new Contract.Target.Event(topic), id, "ignored", id,
                new(KeepFor: TimeSpan.FromMilliseconds(keep)));

        List<string> Observed() => Json.Read<List<string>>(store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!.Row.State);

        async Task Turn(string id, string kind)
        {
            await edge.SendAsync(scope, target, id, kind);
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(store.FoundationTarget(scope.Tenant, target.Actor.Id)!.Id);
            await worker.FlushAsync();
        }
    }

    private static async Task CheckMatchOrder(Action<bool, string> check)
    {
        var now = 1_702_200_020_000L;
        var incarnation = 0;
        var store = new InMemoryStore(2, () => now) { NewPublicationIncarnation = () => Incarnation(++incarnation) };
        var scope = new Contract.BoundScope("publication-order");
        var ingress = new InMemoryIngress(store, new Requests("order"), "order");
        var events = new EventIngress(store, new Requests("publish-order"), "order");
        var left = StartWait(0, "left");
        var right = StartWait(1, "right");
        var workers = Enumerable.Range(0, 2).Select(partition => new Worker("order", partition, store,
            new ActorAdapter(store, new Probe()), new WorkerOptions { Monotonic = () => 0 })).ToArray();
        await using var firstOwner = (await new DurableHost([workers[0]]).ActivateAsync(0))!;
        await using var secondOwner = (await new DurableHost([workers[1]]).ActivateAsync(1))!;
        await Pump(firstOwner);
        await Pump(secondOwner);

        var first = EventId(0, Incarnation(1));
        var second = EventId(1, Incarnation(2));
        var a = await events.SendAsync(scope, new("live"), first, new string('A', 3 * 1_024 * 1_024), null);
        now++;
        var b = await events.SendAsync(scope, new("live"), second, "B", null);
        check(a is { Outcome: Contract.AdmissionOutcome.Accepted, Publication.Status: Contract.PublicationStatus.Pending } &&
            b is { Outcome: Contract.AdmissionOutcome.Accepted, Publication.Status: Contract.PublicationStatus.Pending } &&
            !store.Messages.Values.Any(row => row.Message.Kind == Kinds.Event),
            "accepted receipts precede match commits for both independent publications");

        await Step(workers[0]); // A reaches left, then stops at the byte allowance.
        await Step(workers[1]); // B reaches both waits before A can reach right.
        await Step(workers[0]); // A reaches right.
        await Pump(firstOwner);
        await Pump(secondOwner);
        check(PayloadOrder(left).SequenceEqual(['A', 'B']) && PayloadOrder(right).SequenceEqual(['B', 'A']),
            "each wait observes publication match commit order across overlapping pages and independent publishers");

        await ingress.SendAsync(scope, new Contract.Target.Ref(left), "relay", "send-direct-pair", right.Id);
        await Pump(firstOwner);
        await Pump(secondOwner);
        check(PayloadOrder(right).Where(payload => payload is 'X' or 'Y').SequenceEqual(['X', 'Y']),
            "the same actor's direct sends to one target stay in authored FIFO order");

        Contract.ActorRef StartWait(int partition, string name)
        {
            var target = InstanceRef.In(partition, name);
            var reference = store.AdmitFoundationStart(scope.Tenant, new Message(name, target, Kinds.Start,
                Start: new("event-send", "live", "twice", Creation: new(1, 0, name)), TargetCreation: 1));
            return new(reference ?? throw new InvalidOperationException("The test wait was not admitted."));
        }

        IEnumerable<char> PayloadOrder(Contract.ActorRef actor) =>
            Json.Read<List<string>>(store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row.State).Select(payload => payload[0]);

        string EventId(int partition, Guid identity) => Enumerable.Range(0, 32).Select(i => $"{partition}-{i}").First(id =>
            InstanceRef.Spread(FoundationEventIdentity.PublishId(new(scope.Tenant + "_live", id, null, now), identity), 2) == partition);

        static Guid Incarnation(int id) => new(id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private static async Task CheckConcurrentAdmission(Action<bool, string> check)
    {
        const long now = 1_702_200_010_000;
        var store = new InMemoryStore(1, () => now, Flaw.None, StringComparer.OrdinalIgnoreCase);
        var scope = new Contract.BoundScope("publication-race");
        var edge = new EventIngress(store, new Requests("race"), "race");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Contract.Admission> Publish(string key, string id) => Task.Run(async () =>
        {
            await ready.Task;
            return await edge.SendAsync(scope, new(key), id, key, null);
        });
        var first = Publish("Topic", "E");
        var second = Publish("topic", "e");
        ready.SetResult();
        var receipts = await Task.WhenAll(first, second);
        var original = store.FoundationEvents.Values.Single();
        check(receipts.Count(result => result.Outcome == Contract.AdmissionOutcome.Accepted) == 1 &&
            receipts.Count(result => result.Outcome == Contract.AdmissionOutcome.Duplicate) == 1 &&
            receipts[0].Publication == receipts[1].Publication && receipts[0].Publication?.EventId == original.Id &&
            original.Payload == receipts[0].Publication?.CorrelationKey && store.Messages.Count == 1,
            "concurrent folded first submissions leave one ticket and answer with the winner's spelling and incarnation");
    }

    private static async Task CheckPage(Action<bool, string> check, bool byteCut, bool? loseAfter, bool largeStart = false)
    {
        const long start = 1_702_200_000_000;
        var now = start;
        var store = new InMemoryStore(1, () => now, Flaw.None, StringComparer.OrdinalIgnoreCase);
        var scope = new Contract.BoundScope("broadcast");
        var edge = new EventIngress(store, new Requests("publisher"), "publisher");
        var count = byteCut ? 2 : InMemoryStore.ParkPageLimit + 1;
        var firstSize = byteCut ? 1 : InMemoryStore.ParkPageLimit;
        var typeData = largeStart ? new string('z', FoundationMetadataLimits.TypeDataBytes) : "subscriber";
        for (var i = count - 1; i >= 0; i--)
            await edge.SetStartAsync(scope, "Signal", $"wait-{i:D3}", new("event-send", typeData));
        var payload = byteCut ? new string('x', ParkRecoveryCredit.LimitBytes) : largeStart ? new string('x', 16 * 1_024) : "intact";
        var accepted = await edge.SendAsync(scope, new("signal"), "Event", payload, new(KeepFor: TimeSpan.Zero));
        var duplicate = await edge.SendAsync(scope, new("SIGNAL"), "event", "replacement", null);
        var evt = store.FoundationEvents.Values.Single();
        var ticket = store.Messages.Values.Single();
        var key = ticket.Message.InboxKey;
        check(accepted is { Outcome: Contract.AdmissionOutcome.Accepted, Publication.Status: Contract.PublicationStatus.Pending } &&
              duplicate is { Outcome: Contract.AdmissionOutcome.Duplicate, Publication: { CorrelationKey: "signal", EventId: "Event" } } &&
              accepted.Publication == duplicate.Publication && evt.FoundationAcceptedAt == now && evt.KeepUntil == now &&
              ticket.Message.Payload is null && evt.FoundationPublishTicket == key &&
              key.Id == FoundationEventIdentity.PublishId(evt, evt.FoundationIncarnation!.Value),
            "outside admission atomically stores one event and one payload-free issued ticket with the original receipt");

        CommitBatch? firstRequest = null;
        CommitBatch? firstReservation = null;
        var lost = false;
        var lateRefused = false;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed && batch.Trial.Contains(key)) firstReservation ??= batch;
            if (batch.Turns is not [{ FoundationPublicationSequence: not null, FoundationCatchUpStep: not null }]) return;
            if (loseAfter == landed && !lost)
            {
                lost = true;
                throw new StoreException(Failure.Unknown, "publisher page outcome unavailable");
            }
            if (landed) return;
            if (firstRequest is null) firstRequest = batch;
            else if (loseAfter is null && !lateRefused)
                try { _ = store.CommitAsync(firstRequest).Result; }
                catch (StoreException error) when (error.Failure == Failure.Definite) { lateRefused = true; }
        });
        Worker NewWorker() => new("publisher", 0, wire, new ActorAdapter(store, new Probe()),
            new WorkerOptions { Monotonic = () => now - start, MaxAttempts = 2 });
        var worker = NewWorker();
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Step(worker);

        var front = store.Messages[key];
        if (loseAfter == false)
            check(lost && !worker.Owns && Deliveries() == 0 && front is { Attempts: 0, Trial: > 0, Message.FoundationPublish.After: null },
                "a lost page before its commit preserves just the reserved current ticket");
        else if (largeStart)
        {
            var written = Deliveries();
            var actualContent = store.Messages.Values.Sum(row => (row.Message.Payload?.Length ?? 0L) + (row.Message.Start?.Definition.Length ?? 0L));
            check(written is > 0 and < InMemoryStore.ParkPageLimit && actualContent <= ParkRecoveryCredit.LimitBytes &&
                front.Message.FoundationPublish?.After?.Wait == $"wait-{written - 1:D3}",
                "the byte cut counts stored start TypeData alongside every event payload");
        }
        else
            check(Deliveries() == firstSize && front is { Done: false, Attempts: 0, Trial: 0, Message.FoundationPublish.After: { } cursor } &&
                cursor.Wait == $"wait-{firstSize - 1:D3}" && front.Seq != ticket.Seq,
                byteCut ? "one delivery above 4 MiB makes singleton progress" : "one page commits exactly the 170 indexed pairs and advances its cursor");

        if (loseAfter is null)
        {
            var oldReservationRefused = false;
            try { await store.CommitAsync(firstReservation!); }
            catch (StoreException error) when (error.Failure == Failure.Definite) { oldReservationRefused = true; }
            check(oldReservationRefused && front is { Seen: 0, Trial: 0, Attempts: 0 },
                "a late reservation for the old sequence cannot mark the next page seen or charge it on takeover");
        }

        now++;
        await store.SweepAsync();
        check(store.FoundationEvents.Count == 1, "sweep preserves the source past its keep window while its page is pending");
        if (loseAfter.HasValue)
        {
            await owner.DisposeAsync();
            worker = NewWorker();
            await using var successor = (await new DurableHost([worker]).ActivateAsync(0))!;
            check(lost && store.Messages[key] is { Trial: 0 } reloaded && reloaded.Attempts == (loseAfter.Value ? 0 : 1),
                "takeover charges only a reserved page that did not land");
            await Finish(worker);
        }
        else
        {
            await Finish(worker);
            check(lateRefused, "a late first page is refused after the same ticket's next attempt is reserved");
        }
        var done = await edge.SendAsync(scope, new("SIGNAL"), "event", null, null);
        check(Deliveries() == count && store.Messages.Values.Count(row => row.Message.InboxScope == InboxKey.EventCreatorScope) == count &&
              store.Messages[key].Done && done.Publication is { Status: Contract.PublicationStatus.Matched } &&
              done.Publication.Incarnation == accepted.Publication!.Incarnation,
            "the completed broadcast stores one creator and delivery per wait and retains its matched receipt");

        int Deliveries() => store.Messages.Values.Count(row => row.Message.Kind == Kinds.Event);
        static async Task Finish(Worker worker)
        {
            for (var step = 0; step < 4; step++) await Step(worker);
        }
    }

    private static async Task CheckDeadLetter(Action<bool, string> check)
    {
        const long start = 1_702_200_020_000;
        var now = start;
        var store = new InMemoryStore(1, () => now) { IngressDeduplicationWindow = TimeSpan.FromMilliseconds(20) };
        var scope = new Contract.BoundScope("broadcast-dead-letter");
        var edge = new EventIngress(store, new Requests("publisher"), "publisher");
        await edge.SetStartAsync(scope, "live", "wait", new("event-send", "receiver"));
        var published = await edge.SendAsync(scope, new("live"), "stalled", "skip", null);
        var failedKey = store.FoundationEvents.Values.Single().FoundationPublishTicket!.Value;
        await edge.SendAsync(scope, new("live"), "healthy", "deliver", null);
        CommitBatch? firstFailure = null;
        var oldAttemptRefused = false;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed || batch.Turns is not [{ FoundationCatchUpStep: { } key }] || key != failedKey) return;
            if (firstFailure is null) firstFailure = batch;
            else
                try { _ = store.CommitAsync(firstFailure).Result; }
                catch (StoreException error) when (error.Failure == Failure.Definite) { oldAttemptRefused = true; }
            throw new StoreException(Failure.Definite, "publication page did not land");
        });
        var worker = new Worker("publisher", 0, wire, new ActorAdapter(store, new Probe()),
            new WorkerOptions { Monotonic = () => now - start, MaxAttempts = 2 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        for (var step = 0; step < 8; step++) await Step(worker);
        now++;
        await store.SweepAsync();
        var repeat = await edge.SendAsync(scope, new("live"), "stalled", "ignored", null);
        check(oldAttemptRefused, "a late request cannot use a newer reservation on the same sequence after its own failure was charged");
        check(repeat is { Outcome: Contract.AdmissionOutcome.Duplicate, Publication.Status: Contract.PublicationStatus.DeadLettered } &&
              repeat.Publication.Incarnation == published.Publication!.Incarnation && store.Messages[failedKey] is { Done: true, Attempts: 2 } &&
              store.Messages.Values.Count(row => row.Message.Kind == Kinds.Event) == 1,
            "an exhausted publication keeps a dead-letter receipt and does not block an unrelated page");
        now += 20;
        await store.SweepAsync();
        check(!store.FoundationEvents.ContainsKey((scope.Tenant + "_live", "stalled")) && !store.Messages.ContainsKey(failedKey),
            "outside dead-letter retention ends at the receipt window when the keep window was shorter");
    }

    private static async Task CheckAlreadyDeliveredPage(Action<bool, string> check)
    {
        const long now = 1_702_200_030_000;
        const int count = 12;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("prior-deliveries");
        var edge = new EventIngress(store, new Requests("publisher"), "publisher");
        await edge.SendAsync(scope, new("live"), "kept", new string('x', 1_024 * 1_024), new(KeepFor: TimeSpan.FromMinutes(1)));
        for (var i = 0; i < count; i++)
            await edge.SetStartAsync(scope, "live", $"late-{i:D2}", new("event-send", "receiver"));

        var published = store.FoundationEvents.Values.Single().FoundationPublishTicket!.Value;
        var allocatedAt = 0L;
        var allocation = -1L;
        var thread = 0;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (batch.Turns is not [{ FoundationCatchUpStep: { } key }] || key != published) return;
            if (!landed)
            {
                thread = Environment.CurrentManagedThreadId;
                allocatedAt = GC.GetAllocatedBytesForCurrentThread();
            }
            else if (thread == Environment.CurrentManagedThreadId)
                allocation = GC.GetAllocatedBytesForCurrentThread() - allocatedAt;
        });
        var worker = new Worker("publisher", 0, wire, new ActorAdapter(store, new Probe()), new WorkerOptions { Monotonic = () => 0 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Step(worker);
        check(allocation is >= 0 and < 1_024 * 1_024 && store.Messages[published].Done &&
            store.Messages.Values.Count(row => row.Message.Kind == Kinds.Event) == count,
            "a page advances past existing deliveries without copying their megabyte payloads");
    }

    private static async Task Step(Worker worker)
    {
        await worker.PollAsync();
        await worker.MeetAsync();
    }

    private static async Task Pump(IScopeActivation activation)
    {
        for (var step = 0; step < 20; step++)
        {
            await activation.WokenAsync(null);
            await activation.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "event-send";
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var state = turn.State is null ? [] : Json.Read<List<string>>(turn.State);
            foreach (var message in turn.Inbox)
            {
                switch (message)
                {
                    case { Kind: Contract.Kinds.Start, Payload: "arm" }:
                        turn.Subscribe("live", "active");
                        break;
                    case { Kind: Contract.Kinds.Start, Payload: "twice" }:
                        turn.Subscribe(turn.Header.TypeData, "single");
                        turn.Subscribe(turn.Header.TypeData, "single");
                        break;
                    case { Kind: Contract.Kinds.Event, Payload: { } input }:
                        state.Add(input);
                        break;
                    case { Kind: "send-direct-pair", Payload: { } target }:
                        turn.Send(new(target), "direct-order", "X");
                        turn.Send(new(target), "direct-order", "Y");
                        break;
                    case { Kind: "direct-order", Payload: { } direct }:
                        state.Add(direct);
                        break;
                    case { Kind: "wait-remove" }:
                        turn.Unsubscribe(turn.Header.TypeData, "single");
                        break;
                    case { Kind: "wait-keep" }:
                        turn.Subscribe(turn.Header.TypeData, "single", keep: true);
                        turn.Subscribe(turn.Header.TypeData.ToUpperInvariant(), "SINGLE", keep: true);
                        break;
                    case { Kind: "wait-end" }:
                        turn.End(Contract.ActorStatus.Finished);
                        break;
                    case { Kind: "wait-park" }:
                        turn.Subscribe("must-not-commit", "single");
                        throw new InvalidOperationException("subscription Park witness");
                }
            }
            turn.State = Json.Write(state);
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
