using System.Text.Json;
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

        await edge.SendAsync(scope, target, "older", "update", "ordinary input to remove",
            options: new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1)));
        await Pump(owner);
        var row = store.Instances.Values.Single();
        var ordinary = store.Messages.Values.Single(message => message.Message.Id == "older");
        check(ordinary is { Done: true, KeptBy: null, Message.Payload: null } && row.State == "saved",
            "ordinary done input retains its receipt without its payload");
        var offers = actor.Offers;
        var sameId = await edge.SendAsync(scope, target, "older", "update", "different payload",
            options: new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1)));
        await Pump(owner);
        check(sameId.Outcome == Contract.AdmissionOutcome.Duplicate && sameId.Actor == target.Actor && actor.Offers == offers,
            "a payload-free outside marker preserves duplicate admission without another actor turn");

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
        await CheckRecoverArrivals(check, faultOnAdvance: false, throwOnOpen: false);
        await CheckRecoverArrivals(check, faultOnAdvance: true, throwOnOpen: false);
        await CheckRecoverArrivals(check, faultOnAdvance: false, throwOnOpen: true);
        await CheckRecoverArrivals(check, faultOnAdvance: false, throwOnOpen: false, holdAdvance: true);
        await CheckRecoverArrivals(check, faultOnAdvance: false, throwOnOpen: false, holdControl: true);
        await CheckRecoverArrivals(check, faultOnAdvance: false, throwOnOpen: false, loseOpenAnswer: true);
        foreach (var kind in new[] { Contract.Kinds.Delete, Contract.Kinds.Discard })
            foreach (var phase in Enum.GetValues<RecoveryPhase>())
                await CheckDeferredParkCommand(kind, phase, check);
        await CheckTerminalCohort(takeover: false, check);
        await CheckTerminalCohort(takeover: true, check);
        await CheckCommandAfterRecoveryEnd(protectAtComplete: false, check);
        await CheckCommandAfterRecoveryEnd(protectAtComplete: true, check);
        await CheckCommandAfterRecoveryEnd(protectAtComplete: false, check, thenDelete: true);
        await CheckCommandAfterRecoveryEnd(protectAtComplete: false, check, thenDelete: true, competes: true);
        await CheckCommandAfterRecoveryEnd(protectAtComplete: false, check, endedTerminate: EndedTerminate.Selected);
        await CheckCommandAfterRecoveryEnd(protectAtComplete: false, check, endedTerminate: EndedTerminate.Stale);
        await CheckCommandAfterRecoveryEnd(protectAtComplete: false, check, endedTerminate: EndedTerminate.Unselected);
        await CheckCommandAfterRecoveryEnd(protectAtComplete: false, check, endedTerminate: EndedTerminate.Empty);
        await CheckTerminalCohort(takeover: false, check, cutAfter: false);
        await CheckTerminalCohort(takeover: false, check, cutAfter: true);
        await CheckTerminalChildNotice(keptBeforeResume: false, check);
        await CheckTerminalChildNotice(keptBeforeResume: true, check);
        await CheckTerminalChildNotice(keptBeforeResume: true, check, holdNotices: true);
        await CheckTerminalChildNotice(keptBeforeResume: false, check, ordinaryEnd: true);
    }

    private enum RecoveryPhase { Open, Reparked, Complete }

    private static async Task CheckDeferredParkCommand(string kind, RecoveryPhase phase, Action<bool, string> check)
    {
        const long initial = 1_701_400_028_500;
        const int attempts = 3;
        var now = initial;
        var due = initial + (phase == RecoveryPhase.Open ? 1 : 1_000);
        var store = new InMemoryStore(1, () => now);
        var probe = new Probe();
        var scope = new Contract.BoundScope("deferred-park-command");
        var requests = new Requests("deferred-park-command");
        var ingress = new InMemoryIngress(store, requests, "deferred-park-command", (_, name) => name == Contract.Kinds.Recover);
        var label = $"P-6 {kind} at {phase}";
        var first = Owner("phase-first");
        await using var initialOwner = (await new DurableHost([first]).ActivateAsync(0))!;
        var birth = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready")), "birth", Contract.Kinds.Start);
        var actor = birth.Actor!.Value;
        var target = new Contract.Target.Ref(actor);
        await Pump(initialOwner);
        await ingress.SendAsync(scope, target, "fault", "poison");
        await Pump(initialOwner);
        await ingress.SendAsync(scope, target, "trip", phase == RecoveryPhase.Reparked ? "poison" : "advance");
        if (phase == RecoveryPhase.Reparked)
            for (var index = 0; index < InMemoryStore.ParkPageLimit - 1; index++)
                await ingress.SendAsync(scope, target, $"kept-{index:D3}", "advance");
        await Pump(initialOwner);

        var parked = Read();
        var selection = (await new ActorList(store, 5).GetAsync(scope, actor))!.Selection;
        var request = requests.OpenBound("operator", due + 1_000, scope.Tenant, null);
        var queued = store.AdmitFoundationOperator(scope.Tenant, actor.Id, null, "operator", kind,
            null, due, null, selection, request.ReplyAddress("deferred-park-command"));
        var staged = parked is { Park: not null, Terminal: not null } && queued.Outcome == InMemoryStore.OperatorAdmission.Accepted &&
            Delivery("operator").Message.TargetCreation == parked.Row.Creation &&
            Delivery("trip") is { Done: true, KeptBy: ParkKeepReason.Arrived };
        check(staged, $"{label}: the original Park mark qualifies the delayed command before Resume");
        if (!staged) return;

        probe.PausePages = phase == RecoveryPhase.Open;
        await ingress.SendAsync(scope, target, "resume", Contract.Kinds.Recover, Json.Write(new Contract.OperatorCommand(selection)));
        await Step(first);
        if (phase == RecoveryPhase.Open)
            await Step(first);
        if (phase == RecoveryPhase.Reparked)
        {
            for (var pass = 0; pass < 10 && Delivery("trip").ReleasedBy is null; pass++) await Step(first);
            var released = Read().Row.ParkRecovery is { Cursor: not null } &&
                Delivery("trip") is { Done: false, ReleasedBy: not null } && Kept().Length != 0;
            check(released, $"{label}: page one releases the trip input while a kept suffix remains");
            if (!released) return;
            Delivery("trip").Attempts = attempts;
        }
        var epoch = first.Fences.Single().Epoch;
        await initialOwner.DisposeAsync();
        now++;
        var cold = Owner("phase-cold");
        await using var successor = (await new DurableHost([cold]).ActivateAsync(0))!;
        for (var pass = 0; phase != RecoveryPhase.Open && pass < 20 &&
            (Read().Row.ParkRecovery is not null || phase == RecoveryPhase.Complete && probe.RecoveredArrivals == 0); pass++)
        {
            if (phase == RecoveryPhase.Complete) now += 11;
            await Step(cold);
        }

        var before = Read();
        var ready = phase switch
        {
            RecoveryPhase.Open => before is { Terminal: null, Row: { Status: InstanceStatus.Running, ParkRecovery: { } drain } } &&
                store.Messages[drain.Control].Message.Due > due,
            RecoveryPhase.Reparked => before.Row is { Status: InstanceStatus.Faulted, ParkRecovery: null } &&
                before.Park == parked.Park && before.Terminal == parked.Terminal && Delivery("trip").KeptBy == ParkKeepReason.Consumed,
            _ => before is { Park: null, Terminal: null, Row: { Status: InstanceStatus.Running, ParkRecovery: null } } && probe.RecoveredArrivals == 1
        };
        ready &= cold.Fences.Single().Epoch > epoch && request.Answer is null && Delivery("operator") is { Done: false, Seen: 0 };
        check(ready, $"{label}: the successor has the intended lifecycle phase before the command is due " +
            $"(status {before.Row.Status}; drain {before.Row.ParkRecovery is not null}; applied {probe.RecoveredArrivals})");
        if (!ready) return;

        var kept = Kept();
        var journal = store.Journal.Values.ToArray();
        now = due;
        for (var pass = 0; pass < 5 && request.Answer is null; pass++) await Step(cold);
        if (phase == RecoveryPhase.Reparked)
        {
            var expectedKind = kind == Contract.Kinds.Delete ? ParkClearKind.Delete : ParkClearKind.Discard;
            check(request.Answer is "started" && Read().Row.ParkDiscard is { Operation: "operator", Kind: var actual } && actual == expectedKind,
                $"{label}: the original selection opens the requested resolution after re-park");
            await Pump(successor);
            var ended = store.Instances[parked.Row.Id];
            check(Kept().Length == 0 && ended.ParkDiscard is null &&
                (expectedKind == ParkClearKind.Delete ? ended.Retired : ended.ParkDiscarded),
                $"{label}: the resolution completes with all kept rows settled");
        }
        else
        {
            var reason = phase == RecoveryPhase.Open && kind == Contract.Kinds.Discard ? "ResolutionOpen" : "StaleSelection";
            check(request.Answer is CreationResult result && result.Result == reason && Read() == before &&
                Kept().SequenceEqual(kept) && store.Journal.Values.SequenceEqual(journal),
                $"{label}: owner refuses {reason} and preserves the checkpoint, kept rows and journal (reply {request.Answer})");
        }

        Worker Owner(string node) => new(node, 0, store, new ActorAdapter(store, probe), new WorkerOptions
        {
            Monotonic = () => now - initial, Horizon = TimeSpan.Zero, MaxAttempts = attempts, PassTurnBudget = 1,
            Transport = new OperatorReplies(requests, _ => { })
        });
        InMemoryStore.ActorCheckpoint Read() => store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!;
        InMemoryStore.MessageRow Delivery(string messageId) => store.Messages.Values.Single(row => row.Message.Id == messageId);
        (InboxKey Key, ParkKeepReason? Reason)[] Kept() => store.Messages.Values.Where(row => row.KeptBy is not null)
            .OrderBy(row => row.Seq).Select(row => (row.Message.InboxKey, row.KeptBy)).ToArray();
        async Task Step(Worker worker)
        {
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(parked.Row.Id);
            await worker.FlushAsync();
        }
    }

    private static async Task CheckRecoverArrivals(Action<bool, string> check, bool faultOnAdvance, bool throwOnOpen,
        bool holdAdvance = false, bool holdControl = false, bool loseOpenAnswer = false)
    {
        const long initial = 1_701_400_028_000L;
        var now = initial;
        var store = new InMemoryStore(1, () => now) { ParkedRetentionWindow = TimeSpan.FromSeconds(1) };
        var probe = new Probe(withholdFaulted: true) { FaultOnAdvance = faultOnAdvance, HoldAdvance = holdAdvance, HoldSuspend = holdControl };
        var scope = new Contract.BoundScope("recover-arrivals");
        var opens = 0;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed && loseOpenAnswer && batch.Turns.Any(turn => turn.FoundationRecoveryTimers is not null) && ++opens == 1)
                throw new StoreException(Failure.Unknown, "The first recovery Open landed without its answer.");
        });
        Worker Owner(string node) => new(node, 0, wire, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - initial });
        var worker = Owner("recover-arrivals");
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node,
            (_, kind) => kind == Contract.Kinds.Recover || holdControl && kind == Contract.Kinds.Suspend);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(started.Actor!.Value);
        await Pump(owner);
        var shortWindow = new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(1));
        await ingress.SendAsync(scope, target, "fault", "poison", options: shortWindow);
        await Pump(owner);
        await ingress.SendAsync(scope, target, "keep", "advance",
            faultOnAdvance ? new string('x', ParkRecoveryCredit.LimitBytes) : null, options: shortWindow);
        if (faultOnAdvance) await ingress.SendAsync(scope, target, "suffix", "advance", options: shortWindow);
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
        var command = Json.Write(new Contract.OperatorCommand(selection, Contract.RecoverMode.Resume));
        if (throwOnOpen)
        {
            probe.ThrowOnRunningWithholds = true;
            await ingress.SendAsync(scope, target, "bad-resume", Contract.Kinds.Recover, command);
            var escaped = false;
            try { await Pump(owner); }
            catch (InvalidOperationException error) when (error.Message == "withholding probe failed") { escaped = true; }
            var afterFailure = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
            check(!escaped && afterFailure.Row == parked.Row && afterFailure.Park == parked.Park &&
                store.FoundationExpiry(afterFailure.Row)?.Armed is not null &&
                store.Messages.Values.Any(row => row.Message.Id == "bad-resume" && row.Done),
                "a failing withholding predicate settles Resume and preserves the original Park");
            if (escaped) return;
            probe.ThrowOnRunningWithholds = false;
        }
        probe.PausePages = holdControl;
        var result = await ingress.SendAsync(scope, target, "resume", Contract.Kinds.Recover, command);
        if (loseOpenAnswer)
        {
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            var originalEpoch = worker.Fences.Single().Epoch;
            worker.RunTurn(parked.Row.Id);
            await worker.FlushAsync();
            var opened = store.Instances[parked.Row.Id];
            var unacknowledged = opens == 1 && opened.ParkRecovery is { Cursor: null, Operation: "resume" } &&
                opened.ParkResolutionGeneration == parked.Row.ParkResolutionGeneration + 1 && probe.RecoveredArrivals == 0;
            check(unacknowledged, "M1-3 the first Open is durable before its owner loses the answer");
            if (!unacknowledged) return;
            var retry = await ingress.SendAsync(scope, target, "resume", Contract.Kinds.Recover, command);
            await owner.DisposeAsync();
            now += (long)Defaults.Lease.TotalMilliseconds + 1;
            var next = Owner("recover-after-lost-open");
            await using var successor = (await new DurableHost([next]).ActivateAsync(0))!;
            for (var pass = 0; pass < 5; pass++)
            {
                await Pump(successor);
                now += 11;
            }
            var ended = store.Instances[parked.Row.Id];
            var again = await ingress.SendAsync(scope, target, "resume", Contract.Kinds.Recover, command);
            var history = await new ActorList(store, 20).TailAsync(scope, target.Actor);
            var outcomes = history.Items.Select(entry => Json.Read<JsonElement>(entry.Entry))
                .Where(entry => entry.ValueKind == JsonValueKind.Object &&
                    entry.TryGetProperty("Outcome", out var outcome) && outcome.GetString() == "recovered").ToArray();
            var oneOutcome = outcomes is [var recovery] && recovery.GetProperty("Operation").GetString() == "resume" &&
                recovery.GetProperty("Restored").GetInt32() == 1 && recovery.GetProperty("Cleared").GetInt32() == 1;
            check(result.Outcome == Contract.AdmissionOutcome.Accepted &&
                retry is { Outcome: Contract.AdmissionOutcome.Duplicate } && retry.Actor == started.Actor &&
                again is { Outcome: Contract.AdmissionOutcome.Duplicate } && again.Actor == started.Actor &&
                next.Fences.Single().Epoch > originalEpoch && opens == 1 &&
                ended.ParkRecovery is null && ended.ParkResolutionGeneration == opened.ParkResolutionGeneration &&
                oneOutcome && probe.RecoveredArrivals == 1,
                "M1-3 both retries return the original receipt and one recovery outcome survives the cold take");
            return;
        }
        if (holdControl)
        {
            await Pump(owner);
            var drain = store.Instances[parked.Row.Id].ParkRecovery;
            var delayed = drain is not null && store.Messages.GetValueOrDefault(drain.Control)?.Message.Due > now;
            check(delayed, "the held-command fixture pauses the open recovery page");
            if (!delayed) return;
            var suspend = await ingress.SendAsync(scope, target, "suspend", Contract.Kinds.Suspend);
            await Pump(owner);
            check(suspend.Outcome == Contract.AdmissionOutcome.Accepted && probe.SuspendOffers == 1,
                "a held command is not repeatedly offered without a fresh stimulus");
            await ingress.SendAsync(scope, target, "still-withheld", "late");
            await Pump(owner);
            check(probe.SuspendOffers == 1, "an arrival withheld until Complete cannot wake the held command");
            probe.PausePages = false;
        }
        if (holdAdvance)
        {
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(parked.Row.Id);
            await worker.FlushAsync();
            var opened = store.Instances[parked.Row.Id].ParkRecovery is not null;
            check(opened, "the held-cohort fixture opens before the late arrival");
            if (!opened) return;
            await ingress.SendAsync(scope, target, "late", "late");
            // Exercise the store contract where later acceptance may have a lower ordering tuple.
            store.Messages.Values.Single(row => row.Message.Id == "late").Seq =
                store.Messages.Values.Single(row => row.Message.Id == "keep").Seq - 1;
        }
        for (var pass = 0; pass < 5; pass++)
        {
            await Pump(owner);
            now += 11;
        }
        var restored = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        if (holdControl)
        {
            check(restored is { Park: null, Row.ParkRecovery: null } && probe.RecoveredArrivals == 1,
                "a new page can run while a public command remains held");
            return;
        }
        if (faultOnAdvance)
        {
            check(result.Outcome == Contract.AdmissionOutcome.Accepted &&
                restored is { Park: null, EndedAt: not null, Row: { Status: InstanceStatus.Faulted, ParkRecovery: null } } &&
                probe.RecoveredArrivals == 1 && store.Messages.Values.All(row => row.KeptBy is null),
                "a model that ends Faulted can finish recovery and clear the kept suffix");
            return;
        }
        if (holdAdvance)
        {
            check(probe.LateOffer.SequenceEqual(["advance", "late"]), "a held cohort member precedes the late arrival in the model inbox");
            check(restored is { Park: null, Row.ParkCohort: null } &&
                store.Messages.Values.All(row => row.Done || row.ReleasedBy is null),
                "the turn consuming the last held cohort member clears the durable mark");
            return;
        }
        check(result.Outcome == Contract.AdmissionOutcome.Accepted &&
            restored is { Park: null, Row: { Status: InstanceStatus.Running, State: "advanced" } } &&
            probe.PoisonOffers == 1 && probe.RecoveredArrivals == 1 &&
            !store.Messages.Values.Any(row => row.KeptBy is not null || row.Message.Kind == Kinds.ParkExpiry),
            "Resume clears the consumed fault, applies the kept arrival once and recalls the expiry");
    }

    private static async Task CheckTerminalCohort(bool takeover, Action<bool, string> check, bool? cutAfter = null)
    {
        const long initial = 1_701_400_028_000L;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var probe = new Probe(withholdFaulted: takeover) { FaultOnAdvance = true, HoldTerminalInput = !takeover };
        var scope = new Contract.BoundScope("terminal-recovery");
        var cleared = 0;
        Worker MakeWorker(string node, int limit, IKernelStore? actual = null) => new(node, 0, actual ?? store, new ActorAdapter(store, probe),
            new WorkerOptions
            {
                Monotonic = () => now - initial, PollLimit = limit,
                OnPreparedCheckpoint = (turn, _, _) =>
                {
                    if (turn.FoundationRecoveryPage is { TerminalInputs: true } page) cleared += page.Rows.Count;
                }
            });
        var interrupted = false;
        var cut = cutAfter is null ? null : new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (interrupted || landed != cutAfter || !batch.Turns.Any(turn => turn.FoundationRecoveryPage is { TerminalInputs: true })) return;
            interrupted = true;
            throw new StoreException(Failure.Unknown, "lost terminal page outcome");
        });
        var first = MakeWorker("terminal-first", 1024, cut);
        var ingress = new InMemoryIngress(store, new Requests(first.Node), first.Node, (_, kind) => kind == Contract.Kinds.Recover);
        await using var owner = (await new DurableHost([first]).ActivateAsync(0))!;
        var birth = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(birth.Actor!.Value);
        await Pump(owner);
        await ingress.SendAsync(scope, target, "fault", "poison");
        await Pump(owner);
        await ingress.SendAsync(scope, target, "first", "advance");
        for (var index = 0; index < (takeover ? 174 : 1); index++)
            await ingress.SendAsync(scope, target, $"held-{index:D3}", "terminal-pending");
        await Pump(owner);

        var parked = (await new ActorList(store, 10).GetAsync(scope, target.Actor))!;
        await ingress.SendAsync(scope, target, "resume", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(parked.Selection!, Contract.RecoverMode.Resume)));
        if (cutAfter is not null)
        {
            for (var pass = 0; pass < 16 && !interrupted; pass++) { now += 11; await Pump(owner); }
            var held = store.Messages.Values.Single(row => row.Message.Id == "held-000");
            check(interrupted && held.Done == cutAfter && !first.Owns, "the terminal checkpoint loses its outcome at the requested cut");
            if (!interrupted) return;
            var oldDeadline = held.Message.KeepUntil;
            now += (long)Defaults.Lease.TotalMilliseconds + 1;
            var successor = MakeWorker("terminal-unknown-successor", 1024);
            await using var active = (await new DurableHost([successor]).ActivateAsync(0))!;
            await Verify(active);
            check(held is { Done: true, Held: false, Trial: 0, Attempts: 0 } && held.Message.KeepUntil > now &&
                (cutAfter.Value ? held.Message.KeepUntil == oldDeadline : held.Message.KeepUntil > oldDeadline),
                cutAfter.Value ? "the successor retains a landed terminal page's exact settlement" :
                    "the successor replays an unlanded terminal page without putting its held input on trial");
        }
        else if (takeover)
        {
            var id = store.Instances.Values.Single().Id;
            for (var step = 0; step < 2; step++)
            {
                await first.PollAsync();
                await first.LoadAsync();
                await first.FlushAsync();
                first.RunTurn(id);
                await first.FlushAsync();
            }
            check(store.Messages.Values.Count(row => !row.Done && row.ReleasedBy is not null) == 169,
                "the old owner releases a bounded cohort before takeover");
            var epoch = first.Fences.Single().Epoch;
            await owner.DisposeAsync();
            var successor = MakeWorker("terminal-successor", 1);
            await using var active = (await new DurableHost([successor]).ActivateAsync(0))!;
            check(successor.Fences.Single().Epoch > epoch, "the terminal recovery fixture moves to a fresh one-row poll owner");
            await Verify(active);
        }
        else await Verify(owner);

        async Task Verify(IScopeActivation active)
        {
            for (var pass = 0; pass < 24; pass++) { now += 11; await Pump(active); }
            var resolved = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
            check(resolved is { Park: null, Row: { Status: InstanceStatus.Faulted, ParkCohort: null, ParkRecovery: null } } &&
                store.Messages.Values.All(row => row.KeptBy is null && (row.ReleasedBy is null || row.Done)) &&
                probe.RecoveredArrivals == 1 && cleared > 0,
                takeover ? "a successor settles all released terminal inputs before Complete" :
                    "terminal recovery settles a model-held released input without another stimulus");
            var receipt = store.Journal.Values.SingleOrDefault(row => row.Entries.Contains("\"recovered\"", StringComparison.Ordinal));
            check(receipt is not null && Json.Read<RecoveryTally>(receipt.Entries) == (takeover ? new(169, 7) : new(2, 1)),
                "terminal settlement does not count an already released input twice in the recovery receipt");
            if (!takeover)
            {
                await ingress.SendAsync(scope, target, "after-end", "terminal-query");
                await Pump(active);
                check(probe.TerminalQueryOffer.SequenceEqual(["terminal-query"]),
                    "terminal dispatch after Complete still accepts a model query without returning the old held input");
            }
        }
    }

    private enum EndedTerminate { Selected, Stale, Unselected, Empty }

    private static async Task CheckCommandAfterRecoveryEnd(bool protectAtComplete, Action<bool, string> check,
        bool thenDelete = false, bool competes = false, EndedTerminate? endedTerminate = null)
    {
        const long initial = 1_701_400_029_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var withRegistry = endedTerminate is EndedTerminate.Selected or EndedTerminate.Stale or EndedTerminate.Unselected;
        var probe = new Probe
        {
            FaultOnAdvance = endedTerminate is null, FinishOnAdvance = endedTerminate is not null,
            HoldTerminalInput = true, HonourTerminate = thenDelete || withRegistry, ArmJobOnEnd = withRegistry
        };
        var scope = new Contract.BoundScope("terminal-recovery-delete");
        var replies = new Requests("terminal-recovery-delete");
        var scheduler = new HeldScheduler();
        var ingress = new InMemoryIngress(store, replies, "terminal-recovery-delete",
            (_, kind) => kind is Contract.Kinds.Recover or Contract.Kinds.Delete or Contract.Kinds.Terminate);
        var landed = new List<TurnCommit>();
        var testedGuard = false;
        var wire = new CountingStore(store, () => { }, (batch, committed) =>
        {
            if (committed) landed.AddRange(batch.Turns);
        }, async batch =>
        {
            if (testedGuard || batch.Turns.FirstOrDefault(turn => turn.Row?.ParkRecovery?.Deletion is not null) is not { } attach)
                return;
            testedGuard = true;
            var row = attach.Row!;
            var drain = row.ParkRecovery!;
            var prior = store.Instances[row.Id];
            var forged = attach with { Row = row with { ParkRecovery = drain with { Control = new("forged", ParkDrainEnvelope.Scope) } } };
            var altered = batch with { Turns = batch.Turns.Select(turn => ReferenceEquals(turn, attach) ? forged : turn).ToArray() };
            check(await DefiniteRejection(() => store.CommitAsync(altered)) && store.Instances[row.Id] == prior &&
                store.Messages.ContainsKey(drain.Control),
                "the fenced writer refuses a takeover that changes the outstanding control");
        });
        var first = Owner("ended-delete-first");
        await using var active = (await new DurableHost([first]).ActivateAsync(0))!;
        var root = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "root", null)), "parent", Contract.Kinds.Start);
        var parent = new Contract.Target.Ref(root.Actor!.Value);
        var parentId = store.FoundationTarget(scope.Tenant, parent.Actor.Id)!.Id;
        await Pump(active);
        await ingress.SendAsync(scope, parent, "spawn", "spawn");
        await Pump(active);
        var target = new Contract.Target.Ref(probe.Child!.Value);
        var id = Checkpoint().Row.Id;
        string? descendant = null;
        if (thenDelete || withRegistry)
        {
            await ingress.SendAsync(scope, target, "spawn-descendant", "spawn");
            await Pump(active);
            descendant = store.FoundationTarget(scope.Tenant, probe.Child!.Value.Id)!.Id;
        }
        var due = now + 1;
        var terminate = endedTerminate is null ? null : replies.OpenBound("ended-terminate", now + 1_000, scope.Tenant, null);
        if (endedTerminate is EndedTerminate.Unselected or EndedTerminate.Empty)
            QueueTerminate(selection: null);
        await ingress.SendAsync(scope, target, "fault", "poison");
        await Pump(active);
        var reads = new ActorList(store, 5);
        if (endedTerminate == EndedTerminate.Stale)
        {
            var original = Checkpoint();
            var previousPark = (await reads.GetAsync(scope, target.Actor))!.Selection!;
            QueueTerminate(previousPark);
            await ingress.SendAsync(scope, target, "earlier-recovery", Contract.Kinds.Recover,
                Json.Write(new Contract.OperatorCommand(previousPark, Contract.RecoverMode.Resume)));
            await Pump(active);
            if (Checkpoint() is not { Park: null, Row.Status: InstanceStatus.Running })
            {
                check(false, "P-8 Stale: the earlier recovery must complete while its Terminate is still delayed");
                return;
            }
            await ingress.SendAsync(scope, target, "fault-again", "poison");
            await Pump(active);
            var later = Checkpoint();
            var changedPark = later.Park is not null && later.Park != original.Park &&
                later.Row.Creation == original.Row.Creation;
            check(changedPark, "P-8 Stale: the selected Terminate names a different Park of the same creation");
            if (!changedPark) return;
        }
        await ingress.SendAsync(scope, target, "advance", "advance");
        for (var index = 0; index < 2 * InMemoryStore.ParkPageLimit; index++)
            await ingress.SendAsync(scope, target, $"kept-{index:D4}", "terminal-pending");
        await Pump(active);

        var park = (await reads.GetAsync(scope, target.Actor))!.Selection!;
        if (endedTerminate == EndedTerminate.Selected) QueueTerminate(park);
        await ingress.SendAsync(scope, target, "resume-ended-delete", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(park, Contract.RecoverMode.Resume)));
        await Step(first);
        for (var pass = 0; pass < 5 && Checkpoint().Row.Status == InstanceStatus.Running; pass++)
            await Step(first);
        var ended = Checkpoint();
        var drain = ended.Row.ParkRecovery;
        var waitingControl = drain is not null && store.Messages.GetValueOrDefault(drain.Control)?.Message.Due > now;
        var expectedEnd = endedTerminate is null ? InstanceStatus.Faulted : InstanceStatus.Finished;
        var endedWithSuffix = ended.Row.Status == expectedEnd && drain?.Cursor is not null && waitingControl &&
            ended.Terminal is not null && Kept() > 0;
        check(endedWithSuffix, "the model ends with its own mark while the delayed recovery page still has a kept suffix");
        if (!endedWithSuffix) return;
        if (endedTerminate is { } variant)
        {
            var job = withRegistry ? InboxKey.Ticket(id, "cleanup", ended.Row.Creation,
                JobRegistry.Generation(ended.Row, "cleanup")!.Value) : (InboxKey?)null;
            var current = store.Messages.Values.Single(row => row.Message.Id == "ended-terminate");
            check(current is { Done: false } && terminate!.Answer is null &&
                  (job is null || store.Messages.ContainsKey(job.Value)) &&
                  (descendant is null || store.Instances[descendant].Status == InstanceStatus.Running),
                $"P-8 {variant}: the queued Terminate reaches the ended model with its initial registry and drain");
            now = due;
            if (variant == EndedTerminate.Stale)
            {
                var next = ended.Row with { Revision = ended.Row.Revision + 1, Jobs = null, Children = null };
                var registry = CascadeRegistry.From(ended.Row);
                var page = CascadeProtocol.Page(next, registry, cursor: 0, serial: next.Revision);
                var forged = new TurnCommit(id, next with { Cascade = page.Next }, ended.Row.Revision,
                    [current.Message.InboxKey], [], page.Sent, [], [], null) { FoundationCascade = new(registry) };
                var fence = first.Fences.Single();
                var rejected = await DefiniteRejection(() => store.CommitAsync(new(
                    fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [forged], [], [])));
                var intact = rejected && Checkpoint() == ended && store.Messages.ContainsKey(current.Message.InboxKey) &&
                    store.Partitions[fence.Partition].Position == fence.Position;
                check(intact, "P-8 Stale: the store rejects an unauthorized composed cascade before any write");
                if (!intact) return;
            }
            await Step(first);
            var settled = Checkpoint();
            var actualReply = terminate!.Answer;
            var discharges = withRegistry && variant != EndedTerminate.Stale;
            var expectedRow = discharges
                ? ended.Row with { Revision = ended.Row.Revision + 1, Children = null, Jobs = null }
                : ended.Row;
            check(settled.Row == expectedRow && settled.Terminal == ended.Terminal &&
                (variant is EndedTerminate.Selected or EndedTerminate.Stale
                    ? actualReply is CreationResult { Result: "ResolutionOpen" } : actualReply is null) &&
                (job is null || store.Messages.ContainsKey(job.Value) != discharges),
                $"P-8 {variant}: the command preserves the model end and applies only authorized cleanup");
            for (var pass = 0; pass < 16 && Checkpoint().Row.ParkRecovery is not null; pass++)
            {
                now += 11;
                await Step(first);
            }
            await Pump(active);
            var notices = landed.SelectMany(turn => turn.Sent).Count(message =>
                message.Kind == Kinds.ChildFinished && message.Sender == id && message.Read<FoundationChildNotice>().Final);
            check(Checkpoint() is { Park: null, Row: { Status: InstanceStatus.Finished, ParkRecovery: null } } &&
                Checkpoint().Terminal == ended.Terminal && notices == 1 &&
                (descendant is null || store.Instances[descendant].Status ==
                    (discharges ? InstanceStatus.Cancelled : InstanceStatus.Running)),
                $"P-8 {variant}: the drain completes and the parent gets only the model's original final notice");
            return;
        }

        var selection = (await reads.GetAsync(scope, target.Actor))!.Selection!;
        var invalid = await ingress.SendAsync(scope, target, "old-park-delete", Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(park)));
        var oldRejected = invalid is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection };
        var owns = thenDelete && !competes;
        var command = new Contract.OperatorCommand(owns ? park : selection, ThenDelete: owns, ExpectedTarget: owns ? target.Actor : null);
        var admitted = await ingress.SendAsync(scope, target, "after-end-delete",
            owns ? Contract.Kinds.Terminate : Contract.Kinds.Delete, Json.Write(command));
        await Step(first);
        var attached = Checkpoint();
        var deletion = new ParkRecoveryDeletion("after-end-delete", command.Selection, owns);
        var preserves = oldRejected && admitted.Outcome == Contract.AdmissionOutcome.Accepted &&
            attached.Terminal == ended.Terminal && attached.Row == (ended.Row with
            {
                Revision = ended.Row.Revision + 1,
                ParkRecovery = drain! with { Deletion = deletion },
                Children = owns ? null : ended.Row.Children
            });
        check(preserves, "Delete rejects the old Park mark and attaches to the ended Recover under the model end's exact mark");
        if (!preserves) return;

        probe.ProtectAfterRecoveryEnd = true;
        var secondCommand = new Contract.OperatorCommand(competes ? park : selection,
            ThenDelete: competes, ExpectedTarget: competes ? target.Actor : null);
        var second = ingress.RequestAsync(scope, target, "competing-ended-delete",
            competes ? Contract.Kinds.Terminate : Contract.Kinds.Delete,
            Json.Write(secondCommand), null, new Contract.RequestOptions(null, now + 1_000)).AsTask();
        await Step(first);
        var competing = await second.WaitAsync(TimeSpan.FromSeconds(5));
        var expectedAfter = competes ? attached.Row with { Revision = attached.Row.Revision + 1, Children = null } : attached.Row;
        check(testedGuard && competing is { Error: "in progress", Item: "after-end-delete" } && Checkpoint().Row == expectedAfter,
            competes ? "a competing ThenDelete returns in progress and discharges its subtree without taking over"
                : "a competing Delete sees the owning operation even after deletion protection begins");
        probe.ProtectAfterRecoveryEnd = protectAtComplete;
        await active.DisposeAsync();
        now += 11;
        var cold = Owner("ended-delete-cold");
        await using var successor = (await new DurableHost([cold]).ActivateAsync(0))!;
        for (var pass = 0; pass < 30 && store.Instances[id].ParkRecovery is not null; pass++)
        {
            now += 11;
            await Step(cold);
        }
        if (descendant is not null) await Pump(successor);
        var completed = store.Instances[id];
        var journal = store.Journal.Values.Where(row => row.Instance == id).ToArray();
        var receipt = journal.SingleOrDefault(row => row.Entries.Contains("after-end-delete", StringComparison.Ordinal));
        var noticeCount = landed.SelectMany(turn => turn.Sent).Count(message =>
            message.Kind == Kinds.ChildFinished && message.Sender == id && message.Read<FoundationChildNotice>().Final);
        check(completed.ParkRecovery is null && Kept() == 0 && noticeCount == 1 && receipt is not null &&
            (descendant is null || store.Instances[descendant].Status == InstanceStatus.Cancelled) &&
            receipt.Entries.Contains("resume-ended-delete", StringComparison.Ordinal) &&
            (protectAtComplete
                ? !completed.Retired && Checkpoint().Terminal == ended.Terminal && receipt.Entries.Contains("protected", StringComparison.Ordinal)
                : completed.Retired && journal is [{ Revision: -1 }] && receipt.Entries.Contains("deleted", StringComparison.Ordinal)),
            protectAtComplete
                ? "Complete preserves the model end mark when protection starts after an ended Recover is taken over"
                : thenDelete ? "ThenDelete discharges the ended Recover subtree once with no second parent notice"
                : "a cold successor settles the suffix and retires an ended Recover without sending a second parent notice");

        Worker Owner(string node) => new(node, 0, wire, new ActorAdapter(store, probe),
            new WorkerOptions
            {
                Monotonic = () => now - initial, PollLimit = 1_024,
                Transport = new OperatorReplies(replies, _ => { }),
                JobScheduler = endedTerminate is null ? null : scheduler
            });
        void QueueTerminate(string? selection)
        {
            var accepted = store.AdmitFoundationOperator(scope.Tenant, target.Actor.Id, null, "ended-terminate",
                Kinds.Terminate, null, due, null, selection, terminate!.ReplyAddress("terminal-recovery-delete"));
            if (accepted.Outcome != InMemoryStore.OperatorAdmission.Accepted)
                throw new InvalidOperationException($"The fixture could not queue Terminate: {accepted.Outcome}.");
        }
        InMemoryStore.ActorCheckpoint Checkpoint() => store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        int Kept() => store.Messages.Values.Count(message => message.Message.Target.Id == id && message.KeptBy is not null);
        async Task Step(Worker worker)
        {
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(parentId);
            worker.RunTurn(id);
            await worker.FlushAsync();
        }
    }

    private sealed record RecoveryTally(long Restored, long Cleared);

    private static async Task CheckTerminalChildNotice(bool keptBeforeResume, Action<bool, string> check,
        bool holdNotices = false, bool ordinaryEnd = false)
    {
        const long initial = 1_701_400_040_000L;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var probe = new Probe(withholdFaulted: !holdNotices && !ordinaryEnd)
        {
            FaultOnAdvance = !ordinaryEnd, HoldTerminalNotices = holdNotices
        };
        var scope = new Contract.BoundScope("terminal-child");
        var worker = new Worker("terminal-parent", 0, store, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - initial });
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node,
            (_, kind) => kind is Contract.Kinds.Recover or Contract.Kinds.Delete);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var birth = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready", null)), "parent", Contract.Kinds.Start);
        var parent = new Contract.Target.Ref(birth.Actor!.Value);
        await Pump(owner);
        var children = new List<Contract.Target.Ref>();
        if (!ordinaryEnd) await Spawn();
        if (holdNotices) await Spawn();
        await ingress.SendAsync(scope, parent, "park", "poison");
        await Pump(owner);
        if (holdNotices) await FinishChildren();
        await ingress.SendAsync(scope, parent, "end", "advance");
        if (keptBeforeResume)
        {
            if (!holdNotices) await FinishChildren();
            await Pump(owner);
            check(store.Messages.Values.Count(row => row.Message.Kind == Contract.Kinds.ChildFinished && row.KeptBy == ParkKeepReason.Arrived) == children.Count,
                "the authentic final child notice is kept before Resume");
        }
        else await Pump(owner);
        var reader = new ActorList(store, 10);
        var parked = (await reader.GetAsync(scope, parent.Actor))!;
        await ingress.SendAsync(scope, parent, "resume", Contract.Kinds.Recover, Json.Write(new Contract.OperatorCommand(parked.Selection!)));
        if (holdNotices)
        {
            var id = store.ReadFoundationActor(scope.Tenant, parent.Actor.Id).Actor!.Row.Id;
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(id);
            await worker.FlushAsync();
            await ingress.SendAsync(scope, parent, "query", "terminal-query");
        }
        for (var pass = 0; pass < 8; pass++) { now += 11; await Pump(owner); }
        if (ordinaryEnd)
        {
            await Spawn();
            await ingress.SendAsync(scope, parent, "end-outside-recovery", "finish-child");
            await Pump(owner);
        }
        if (!keptBeforeResume) await FinishChildren();
        var terminal = store.ReadFoundationActor(scope.Tenant, parent.Actor.Id).Actor!;
        check(terminal is { Park: null, Row: { ParkRecovery: null, Children: null } } &&
            terminal.Row.Status == (ordinaryEnd ? InstanceStatus.Finished : InstanceStatus.Faulted) &&
            terminal.Row.EndedDuringParkRecovery != ordinaryEnd && probe.Notices.Count == (ordinaryEnd ? 1 : 0),
            ordinaryEnd ? "an actor that ends after a running recovery still receives its child's notice in the model" :
                "authentic final child notices settle the terminal registry without model entry");
        if (holdNotices)
            check(probe.HeldChildOffers >= 2 && probe.TerminalQueryOffer.SequenceEqual(["terminal-query"]),
                "two held child notices settle alone and do not consume the queued terminal query");
        var selected = (await reader.GetAsync(scope, parent.Actor))!.Selection!;
        await ingress.SendAsync(scope, parent, "delete", Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(selected)));
        await Pump(owner);
        check(store.Instances[terminal.Row.Id].Retired, "the settled child no longer protects the parent from a qualified delete");

        async Task Spawn()
        {
            await ingress.SendAsync(scope, parent, $"spawn-{children.Count}", "spawn");
            await Pump(owner);
            children.Add(new(probe.Child!.Value));
        }

        async Task FinishChildren()
        {
            for (var index = 0; index < children.Count; index++)
                await ingress.SendAsync(scope, children[index], $"finish-{index}", "finish-child");
            await Pump(owner);
        }
    }

    internal static async Task CheckInvalidTimerRecoveryPlan(Action<bool, string> check)
    {
        var now = 1_701_400_030_100L;
        var store = new InMemoryStore(1, () => now);
        var probe = new RestorePlanProbe();
        var scope = new Contract.BoundScope("recover-invalid-plan");
        var siblingRefused = false;
        var worker = new Worker("recover-invalid-plan", 0, store, new ActorAdapter(store, probe), new WorkerOptions
        {
            Monotonic = () => 0,
            OnPreparedCheckpoint = (opening, _, _) =>
            {
                if (opening.FoundationRecoveryTimers?.Preserved is not [var key]) return;
                var prior = store.Instances[opening.Instance];
                var timer = store.Messages[key];
                var fence = store.Partitions[0];
                var sibling = new TurnCommit(opening.Instance, null, null, [key], [], [], [], [], null);
                try
                {
                    store.CommitAsync(new(0, fence.Epoch, fence.Position, Defaults.Lease, false, [opening, sibling], [], []))
                        .GetAwaiter().GetResult();
                }
                catch (StoreException error) when (error.Failure == Failure.Definite)
                {
                    siblingRefused = store.Instances[opening.Instance] == prior && store.Messages.GetValueOrDefault(key) == timer;
                }
            }
        });
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node, (_, kind) => kind == Contract.Kinds.Recover);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(started.Actor!.Value);
        await Pump(owner);
        await ingress.SendAsync(scope, target, "fault", "fault");
        await Pump(owner);
        var parked = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        var selection = (await new ActorList(store, 10).GetAsync(scope, target.Actor))!.Selection!;
        var payload = Json.Write(new Contract.OperatorCommand(selection));
        var admitted = await ingress.SendAsync(scope, target, "invalid-plan", Contract.Kinds.Recover, payload);
        await Pump(owner);
        var intact = admitted.Outcome == Contract.AdmissionOutcome.Accepted && store.Instances[parked.Row.Id] == parked.Row &&
            store.Messages.Values.Any(row => row.Message.Id == "invalid-plan" && row.Done) &&
            store.Messages.Values.All(row => row.Message.Kind != Kinds.Drain);
        check(intact, "a null timer entry refuses Resume inside the worker and leaves the exact Park checkpoint intact");
        if (!intact) return;

        probe.ReturnInvalidJob = true;
        await ingress.SendAsync(scope, target, "invalid-job", Contract.Kinds.Recover, payload);
        await Pump(owner);
        check(store.Instances[parked.Row.Id] == parked.Row &&
            store.Subscriptions.Count == 0 && store.Messages.Values.Any(row => row.Message.Id == "invalid-job" && row.Done) &&
            store.Messages.Values.All(row => row.Message.Kind != Kinds.Drain),
            "a malformed unregistered job recipe refuses the whole Open without changing the preserved timer");
        probe.ReturnInvalidJob = false;
        probe.ReturnOversizeJobs = true;
        await ingress.SendAsync(scope, target, "oversize-jobs", Contract.Kinds.Recover, payload);
        await Pump(owner);
        check(store.Instances[parked.Row.Id] == parked.Row &&
            store.Messages.Values.Any(row => row.Message.Id == "oversize-jobs" && row.Done) &&
            store.Messages.Values.All(row => row.Message.Kind != Kinds.Drain),
            "individually valid unregistered job recipes still count against the aggregate Open budget");
        if (store.Instances[parked.Row.Id] != parked.Row) return;
        probe.ReturnOversizeJobs = false;
        probe.ReturnPlan = true;
        var timer = store.Messages.Values.Single(row => row.Message.Kind == "");
        var original = timer.Message;
        await ingress.SendAsync(scope, target, "valid-plan", Contract.Kinds.Recover, payload);
        for (var step = 0; step < 3; step++)
        {
            now += 11;
            await Pump(owner);
        }
        var recovered = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        check(siblingRefused, "a sibling consume cannot remove the timer that Open validates and preserves");
        check(recovered is { Park: null, Row.Status: InstanceStatus.Running } && timer is { Done: false } &&
            timer.Message == original && store.Messages.GetValueOrDefault(original.InboxKey) == timer,
            "a valid originally admitted empty-kind timer keeps its exact pending envelope through Resume");
    }

    internal static async Task CheckRecoverKeptEvents(Action<bool, string> check)
    {
        var chain = FoundationEventIdentity.CatchUpId(InboxKey.Issued("0/α#17.0001"));
        check(chain == "669604af03f392c8860a86731f4fa827b1ae0685646d98812ead016c36b9090a" &&
            FoundationEventIdentity.CatchUpSuccessor(chain, 1) == "meet:1fb93a79fe43ec8fb11eb5c46635d14b6f4328b5e026c22a2061b254742486dc",
            "chain and successor IDs match the canonical full-key and page encodings");
        await CheckRecoverKeptEvents(count: 1, revoke: false, check);
        await CheckRecoverKeptEvents(count: 171, revoke: false, check);
        await CheckRecoverKeptEvents(count: 1, revoke: true, check);
        await CheckRecoverKeptEvents(count: 2, revoke: false, check, byteCut: true);
        await CheckRecoverKeptEvents(count: 1, revoke: false, check, sweepBackstep: true);
        await CheckRecoverKeptEvents(count: 1, revoke: false, check, repeatPublication: true);
        await CheckRecoverKeptEvents(count: 1, revoke: false, check, retryBeforeOpen: true);
        await CheckRecoverKeptEvents(count: 1, revoke: false, check, zeroWindow: true);
        await CheckRecoverKeptEvents(count: 4096, revoke: false, check, probePageWork: true);
        await CheckFailedCatchUp(check, recoverAfterFailure: false);
        await CheckFailedCatchUp(check, recoverAfterFailure: true);
        await CheckFailedCatchUp(check, recoverAfterFailure: false, delayedResponse: true);
        await CheckFailedCatchUp(check, recoverAfterFailure: false, delayedResponse: true, realMonotonic: true);
        await CheckFailedCatchUp(check, recoverAfterFailure: false, revoked: true);
        await CheckFailedCatchUp(check, recoverAfterFailure: false, resolveMeetByDiscard: false);
        await CheckFailedCatchUp(check, recoverAfterFailure: false, resolveMeetByDiscard: true);
        await CheckLostCatchUpStep(check, landedBeforeLoss: false);
        await CheckLostCatchUpStep(check, landedBeforeLoss: true);
        await CheckLostCatchUpStep(check, landedBeforeLoss: false, secondPage: true);
        await CheckLostCatchUpStep(check, landedBeforeLoss: true, secondPage: true);
    }

    private static async Task CheckLostCatchUpStep(Action<bool, string> check, bool landedBeforeLoss, bool secondPage = false)
    {
        var now = 1_701_400_030_200L;
        var store = new InMemoryStore(2, () => now);
        var probe = new KeepingRecoveryProbe();
        var scope = new Contract.BoundScope("catch-up-lost-step");
        var losses = 0;
        var injectRecoveryLoss = false;
        var forward = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (injectRecoveryLoss && batch.Turns is [{ FoundationCatchUpStep: not null }] && landed == landedBeforeLoss &&
                (!landedBeforeLoss || losses == 0))
            {
                losses++;
                throw new StoreException(Failure.Unknown, "the page result was lost");
            }
        });
        Worker TakeWorker(string name) => new(name, 0, forward, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => 0, MaxAttempts = 2 });
        var worker = TakeWorker("first-catch-up-owner");
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node, (_, kind) => kind == Contract.Kinds.Recover);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var reference = store.AdmitFoundationStart(scope.Tenant, new Message("start", InstanceRef.In(0, "lost-step"), Kinds.Start,
            Start: new StartSpec(probe.Type, "ready", null, Creation: new(1, 0, "start")), TargetCreation: 1))!;
        var target = new Contract.Target.Ref(new Contract.ActorRef(reference));
        await Pump(owner);
        await ingress.SendAsync(scope, target, "fault", "fault");
        await Pump(owner);
        var id = store.Instances.Values.Single().Id;
        var count = landedBeforeLoss || secondPage ? InMemoryStore.ParkPageLimit + 1 : 1;
        for (var index = 0; index < count; index++)
            await StoreMatchedEvent(store, scope, "approval", $"owed-{index:D3}", "payload", now + 20);
        var mark = (await new ActorList(store, 10).GetAsync(scope, target.Actor))!.Selection!;
        await ingress.SendAsync(scope, target, "resume", Contract.Kinds.Recover, Json.Write(new Contract.OperatorCommand(mark)));
        await worker.PollAsync();
        await worker.LoadAsync();
        await worker.FlushAsync();
        worker.RunTurn(id);
        await worker.FlushAsync();
        var key = store.Messages.Single(pair => pair.Value.Message.ParkCatchUp is not null).Key;
        if (!landedBeforeLoss && !secondPage)
        {
            var other = (await store.TakeAsync(1, "other-partition", Defaults.Lease))!;
            var local = worker.Fences.Single();
            var foreign = await DefiniteRejection(() => store.CommitAsync(new(1, other.Epoch, other.Position,
                Defaults.Lease, false, [], [], [key])));
            var seenOnly = await DefiniteRejection(() => store.CommitAsync(new(0, local.Epoch, local.Position,
                Defaults.Lease, false, [], [key], [])));
            check(foreign && seenOnly && store.Messages[key] is { Seen: 0, Trial: 0, Attempts: 0 },
                "a positional foreign fence and an ordinary Seen mark cannot hide a pending catch-up ticket");
        }
        if (secondPage)
        {
            var first = store.Messages[key].Message.ParkCatchUp!;
            await worker.PollAsync();
            await worker.MeetAsync();
            var successor = store.Messages.Values.SingleOrDefault(row => row.Message.ParkCatchUp is { Page: 1 })?.Message;
            var advanced = successor is not null && successor.InboxKey != key &&
                successor.ParkCatchUp!.Chain == first.Chain &&
                successor.Id == FoundationEventIdentity.CatchUpSuccessor(first.Chain, 1) &&
                store.Messages.Values.Count(row => row.Message.Kind == Kinds.Event) == InMemoryStore.ParkPageLimit;
            check(advanced, "CC-1 step one consumes its ticket and stores a distinct successor in the same chain");
            if (!advanced) return;
            key = successor!.InboxKey;
            now += 21;
            await store.SweepAsync();
            check(store.FoundationEvents.Values.Any(row => row.Id == "owed-170") && store.Messages.ContainsKey(key),
                "CC-1 an inter-page sweep keeps the expired suffix through the original coverage entry");
        }
        injectRecoveryLoss = true;
        await worker.PollAsync();
        await worker.MeetAsync();
        check(!worker.Owns && losses == 1 && (!landedBeforeLoss ?
                store.Messages.GetValueOrDefault(key) is { Done: false, Attempts: 0, Trial: > 0 } :
                !store.Messages.ContainsKey(key)),
            "an unknown page leaves evidence only when the reserved ticket is still pending");

        // A same-node take after reset advances the fence without treating ordinary job or model trials as deaths.
        worker = TakeWorker("first-catch-up-owner");
        await using var replacement = (await new DurableHost([worker]).ActivateAsync(0))!;
        if (secondPage)
        {
            check(landedBeforeLoss
                    ? !store.Messages.Values.Any(row => row.Message.ParkCatchUp is not null)
                    : store.Messages.GetValueOrDefault(key) is { Done: false, Attempts: 1, Trial: 0 },
                "CC-1 a lost final page leaves either its committed removal or one charged pending step");
            injectRecoveryLoss = false;
            await Pump(replacement);
            await store.SweepAsync();
            await store.SweepAsync();
            check(probe.Received.Count == count && probe.Received.Distinct().Count() == count &&
                store.FoundationEvents.Count == 0 && store.Messages.Values.All(row => row.Message.ParkCatchUp is null),
                "CC-1 the retaken final page delivers the suffix once and releases its sweep coverage");
            return;
        }
        if (landedBeforeLoss)
        {
            now += 21;
            await store.SweepAsync();
            check(store.FoundationEvents.Values.Any(row => row.Id == "owed-170"),
                "CC-1 the sweep still keeps the suffix when step one's landed answer was lost");
            check(store.Messages.Values.Single(row => row.Message.ParkCatchUp is not null) is
                { Attempts: 0, Trial: 0, Message.ParkCatchUp.Page: 1 },
                "a new take leaves the successor of an acknowledged-late page uncharged");
            await Pump(replacement);
            check(probe.Received.Count == count && probe.Received.Distinct().Count() == count,
                "after an unknown landed page all entitled events still reach the model once");
            return;
        }
        check(store.Messages.GetValueOrDefault(key) is { Done: false, Attempts: 1, Trial: 0 },
            "the next fence charges an unresolved step even on a same-node take");
        await worker.PollAsync();
        await worker.MeetAsync();
        now += (long)Defaults.Lease.TotalMilliseconds + 1;
        worker = TakeWorker("different-catch-up-owner");
        await using var last = (await new DurableHost([worker]).ActivateAsync(0))!;
        check(store.Messages.GetValueOrDefault(key) is { Done: false, Attempts: 2, Trial: 0 },
            "another owner charges the second abandoned step exactly once");
        await Pump(last);
        await store.SweepAsync();
        check(losses == 2 && store.Instances[id] is { Status: InstanceStatus.Faulted, ParkRecovery: null } &&
            store.Subscriptions.Count == 0 && store.FoundationEvents.Count == 0 && probe.Received.Count == 0,
            "repeated unknown pre-commit outcomes eventually re-park and release sweep coverage");
    }

    private static async Task CheckFailedCatchUp(Action<bool, string> check, bool recoverAfterFailure,
        bool delayedResponse = false, bool revoked = false, bool realMonotonic = false, bool? resolveMeetByDiscard = null)
    {
        var now = 1_701_400_030_200L;
        var store = new InMemoryStore(1, () => now);
        var probe = new KeepingRecoveryProbe();
        var scope = new Contract.BoundScope("catch-up-attempt");
        var faults = 0;
        CommitBatch? delayed = null;
        var injectRecoveryFailures = false;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var forward = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (landed || !injectRecoveryFailures || batch.Turns is not [{ FoundationCatchUpStep: not null } step]) return;
            switch (faults)
            {
                case 0:
                    faults++;
                    throw new StoreException(Failure.Definite, "injected refusal before the step");
                case 1 when !recoverAfterFailure:
                    faults++;
                    if (delayedResponse) delayed = batch;
                    else now = step.FoundationCatchUpBefore!.Value;
                    break;
            }
        }, beforeCommit: batch => ReferenceEquals(batch, delayed) ? new(release.Task) : default);
        Worker NewOwner(string name) => new(name, 0, forward, new ActorAdapter(store, probe),
            new WorkerOptions
            {
                Monotonic = realMonotonic ? static () => Environment.TickCount64 : static () => 0, MaxAttempts = 2,
                Lease = realMonotonic ? TimeSpan.FromSeconds(1) : Defaults.Lease,
                ParkCatchUpTimeout = delayedResponse && !realMonotonic ? TimeSpan.FromMilliseconds(30) : null
            });
        var worker = NewOwner("first-catch-up");
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node, (_, kind) => kind is Contract.Kinds.Recover or Contract.Kinds.Discard);
        var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(started.Actor!.Value);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump(owner);
        await ingress.SendAsync(scope, target, "fault", "fault");
        await Pump(owner);
        var id = store.Instances.Values.Single().Id;
        var before = (await new ActorList(store, 10).GetAsync(scope, target.Actor))!.Selection!;
        var count = recoverAfterFailure ? 171 : 1;
        for (var index = 0; index < count; index++)
            await StoreMatchedEvent(store, scope, "approval", $"kept-{index:D4}", "owed", now + 20);
        await ingress.SendAsync(scope, target, "resume", Contract.Kinds.Recover, Json.Write(new Contract.OperatorCommand(before)));
        await worker.PollAsync();
        await worker.LoadAsync();
        await worker.FlushAsync();
        worker.RunTurn(id);
        await worker.FlushAsync();
        var originalControl = store.Instances[id].ParkRecovery!.Control;
        var (key, _) = store.Messages.Single(pair => pair.Value.Message.ParkCatchUp is not null);
        injectRecoveryFailures = true;
        await CatchUpOnly(worker);
        var unchanged = worker.Owns && faults == 1 && store.Messages.GetValueOrDefault(key) is
            { Done: false, Attempts: 1, Message.ParkCatchUp.Page: 0 } &&
            store.Messages.Values.All(row => row.Message.Kind != Kinds.Event);
        check(unchanged, "a refused isolated step counts one fenced attempt and advances no delivery or cursor");
        if (!unchanged) return;
        await owner.DisposeAsync();
        worker = NewOwner("second-catch-up");
        await using var successor = (await new DurableHost([worker]).ActivateAsync(0))!;
        try { await CatchUpOnly(worker).WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { release.TrySetResult(); }
        if (recoverAfterFailure)
        {
            check(store.Messages.Values.Single(row => row.Message.ParkCatchUp is not null) is
                { Done: false, Attempts: 0, Message.ParkCatchUp.Page: 1 },
                "a successful retry writes a fresh next-page ticket with its full attempt budget");
            await Pump(successor);
            check(probe.Received.Count == count && probe.Received.Distinct().Count() == count &&
                store.Instances[id] is { Status: InstanceStatus.Running, ParkRecovery: null },
                "the successor completes all kept-event pages once after a prior failed step");
            return;
        }
        check(worker.Owns && faults == 2 && store.Messages.GetValueOrDefault(key) is
            { Done: false, Attempts: 2, Message.ParkCatchUp.Page: 0 } &&
            store.Instances[id].ParkRecovery?.Control == originalControl &&
            store.Messages.Values.All(row => row.Message.Kind != Kinds.Event),
            realMonotonic ? "the default timeout leaves live lease time for a fenced failure write without a heartbeat" :
            delayedResponse ? "the owner stops waiting on a stalled store request and records one failure" :
                "the step deadline at equality writes no page and the successor counts its second failure");
        if (delayed is not null)
        {
            var ejected = await DefiniteRejection(() => store.CommitAsync(delayed));
            check(ejected && store.Messages.GetValueOrDefault(key) is { Attempts: 2, Done: false },
                "the old request cannot apply after its attempt is counted, even before the database deadline");
            now += 21;
        }
        if (revoked)
        {
            var oldWait = store.Subscriptions.Single();
            var unbind = new TurnCommit(id, null, null, [], [], [], [], [oldWait], null);
            var fence = worker.Fences.Single();
            var result = await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease,
                false, [unbind], [], []));
            var newWait = oldWait with { FoundationIncarnation = Guid.NewGuid() };
            await store.SubscribeAsync(newWait);
            await CatchUpOnly(worker);
            check(result.Landed && !store.Messages.ContainsKey(key) && store.Instances[id].ParkRecovery?.Control == originalControl &&
                store.Subscriptions.Single().FoundationIncarnation == newWait.FoundationIncarnation,
                "an exhausted superseded chain settles without re-parking the actor or removing a new wait");
            return;
        }
        await worker.PollAsync();
        await worker.FlushAsync();
        worker.RunTurn(id);
        await worker.FlushAsync();
        check(store.Instances[id].ParkRecovery?.Control == originalControl,
            "the open recovery cannot Complete ahead of an exhausted meet with active coverage");
        await CatchUpOnly(worker);
        check(store.Instances[id] is { Status: InstanceStatus.Faulted, ParkRecovery: null } &&
            store.Messages.GetValueOrDefault(key) is { Done: true, KeptBy: ParkKeepReason.Consumed } &&
            !store.Messages.ContainsKey(originalControl) && store.Subscriptions.Count == 0 && probe.Received.Count == 0,
            "the exhausted meet re-parks an open recovery and atomically removes its subscriptions");
        await store.SweepAsync();
        check(store.FoundationEvents.Count == 0, "the exhausted meet releases coverage of its expired publication");

        if (resolveMeetByDiscard is { } discard)
        {
            injectRecoveryFailures = false;
            var selection = (await new ActorList(store, 5).GetAsync(scope, target.Actor))!.Selection!;
            var resolution = discard ? Contract.Kinds.Discard : Contract.Kinds.Recover;
            await ingress.SendAsync(scope, target, "resolve-old-meet", resolution, Json.Write(new Contract.OperatorCommand(selection)));
            for (var pass = 0; pass < 4; pass++)
            {
                await Pump(successor);
                now += 11;
            }
            var row = store.Instances[id];
            check(row.ParkRecovery is null && row.ParkDiscard is null &&
                store.Messages.GetValueOrDefault(key) is { Done: true, KeptBy: null, ParkDeadLetter: false } &&
                probe.Received.Count == 0 && store.FoundationEvents.Count == 0,
                $"KP-1 {(discard ? "Discard" : "Resume")} clears the old consumed meet without restoring its ticket or delivering an event");
        }

        static async Task CatchUpOnly(Worker owner)
        {
            await owner.PollAsync();
            await owner.LoadAsync();
            await owner.FlushAsync();
            await owner.MeetAsync();
            await owner.FlushAsync();
        }
    }

    // These fixtures start with the publisher already settled so an injected step failure targets recovery.
    private static ValueTask StoreMatchedEvent(InMemoryStore store, Contract.BoundScope scope, string correlation,
        string id, string payload, long keepUntil) => store.PublishAsync(new EventRow(scope.Tenant + "_" + correlation, id, payload, keepUntil)
            { FoundationTenant = scope.Tenant });

    private static async Task CheckRecoverKeptEvents(int count, bool revoke, Action<bool, string> check, bool byteCut = false,
        bool sweepBackstep = false, bool repeatPublication = false, bool probePageWork = false,
        bool retryBeforeOpen = false, bool zeroWindow = false)
    {
        const long initial = 1_701_400_030_200;
        var now = initial;
        var comparisons = probePageWork ? new EventKeyComparisons() : null;
        var store = new InMemoryStore(1, () => now, Flaw.None, comparisons ?? StringComparer.Ordinal);
        var probe = new KeepingRecoveryProbe();
        var scope = new Contract.BoundScope("recover-kept-event");
        var verifyCollisions = count == 171;
        Worker worker = null!;
        Worker MakeWorker(string node) => new(node, 0, store, new ActorAdapter(store, probe),
            new WorkerOptions
            {
                Monotonic = () => now - initial,
                OnPreparedCheckpoint = (opening, _, _) =>
                {
                    if (!verifyCollisions || opening.FoundationRecoverySubscriptions is null || opening.Row is not { } row) return;
                    var prior = store.Instances[row.Id];
                    var key = InboxKey.Issued(FormattableString.Invariant($"{CreationProtocol.Origin(row.Id, row.Creation)}#{row.Revision}.0001"));
                    var collision = new Message(key.Id, new(row.Id), "ordinary").AsIssued();
                    CheckCatchUpCollision(store, worker.Fences.Single(), opening, collision,
                        () => store.Instances[row.Id] == prior && store.Subscriptions.Count == 0,
                        "first ticket", check);
                }
            });
        worker = MakeWorker("recover-kept-event");
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node, (_, kind) => kind == Contract.Kinds.Recover);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(started.Actor!.Value);
        await Pump(owner);
        await ingress.SendAsync(scope, target, "fault", "fault");
        await Pump(owner);
        var parked = await new ActorList(store, 10).GetAsync(scope, target.Actor);
        var publisher = new Contract.Target.Event(probePageWork ? "Approval" : "approval");
        string[] expected = [.. Enumerable.Range(0, count).Select(index => $"owed-{index:D4}")];
        foreach (var eventId in expected)
            await StoreMatchedEvent(store, scope, publisher.CorrelationKey, eventId,
                byteCut && eventId == expected[0] ? new string('x', ParkRecoveryCredit.LimitBytes) : "payload",
                now + (zeroWindow ? 0 : 20));
        if (retryBeforeOpen)
        {
            var key = (scope.Tenant + "_approval", expected[0]);
            var original = store.FoundationEvents[key];
            now += 21;
            var retry = await ingress.SendAsync(scope, publisher, expected[0], Contract.Kinds.Event, "replacement",
                new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(20)));
            check(retry.Outcome == Contract.AdmissionOutcome.Duplicate && store.FoundationEvents[key] == original,
                "a retry after the window leaves the unswept publication intact before there is any coverage");
            now = initial;
        }
        await StoreMatchedEvent(store, scope, publisher.CorrelationKey, "expired-before-open", "too old", now + (zeroWindow ? -1 : 1));
        await Pump(owner);
        if (!zeroWindow) now += 2;
        var fixture = parked is { Header.Status: Contract.ActorStatus.Faulted, Selection: not null } &&
            store.FoundationEvents.Count == count + 1 && store.Subscriptions.Count == 0 && probe.Received.Count == 0;
        check(fixture, "a kept event is still available when the keeping wait's Park is unresolved");
        if (!fixture) return;
        if (sweepBackstep)
        {
            await store.SweepAsync();
            now = initial;
        }

        await ingress.SendAsync(scope, target, "resume", Contract.Kinds.Recover, Json.Write(new Contract.OperatorCommand(parked!.Selection!)));
        var id = store.Instances.Values.Single().Id;
        await worker.PollAsync();
        await worker.LoadAsync();
        await worker.FlushAsync();
        worker.RunTurn(id);
        await worker.FlushAsync();
        var opened = store.Instances[id].ParkRecovery is not null && store.Subscriptions.Count == 1 && probe.Received.Count == 0;
        check(opened, "Open restores a keeping subscription and records its catch-up before delivering any event");
        if (!opened) return;

        var originalTicket = store.Messages.Values.Single(row => row.Message.ParkCatchUp is not null).Message;
        check(originalTicket.ParkCatchUp!.Chain == FoundationEventIdentity.CatchUpId(originalTicket.InboxKey),
            "the Open records the canonical chain ID of its first issued ticket");
        if (sweepBackstep)
            check(originalTicket.ParkCatchUp!.Entitlement == initial + 2,
                "the tenant sweep horizon keeps a later Open's entitlement above deleted windows after a clock step-back");
        now += 21;
        await store.SweepAsync();
        check(store.FoundationEvents.Count == count && store.FoundationEvents.Values.All(evt => evt.Id != "expired-before-open"),
            "the sweep retains owed events and deletes the event that expired before Open");

        EventRow? firstPublication = null;
        if (repeatPublication)
        {
            firstPublication = store.FoundationEvents.Values.Single();
            var duplicate = await ingress.SendAsync(scope, publisher, firstPublication.Id, Contract.Kinds.Event, "replacement");
            check(duplicate.Outcome == Contract.AdmissionOutcome.Duplicate && store.FoundationEvents.Values.Single() == firstPublication,
                "an outside retry after receipt expiry preserves the covered publication and its incarnation");
        }

        if (revoke)
        {
            await Reserve(originalTicket);
            var originalWait = store.Subscriptions.Single();
            var replacement = originalWait with { FoundationIncarnation = Guid.NewGuid() };
            var step = new TurnCommit(id, null, null, [originalTicket.InboxKey], [], [], [], [], null)
            { FoundationCatchUpStep = originalTicket.InboxKey, FoundationCatchUpExpected = 0, FoundationCatchUpBefore = now + 10_000 };
            var unregister = new TurnCommit(id, null, null, [], [], [], [], [originalWait], null);
            var fence = worker.Fences.Single();
            var batch = new CommitBatch(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [step, unregister], [], []);
            var committed = await store.CommitAsync(batch);
            await store.SubscribeAsync(replacement);
            await store.SweepAsync();
            await Pump(owner);
            check(committed.Landed && store.Subscriptions.Single().FoundationIncarnation == replacement.FoundationIncarnation &&
                probe.Received.Count == 0 && store.FoundationEvents.Count == 0 && !store.Messages.ContainsKey(originalTicket.InboxKey),
                "a catch-up step batched with unsubscribe leaves a later incarnation untouched and releases sweep coverage");
            return;
        }

        if (count > InMemoryStore.ParkPageLimit || byteCut)
        {
            var firstPageSize = byteCut ? 1 : InMemoryStore.ParkPageLimit;
            if (verifyCollisions)
            {
                await Reserve(originalTicket);
                var nextId = FoundationEventIdentity.CatchUpSuccessor(originalTicket.ParkCatchUp.Chain, 1);
                var successorCollision = (originalTicket with { Id = nextId, Kind = "ordinary", ParkCatchUp = null }).AsIssued();
                var publication = store.FoundationEvents.Values.Single(row => row.Id == expected[0]);
                var generated = new EventRoute(publication, store.Subscriptions.Single()).Delivery().Message;
                var step = new TurnCommit(id, null, null, [originalTicket.InboxKey], [], [], [], [], null)
                    { FoundationCatchUpStep = originalTicket.InboxKey, FoundationCatchUpExpected = 0, FoundationCatchUpBefore = now + 10_000 };
                bool Unchanged() => store.Messages.ContainsKey(originalTicket.InboxKey) && store.Messages.Values.All(row => row.Message.Kind != Kinds.Event);
                CheckCatchUpCollision(store, worker.Fences.Single(), step, successorCollision, Unchanged, "page successor", check);
                CheckCatchUpCollision(store, worker.Fences.Single(), step, generated with { Kind = "ordinary" }, Unchanged, "event delivery", check);
            }
            var comparisonsBefore = comparisons?.Count ?? 0;
            await worker.PollAsync();
            await worker.FlushAsync();
            await worker.MeetAsync();
            await worker.FlushAsync();
            var successorTicket = store.Messages.Values.SingleOrDefault(row => row.Message.ParkCatchUp is not null)?.Message;
            var paged = !store.Messages.ContainsKey(originalTicket.InboxKey) &&
                store.Messages.Values.Count(row => row.Message.Kind == Kinds.Event) == firstPageSize &&
                successorTicket is { ParkCatchUp: { Page: 1, After: { } after } nextPage } && after.Event == expected[firstPageSize - 1] &&
                successorTicket.InboxKey != originalTicket.InboxKey && nextPage.Chain == originalTicket.ParkCatchUp!.Chain &&
                nextPage.Entitlement == originalTicket.ParkCatchUp.Entitlement;
            check(paged, byteCut ? "a single event above the byte allowance goes alone in its fenced catch-up step" :
                "one fenced catch-up step commits a bounded page and a distinct successor with the same entitlement");
            if (comparisons is not null)
            {
                var used = comparisons.Count - comparisonsBefore;
                check(paged && used <= 2 * InMemoryStore.ParkPageLimit + 4,
                    $"a folded-key page over 4,096 events uses its index ({used} equality probes)");
                return;
            }
            if (!paged) return;

            var oldFence = worker.Fences.Single();
            await owner.DisposeAsync();
            var successor = MakeWorker("recover-kept-successor");
            await using var next = (await new DurableHost([successor]).ActivateAsync(0))!;
            var staleStep = new TurnCommit(id, null, null, [successorTicket!.InboxKey], [], [], [], [], null)
                { FoundationCatchUpStep = successorTicket.InboxKey, FoundationCatchUpExpected = 0, FoundationCatchUpBefore = now + 10_000 };
            var stale = await store.CommitAsync(new(oldFence.Partition, oldFence.Epoch, oldFence.Position, Defaults.Lease, false, [staleStep], [], []));
            check(!stale.Landed && store.Messages.ContainsKey(successorTicket.InboxKey), "a stale owner cannot advance the successor catch-up page");
            await Complete(next);
        }
        else await Complete(owner);

        await store.SweepAsync();
        if (count > InMemoryStore.ParkPageLimit)
        {
            check(store.FoundationEvents.Count == count - InMemoryStore.ParkPageLimit,
                "a tenant sweep removes at most one page of uncovered publications per call");
            await store.SweepAsync();
        }
        check(probe.Received.SequenceEqual(expected) && store.Instances[id].ParkRecovery is null && store.FoundationEvents.Count == 0,
            "catch-up delivers each owed event once in order and releases its sweep coverage");

        if (firstPublication is not null)
        {
            check(probe.Payloads.SequenceEqual(["payload"]), "catch-up offers the original payload once");
            var fresh = await ingress.SendAsync(scope, publisher, firstPublication.Id, Contract.Kinds.Event, "replacement");
            var secondPublication = store.FoundationEvents.Values.SingleOrDefault();
            await Complete(owner);
            check(fresh.Outcome == Contract.AdmissionOutcome.Accepted && secondPublication is not null &&
                secondPublication.FoundationIncarnation != firstPublication.FoundationIncarnation &&
                probe.Payloads.SequenceEqual(["payload", "replacement"]),
                "after sweep the outside key admits a fresh incarnation and delivers its own payload");
        }

        async Task Complete(IScopeActivation active)
        {
            for (var pass = 0; pass < 3; pass++)
            {
                await Pump(active);
                now += 11;
            }
        }

        async Task Reserve(Message ticket)
        {
            // Keep the ticket in the Worker's inbox when exercising the store's prepare checks directly.
            await worker.PollAsync();
            var fence = worker.Fences.Single();
            var reserved = await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease,
                false, [], [], [ticket.InboxKey]));
            check(reserved.Landed, "the exact pending catch-up step reserves a trial under its partition fence");
        }
    }

    private static void CheckCatchUpCollision(InMemoryStore store, PartitionFence fence, TurnCommit original,
        Message collision, Func<bool> unchanged, string description, Action<bool, string> check)
    {
        var send = new TurnCommit(collision.Target.Id, null, null, [], [], [collision], [], [], null);
        foreach (var order in new TurnCommit[][] { [send, original], [original, send] })
        {
            var refused = false;
            try
            {
                store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, order, [], [])).GetAwaiter().GetResult();
            }
            catch (StoreException error) when (error.Failure == Failure.Definite)
            {
                refused = true;
            }
            catch (ArgumentException)
            {
                // The old late duplicate insert throws after deleting the original page ticket.
            }
            var intact = unchanged() && !store.Messages.ContainsKey(collision.InboxKey) && store.Partitions[fence.Partition].Position == fence.Position;
            check(refused && intact, $"a composed collision with the {description} refuses the entire fenced batch");
            if (!intact) return;
        }
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

    internal static async Task CheckRecoverJobResult(Action<bool, string> check)
    {
        await CaptureParkedCompletion(timed: false, check, recover: true);
        await CaptureParkedCompletion(timed: true, check, recover: true);
        await CaptureParkedCompletion(timed: false, check, recover: true, pendingAtOpen: true);
        await CaptureParkedCompletion(timed: true, check, recover: true, endHoldingResult: true);
        await CaptureParkedCompletion(timed: false, check, recover: true, endBeforeResult: true);
        await CaptureParkedCompletion(timed: false, check, recover: true, pendingAtOpen: true, endBeforeResult: true);
        await CheckRestartedJob(timed: false, check);
        await CheckRestartedJob(timed: true, check);
        await CheckRestartedJob(timed: true, check, sweepDuringOpen: true);
        await CheckRetryConsumedResult(Contract.RecoverMode.Resume, check);
        await CheckRetryConsumedResult(Contract.RecoverMode.RetryAll, check);
        await CheckRetryConsumedResult(Contract.RecoverMode.Retry, check);
        await CheckPendingJobAfterPark(check);
        await CheckRetryConsumedTicket(check);
        await CheckRetryConsumedTicket(check, timed: true);
        await CheckRetryConsumedTicket(check, timed: true, endBeforeTicket: true);
        await CheckRetryConsumedTicket(check, timed: true, endBeforeTicket: true, releaseTicketBeforeEnd: true);
        await CheckRetryConsumedTicket(check, timed: true, endBeforeTicket: true, restartAtOpen: true);
        await CheckRetryConsumedTicket(check, mode: Contract.RecoverMode.Retry);
        await CheckRetryConsumedTicket(check, timed: true, mode: Contract.RecoverMode.Retry);
        await CheckRetryConsumedTicket(check, timed: true, missingDeadline: true);
        await CheckRetryConsumedTicket(check, timed: true, mode: Contract.RecoverMode.Retry, missingDeadline: true);
        await CheckRetryConsumedTicket(check, timed: true, mode: Contract.RecoverMode.Retry, missingDeadline: true, doneDeadlineMarker: true);
        await CheckRetryConsumedTicket(check, timed: true, mode: Contract.RecoverMode.Retry, missingDeadline: true,
            alterTicket: (ticket, foreignTimer) => ticket with { Timer = foreignTimer });
        await CheckRetryConsumedTicket(check, timed: true, mode: Contract.RecoverMode.Retry, missingDeadline: true,
            alterTicket: (ticket, _) => ticket with { Deadline = null });
        await CheckRetryConsumedTicket(check, timed: true, mode: Contract.RecoverMode.RetryAll, missingDeadline: true,
            alterTicket: (ticket, _) => ticket with { Deadline = null, Timer = null });
        await CheckRetryConsumedTicket(check, timed: true, mode: Contract.RecoverMode.Retry, missingDeadline: true,
            alterTicket: (ticket, _) => ticket with { Key = "earlier-job" });
    }

    private static async Task CheckRestartedJob(bool timed, Action<bool, string> check, bool sweepDuringOpen = false)
    {
        var now = 1_701_400_012_000L;
        const string key = "background";
        const string recipe = "captured arguments and effect key";
        var store = new InMemoryStore(1, () => now)
        {
            ParkedRetentionWindow = sweepDuringOpen ? TimeSpan.FromMilliseconds(100) : null
        };
        var actor = new ParkedJobProbe("result", timed, arguments: recipe);
        var scope = new Contract.BoundScope("job-restart");
        InboxKey? ticketToSweep = null;
        var survivedOpenSweep = false;
        var wire = new CountingStore(store, () => { }, beforeCommit: async batch =>
        {
            if (ticketToSweep is not { } selected || !batch.Turns.Any(turn => turn.FoundationRecoveryJobs is not null))
                return;
            ticketToSweep = null;
            await store.SweepAsync();
            survivedOpenSweep = store.Messages.GetValueOrDefault(selected) is { Done: true, KeptBy: ParkKeepReason.Arrived };
        });
        Worker MakeOwner(string node) => new(node, 0, wire, new ActorAdapter(store, actor), new WorkerOptions { Monotonic = () => 0 });
        var first = MakeOwner("before-restart");
        var edge = new InMemoryIngress(store, new Requests(first.Node), first.Node, (_, kind) => kind == Contract.Kinds.Recover);
        var started = await edge.SendAsync(scope, new Contract.Target.Start(new(actor.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(started.Actor!.Value);
        await using var owner = (await new DurableHost([first]).ActivateAsync(0))!;
        await Pump(owner);
        var row = store.Instances.Values.Single();
        var oldGeneration = JobRegistry.Generation(row, key)!.Value;
        var oldKey = InboxKey.Ticket(row.Id, key, row.Creation, oldGeneration);
        var initialTicket = store.Messages[oldKey].Message.Read<JobTicket>();
        await edge.SendAsync(scope, target, "fault", "fault");
        await Pump(owner);
        await owner.DisposeAsync();

        var successor = MakeOwner("restart-open");
        await using var next = (await new DurableHost([successor]).ActivateAsync(0))!;
        await Pump(next);
        var old = store.Messages.GetValueOrDefault(oldKey);
        var parked = store.Instances[row.Id];
        var retained = parked.Status == InstanceStatus.Faulted && old is { Done: true, KeptBy: ParkKeepReason.Arrived } &&
            actor.Entries == 1 && actor.Completions == 0;
        check(retained, "the successor keeps the old job ticket before Resume without entering its job code");
        if (!retained) return;

        // Restart takes the model's recipe, even if the old ticket contains different arguments.
        old!.Message = old.Message with { Payload = Json.Write(old.Message.Read<JobTicket>() with { Arguments = "stale" }) };
        if (sweepDuringOpen)
        {
            // Give the retained ticket a past ordinary sweep deadline while the Park window is still open.
            old.Message = old.Message with { KeepUntil = now - 1 };
            ticketToSweep = oldKey;
        }
        var selection = (await new ActorList(store, 10).GetAsync(scope, target.Actor))!.Selection!;
        var admitted = await edge.SendAsync(scope, target, "restart-operation", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(selection)));
        await successor.PollAsync();
        await successor.LoadAsync();
        await successor.FlushAsync();
        successor.RunTurn(row.Id);
        await successor.FlushAsync();
        var open = store.Instances[row.Id];
        var generation = oldGeneration + 1;
        var currentKey = InboxKey.Ticket(row.Id, key, row.Creation, generation);
        var restarted = admitted.Outcome == Contract.AdmissionOutcome.Accepted && open.ParkRecovery is not null &&
            open.ParkRestartedJobs is not null && JobRegistry.Generation(open, key) == generation && open.NextJobGeneration == generation + 1 &&
            store.Messages.GetValueOrDefault(currentKey) is { Done: false, Seen: 0, ReleasedBy: null } job &&
            job.Message.Read<JobTicket>().Arguments == recipe && actor.Entries == 1;
        check(restarted, "the fenced Open writes a new job generation from the captured recipe while the older ticket stays inert");
        if (sweepDuringOpen)
        {
            await store.SweepAsync();
            check(survivedOpenSweep && restarted && store.FoundationExpiry(open)?.Armed is null &&
                store.Messages.GetValueOrDefault(currentKey) is { Done: false } &&
                store.Messages.Values.Count(item => item is { Done: false, Message.Kind: Kinds.Job }) == 1 &&
                store.Messages.Values.Count(item => item is { Done: false, Message.Kind: Kinds.JobDeadline }) == 1,
                "M1-2 a sweep after planning and after Open preserves the kept ticket and its single timed replacement");
        }
        if (!restarted) return;
        if (timed)
        {
            var replacement = store.Messages[currentKey].Message.Read<JobTicket>();
            check(initialTicket.Timer is not null && replacement.Timer is not null && initialTicket.Timer != replacement.Timer &&
                initialTicket.Deadline == replacement.Deadline && !store.Messages.ContainsKey(InboxKey.Issued(initialTicket.Timer)) &&
                store.Messages.Values.Count(item => item is { Done: false, Message.Kind: Kinds.JobDeadline }) == 1,
                "a timed restart atomically recalls the old deadline and preserves its time under the replacement's identity");
        }

        await next.DisposeAsync();
        var finisher = MakeOwner("restart-after-take");
        await using var finalOwner = (await new DurableHost([finisher]).ActivateAsync(0))!;
        for (var pass = 0; pass < 4; pass++)
        {
            now += 11;
            await Pump(finalOwner);
        }
        check(store.Instances[row.Id] is { ParkRecovery: null, ParkRestartedJobs: null } &&
            old is { Done: true, KeptBy: null, ReleasedBy: null } && actor.Entries == 2 && actor.Arguments.SequenceEqual([recipe, recipe]),
            "the next owner clears the obsolete kept ticket and enters the replacement once after Complete");
        var completion = finisher.JobCompletion(row.Id, key, row.Creation);
        if (completion is null) return;
        actor.Complete.SetResult();
        await completion.WaitAsync(TimeSpan.FromSeconds(5));
        await Pump(finalOwner);
        check(actor.Completions == 1 && actor.Entries == 2 && store.Instances[row.Id].Jobs is null &&
            !store.Messages.ContainsKey(currentKey), "the replacement result clears its own registry entry and reaches the actor once");
    }

    private static async Task CheckRetryConsumedResult(Contract.RecoverMode mode, Action<bool, string> check)
    {
        var now = 1_701_400_013_000L;
        var store = new InMemoryStore(1, () => now);
        var probe = new ParkedJobProbe("original", timed: true) { FaultOnResult = true };
        var worker = new Worker("retry-job-result", 0, store, new ActorAdapter(store, probe), new WorkerOptions { Monotonic = () => 0 });
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node, (_, kind) => kind == Contract.Kinds.Recover);
        var scope = new Contract.BoundScope("retry-job-result");
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var begun = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(begun.Actor!.Value);
        await Pump(owner);
        var row = store.Instances.Values.Single();
        var driver = worker.JobCompletion(row.Id, "background", row.Creation)!;
        probe.Complete.SetResult();
        await driver.WaitAsync(TimeSpan.FromSeconds(5));
        await Pump(owner);
        var parked = await new ActorList(store, 10).GetAsync(scope, target.Actor);
        var resultKey = InboxKey.JobResult(row.Id, "background", row.Creation);
        var kept = parked is { Header.Status: Contract.ActorStatus.Faulted, Selection: not null } && probe.Entries == 1 &&
            store.Messages.GetValueOrDefault(resultKey) is { KeptBy: ParkKeepReason.Consumed, Message.FoundationJobResult: true };
        check(kept, "a fault while applying an authenticated timed result keeps the result as Consumed");
        if (!kept) return;

        var handles = mode == Contract.RecoverMode.Retry
            ? new[] { store.SealFoundationKeptHandle(scope.Tenant, target.Actor.Id, resultKey)! }
            : null;
        await ingress.SendAsync(scope, target, "resume", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(parked!.Selection!, mode, handles)));
        for (var index = 0; index < 3; index++)
        {
            now += 11;
            await Pump(owner);
        }
        var recovered = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        check(recovered is { Park: null, Row: { Status: InstanceStatus.Running, Jobs: null } } && probe.Entries == 1 &&
            (mode == Contract.RecoverMode.Resume ? probe.Completions == 0 : probe.Completions == 1 && probe.Result == "original"),
            $"{mode} {(mode == Contract.RecoverMode.Resume ? "clears" : "replays")} the consumed result without executing its job again");
        if (mode != Contract.RecoverMode.Resume) return;

        await ingress.SendAsync(scope, target, "park-again", "fault");
        await Pump(owner);
        var later = await new ActorList(store, 10).GetAsync(scope, target.Actor);
        await ingress.SendAsync(scope, target, "resume-again", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(later!.Selection!)));
        for (var index = 0; index < 3; index++)
        {
            now += 11;
            await Pump(owner);
        }
        recovered = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        check(recovered is { Park: null, Row: { Status: InstanceStatus.Running, Jobs: null } } && probe.Entries == 1 && probe.Completions == 0,
            "a later Park cannot restart a deliberately discarded result through a leftover model recipe");
    }

    private static async Task CheckPendingJobAfterPark(Action<bool, string> check)
    {
        var now = 1_701_400_014_000L;
        var store = new InMemoryStore(1, () => now);
        var probe = new ParkedJobProbe("original", timed: true);
        var scheduler = new HeldScheduler();
        var adapter = new ActorAdapter(store, probe);
        var worker = new Worker("resume-pending-job", 0, store, adapter,
            new WorkerOptions { Monotonic = () => 0, JobScheduler = scheduler });
        var ingress = new InMemoryIngress(store, new Requests(worker.Node), worker.Node, (_, kind) => kind == Contract.Kinds.Recover);
        var scope = new Contract.BoundScope("resume-pending-job");
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var begun = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(begun.Actor!.Value);
        await Pump(owner);
        var row = store.Instances.Values.Single();
        var ticketKey = InboxKey.Ticket(row.Id, "background", row.Creation);
        var ticket = store.Messages[ticketKey];
        var deadline = store.Messages.Values.Single(message => message.Message.Kind == Kinds.JobDeadline);
        await ingress.SendAsync(scope, target, "fault", "fault");
        await worker.PollAsync();
        await worker.FlushAsync();
        worker.RunTurn(row.Id);
        await worker.FlushAsync();
        var parked = await new ActorList(store, 10).GetAsync(scope, target.Actor);
        var queued = parked is { Header.Status: Contract.ActorStatus.Faulted, Selection: not null } &&
            scheduler.Count == 1 && probe.Entries == 0 && ticket is { Done: false };
        check(queued, "the same owner parks while its job callback is queued and the ticket remains pending");
        if (!queued) return;

        await ingress.SendAsync(scope, target, "recover", Contract.Kinds.Recover, Json.Write(new Contract.OperatorCommand(parked!.Selection!)));
        // Independent acceptance clocks can assign a command a lower order key than an outstanding ticket.
        var command = store.Messages.Values.Single(item => item.Message.Id == "recover");
        command.Seq = ticket.Seq - 1;
        var parkedRow = store.Instances[row.Id];
        var stimulus = new Delivery(command.Message, command.Seq) { Durable = true };
        var staged = adapter.Command(new TurnInput(worker, new Slot { Row = parkedRow, Loaded = true }, row.Id, [stimulus], 0, null, now))!;
        var openTurn = new TurnCommit(row.Id, staged.Row, staged.Expected, staged.Cancelled, [stimulus.Message.InboxKey], staged.Sent,
            staged.Subscribed, staged.Unsubscribed, staged.Journal)
        {
            FoundationRecoveryTimers = staged.FoundationRecoveryTimers, FoundationRecoveryJobs = staged.FoundationRecoveryJobs,
        };
        var fence = worker.Fences.Single();
        if (!await RefusesSiblingConsume(ticketKey, "job ticket")) return;
        if (!await RefusesSiblingConsume(deadline.Message.InboxKey, "issued job deadline")) return;

        await worker.PollAsync();
        await worker.FlushAsync();
        worker.RunTurn(row.Id);
        await worker.FlushAsync();
        var open = store.Instances[row.Id];
        var preserved = open.ParkRecovery is not null && open.ParkRestartedJobs is null && ticket is { Done: false } && probe.Entries == 0;
        check(preserved, "Open preserves the pending ticket while its canceled scheduled callback has not run");
        if (!preserved) return;

        await Pump(owner);
        probe.Complete.SetResult();
        scheduler.RunAll();
        await Pump(owner);
        check(probe is { Entries: 1, Completions: 1, Result: "original" } && store.Instances[row.Id] is { ParkRecovery: null, Jobs: null },
            "the superseded callback leaves the resubmitted pending job able to enter and settle exactly once");

        async Task<bool> RefusesSiblingConsume(InboxKey key, string label)
        {
            var sibling = new TurnCommit(row.Id, null, null, [key], [], [], [], [], null);
            var rejected = await DefiniteRejection(() => store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position,
                Defaults.Lease, false, [openTurn, sibling], [], [])));
            check(rejected && store.Instances[row.Id] == parkedRow && store.Messages.ContainsKey(key) && !command.Done,
                $"the fenced batch refuses a sibling consume of the {label} that Open preserves");
            return rejected;
        }
    }

    private static async Task CheckRetryConsumedTicket(Action<bool, string> check, bool timed = false,
        bool endBeforeTicket = false, bool releaseTicketBeforeEnd = false, bool restartAtOpen = false,
        Contract.RecoverMode mode = Contract.RecoverMode.RetryAll, bool missingDeadline = false, bool doneDeadlineMarker = false,
        Func<JobTicket, string, JobTicket>? alterTicket = null)
    {
        var now = 1_701_400_014_100L;
        var store = new InMemoryStore(1, () => now);
        var probe = new ParkedJobProbe("continued", timed)
        {
            BeforeDeadline = endBeforeTicket ? (releaseTicketBeforeEnd ? 0 : 1) :
                timed && !missingDeadline ? InMemoryStore.ParkPageLimit - 1 : 0,
            EndBeforeTicket = endBeforeTicket
        };
        var scheduler = new HeldScheduler();
        var first = new Worker("before-poison-ticket", 0, store, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => 0, JobScheduler = scheduler });
        var scope = new Contract.BoundScope("retry-consumed-ticket");
        var ingress = new InMemoryIngress(store, new Requests(first.Node), first.Node,
            (_, kind) => kind is Contract.Kinds.Recover or Contract.Kinds.Delete);
        await using var owner = (await new DurableHost([first]).ActivateAsync(0))!;
        var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(started.Actor!.Value);
        await Pump(owner);
        var row = store.Instances.Values.Single();
        var key = InboxKey.Ticket(row.Id, "background", row.Creation);
        store.Messages[key].Attempts = 3;
        var originalDeadline = store.Messages.Values.SingleOrDefault(item => item.Message.Kind == Kinds.JobDeadline);
        if (missingDeadline) originalDeadline!.Attempts = 3;
        await owner.DisposeAsync();
        scheduler.RunAll();

        var pairedPages = 0;
        var largestPage = 0;
        var releasedTicket = false;
        var rearmedDeadline = false;
        var pendingCallbacks = endBeforeTicket ? new HeldScheduler() : null;
        var successor = new Worker("retry-poison-ticket", 0, store, new ActorAdapter(store, probe), new WorkerOptions
        {
            Monotonic = () => 0,
            JobScheduler = pendingCallbacks,
            OnPreparedCheckpoint = (turn, _, _) =>
            {
                if (turn.FoundationRecoveryJobs?.DeadlineRearms is [var deadline])
                    rearmedDeadline = deadline.Envelope.InboxKey == originalDeadline?.Message.InboxKey &&
                        deadline.Envelope.Due == originalDeadline?.Message.Due &&
                        deadline.DoneSequence == (doneDeadlineMarker ? originalDeadline!.Seq : null);
                if (turn.FoundationRecoveryPage is not { } page) return;
                largestPage = Math.Max(largestPage, page.Writes().Count());
                if (page.Rows.Any(item => item.PairedJobInput?.Cursor.Key == key)) pairedPages++;
                if (turn.Row?.Status == InstanceStatus.Running && page.Writes().Any(item => item.Cursor.Key == key)) releasedTicket = true;
            }
        });
        await using var next = (await new DurableHost([successor]).ActivateAsync(0))!;
        await Pump(next);
        var view = await new ActorList(store, 10).GetAsync(scope, target.Actor);
        var retained = view is { Header.Status: Contract.ActorStatus.Faulted, Selection: not null } &&
            store.Messages[key] is { Done: true, KeptBy: ParkKeepReason.Consumed } && probe.Entries == 0;
        check(retained, "an exhausted current ticket is kept as Consumed after the worker Park");
        if (!retained) return;
        if (timed)
        {
            now += endBeforeTicket ? 500 : 1500;
            for (var pass = 0; pass < 24; pass++) await Pump(next);
            if (missingDeadline)
                check(!store.Messages.ContainsKey(originalDeadline!.Message.InboxKey),
                    "the real parked worker consumed the exhausted deadline before the timed ticket could be retried");
            else if (endBeforeTicket)
                check(originalDeadline is { Done: false } && originalDeadline.Message.Due > now &&
                    store.Messages.Values.Any(item => item is { Message.Kind: "end-before-job", KeptBy: ParkKeepReason.Arrived } &&
                        item.Seq < store.Messages[key].Seq),
                    "a large earlier arrival is kept while the timed ticket is poisoned and its absolute deadline is still ahead");
            else
                check(originalDeadline is { KeptBy: ParkKeepReason.Arrived } && originalDeadline.Seq < store.Messages[key].Seq &&
                    store.Messages.Values.Count(message => message.KeptBy == ParkKeepReason.Arrived && message.Seq < originalDeadline.Seq) ==
                        InMemoryStore.ParkPageLimit - 1,
                    "the expired deadline sorts at the page boundary ahead of its kept consumed ticket");
        }
        if (doneDeadlineMarker)
        {
            originalDeadline!.Done = true;
            originalDeadline.Message = originalDeadline.Message with { Payload = null };
            store.Messages.Add(originalDeadline.Message.InboxKey, originalDeadline);
        }
        var parked = store.Instances[row.Id];
        var alteredDeadline = InboxKey.Issued(DurableTurn.FoundationJobDeadlineId(row.Id, row.Creation + 1, 0));
        if (alterTicket is not null)
        {
            var ticket = store.Messages[key];
            var recipe = alterTicket(ticket.Message.Read<JobTicket>(), alteredDeadline.Id);
            ticket.Message = ticket.Message with { Payload = Json.Write(recipe) };
        }
        var handles = mode == Contract.RecoverMode.Retry
            ? new[] { store.SealFoundationKeptHandle(scope.Tenant, target.Actor.Id, key)! }
            : null;
        await ingress.SendAsync(scope, target, "retry", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(view!.Selection!,
                restartAtOpen ? Contract.RecoverMode.Resume : mode, handles)));
        await Pump(next);
        if (alterTicket is not null)
        {
            check(store.Instances[row.Id] == parked && !store.Messages.ContainsKey(alteredDeadline) &&
                store.Messages[key] is { Done: true, KeptBy: ParkKeepReason.Consumed },
                "a changed job identity or timing in a retained ticket cannot authorize Open");
            return;
        }
        if (timed)
        {
            for (var pass = 0; pass < 24; pass++)
            {
                now += 11;
                await Pump(next);
            }
            if (endBeforeTicket)
            {
                pendingCallbacks!.RunAll();
                var currentKey = restartAtOpen ? InboxKey.Ticket(row.Id, "background", row.Creation, 1) : key;
                var disposition = restartAtOpen ? "Open-created" : releaseTicketBeforeEnd ? "released" : "kept";
                check(releasedTicket == releaseTicketBeforeEnd && probe is { Entries: 0, Completions: 0 } &&
                    store.Instances[row.Id] is { Status: InstanceStatus.Finished, ParkRecovery: null, Jobs: null } &&
                    store.Messages.GetValueOrDefault(currentKey) is { Done: true, KeptBy: null } &&
                    store.Messages.Values.All(item => item.Message.Kind != Kinds.JobDeadline || item.Done),
                    $"the terminal page clears the {disposition} ticket, its pending deadline and job registration together");
                var terminal = await new ActorList(store, 10).GetAsync(scope, target.Actor);
                await ingress.SendAsync(scope, target, "delete-after-kept-ticket", Contract.Kinds.Delete,
                    Json.Write(new Contract.OperatorCommand(terminal!.Selection!)));
                await Pump(next);
                check(store.Instances[row.Id].Retired, "qualified Delete can retire the model that ended before its timed ticket was restored");
                return;
            }
            check(probe is { Entries: 0, Completions: 1, JobError: "timeout" } &&
                store.Instances[row.Id] is { ParkRecovery: null, Jobs: null } && !store.Messages.ContainsKey(key),
                "the split-page deadline settles the same job generation once without entering the expired driver");
            if (missingDeadline)
                check(rearmedDeadline && pairedPages == 0,
                    doneDeadlineMarker ? $"{mode} replaces a payload-free deadline marker at the original absolute time and exact key" :
                        $"{mode} re-arms the absent deadline at its original absolute time and exact key");
            else
                check(pairedPages == 1 && largestPage == InMemoryStore.ParkPageLimit - 1,
                    "the deadline and its later ticket share one fenced page within the row limit");
            return;
        }
        var open = store.Instances[row.Id];
        var preserved = JobRegistry.Generation(open, "background") == 0 && open.NextJobGeneration == row.NextJobGeneration &&
            store.Messages[key] is { Done: false, KeptBy: null, ReleasedBy.Generation: 1, Attempts: 0 } && probe.Entries == 1;
        check(preserved, $"{mode} restores the current consumed ticket and its original generation with a fresh poison budget");
        if (!preserved) return;
        var driver = successor.JobCompletion(row.Id, "background", row.Creation)!;
        probe.Complete.SetResult();
        await driver.WaitAsync(TimeSpan.FromSeconds(5));
        for (var index = 0; index < 3; index++)
        {
            now += 11;
            await Pump(next);
        }
        check(probe is { Entries: 1, Completions: 1, Result: "continued" } && store.Instances[row.Id] is { ParkRecovery: null, Jobs: null },
            "the retried current ticket settles once and clears its registry entry");
    }

    private static async Task CaptureParkedCompletion(bool timed, Action<bool, string> check, bool recover = false,
        bool pendingAtOpen = false, bool endHoldingResult = false, bool endBeforeResult = false)
    {
        var now = 1_701_400_010_000L;
        const string jobKey = "background";
        const int payloadLimit = 16 * 1024 * 1024 - 2 * 24 * 1024;
        var overhead = System.Text.Encoding.UTF8.GetByteCount(Json.Write(new JobFinished(jobKey, Json.Write(""), null)));
        var store = new InMemoryStore(1, () => now);
        var actor = new ParkedJobProbe(new string('x', payloadLimit - overhead + 1), timed) { EndHoldingResult = endHoldingResult };
        var scope = new Contract.BoundScope("parked-job");
        Message? competingTicket = null;
        var attempted = false;
        var refused = false;
        Worker MakeOwner(string node) => new(node, 0, store, new ActorAdapter(store, actor), new WorkerOptions
        {
            Monotonic = () => 0,
            OnPreparedCheckpoint = (opening, _, _) =>
            {
                if (attempted || competingTicket is not { } original || opening.FoundationRecoveryJobs is null) return;
                attempted = true;
                var prior = store.Instances[opening.Instance];
                var fence = store.Partitions[0];
                var sibling = new TurnCommit(opening.Instance, null, null, [], [], [original], [], [], null);
                try
                {
                    store.CommitAsync(new(0, fence.Epoch, fence.Position, Defaults.Lease, false, [sibling, opening], [], []))
                        .GetAwaiter().GetResult();
                }
                catch (StoreException error) when (error.Failure == Failure.Definite)
                {
                    refused = store.Instances[opening.Instance] == prior && !store.Messages.ContainsKey(original.InboxKey);
                }
            }
        });
        var worker = MakeOwner("parked-job-first");
        Contract.IIngress edge = new InMemoryIngress(store, new Requests(worker.Node), worker.Node,
            (_, kind) => kind is Contract.Kinds.Recover or Contract.Kinds.Delete);
        var start = await edge.SendAsync(scope, new Contract.Target.Start(new(actor.Type, "ready", null)), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(start.Actor!.Value);
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump(owner);
        var row = store.Instances.Values.Single();
        var task = worker.JobCompletion(row.Id, jobKey, row.Creation)!;
        var ticket = InboxKey.Ticket(row.Id, jobKey, row.Creation);
        if (pendingAtOpen) competingTicket = store.Messages[ticket].Message;
        var resultKey = InboxKey.JobResult(row.Id, jobKey, row.Creation);
        await edge.SendAsync(scope, target, "fault", "fault");
        await Pump(owner);
        var parked = store.Instances[row.Id];
        check(parked is { Status: InstanceStatus.Faulted, Jobs: not null } && store.Messages.ContainsKey(ticket) && actor.Entries == 1,
            "the fault parks the actor while the entered job and its counted ticket are outstanding");
        check(store.Messages.Values.Count(message => !message.Done && message.Message.Kind == Kinds.JobDeadline) == (timed ? 1 : 0),
            "the parked job still has its pending deadline if armed");

        if (endBeforeResult)
        {
            await edge.SendAsync(scope, target, "end-before-job", "end-before-job", new string('x', ParkRecoveryCredit.LimitBytes));
            await Pump(owner);
            check(store.Messages.Values.Any(message => message is { Message.Id: "end-before-job", KeptBy: ParkKeepReason.Arrived }),
                "the earlier large arrival is kept before the authenticated job result exists");
            parked = store.Instances[row.Id];
        }

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
        var nextWorker = MakeOwner("parked-job-next");
        await using var successor = (await new DurableHost([nextWorker]).ActivateAsync(0))!;
        if (!pendingAtOpen)
        {
            await Pump(successor);
            var kept = actor.Entries == 1 && actor.Completions == 0 && store.Instances[row.Id].Jobs == parked.Jobs &&
                store.Messages.GetValueOrDefault(resultKey) is { Done: true, KeptBy: ParkKeepReason.Arrived };
            check(kept, "a successor keeps the bounded result as an arrival and does not rerun the job");
            if (!recover || !kept) return;
        }

        var selection = (await new ActorList(store, 10).GetAsync(scope, target.Actor))!.Selection!;
        var resultAdmission = await edge.SendAsync(scope, target, "recover-job", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(selection)));
        if (pendingAtOpen)
        {
            // A lower order key for the external command models a result behind Open on a provider with separate clocks.
            var command = store.Messages.Values.Single(item => item.Message.Id == "recover-job");
            command.Seq = result!.Seq - 1;
            await nextWorker.PollAsync();
            await nextWorker.LoadAsync();
            await nextWorker.FlushAsync();
            nextWorker.RunTurn(row.Id);
            await nextWorker.FlushAsync();
            check(attempted && refused, "Open refuses a sibling insertion of the genuine ticket that result conversion removed");
            if (!refused) return;
            var preserved = store.Instances[row.Id] is { ParkRecovery: not null, ParkRestartedJobs: null } resumed &&
                resumed.Jobs == parked.Jobs && result is { Done: false, ReleasedBy: null } && actor.Entries == 1;
            check(preserved, "Open finds the pending result after its ticket has been removed and preserves the original generation");
            if (!preserved) return;
        }
        for (var pass = 0; pass < 4; pass++)
        {
            now += 11;
            await Pump(successor);
        }
        var recovered = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor;
        if (endHoldingResult || endBeforeResult)
        {
            check(resultAdmission.Outcome == Contract.AdmissionOutcome.Accepted && actor.Entries == 1 &&
                actor.Completions == (endBeforeResult ? 0 : 1) &&
                recovered is { Park: null, Row: { Status: InstanceStatus.Finished, Jobs: null, EndedDuringParkRecovery: true } } &&
                result is { Done: true, Held: false, KeptBy: null },
                endBeforeResult ? $"the terminal page settles a {(pendingAtOpen ? "pending untagged" : "still-kept")} result and its exact job registration together" :
                    "the terminal page settles a held authenticated result and its exact job registration together");
            if (recovered is null) return;
            var terminal = (await new ActorList(store, 10).GetAsync(scope, target.Actor))!.Selection!;
            var deletion = await edge.SendAsync(scope, target, "delete-after-job", Contract.Kinds.Delete,
                Json.Write(new Contract.OperatorCommand(terminal)));
            await Pump(successor);
            check(deletion.Outcome == Contract.AdmissionOutcome.Accepted && store.Instances[row.Id].Retired,
                "the settled job result no longer protects the terminal actor from qualified Delete");
            return;
        }
        check(resultAdmission.Outcome == Contract.AdmissionOutcome.Accepted && actor is { Entries: 1, Completions: 1 } &&
            recovered is { Park: null, Row: { Status: InstanceStatus.Running, Jobs: null } } && !store.Messages.ContainsKey(ticket),
            $"Resume uses the current-generation {(pendingAtOpen ? "pending" : "kept")} result once and never restarts the settled job");
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
        public bool FaultOnAdvance { get; init; }
        public bool FinishOnAdvance { get; init; }
        public bool ArmJobOnEnd { get; init; }
        public bool HoldTerminalInput { get; init; }
        public bool HonourTerminate { get; init; }
        public bool HoldTerminalNotices { get; init; }
        public int HeldChildOffers { get; private set; }
        public string[] TerminalQueryOffer { get; private set; } = [];
        public bool ThrowOnRunningWithholds { get; set; }
        public bool HoldAdvance { get; init; }
        public string[] LateOffer { get; private set; } = [];
        public bool PausePages { get; set; }
        public bool HoldSuspend { get; init; }
        public bool ProtectAfterRecoveryEnd { get; set; }
        public int SuspendOffers { get; private set; }
        public Contract.ActorRef? Child { get; private set; }
        public List<Contract.ChildFinished> Notices { get; } = [];
        public bool Withholds(Contract.ActorHeader header) => ThrowOnRunningWithholds && header.Status == Contract.ActorStatus.Running
            ? throw new InvalidOperationException("withholding probe failed")
            : PausePages && header.Status == Contract.ActorStatus.Running ||
                withholdFaulted && header.Status == Contract.ActorStatus.Faulted;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            Offers++;
            if (HonourTerminate && turn.Header.Status == Contract.ActorStatus.Running &&
                turn.Inbox is [{ Kind: Contract.Kinds.Terminate }])
            {
                turn.End(Contract.ActorStatus.Cancelled);
                return default;
            }
            if (turn.Inbox.Any(message => message.Kind == "terminal-query"))
                TerminalQueryOffer = turn.Inbox.Select(message => message.Kind).ToArray();
            if (HoldSuspend && turn.Inbox is [var suspend] && suspend.Kind == Contract.Kinds.Suspend)
            {
                SuspendOffers++;
                turn.Hold(suspend);
                return default;
            }
            if (turn.Inbox.Any(message => message.Kind == "poison"))
            {
                PoisonOffers++;
                throw new InvalidOperationException("turn fault");
            }
            foreach (var message in turn.Inbox)
                if (message.Kind == "spawn") Child = turn.Start(new(Type, "child", null));
                else if (message.Kind == Contract.Kinds.ChildFinished && HoldTerminalNotices)
                {
                    HeldChildOffers++;
                    turn.Hold(message);
                }
                else if (message.Kind == Contract.Kinds.ChildFinished) Notices.Add(Json.Read<Contract.ChildFinished>(message.Payload!));
                else if (message.Kind == "finish-child") turn.End(Contract.ActorStatus.Finished);
            if (turn.Inbox.Any(message => message.Kind == "advance"))
            {
                if (HoldAdvance && !turn.Inbox.Any(message => message.Kind == "late"))
                {
                    turn.Hold(turn.Inbox.Single(message => message.Kind == "advance"));
                    return default;
                }
                if (HoldAdvance) LateOffer = turn.Inbox.Select(message => message.Kind).ToArray();
                RecoveredArrivals++;
                turn.State = "advanced";
                if (FaultOnAdvance || FinishOnAdvance)
                {
                    if (HoldTerminalInput)
                        foreach (var pending in turn.Inbox.Where(message => message.Kind == "terminal-pending"))
                            turn.Hold(pending);
                    if (ArmJobOnEnd) turn.StartJob("cleanup");
                    turn.End(FinishOnAdvance ? Contract.ActorStatus.Finished : Contract.ActorStatus.Faulted);
                }
            }
            else turn.State = "saved";
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => ProtectAfterRecoveryEnd;
    }

    private sealed class KeepingRecoveryProbe : Contract.IActorType
    {
        public string Type => "keeping-recovery";
        public List<string> Received { get; } = [];
        public List<string?> Payloads { get; } = [];

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.Start) turn.Subscribe("approval", "waiting", keep: true);
                else if (message.Kind == "fault") throw new InvalidOperationException("park with a keeping wait");
                else if (message.Event is { } received)
                {
                    Received.Add(received.Id);
                    Payloads.Add(message.Payload);
                }
            }
            return default;
        }

        public bool Withholds(Contract.ActorHeader header) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], [new("approval", "waiting", true)]);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }

    private sealed class EventKeyComparisons : StringComparer
    {
        public int Count { get; private set; }
        public override int Compare(string? left, string? right) => OrdinalIgnoreCase.Compare(left, right);
        public override int GetHashCode(string value) => OrdinalIgnoreCase.GetHashCode(value);

        public override bool Equals(string? left, string? right)
        {
            Count++;
            return OrdinalIgnoreCase.Equals(left, right);
        }
    }

    private sealed class RestorePlanProbe : Contract.IActorType
    {
        public string Type => "restore-plan";
        public bool ReturnPlan { get; set; }
        public bool ReturnInvalidJob { get; set; }
        public bool ReturnOversizeJobs { get; set; }
        private long _due;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
                if (message.Kind == Contract.Kinds.Start)
                {
                    _due = turn.Now + 10_000;
                    turn.State = turn.Send(turn.Header.Ref, "", "timer content", _due);
                }
                else if (message.Kind == "fault") throw new InvalidOperationException("park with a timer");
            return default;
        }

        public bool Withholds(Contract.ActorHeader header) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state)
        {
            if (ReturnInvalidJob) return new([new(state, "", "timer content", _due)], [new(null!, null)], [new("approval", "waiting", false)]);
            if (ReturnOversizeJobs)
                return new([new(state, "", "timer content", _due)],
                    [new("first", new string('x', 13 * 1024 * 1024)), new("second", new string('y', 13 * 1024 * 1024))], []);
            return ReturnPlan ? new([new(state, "", "timer content", _due)], [], []) : new([null!], [], []);
        }
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }

    private sealed class ParkedJobProbe(string value, bool timed, string? arguments = null) : Contract.IActorType
    {
        public string Type => "parked-job-check";
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Entries { get; private set; }
        public int Completions { get; private set; }
        public bool FaultOnResult { get; init; }
        public bool EndHoldingResult { get; init; }
        public int BeforeDeadline { get; init; }
        public bool EndBeforeTicket { get; init; }
        private bool _faulted;
        private long? _deadline;
        public string? Result { get; private set; }
        public string? JobError { get; private set; }
        public System.Collections.Concurrent.ConcurrentQueue<string?> Arguments { get; } = new();
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
                switch (message.Kind)
                {
                    case Contract.Kinds.Start:
                        if (EndBeforeTicket)
                            turn.Send(turn.Header.Ref, "end-before-job", new string('x', ParkRecoveryCredit.LimitBytes), turn.Now + 300);
                        for (var index = 0; index < BeforeDeadline; index++)
                            turn.Send(turn.Header.Ref, "before-job-deadline", null, turn.Now + 400);
                        _deadline = timed ? turn.Now + 1000 : null;
                        turn.StartJob("background", arguments, timeout: timed ? TimeSpan.FromSeconds(1) : null);
                        break;
                    case "fault": throw new InvalidOperationException("park with a running job");
                    case "end-before-job":
                        turn.End(Contract.ActorStatus.Finished);
                        break;
                    case Contract.Kinds.JobFinished:
                        if (FaultOnResult && !_faulted)
                        {
                            _faulted = true;
                            throw new InvalidOperationException("park while applying the job result");
                        }
                        var result = Json.Read<Contract.JobFinished>(message.Payload!);
                        if (result.Result is { } serialized) Result = Json.Read<string>(serialized);
                        JobError = result.Error;
                        Completions++;
                        if (EndHoldingResult)
                        {
                            turn.Hold(message);
                            turn.End(Contract.ActorStatus.Finished);
                        }
                        break;
                }
            return default;
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            Arguments.Enqueue(job.Arguments);
            Entries++;
            await Complete.Task.WaitAsync(cancellationToken);
            return value;
        }

        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [new("background", arguments) { Deadline = _deadline }], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
