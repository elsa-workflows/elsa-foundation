using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkKeptChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long initial = 1_701_400_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var actor = new Probe();
        var scope = new Contract.BoundScope("park");
        Worker MakeOwner(string node) => new(node, 0, store, new ActorAdapter(store, actor),
            new WorkerOptions { Monotonic = () => now - initial });
        var worker = MakeOwner("park-first");
        Contract.IIngress edge = new InMemoryIngress(store, new Requests("park-first"), "park-first");
        var start = await edge.SendAsync(scope, new Contract.Target.Start(new(actor.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(start.Actor!.Value);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump(owner);

        await edge.SendAsync(scope, target, "older", "update", options: new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1)));
        await Pump(owner);
        var row = store.Instances.Values.Single();
        var ordinary = store.Messages.Values.Single(message => message.Message.Id == "older");
        check(ordinary is { Done: true, KeptBy: null } && row.State == "saved", "ordinary done input has no Park disposition");

        var poison = new Message("twin", new(row.Id), "poison", "private payload", TargetCreation: row.Creation).AsIssued();
        var twin = new Message("twin", new(row.Id), "update", "ordinary payload", TargetCreation: row.Creation, KeepUntil: now + 2);
        var staged = new TurnCommit(row.Id, null, null, [], [], [poison, twin], [], [], null);
        var fence = worker.Fences.Single();
        var wrote = await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [staged], [], []));
        check(wrote.Landed, "the fixture inserts both full keys under the owner's fence");
        await Pump(owner);

        var parked = await new ActorList(store, 10).GetAsync(scope, target.Actor);
        check(parked is { Header.Status: Contract.ActorStatus.Faulted, Park: not null } && store.Instances[row.Id].State == "saved",
            "the real failed turn parks without changing the last saved state");
        check(Retained(poison, "private payload", ParkKeepReason.Consumed) && Retained(twin, "ordinary payload", ParkKeepReason.Consumed),
            "the Park keeps both consumed deliveries by full key with their original payloads");
        check(store.Journal.Values.Count(entry => entry.Entries.Contains("parked", StringComparison.Ordinal)) == 1,
            "the Park checkpoint and journal land once");

        var priorOffers = actor.Offers;
        var admission = await edge.SendAsync(scope, target, "twin", "update", "late payload",
            options: new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1)));
        var late = store.Messages.Values.Single(message => message.Message.Payload == "late payload").Message;
        var checkpoint = store.Instances[row.Id];
        fence = worker.Fences.Single();
        var malformed = new TurnCommit(row.Id, checkpoint with { Revision = checkpoint.Revision + 1 }, checkpoint.Revision,
            [], [late.InboxKey], [], [], [], Json.Write(new[] { "must not land" }))
        { FoundationKept = new Dictionary<InboxKey, ParkKeepReason> { [late.InboxKey] = ParkKeepReason.Arrived } };
        var rejected = false;
        try
        {
            await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false,
                [malformed], [], [late.InboxKey]));
        }
        catch (StoreException error) when (error.Failure == Failure.Definite) { rejected = true; }
        check(rejected && store.Instances[row.Id] == checkpoint && store.Messages[late.InboxKey] is { Done: false, Trial: 0, KeptBy: null } &&
            store.Journal.Values.All(entry => !entry.Entries.Contains("must not land", StringComparison.Ordinal)),
            "a commit cannot keep a row and also put it on trial");

        await Pump(owner);
        check(admission.Outcome == Contract.AdmissionOutcome.Accepted && late.InboxKey != poison.InboxKey && late.InboxKey != twin.InboxKey &&
            Retained(late, "late payload", ParkKeepReason.Arrived) && actor.Offers == priorOffers &&
            (await new ActorList(store, 10).GetAsync(scope, target.Actor))?.Park == parked?.Park,
            "a late input with the same wire ID is kept as Arrived without entering the actor or replacing the Park");

        await edge.SendAsync(scope, target, "exhausted", "update", "exhausted payload",
            options: new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1)));
        var exhausted = store.Messages.Values.Single(message => message.Message.Id == "exhausted");
        exhausted.Attempts = 3;
        await Pump(owner);
        check(Retained(exhausted.Message, "exhausted payload", ParkKeepReason.Consumed) && actor.Offers == priorOffers &&
            (await new ActorList(store, 10).GetAsync(scope, target.Actor))?.Park == parked?.Park,
            "an exhausted late delivery is Consumed by the same Park on re-park");

        await edge.SendAsync(scope, target, "refused", "update", "refused payload",
            options: new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1)));
        var refused = store.Messages.Values.Single(message => message.Message.Id == "refused").Message;
        var refusals = 0;
        store.Refuses = candidate => candidate.Id == row.Id && ++refusals <= 2;
        await Pump(owner);
        store.Refuses = null;
        check(refusals == 3 && Retained(refused, "refused payload", ParkKeepReason.Consumed) && actor.Offers == priorOffers &&
            (await new ActorList(store, 10).GetAsync(scope, target.Actor))?.Park == parked?.Park,
            "a definite arrival commit refusal re-parks and keeps the input as Consumed");

        now += 3;
        await store.SweepAsync();
        check(!store.Messages.ContainsKey(ordinary.Message.InboxKey) && Retained(poison, "private payload", ParkKeepReason.Consumed) &&
            Retained(twin, "ordinary payload", ParkKeepReason.Consumed) && Retained(late, "late payload", ParkKeepReason.Arrived) &&
            Retained(exhausted.Message, "exhausted payload", ParkKeepReason.Consumed) && Retained(refused, "refused payload", ParkKeepReason.Consumed),
            "the sweep deletes expired ordinary markers and preserves expired kept rows");
        await owner.DisposeAsync();
        var successorWorker = MakeOwner("park-next");
        await using var successor = (await new DurableHost([successorWorker]).ActivateAsync(0))!;
        await Pump(successor);
        check(actor.PoisonOffers == 1 && actor.Offers == priorOffers && Retained(poison, "private payload", ParkKeepReason.Consumed) &&
            Retained(twin, "ordinary payload", ParkKeepReason.Consumed) && Retained(late, "late payload", ParkKeepReason.Arrived),
            "takeover does not offer done kept rows to the parked actor");

        await CheckKeptPages(store, successorWorker.Fences.Single(), row.Id, row.Creation, check);
        await CheckDiscardDrain(check);
        await CheckParkSelectionTarget(check);
        await CheckUnarmedParkExpiry(check);
        await CheckFiniteParkExpiry(manual: false, check);
        await CheckFiniteParkExpiry(manual: true, check);
        await CheckZeroWindowWithoutWake(check);
        await CheckParkExpiryClockAtFence(early: false, check);
        await CheckParkExpiryClockAtFence(early: true, check);

        foreach (var timed in new[] { false, true })
            await CaptureParkedCompletion(timed, (ok, name) => check(ok, $"{(timed ? "timed" : "untimed")} completion: {name}"));

        bool Retained(Message message, string payload, ParkKeepReason reason) => store.Messages.GetValueOrDefault(message.InboxKey) is
            { Done: true, Held: false, Trial: 0, KeptBy: var actualReason, Message.Payload: var actual } && actual == payload && actualReason == reason;
    }

    private static async Task CheckDiscardDrain(Action<bool, string> check)
    {
        const long openedAt = 1_701_400_020_000;
        var now = openedAt;
        var store = new InMemoryStore(1, () => now);
        var actor = new Probe(withholdFaulted: true);
        var scope = new Contract.BoundScope("discard");
        var replies = new Requests("discard-operator");
        bool landedAtReply = false;
        Worker Owner(string node) => new(node, 0, store, new ActorAdapter(store, actor),
            new WorkerOptions
            {
                Monotonic = () => now - openedAt,
                Transport = new OperatorReplies(replies, reply =>
                {
                    if (reply.Item is "started")
                        landedAtReply = store.Instances.Values.Any(row => row.ParkDiscard?.Operation == "op");
                })
            });
        var first = Owner("discard-first");
        var ingress = new InMemoryIngress(store, new Requests(first.Node), first.Node);
        var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(actor.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(started.Actor!.Value);
        await using var activation = (await new DurableHost([first]).ActivateAsync(0))!;
        await Pump(activation);
        await ingress.SendAsync(scope, target, "fault", "poison");
        await Pump(activation);

        const int keptCount = InMemoryStore.ParkPageLimit + 3;
        var widePayload = new string('p', 5 * 1024 * 1024);
        for (var index = 0; index < keptCount - 2; index++)
            await ingress.SendAsync(scope, target, $"arrived-{index}", "update", index == 0 ? widePayload : null);
        await Pump(activation);

        var parked = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        var id = parked.Row.Id;
        var publication = Json.Write(new EventRow("key", "event", "undelivered", now + 1));
        // Seed the result of a failed publish step. The paged publisher itself has its own K3 cases.
        var ticket = new Message("parked-publication", new(id), Kinds.Publish, publication, id,
            TargetCreation: parked.Row.Creation, SenderCreation: parked.Row.Creation).AsIssued().AsKernelTicket();
        var fence = first.Fences.Single();
        var staged = new TurnCommit(id, null, null, [], [], [ticket], [], [], null);
        await Commit(staged);
        var keep = new TurnCommit(id, parked.Row with { Revision = parked.Row.Revision + 1 }, parked.Row.Revision,
            [], [ticket.InboxKey], [], [], [], null)
        { FoundationKept = new Dictionary<InboxKey, ParkKeepReason> { [ticket.InboxKey] = ParkKeepReason.Consumed } };
        await Commit(keep);
        await activation.DisposeAsync();
        var drainer = Owner("discard-open");
        await using var drainingActivation = (await new DurableHost([drainer]).ActivateAsync(0))!;
        await Pump(drainingActivation);
        var operators = new InMemoryIngress(store, replies, drainer.Node, (_, kind) => kind == Contract.Kinds.Discard);
        var selection = (await new ActorList(store, 10).GetAsync(scope, target.Actor))!.Selection!;
        var payload = Json.Write(new Contract.OperatorCommand(selection));
        var denied = await ingress.SendAsync(scope, target, "ordinary-discard", Contract.Kinds.Discard, payload);
        var foreign = await operators.SendAsync(new Contract.BoundScope("foreign"), target, "cross-tenant",
            Contract.Kinds.Discard, payload);
        var missing = await operators.SendAsync(scope, target, "no-selection", Contract.Kinds.Discard);
        check(denied.Outcome == Contract.AdmissionOutcome.Refused && foreign.Outcome == Contract.AdmissionOutcome.Refused &&
            missing is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection },
            "only a bound operator with a qualified Park selection can submit discard");

        var request = operators.RequestAsync(scope, target, "op", Contract.Kinds.Discard, payload, null,
            new(null, now + 100, ReleaseBeforeCommit: true)).AsTask();
        await Step(drainer);
        var open = store.Instances[id];
        var reply = await request.WaitAsync(TimeSpan.FromSeconds(2));
        var opened = reply is { Item: "started", Error: null } && landedAtReply &&
            open.ParkResolutionGeneration == 1 &&
            open.ParkDiscard is { Operation: "op", Cursor: null, Serial: 1, Cleared: 0 } begun && OnlyOutstanding(begun.Control) &&
            store.Messages.Values.Count(item => item.KeptBy is not null) == keptCount;
        check(opened, "discard Open commits its first private control without clearing any input");
        if (!opened) return;
        var firstControl = store.Messages[open.ParkDiscard!.Control];
        if (!await CheckDiscardPageAuthority(store, drainer.Fences.Single(), open, parked.Park!, check)) return;

        await Step(drainer);
        var firstPage = store.Instances[id].ParkDiscard;
        var paged = firstPage is { Cursor: not null, Serial: 2, Cleared: InMemoryStore.ParkPageLimit } advanced &&
            OnlyOutstanding(advanced.Control) && store.Messages.Values.Count(item => item.KeptBy is not null) == keptCount - InMemoryStore.ParkPageLimit;
        check(paged, "one real drain turn clears one page and commits the cursor with its successor control");
        if (!paged) return;

        // Replay a known earlier control below admission to model a delayed delivery from this generation.
        store.Messages.Add(firstControl.Message.InboxKey, new(firstControl.Message, firstControl.Seq, 0));
        await Step(drainer);
        check(store.Instances[id].ParkDiscard == firstPage && OnlyOutstanding(firstPage!.Control) &&
            !store.Messages.ContainsKey(firstControl.Message.InboxKey),
            "a late control from an earlier page cannot advance the current drain");

        await drainingActivation.DisposeAsync();
        now += 100;
        await ingress.SendAsync(scope, target, "after-open", "update");
        var next = Owner("discard-next");
        await using var successor = (await new DurableHost([next]).ActivateAsync(0))!;
        await Pump(successor);
        var finished = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        check(finished is { Park: null, EndedAt: var end, Row: { ParkDiscard: null, ParkDiscarded: true, ParkResolutionGeneration: 1 } }
            && end == now && finished.Row.Status == InstanceStatus.Faulted && finished.Row.State == parked.Row.State,
            "a successor completes from the committed cursor and writes the terminal mark without changing the saved state");
        check(store.Messages.Values.All(item => item.KeptBy is null && item.Message.Kind != Kinds.Drain) &&
            store.Messages.Values.Count(item => item.Message.Id == "after-open" && item.Done) == 1 && actor.PoisonOffers == 1 &&
            store.Journal.Values.Count(item => item.Entries.Contains("\"discarded\"", StringComparison.Ordinal) &&
                item.Entries.Contains("\"Operation\":\"op\"", StringComparison.Ordinal)) == 1,
            "Complete removes the outstanding control and journals once, and the late input is dropped without replay");
        var repeated = await operators.SendAsync(scope, target, "op", Contract.Kinds.Discard, payload);
        var stale = await operators.SendAsync(scope, target, "another-op", Contract.Kinds.Discard, payload);
        var repeatedRequest = await operators.RequestAsync(scope, target, "op", Contract.Kinds.Discard, payload, null,
            new(null, now + 100));
        check(repeated.Outcome == Contract.AdmissionOutcome.Duplicate && repeatedRequest.Error == "repeat" &&
            stale is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection } &&
            store.Instances[id].ParkResolutionGeneration == 1,
            "after Complete the same operation is a duplicate and a new operation using the old selection is refused");
        await ingress.SendAsync(scope, target, "after-complete", "update");
        await Pump(successor);
        check(store.Messages.Values.Count(item => item.Message.Id == "after-complete" && item.Done) == 1,
            "author withholding cannot leave ordinary input pending after Park discards");
        now += (long)store.IngressDeduplicationWindow.TotalMilliseconds + 1;
        await store.SweepAsync();
        check(store.Messages.GetValueOrDefault(ticket.InboxKey) is
            { Done: true, KeptBy: null, ParkDeadLetter: true, Message.Payload: var remaining } && remaining == publication &&
            store.Messages.Values.All(item => item.Message.Id != "arrived-0"),
            "discard preserves the publish ticket's undelivered payload as a dead letter after the normal markers expire");

        ValueTask<Committed> Commit(TurnCommit turn) =>
            store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [turn], [], []));

        bool OnlyOutstanding(InboxKey key) => store.Messages.Values.Where(item => item.Message.Kind == Kinds.Drain && !item.Done)
            .Select(item => item.Message.InboxKey).SequenceEqual([key]);

        async Task Step(Worker worker)
        {
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(id);
            await worker.FlushAsync();
        }
    }

    internal static async Task CheckRecoverArrivals(Action<bool, string> check)
    {
        const long initial = 1_701_400_028_000L;
        var now = initial;
        var store = new InMemoryStore(1, () => now) { ParkedRetentionWindow = TimeSpan.FromSeconds(1) };
        var probe = new Probe(withholdFaulted: true);
        var scope = new Contract.BoundScope("recover-arrivals");
        var worker = new Worker("recover-arrivals", 0, store, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - initial });
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node,
            (_, kind) => kind == Contract.Kinds.Recover);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(started.Actor!.Value);
        await Pump(owner);
        var shortWindow = new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1));
        await ingress.SendAsync(scope, target, "fault", "poison", options: shortWindow);
        await Pump(owner);
        await ingress.SendAsync(scope, target, "keep", "advance", options: shortWindow);
        await Pump(owner);
        now += 3;
        await store.SweepAsync();

        var parked = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        var retained = parked.Park is not null && store.FoundationExpiry(parked.Row) is { Armed: not null } &&
            store.Messages.Values.Any(row => row.Message.Id == "fault" && row.KeptBy == ParkKeepReason.Consumed) &&
            store.Messages.Values.Any(row => row.Message.Id == "keep" && row.KeptBy == ParkKeepReason.Arrived);
        check(retained, "the recovery fixture keeps both input reasons across sweep before the armed deadline");
        if (!retained) return;

        var selection = (await new ActorList(store, 10).GetAsync(scope, target.Actor))!.Selection!;
        var result = await ingress.SendAsync(scope, target, "resume", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(selection, Contract.RecoverMode.Resume)));
        for (var pass = 0; pass < 3; pass++)
        {
            await Pump(owner);
            now += 11;
        }
        var restored = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        check(result.Outcome == Contract.AdmissionOutcome.Accepted &&
            restored is { Park: null, Row: { Status: InstanceStatus.Running, State: "advanced" } } &&
            probe.PoisonOffers == 1 && probe.RecoveredArrivals == 1 &&
            !store.Messages.Values.Any(row => row.KeptBy is not null || row.Message.Kind == Kinds.ParkExpiry),
            "Resume clears the consumed fault, applies the kept arrival once and recalls the expiry");
    }

    private static async Task CheckUnarmedParkExpiry(Action<bool, string> check)
    {
        const long now = 1_701_400_029_000;
        var store = new InMemoryStore(1, () => now);
        var probe = new Probe(withholdFaulted: true);
        var scope = new Contract.BoundScope("no-expiry");
        var worker = new Worker("no-expiry", 0, store, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => 0 });
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var result = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(result.Actor!.Value);
        await Pump(owner);
        await ingress.SendAsync(scope, target, "fault", "poison");
        await Pump(owner);
        var parked = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        check(parked.Park is not null, "the null-window fixture is parked before expiry is injected");
        if (parked.Park is not { } mark) return;

        // A row inserted below producers uses the actor's own identity and matching Park payload, but was never armed.
        var drain = ParkDrainEnvelope.For(parked.Row, mark).Issue(parked.Row, serial: 1);
        var unarmed = (drain with { Kind = Kinds.ParkExpiry, Due = now }).AsKernelTicket();
        store.Messages.Add(unarmed.InboxKey, new(unarmed, long.MaxValue - 1, 0));
        var retained = store.Messages.Values.Count(row => row.KeptBy is not null);
        await Pump(owner);
        check(!store.Messages.ContainsKey(unarmed.InboxKey) && store.Instances[parked.Row.Id] == parked.Row &&
            store.Messages.Values.Count(row => row.KeptBy is not null) == retained,
            "a null-window Park drops an unarmed expiry without keeping it or changing the checkpoint");
    }

    private static async Task CheckFiniteParkExpiry(bool manual, Action<bool, string> check)
    {
        const long startAt = 1_701_400_030_000;
        const long window = 20_000;
        var now = startAt;
        var store = new InMemoryStore(1, () => now) { ParkedRetentionWindow = TimeSpan.FromMilliseconds(window) };
        var probe = new Probe(withholdFaulted: true);
        var scope = new Contract.BoundScope("finite-expiry");
        Worker Owner(string name) => new(name, 0, store, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - startAt, Horizon = TimeSpan.Zero });
        var first = Owner("expiry-first");
        var ingress = new InMemoryIngress(store, new Requests(first.Node), first.Node,
            (_, kind) => kind == Contract.Kinds.Discard);
        await using var active = (await new DurableHost([first]).ActivateAsync(0))!;
        var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready", null)), "start", Contract.Kinds.Start);
        await Pump(active);
        await ingress.SendAsync(scope, new Contract.Target.Ref(started.Actor!.Value), "spawn-child", "spawn");
        await Pump(active);
        var target = new Contract.Target.Ref(probe.Child!.Value);
        await ingress.SendAsync(scope, target, "fault", "poison");
        await Pump(active);
        var firstPark = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        var firstExpiry = store.FoundationExpiry(firstPark.Row);
        now++;
        for (var i = 0; i < InMemoryStore.ParkPageLimit; i++)
            await ingress.SendAsync(scope, target, $"input-{i}", "update");
        var exhausted = store.Messages.Values.Single(row => row.Message.Id == "input-0");
        exhausted.Attempts = 3;
        await Pump(active);

        var parked = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        var expiry = store.FoundationExpiry(parked.Row);
        var label = manual ? "operator first" : "expiry first";
        var armed = expiry is { Serial: 1, Armed: { } armedKey } && expiry.At == startAt + window &&
            parked.Park?.At == startAt && store.Messages.GetValueOrDefault(armedKey) is
                { Done: false, Message: { Kind: Kinds.ParkExpiry, Due: var due } } && due == expiry.At;
        check(armed, $"{label}: Park commits one timer at the commit-time deadline");
        if (!armed) return;
        check(exhausted is { Done: true, KeptBy: ParkKeepReason.Consumed } &&
            parked.Park == firstPark.Park && firstExpiry == expiry &&
            store.Messages.Values.Count(row => row.Message.Kind == Kinds.ParkExpiry) == 1,
            $"{label}: re-park keeps the original armed timer and deadline");
        var original = store.Messages[expiry!.Armed!.Value].Message;
        var id = parked.Row.Id;
        var keptCount = InMemoryStore.ParkPageLimit + 1;
        var oldEpoch = first.Fences.Single().Epoch;

        now = expiry.At - 1;
        var second = Owner("expiry-take");
        await using var successor = (await new DurableHost([second]).ActivateAsync(0))!;
        await store.SweepAsync();
        await Pump(successor);
        check(second.Fences.Single().Epoch > oldEpoch && store.Instances[id] == parked.Row &&
            store.Messages.Values.Count(row => row.KeptBy is not null) == keptCount && store.Messages.ContainsKey(original.InboxKey),
            $"{label}: takeover and sweep before the deadline preserve the Park and its timer");

        var fence = second.Fences.Single();
        var rowless = new TurnCommit(id, null, null, [original.InboxKey], [], [], [], [], null);
        var protectedExpiry = await DefiniteRejection(() => store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position,
            Defaults.Lease, false, [rowless], [], []))) && store.Messages.ContainsKey(original.InboxKey);
        check(protectedExpiry, $"{label}: even a fenced rowless commit cannot consume the outstanding expiry");
        if (!protectedExpiry) return;

        var envelope = ParkDrainEnvelope.For(parked.Row, parked.Park!);
        var wrongSerial = envelope.Issue(parked.Row, serial: 999, expiryAt: now);
        store.Messages.Add(wrongSerial.InboxKey, new(wrongSerial, long.MaxValue - 2, 0));
        await Step(second);
        check(!store.Messages.ContainsKey(wrongSerial.InboxKey) && store.Instances[id] == parked.Row &&
            store.FoundationExpiry(parked.Row) == expiry,
            $"{label}: a correctly addressed timer with another key cannot open a drain");

        if (manual)
        {
            var selection = (await new ActorList(store, 10).GetAsync(scope, target.Actor))!.Selection!;
            await ingress.SendAsync(scope, target, "discard", Contract.Kinds.Discard, Json.Write(new Contract.OperatorCommand(selection)));
        }
        else
        {
            // Model a due-time fault below the poll. The current authenticated key reaches the owner too soon.
            var reservation = store.Messages[original.InboxKey];
            reservation.Message = original with { Due = now };
            await Step(second);
            var rearm = store.FoundationExpiry(parked.Row);
            var replaced = rearm is { Serial: 2, Armed: { } rearmed } && rearm.At == expiry.At &&
                !store.Messages.ContainsKey(original.InboxKey) &&
                store.Messages.GetValueOrDefault(rearmed) is { Done: false, Message.Due: var at } && at == expiry.At &&
                store.Instances[id].ParkDiscard is null;
            check(replaced, "an early armed timer is consumed and atomically re-armed with the same deadline and a fresh key");
            if (!replaced) return;
            now = expiry.At;
        }

        await Step(second);
        var opening = store.Instances[id];
        var began = opening.ParkDiscard is { Cleared: 0, Expired: var expired } && expired != manual &&
            opening.ParkResolutionGeneration == 1 && store.FoundationExpiry(opening) is { Armed: null } &&
            !store.Messages.Values.Any(row => row.Message.Kind == Kinds.ParkExpiry);
        check(began, $"{label}: Open consumes or recalls the expiry and records the matching resolution");
        if (!began) return;
        // A recalled or superseded timer can arrive after the Open that invalidated it.
        store.Messages.Add(original.InboxKey, new(original, long.MaxValue - 1, 0));
        now = expiry.At;
        await Step(second);
        check(store.Instances[id].ParkDiscard is { Cleared: InMemoryStore.ParkPageLimit } &&
            store.Messages.Values.Count(row => row.KeptBy is not null) == 1,
            $"{label}: a single committed page leaves a bounded suffix");

        await successor.DisposeAsync();
        now += 10;
        var third = Owner("expiry-complete");
        await using var completing = (await new DurableHost([third]).ActivateAsync(0))!;
        await Pump(completing);
        var finished = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        var outcome = manual ? "discarded" : "expired";
        check(finished is { Park: null, Row: { ParkDiscarded: true, Status: InstanceStatus.Faulted, ParkDiscard: null } } &&
            finished.EndedAt == now && finished.Row.State == parked.Row.State && store.FoundationExpiry(finished.Row) is null &&
            !store.Messages.Values.Any(row => row.KeptBy is not null || ParkDrainEnvelope.IsInDomain(row.Message)) &&
            store.Journal.Values.Count(row => row.Instance == id && row.Entries.Contains($"\"{outcome}\"", StringComparison.Ordinal)) == 1,
            $"{label}: the next owner clears the suffix, drops the stale timer and stamps one Faulted outcome");
        check(probe.Notices is [{ Final: false, Status: Contract.ActorStatus.Faulted }, { Final: true, Status: Contract.ActorStatus.Faulted }],
            $"{label}: the parent receives its provisional Park notice and one final Faulted notice");

        async Task Step(Worker worker)
        {
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(id);
            await worker.FlushAsync();
        }
    }

    private static async Task CheckZeroWindowWithoutWake(Action<bool, string> check)
    {
        const long now = 1_701_400_070_000;
        var store = new InMemoryStore(1, () => now) { ParkedRetentionWindow = TimeSpan.Zero };
        var probe = new Probe(withholdFaulted: true);
        var scope = new Contract.BoundScope("zero-window");
        var worker = new Worker("zero-window", 0, store, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => 0 });
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node);
        await using var active = (await new DurableHost([worker]).ActivateAsync(0))!;
        var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready", null)), "start", Contract.Kinds.Start);
        await Pump(active);
        await ingress.SendAsync(scope, new Contract.Target.Ref(started.Actor!.Value), "fault", "poison");
        await active.WokenAsync(null);
        for (var i = 0; i < 12; i++) await active.RunAsync();
        var finished = store.ReadFoundationActor(scope.Tenant, started.Actor.Value.Id).Actor!;
        check(finished is { Park: null, Row.ParkDiscarded: true } && finished.EndedAt == now,
            "a zero-window expiry enters the owner's next pass without another wake");
    }

    private static async Task CheckParkExpiryClockAtFence(bool early, Action<bool, string> check)
    {
        const long startAt = 1_701_400_080_000;
        const long window = 20_000;
        long now = startAt, elapsed = 0;
        var store = new InMemoryStore(1, () => now) { ParkedRetentionWindow = TimeSpan.FromMilliseconds(window) };
        var probe = new Probe(withholdFaulted: true);
        var scope = new Contract.BoundScope("expiry-clock");
        Worker Owner(string node) => new(node, 0, store, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => elapsed, Horizon = TimeSpan.Zero });
        var first = Owner("expiry-clock-first");
        var ingress = new InMemoryIngress(store, new Requests(first.Node), first.Node);
        await using var active = (await new DurableHost([first]).ActivateAsync(0))!;
        var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(started.Actor!.Value);
        await Pump(active);
        await ingress.SendAsync(scope, target, "fault", "poison");
        await Pump(active);

        var parked = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        var timer = store.FoundationExpiry(parked.Row);
        if (timer is not { Armed: { } key })
        {
            check(false, "the expiry clock fixture must have a committed timer");
            return;
        }

        now = early ? timer.At - 1 : timer.At;
        elapsed = now - startAt;
        if (early)
        {
            var source = store.Messages[key];
            source.Message = source.Message with { Due = now };
        }

        var next = Owner("expiry-clock-take");
        await using var successor = (await new DurableHost([next]).ActivateAsync(0))!;
        await next.PollAsync();
        await next.LoadAsync();
        await next.FlushAsync();
        next.RunTurn(parked.Row.Id);
        // Only the database clock moves, after the commit captures its time and during fence acquisition.
        var sampled = 0;
        store.Locking = _ =>
        {
            sampled++;
            now = early ? timer.At : timer.At - 1;
        };
        try { await next.FlushAsync(); }
        finally { store.Locking = null; }

        var invalidated = sampled == 1 && store.Instances[parked.Row.Id] == parked.Row &&
            store.FoundationExpiry(parked.Row) == timer && store.Messages.ContainsKey(key);
        check(invalidated, early
            ? "crossing the deadline inside the fence rolls back a premature re-arm"
            : "a clock step back inside the fence rolls back an expiry Open");
        if (!invalidated) return;

        await successor.DisposeAsync();
        now = timer.At + (long)Defaults.Lease.TotalMilliseconds + 1;
        elapsed = now - startAt;
        var complete = Owner("expiry-clock-complete");
        await using var completed = await new DurableHost([complete]).ActivateAsync(0);
        check(completed is not null, "the next owner takes the partition after the invalidated owner's lease expires");
        if (completed is null) return;
        await Pump(completed);
        check(store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor is { Park: null, Row.ParkDiscarded: true } ended &&
            ended.EndedAt == now && store.Journal.Values.Count(row => row.Entries.Contains("\"expired\"", StringComparison.Ordinal)) == 1,
            "the next owner expires the same Park once when the database clock permits it");
    }

    private static async Task CheckParkSelectionTarget(Action<bool, string> check)
    {
        const long now = 1_701_400_025_000;
        var store = new InMemoryStore(1, () => now);
        var probe = new Probe(withholdFaulted: true);
        var scope = new Contract.BoundScope("park-selection");
        var worker = new Worker("park-selection", 0, store, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => 0 });
        var edge = new InMemoryIngress(store, new Requests(worker.Node), worker.Node,
            (_, kind) => kind is Contract.Kinds.Discard or Contract.Kinds.Delete or Contract.Kinds.Terminate);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var created = new Contract.Target.Start(new(probe.Type, "ready", null));
        var a = (await edge.SendAsync(scope, created, "start-a", Contract.Kinds.Start)).Actor!.Value;
        var b = (await edge.SendAsync(scope, created, "start-b", Contract.Kinds.Start)).Actor!.Value;
        var foreign = new Contract.BoundScope("another-tenant");
        var c = (await edge.SendAsync(foreign, created, "start-a", Contract.Kinds.Start)).Actor!.Value;
        var targetA = new Contract.Target.Ref(a);
        var targetB = new Contract.Target.Ref(b);
        var targetC = new Contract.Target.Ref(c);
        await Pump(owner);
        await edge.SendAsync(scope, targetA, "selected", "poison");
        await edge.SendAsync(scope, targetB, "fault-b", "poison");
        await edge.SendAsync(foreign, targetC, "fault-c", "poison");
        await Pump(owner);

        var parkedA = store.ReadFoundationActor(scope.Tenant, a.Id).Actor!;
        var parkedB = store.ReadFoundationActor(scope.Tenant, b.Id).Actor!;
        var pinned = parkedA.Park is not null && parkedA.Park == parkedB.Park &&
            parkedA.Row.Creation == parkedB.Row.Creation && parkedA.Row.Id != parkedB.Row.Id;
        check(pinned, "the two targets have exactly the same Park time, revision and creation");
        if (!pinned) return;

        var selection = (await new ActorList(store, 10).GetAsync(scope, a))!.Selection!;
        var payload = Json.Write(new Contract.OperatorCommand(selection));
        var other = await edge.SendAsync(scope, targetB, "swapped", Contract.Kinds.Discard, payload);
        var own = await edge.SendAsync(scope, targetA, "selected", Contract.Kinds.Discard, payload);
        var swappedDelete = await edge.SendAsync(scope, targetB, "selected", Contract.Kinds.Delete, payload);
        var swappedTerminate = await edge.SendAsync(scope, targetB, "selected", Contract.Kinds.Terminate, payload);
        check(swappedDelete is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection, Actor: null } &&
            swappedTerminate is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection, Actor: null },
            "the tenant receipt cannot turn an invalid delete or terminate selection into a duplicate");
        var list = new ActorList(store, 10);
        var selectionB = (await list.GetAsync(scope, b))!.Selection!;
        var selectionC = (await list.GetAsync(foreign, c))!.Selection!;
        var retargeted = await edge.SendAsync(scope, targetB, "selected", Contract.Kinds.Discard,
            Json.Write(new Contract.OperatorCommand(selectionB)));
        var independent = await edge.SendAsync(foreign, targetC, "selected", Contract.Kinds.Discard,
            Json.Write(new Contract.OperatorCommand(selectionC)));
        await Pump(owner);
        var expiredDelete = await edge.SendAsync(scope, targetA, "selected", Contract.Kinds.Delete, payload);
        var expiredTerminate = await edge.SendAsync(scope, targetA, "selected", Contract.Kinds.Terminate, payload);
        check(expiredDelete is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection } &&
            expiredTerminate is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection },
            "a discard receipt does not make a resolved Park selection current for delete or terminate");
        check(retargeted is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: var first } && first == a &&
            independent.Outcome == Contract.AdmissionOutcome.Accepted &&
            store.ReadFoundationActor(foreign.Tenant, c.Id).Actor is { Row.ParkDiscarded: true },
            "an operator id pins the first target in its tenant independently of ordinary sends and other tenants");
        check(other is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection } &&
            own.Outcome == Contract.AdmissionOutcome.Accepted && store.Instances[parkedA.Row.Id].ParkDiscarded &&
            store.Instances[parkedB.Row.Id] == parkedB.Row &&
            store.Messages.Values.Any(row => row.Message.Target.Id == parkedB.Row.Id && row.KeptBy == ParkKeepReason.Consumed),
            "a same-tenant Park selection can discard only the actor that issued it");
    }

    private sealed class OperatorReplies(Requests requests, Action<Reply> observe) : IReplyTransport
    {
        public void Send(string node, Reply reply)
        {
            observe(reply);
            requests.Receive(reply);
        }
    }

    private static async Task<bool> CheckDiscardPageAuthority(InMemoryStore store, PartitionFence fence, InstanceRow open,
        InMemoryStore.FoundationParkMark park, Action<bool, string> check)
    {
        var drain = open.ParkDiscard!;
        var page = store.ReadFoundationKeptPage(new(open.Id, open.Creation), park, drain.Cursor)!;
        var envelope = ParkDrainEnvelope.For(open, park);
        var nextControl = envelope.Issue(open, drain.Serial + 1);
        var next = open with { Revision = open.Revision + 1,
            ParkDiscard = new(drain.Operation, page[^1].Cursor, page.Count, drain.Serial + 1, nextControl.InboxKey) };
        var clear = new TurnCommit(open.Id, next, open.Revision, [drain.Control], [], [nextControl], [], [], null)
        { FoundationParkClear = new(park, page) };
        var rowless = new TurnCommit(open.Id, null, null, [drain.Control], [], [], [], [], null);
        var recovered = await DefiniteRejection(() => Commit(fence, rowless));
        check(recovered && store.Messages.ContainsKey(drain.Control) && store.Instances[open.Id] == open,
            "a rowless commit cannot consume the outstanding control");
        if (!recovered) return false;

        var skippedPrefix = clear with { Row = next with { ParkDiscard = next.ParkDiscard! with { Cleared = 1 } },
            FoundationParkClear = new(park, [page[^1]]) };
        var refusedGap = await DefiniteRejection(() => Commit(fence, skippedPrefix));
        check(refusedGap && store.Instances[open.Id] == open && store.Messages.ContainsKey(drain.Control),
            "the fenced writer refuses a page that would skip rows below its cursor");
        if (!refusedGap) return false;

        var altered = page.ToArray();
        altered[^1] = altered[^1] with { Reason = ParkKeepReason.Consumed };
        var divergent = false;
        try { await Commit(fence, clear with { FoundationParkClear = new(park, altered) }); }
        catch (StoreException error) when (error.Failure == Failure.Unknown) { divergent = true; }
        var rolledBack = divergent && store.Instances[open.Id] == open && store.Messages.ContainsKey(drain.Control)
            && !store.Messages.ContainsKey(nextControl.InboxKey) && page.All(item => store.Messages[item.Cursor.Key].KeptBy == item.Reason);
        check(rolledBack, "one mismatched kept reason rolls back the entire page and checkpoint");
        if (!rolledBack) return false;

        var stale = await Commit(fence with { Epoch = fence.Epoch - 1 }, clear);
        check(!stale.Landed && store.Instances[open.Id] == open && store.Messages.ContainsKey(drain.Control),
            "a stale fence cannot clear kept inputs");
        return true;

        ValueTask<Committed> Commit(PartitionFence authority, TurnCommit turn) =>
            store.CommitAsync(new(authority.Partition, authority.Epoch, authority.Position, Defaults.Lease, false, [turn], [], []));
    }

    private static async Task<bool> DefiniteRejection(Func<ValueTask<Committed>> commit)
    {
        try { await commit(); }
        catch (StoreException error) when (error.Failure == Failure.Definite) { return true; }
        return false;
    }

    private static async Task CheckKeptPages(InMemoryStore store, PartitionFence fence, string id, long creation, Action<bool, string> check)
    {
        var address = new CreationAddress(id, creation);
        var identity = store.FoundationIdentity(id, creation);
        var park = store.ReadFoundationActor(identity.Tenant, identity.Reference).Actor!.Park!;
        const int wideBytes = 5 * 1024 * 1024;
        var widePayload = new string('x', wideBytes);
        var input = Enumerable.Range(0, InMemoryStore.ParkPageLimit + 2).Reverse().Select(index =>
            new Message("page-tie", new(id), "update", index == 0 ? widePayload : null, TargetCreation: creation)
                .WithInboxScope(index / 2, index % 2 == 0 ? null : "match"))
            .ToArray();
        var keys = input.Select(message => message.InboxKey).ToArray();
        var add = new TurnCommit(id, null, null, [], [], input, [], [], null);
        var added = await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [add], [], []));
        var checkpoint = store.Instances[id];
        var keep = new TurnCommit(id, checkpoint with { Revision = checkpoint.Revision + 1 }, checkpoint.Revision,
            [], keys, [], [], [], null)
        { FoundationKept = keys.ToDictionary(key => key, _ => ParkKeepReason.Arrived) };
        var kept = await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [keep], [], []));
        check(added.Landed && kept.Landed, "the fenced store retains the complete paging fixture");

        // Model an allowed provider order-key collision without depending on the in-memory counter to generate one.
        var sharedSeq = store.Messages[keys[0]].Seq;
        foreach (var key in keys) store.Messages[key].Seq = sharedSeq;
        var after = new ParkPageCursor(sharedSeq - 1, new InboxKey("\uffff"));
        var front = store.ReadFoundationKeptPage(address, park, after)!;
        var start = keys[^1];
        check(front.Count == InMemoryStore.ParkPageLimit && front[0] == new ParkPageRow(new(sharedSeq, start), "update", null, ParkKeepReason.Arrived, wideBytes),
            "the page starts with an oversized payload's metadata and enforces its row limit");
        var tail = store.ReadFoundationKeptPage(address, park, front[^1].Cursor)!;
        check(front.Concat(tail).Select(item => item.Cursor.Key).SequenceEqual(keys.Reverse()) &&
            tail.Count == 2 && store.ReadFoundationKeptPage(address, park, tail[^1].Cursor) is { Count: 0 },
            "the full tuple advances through scope and match ties without skipping or repeating a kept input");
        check(store.ReadFoundationKeptPage(address, park with { Revision = park.Revision + 1 }, null) is null &&
            store.ReadFoundationKeptPage(address with { Creation = creation + 1 }, park, null) is null,
            "a mismatched Park mark or creation is distinguishable from an empty page");

        var clearWithoutDrain = new TurnCommit(id, checkpoint with { Revision = checkpoint.Revision + 2 },
            checkpoint.Revision + 1, [], [], [], [], [], null) { FoundationParkClear = new(park, front) };
        var refused = await DefiniteRejection(() => store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease,
            false, [clearWithoutDrain], [], [])));
        check(refused && front.All(item => store.Messages[item.Cursor.Key].KeptBy == item.Reason),
            "a parked row cannot clear inputs without an open drain");

        await CheckInterruptedPage(store, fence, checkpoint.Id, park, keys, after, check);
    }

    private static async Task CheckInterruptedPage(InMemoryStore store, PartitionFence fence, string id,
        InMemoryStore.FoundationParkMark park, InboxKey[] keys, ParkPageCursor after, Action<bool, string> check)
    {
        // Seed the durable row state of a previous recovery interrupted after one page. This fixture exercises
        // the bounded store read. The operator-to-Worker recovery fixture exercises the real transition separately.
        const long generation = 2;
        var original = store.Instances[id];
        var address = new CreationAddress(id, original.Creation);
        var stale = store.Messages[keys[^1]];
        var current = store.Messages[keys[^2]];
        var foreign = store.Messages[keys[^3]];
        var unmarked = store.Messages[keys[^4]];
        var inactive = new[] { stale, current, foreign, unmarked };
        var release = new ParkRelease(address.Creation, generation - 1);
        var opening = original with { Status = InstanceStatus.Running, ParkResolutionGeneration = generation };
        var envelope = ParkDrainEnvelope.For(opening, park);
        var control = envelope.Issue(opening, 1);
        var drain = new ParkRecoveryDrain("test-recovery", ParkRecoveryMode.Resume, after, 1, control.InboxKey);
        opening = opening with { ParkRecovery = drain };
        store.Instances[id] = opening;
        store.Messages.Add(control.InboxKey, new(control, after.Seq, 0));
        foreach (var row in inactive) (row.Done, row.KeptBy) = (false, null);
        stale.ReleasedBy = release;
        current.ReleasedBy = release with { Generation = generation };
        foreign.ReleasedBy = release with { Creation = address.Creation + 1 };

        var first = store.ReadFoundationRecoveryPage(address, park, generation, after, limit: 1);
        var suffix = first is [var head]
            ? store.ReadFoundationRecoveryPage(address, park, generation, head.Cursor) : null;
        var ordered = first is [var released] && released.Cursor.Key == keys[^1] && released.Reason is null &&
            released.ReleasedBy == release && released.PayloadBytes == stale.PayloadLength &&
            suffix is not null && suffix.Select(item => item.Cursor.Key).SequenceEqual(keys.Reverse().Skip(4));
        check(ordered, "a later recovery merges the older release into the kept order and skips current, foreign and unmarked rows");
        check(store.ReadFoundationKeptPage(address, park, after)!.Select(item => item.Cursor.Key)
            .SequenceEqual(keys.Reverse().Skip(4)) &&
            store.ReadFoundationRecoveryPage(address, park, generation - 1, after) is null,
            "a discard skips stale releases and an outdated recovery generation cannot read the page");
        if (!ordered) return;

        var arrivedKey = keys[^5];
        var consumedKey = keys[^6];
        var arrived = store.Messages[arrivedKey];
        var consumed = store.Messages[consumedKey];
        consumed.KeptBy = ParkKeepReason.Consumed;
        stale.Held = true;
        (stale.Seen, stale.Trial, stale.Attempts) = (fence.Epoch, fence.Epoch, 3);
        (arrived.Seen, arrived.Trial, arrived.Attempts) = (fence.Epoch, fence.Epoch, 3);
        var page = store.ReadFoundationRecoveryPage(address, park, generation, drain.Cursor, limit: 3)!;
        var nextControl = envelope.Issue(opening, 2);
        var next = opening with
        {
            Revision = opening.Revision + 1,
            ParkRecovery = drain with
            {
                Cursor = page[^1].Cursor, Serial = 2, Control = nextControl.InboxKey,
                Restored = 2, Cleared = 1, Gate = arrivedKey
            }
        };
        var advance = new TurnCommit(id, next, opening.Revision, [control.InboxKey], [], [nextControl], [], [], null)
        { FoundationRecoveryPage = new(park, page) };
        var denied = await DefiniteRejection(() => Commit(fence, new TurnCommit(id, null, null, [control.InboxKey], [], [], [], [], null)));
        var lostFence = await Commit(fence with { Epoch = fence.Epoch - 1 }, advance);
        check(denied && !lostFence.Landed && store.Instances[id] == opening && stale.ReleasedBy == release &&
            store.Messages.ContainsKey(control.InboxKey) && !store.Messages.ContainsKey(nextControl.InboxKey),
            "a recovery page requires an advancing checkpoint and the current partition fence");

        var altered = page.ToArray();
        altered[^1] = altered[^1] with { Reason = ParkKeepReason.Arrived };
        var rolledBack = false;
        try { await Commit(fence, advance with { FoundationRecoveryPage = new(park, altered) }); }
        catch (StoreException error) when (error.Failure == Failure.Unknown) { rolledBack = true; }
        check(rolledBack && store.Instances[id] == opening && stale.ReleasedBy == release && arrived.Done &&
            consumed.KeptBy == ParkKeepReason.Consumed && store.Messages.ContainsKey(control.InboxKey) &&
            !store.Messages.ContainsKey(nextControl.InboxKey),
            "a changed kept reason rolls back the recovery cursor, control and all page rows");

        foreach (var trial in new[] { false, true })
        {
            var marked = new CommitBatch(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [advance],
                trial ? [] : [nextControl.InboxKey], trial ? [nextControl.InboxKey] : []);
            var refusedMark = await DefiniteRejection(() => store.CommitAsync(marked));
            var intact = store.Instances[id] == opening && stale.ReleasedBy == release && arrived.Done &&
                store.Messages.ContainsKey(control.InboxKey) && !store.Messages.ContainsKey(nextControl.InboxKey);
            check(refusedMark && intact, trial ? "a recovery page cannot mark its new control on trial" :
                "a recovery page cannot mark its new control seen before poll");
            if (!intact) return;
        }

        var written = await Commit(fence, advance);
        var polled = await store.PollAsync(fence.Partition, fence.Epoch, TimeSpan.Zero, 1024);
        var active = new ParkRelease(address.Creation, generation);
        check(written.Landed && store.Instances[id] == next && !store.Messages.ContainsKey(control.InboxKey) &&
            store.Messages.ContainsKey(nextControl.InboxKey) &&
            stale is { Done: false, Held: false, Seen: 0, Trial: 0, Attempts: 0, KeptBy: null } && stale.ReleasedBy == active &&
            arrived is { Done: false, KeptBy: null, Trial: 0, Attempts: 0 } && arrived.ReleasedBy == active &&
            consumed is { Done: true, KeptBy: null } &&
            polled.Messages.Any(row => row.Message.InboxKey == nextControl.InboxKey) &&
            polled.Messages.Any(row => row.Message.InboxKey == keys[^1] && row.ReleasedBy == active &&
                row.PayloadLength == stale.PayloadLength && row.Seq == page[0].Cursor.Seq && row.Message.Payload == stale.Message.Payload),
            "a fenced page clears consumed work, resets restored poison counters and publishes the generation through poll");
        if (!written.Landed) return;

        await store.ReleaseAsync(fence.Partition, fence.Epoch);
        var successor = await store.TakeAsync(fence.Partition, "recovery-page-takeover", Defaults.Lease);
        var nextPage = store.ReadFoundationRecoveryPage(address, park, generation, next.ParkRecovery!.Cursor, limit: 1);
        var recoveredPoll = successor is null ? null : await store.PollAsync(fence.Partition, successor.Epoch, TimeSpan.Zero, 1024);
        check(successor is { Epoch: var epoch } && epoch > fence.Epoch && nextPage is [{ Cursor.Key: var nextKey }] &&
            nextKey == keys[^7] && recoveredPoll is not null &&
            recoveredPoll.Messages.Any(row => row.Message.InboxKey == nextControl.InboxKey) &&
            recoveredPoll.Messages.Any(row => row.Message.InboxKey == keys[^1] && row.ReleasedBy == active),
            "a takeover keeps the released identity and continues with the next tuple and its stored control");

        ValueTask<Committed> Commit(PartitionFence authority, TurnCommit turn) =>
            store.CommitAsync(new(authority.Partition, authority.Epoch, authority.Position, Defaults.Lease, false, [turn], [], []));
    }

    private static async Task CaptureParkedCompletion(bool timed, Action<bool, string> check)
    {
        const long now = 1_701_400_010_000;
        const string jobKey = "background";
        const int payloadLimit = 16 * 1024 * 1024 - 2 * 24 * 1024;
        var overhead = System.Text.Encoding.UTF8.GetByteCount(Json.Write(new JobFinished(jobKey, Json.Write(""), null)));
        var store = new InMemoryStore(1, () => now);
        var actor = new ParkedJobProbe(new string('x', payloadLimit - overhead + 1), timed);
        var scope = new Contract.BoundScope("parked-job");
        Worker MakeOwner(string node) => new(node, 0, store, new ActorAdapter(store, actor), new WorkerOptions { Monotonic = () => 0 });
        var worker = MakeOwner("parked-job-first");
        Contract.IIngress edge = new InMemoryIngress(store, new Requests(worker.Node), worker.Node);
        var start = await edge.SendAsync(scope, new Contract.Target.Start(new(actor.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(start.Actor!.Value);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump(owner);
        var row = store.Instances.Values.Single();
        var task = worker.JobCompletion(row.Id, jobKey, row.Creation)!;
        var ticket = InboxKey.Ticket(row.Id, jobKey, row.Creation);
        var resultKey = InboxKey.JobResult(row.Id, jobKey, row.Creation);
        await edge.SendAsync(scope, target, "fault", "fault");
        await Pump(owner);
        var parked = store.Instances[row.Id];
        check(parked is { Status: InstanceStatus.Faulted, Jobs: not null } && store.Messages.ContainsKey(ticket) && actor.Entries == 1,
            "the fault parks the actor while the entered job and its counted ticket are outstanding");
        check(store.Messages.Values.Count(message => !message.Done && message.Message.Kind == Kinds.JobDeadline) == (timed ? 1 : 0),
            "the parked job still has its pending deadline if armed");

        actor.Complete.SetResult();
        await task.WaitAsync(TimeSpan.FromSeconds(5));
        worker.RunTurn(row.Id);
        await worker.FlushAsync();
        var result = store.Messages.GetValueOrDefault(resultKey);
        check(store.Instances[row.Id] == parked && !store.Messages.ContainsKey(ticket) &&
            result is { Done: false, Message.FoundationJobResult: true } &&
            result.Message.Read<JobFinished>() is { Result: null, Error: "result too large" } &&
            !store.Messages.Values.Any(message => !message.Done && message.Message.Kind == Kinds.JobDeadline),
            "a fenced standalone conversion stores the bounded authenticated fault without rewriting the actor checkpoint");

        await owner.DisposeAsync();
        await using var successor = (await new DurableHost([MakeOwner("parked-job-next")]).ActivateAsync(0))!;
        await Pump(successor);
        check(actor.Entries == 1 && actor.Completions == 0 && store.Instances[row.Id].Jobs == parked.Jobs &&
            store.Messages.GetValueOrDefault(resultKey) is { Done: true, KeptBy: ParkKeepReason.Arrived },
            "a successor keeps the bounded result as an arrival and does not rerun the job");
    }

    private static async Task Pump(IScopeActivation owner)
    {
        for (var step = 0; step < 10; step++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
    }

    private sealed class Probe(bool withholdFaulted = false) : Contract.IActorType
    {
        public string Type => "kept-check";
        public int PoisonOffers { get; private set; }
        public int Offers { get; private set; }
        public int RecoveredArrivals { get; private set; }
        public Contract.ActorRef? Child { get; private set; }
        public List<Contract.ChildFinished> Notices { get; } = [];
        public bool Withholds(Contract.ActorHeader header) => withholdFaulted && header.Status == Contract.ActorStatus.Faulted;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            Offers++;
            if (turn.Inbox.Any(message => message.Kind == "poison"))
            {
                PoisonOffers++;
                throw new InvalidOperationException("turn fault");
            }
            foreach (var message in turn.Inbox)
                if (message.Kind == "spawn") Child = turn.Start(new(Type, "child", null));
                else if (message.Kind == Contract.Kinds.ChildFinished) Notices.Add(Json.Read<Contract.ChildFinished>(message.Payload!));
            if (turn.Inbox.Any(message => message.Kind == "advance"))
            {
                RecoveredArrivals++;
                turn.State = "advanced";
            }
            else turn.State = "saved";
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }

    private sealed class ParkedJobProbe(string value, bool timed) : Contract.IActorType
    {
        public string Type => "parked-job-check";
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Entries { get; private set; }
        public int Completions { get; private set; }
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
                switch (message.Kind)
                {
                    case Contract.Kinds.Start: turn.StartJob("background", timeout: timed ? TimeSpan.FromSeconds(1) : null); break;
                    case "fault": throw new InvalidOperationException("park with a running job");
                    case Contract.Kinds.JobFinished: Completions++; break;
                }
            return default;
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            Entries++;
            await Complete.Task.WaitAsync(cancellationToken);
            return value;
        }

        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
