using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkRetryChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await CheckScopeAndAtomicity(check);
        await CheckTargetSelection(Contract.RecoverMode.Resume, check);
        await CheckTargetSelection(Contract.RecoverMode.RetryAll, check);
        await CheckBoundsAndInterruption(check);
        await CheckLegacyTimerSelection(check);
        await CheckBoundaryEventSelection(check);
        await CheckLegacyEventSelection(check, selected: 0);
        await CheckLegacyEventSelection(check, selected: 1);
    }

    private static async Task CheckBoundaryEventSelection(Action<bool, string> check)
    {
        const int limit = FoundationMetadataLimits.NameBytes;
        var name = new string('k', limit);
        var fixture = new Fixture { EventWaits = [name], EventWaitName = new string('w', limit) };
        var (store, _, owner) = await fixture.ActivateAsync();
        await using var activation = owner;
        var scope = ContractScopes.Bind(new string('t', 64));
        var target = await fixture.StartRunningAsync(owner, scope, name, keyed: true);
        var publication = await fixture.Publish(scope, name, new string('e', limit), "boundary event");
        await fixture.Finish(owner);
        var read = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        var key = store.Messages.Values.Single(row => row.Message.Kind == Kinds.Event).Message.InboxKey;
        var valid = publication.Outcome == Contract.AdmissionOutcome.Accepted && read.Park is not null &&
            key is { Id.Length: 67, Match.Length: 64, Scope: InboxKey.EventDeliveryScope } &&
            store.Messages.GetValueOrDefault(key) is { Done: true, KeptBy: ParkKeepReason.Consumed };
        check(valid, "S-2 maximum event names produce an authentic 64-byte match in a consumed input");
        if (!valid) return;

        var parked = new Parked(scope, target, read.Row, read.Park!, store.SealFoundationSelection(scope.Tenant, read)!);
        var page = store.ReadFoundationKeptPage(new(read.Row.Id, read.Row.Creation), parked.Park, after: null);
        var handle = fixture.Handle(parked, key);
        check(InMemoryStore.FitsFoundationRetryHandles([handle]) && page is [var head] && head.Cursor.Key == key,
            "S-2 the event handle preserves the full digest-bearing key at the page head");
        var retry = await fixture.Retry(parked, "select-boundary-event", [handle]);
        await fixture.Finish(owner);
        check(retry.Outcome == Contract.AdmissionOutcome.Accepted && fixture.Delivered.SequenceEqual(["boundary event"]) &&
            store.Instances[read.Row.Id] is { Status: InstanceStatus.Running, ParkRecovery: null } &&
            store.Messages.Values.All(row => row.KeptBy is null), "S-2 Retry completes after delivering the boundary event once");
    }

    private static async Task CheckTargetSelection(Contract.RecoverMode mode, Action<bool, string> check)
    {
        var fixture = new Fixture();
        var (store, _, owner) = await fixture.ActivateAsync();
        await using var activation = owner;
        var scope = ContractScopes.Bind("retry-park-selection");
        var a = await fixture.StartRunningAsync(owner, scope, "first", keyed: true);
        var b = await fixture.StartRunningAsync(owner, scope, "second", keyed: true);
        await fixture.Submit(scope, a, "park-first", "fault");
        await fixture.Submit(scope, b, "park-second", "fault");
        await fixture.Finish(owner);

        var parkedA = store.ReadFoundationActor(scope.Tenant, a.Actor.Id).Actor!;
        var parkedB = store.ReadFoundationActor(scope.Tenant, b.Actor.Id).Actor!;
        var equalMark = parkedA.Park is not null && parkedA.Park == parkedB.Park &&
            parkedA.Row.Creation == parkedB.Row.Creation && parkedA.Row.Id != parkedB.Row.Id;
        check(equalMark, $"PS-1 {mode}: two distinct callers have equal Park marks and creation");
        if (!equalMark) return;

        var reads = new ActorList(store, 5);
        var selection = (await reads.GetAsync(scope, a.Actor))!.Selection!;
        var sameSelection = Json.Write(new Contract.OperatorCommand(selection, mode));
        var kept = store.Messages.Values.Single(row => row.Message.Id == "park-second" && row.KeptBy == ParkKeepReason.Consumed);
        var journal = store.Journal.Values.Where(row => row.Instance == parkedB.Row.Id).ToArray();
        var refused = await fixture.Submit(scope, b, "wrong-target", Contract.Kinds.Recover, sameSelection);
        await fixture.Finish(owner);
        check(refused is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection, Actor: null } &&
            store.Instances[parkedB.Row.Id] == parkedB.Row && kept.KeptBy == ParkKeepReason.Consumed &&
            store.Journal.Values.Where(row => row.Instance == parkedB.Row.Id).SequenceEqual(journal),
            $"PS-1 {mode}: the other actor's selection cannot change this actor or its retained input");

        await fixture.Submit(scope, a, "recovered-arrival", "observation", "expected");
        await fixture.Finish(owner);
        check(store.Messages.Values.Single(row => row.Message.Id == "recovered-arrival").KeptBy == ParkKeepReason.Arrived,
            $"PS-1 {mode}: the intended actor keeps the arrival before Recover");
        // RetryAll also delivers the original fault input. Let this fixture observe its retry without throwing again.
        fixture.AllowRecoveredFault = true;
        var accepted = await fixture.Submit(scope, a, "right-target", Contract.Kinds.Recover, sameSelection);
        await fixture.Finish(owner);
        check(accepted is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: var actual } && actual == a.Actor &&
            store.Instances[parkedA.Row.Id] is { Status: InstanceStatus.Running, ParkRecovery: null } &&
            store.Messages.Values.Where(row => row.Message.Target.Id == parkedA.Row.Id && !row.Message.IsControl)
                .All(row => row is { Done: true, KeptBy: null }) && fixture.Delivered.SequenceEqual(["expected"]) &&
            fixture.RecoveredFaults == (mode == Contract.RecoverMode.RetryAll ? 1 : 0),
            $"PS-1 {mode}: the same read selection resolves its own actor and restores the arrival once");
    }

    private static async Task CheckLegacyEventSelection(Action<bool, string> check, int selected)
    {
        var correlations = new[] { "live-event-a", "live-event-b" };
        var fixture = new Fixture { EventWaits = correlations };
        var (store, worker, owner) = await fixture.ActivateAsync();
        await using var activation = owner;
        var scope = ContractScopes.Bind("retry-legacy");
        var target = await fixture.StartRunningAsync(owner, scope, "wait");
        var keys = new List<InboxKey>();
        foreach (var correlation in correlations)
        {
            var publication = new EventRow(scope.Tenant + "_" + correlation, "legacy-id", correlation, fixture.Now + 60_000)
                { FoundationTenant = scope.Tenant };
            store.RecordFoundationPublication(publication);
            var delivery = (await store.PublishedMatchesAsync(publication)).Single().Delivery();
            keys.Add(delivery.Message.InboxKey);
            await store.AddEventAsync(delivery);
        }
        await fixture.Finish(owner);

        var read = store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        var authenticTwins = keys[0].Id == keys[1].Id && keys[0].Scope == keys[1].Scope &&
            keys[0].Match != keys[1].Match && read.Park is not null &&
            keys.All(key => key.Match is { Length: > 64 } &&
                store.Messages.GetValueOrDefault(key) is { Done: true, KeptBy: ParkKeepReason.Consumed });
        check(authenticTwins, "B2-3 two historical event deliveries share a wire id and scope and are kept by distinct full keys");
        if (!authenticTwins) return;

        var key = keys[selected];
        var twin = keys[1 - selected];
        var sequence = store.Messages[key].Seq;
        store.Messages[twin].Seq = sequence;
        var parked = new Parked(scope, target, read.Row, read.Park!, store.SealFoundationSelection(scope.Tenant, read)!);
        Contract.IActorReads reads = new ActorList(store, 1);
        var first = await reads.InboxAsync(scope, target.Actor);
        var second = first.Next is { } next ? await reads.InboxAsync(scope, target.Actor, next) : null;
        var paged = first.Items is [{ Id: "legacy-id", State: Contract.DeliveryState.Consumed, Handle.Length: < 128 }] &&
            second is { Items: [{ Id: "legacy-id", State: Contract.DeliveryState.Consumed, Handle.Length: < 128 }], Next: null } &&
            first.Items[0].Handle != second.Items[0].Handle;
        check(paged, "B2-3 one-row inbox pages expose both historical matches with bounded handles at an equal sequence");
        if (!paged) return;
        var keyIsFirst = new ParkPageCursor(sequence, key).CompareTo(new(sequence, twin)) < 0;
        var handle = keyIsFirst ? first.Items[0].Handle : second!.Items[0].Handle;

        await fixture.Retry(parked, "legacy-exact", [handle]);
        await fixture.Step(worker, parked);
        check(store.Messages[key].KeptBy == ParkKeepReason.Selected && store.Messages[twin].KeptBy == ParkKeepReason.Consumed,
            "a compact Retry handle resolves the exact full key even when a different match has the same sequence");
        await fixture.Finish(owner);
        check(store.Instances[parked.Row.Id].ParkRecovery is null && fixture.Delivered.SequenceEqual([correlations[selected]]) &&
            store.Messages[twin] is { Done: true, KeptBy: null, ReleasedBy: null },
            $"B2-3 the Worker replays only the selected historical match ({correlations[selected]}) and clears its twin");
    }

    private static async Task CheckScopeAndAtomicity(Action<bool, string> check)
    {
        var fixture = new Fixture();
        var (store, worker, owner) = await fixture.ActivateAsync();
        await using var activation = owner;
        var parked = await fixture.StartParkedAsync(owner, "retry-scope", "a");
        var otherActor = await fixture.StartParkedAsync(owner, "retry-scope", "b");
        var otherTenant = await fixture.StartParkedAsync(owner, "retry-other", "c");
        if (parked is null || otherActor is null || otherTenant is null)
        {
            check(false, "the scope fixture parks three real Worker activations");
            return;
        }
        var selected = fixture.Seed(parked, new("twin", 5, "match-a"), "selected");
        var otherMatch = fixture.Seed(parked, new("twin", 5, "match-b"), "other-match");
        var otherScope = fixture.Seed(parked, new("twin", 6, "match-a"), "other-scope");
        var arrived = fixture.Seed(parked, new("arrived", 0), "arrival", ParkKeepReason.Arrived);
        var obsoleteTicket = fixture.Seed(parked, InboxKey.Ticket(parked.Row.Id, "retired-job", parked.Row.Creation), "obsolete-ticket", kind: Kinds.Job);
        var obsoleteResult = fixture.Seed(parked, InboxKey.JobResult(parked.Row.Id, "retired-job", parked.Row.Creation), "obsolete-result",
            kind: Kinds.JobFinished, jobResult: true);
        var valid = fixture.Handle(parked, selected);
        var sameKeyAgain = new InboxKey("replaced", 0);
        var oldRow = fixture.Handle(parked, fixture.Seed(parked, sameKeyAgain, "old"));
        fixture.Seed(parked, sameKeyAgain, "replacement");
        var untrusted = new (string Case, string Handle)[]
        {
            ("arrived", fixture.Handle(parked, arrived)),
            ("actor", fixture.Handle(otherActor, fixture.Seed(otherActor, new("foreign-a", 0), "different-actor"))),
            ("tenant", fixture.Handle(otherTenant, fixture.Seed(otherTenant, new("foreign-tenant", 0), "different-tenant"))),
            ("unknown", valid[0] == 'A' ? "B" + valid[1..] : "A" + valid[1..]),
            ("inbox", store.DeliveryHandle(parked.Target.Actor.Id, store.Messages[selected].Seq, selected)),
            ("duplicate", fixture.Handle(parked, selected)),
            ("obsolete-ticket", fixture.Handle(parked, obsoleteTicket)),
            ("obsolete-result", fixture.Handle(parked, obsoleteResult)),
            ("old-sequence", oldRow)
        };

        foreach (var (reason, handle) in untrusted)
        {
            var operation = "invalid-" + reason;
            var admitted = await fixture.Retry(parked, operation, [valid, handle]);
            await fixture.Step(worker, parked);
            check(admitted.Outcome == Contract.AdmissionOutcome.Accepted && store.Instances[parked.Row.Id] == parked.Row &&
                store.Messages[selected].KeptBy == ParkKeepReason.Consumed &&
                store.Messages.Values.Single(row => row.Message.Id == operation).Done &&
                store.Journal.Values.Any(row => row.Instance == parked.Row.Id && row.Entries ==
                    Json.Write(new { Outcome = "refused", Operation = operation, Reason = "selection" })),
                $"the Worker records refusal for {reason} without changing a mixed selection's valid row");
        }

        var accepted = await fixture.Retry(parked, "exact", [valid]);
        var command = store.Messages.Values.Single(row => row.Message.Id == "exact");
        var stimulus = new Delivery(command.Message, command.Seq) { Durable = true };
        var staged = fixture.Adapter.Command(new TurnInput(worker, new Slot { Row = parked.Row, Loaded = true }, parked.Row.Id,
            [stimulus], 0, null, fixture.Now))!;
        var open = new TurnCommit(parked.Row.Id, staged.Row, staged.Expected, staged.Cancelled, [stimulus.Message.InboxKey], staged.Sent,
            staged.Subscribed, staged.Unsubscribed, staged.Journal)
        {
            FoundationRetry = staged.FoundationRetry, FoundationRecoveryTimers = staged.FoundationRecoveryTimers,
            FoundationRecoveryJobs = staged.FoundationRecoveryJobs, FoundationRecoverySubscriptions = staged.FoundationRecoverySubscriptions
        };
        var fence = worker.Fences.Single();
        var wrong = open with { FoundationRetry = staged.FoundationRetry! with { Marked = [] } };
        var rejected = false;
        try
        {
            await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [wrong], [], []));
        }
        catch (StoreException error) when (error.Failure == Failure.Unknown) { rejected = true; }
        check(rejected && store.Instances[parked.Row.Id] == parked.Row && !command.Done &&
            store.Messages[selected].KeptBy == ParkKeepReason.Consumed && store.Partitions[0].Position == fence.Position,
            "the fenced store rejects a staged marking list that differs from the authenticated command, with no partial Open");

        await fixture.Step(worker, parked);
        check(accepted.Outcome == Contract.AdmissionOutcome.Accepted && store.Instances[parked.Row.Id].ParkRecovery is { Cursor: null } &&
            store.Messages[selected].KeptBy == ParkKeepReason.Selected && store.Messages[otherScope].KeptBy == ParkKeepReason.Consumed &&
            store.Messages[otherMatch].KeptBy == ParkKeepReason.Consumed,
            "Open marks only the exact full key, including scope and match");
        await fixture.Finish(owner);
        check(store.Instances[parked.Row.Id].ParkRecovery is null && fixture.Delivered.SequenceEqual(["selected", "arrival"]) &&
            store.Messages.Values.Where(row => row.Message.Target.Id == parked.Row.Id).All(row => row.KeptBy is null),
            "the real Worker replays the selected row and the Arrived row and clears every unselected row");

        var previousSequence = store.Messages[selected].Seq;
        var later = await fixture.ParkAsync(owner, parked.Scope, parked.Target, "new-park");
        if (later is null)
        {
            check(false, "the scope fixture opens a later Park of the same creation");
            return;
        }
        fixture.Seed(later, selected, "new-park-row");
        store.Messages[selected].Seq = previousSequence;
        var expired = await fixture.Retry(later, "old-park-handle", [valid]);
        await fixture.Step(worker, later);
        check(expired.Outcome == Contract.AdmissionOutcome.Accepted &&
            later.Park != parked.Park && later.Row.Creation == parked.Row.Creation && store.Instances[parked.Row.Id] == later.Row &&
            store.Messages[selected].KeptBy == ParkKeepReason.Consumed,
            "the same full key cannot carry a handle from an earlier Park of the same creation");
    }

    private static async Task CheckBoundsAndInterruption(Action<bool, string> check)
    {
        var fixture = new Fixture();
        var (store, worker, owner) = await fixture.ActivateAsync();
        await using var activation = owner;
        var parked = await fixture.StartParkedAsync(owner, "retry-bounds", "bounds");
        if (parked is null)
        {
            check(false, "the bounds fixture parks a real Worker activation");
            return;
        }
        var keys = Enumerable.Range(0, InMemoryStore.MaxRetryHandles + 1).Select(index =>
            fixture.Seed(parked, new($"short-{index}", 0), $"short-{index}", kind: index == 0 ? "trip" : "observation")).ToArray();
        var handles = keys.Select(key => fixture.Handle(parked, key)).ToArray();
        var wide = Enumerable.Range(0, 100).Select(index => fixture.Handle(parked,
            fixture.Seed(parked, new(index + new string('w', 1_000), 0), $"wide-{index}"))).ToArray();
        var tooMany = await fixture.Retry(parked, "too-many", handles);
        var tooLarge = await fixture.Retry(parked, "too-large", wide);
        check(wide.Sum(handle => handle.Length) > InMemoryStore.MaxRetryHandleBytes &&
            tooMany.Outcome == Contract.AdmissionOutcome.Refused && tooLarge.Outcome == Contract.AdmissionOutcome.Refused &&
            store.Messages.Values.All(row => row.Message.Id is not ("too-many" or "too-large")) &&
            store.Instances[parked.Row.Id] == parked.Row &&
            keys.All(key => store.Messages[key].KeptBy == ParkKeepReason.Consumed),
            "ingress refuses 501 handles and more than 256 KiB without inserting an operation or marking a row");

        fixture.FaultNextTrip = true;
        var accepted = await fixture.Retry(parked, "at-bound", handles[..InMemoryStore.MaxRetryHandles]);
        await fixture.Step(worker, parked);
        var opened = accepted.Outcome == Contract.AdmissionOutcome.Accepted && store.Instances[parked.Row.Id].ParkRecovery is { Cursor: null } &&
            store.Messages.Values.Count(row => row.KeptBy == ParkKeepReason.Selected) == InMemoryStore.MaxRetryHandles &&
            store.Messages[keys[^1]].KeptBy == ParkKeepReason.Consumed;
        check(opened, "500 distinct handles mark exactly 500 rows atomically");
        if (!opened) return;

        await fixture.Step(worker, parked);
        var lastSelected = keys[^2];
        var residual = store.Messages[lastSelected].KeptBy == ParkKeepReason.Selected;
        await fixture.Finish(owner);
        var interrupted = store.ReadFoundationActor(parked.Scope.Tenant, parked.Target.Actor.Id).Actor!;
        check(residual && interrupted.Park == parked.Park && interrupted.Row.ParkRecovery is null &&
            interrupted.Row.Status == InstanceStatus.Faulted && store.Messages[lastSelected].KeptBy == ParkKeepReason.Consumed &&
            store.Messages.Values.All(row => row.KeptBy != ParkKeepReason.Selected),
            "a real recovery fault before the last page reverts the bounded residual marks to Consumed under the same Park");

        var again = await fixture.Retry(parked, "again", [handles[^2]]);
        await fixture.Finish(owner);
        check(again.Outcome == Contract.AdmissionOutcome.Accepted && store.Instances[parked.Row.Id].ParkRecovery is null &&
            fixture.Delivered.Count(name => name == lastSelected.Id) == 1 &&
            store.Messages.Values.All(row => row.KeptBy is null),
            "a later explicit retry uses its own selection after the rollback of the prior one");
    }

    private static async Task CheckLegacyTimerSelection(Action<bool, string> check)
    {
        var fixture = new Fixture();
        var (store, _, first) = await fixture.ActivateAsync();
        await using var owner = first;
        var parked = (await fixture.StartParkedAsync(owner, "legacy-retry", "timer"))!;
        var origin = CreationProtocol.Origin(parked.Row.Id, parked.Row.Creation);
        var legacy = store.MessageHandle("legacy-timer");
        var key = InboxKey.Issued(origin + "#" + legacy);
        var envelope = new Message(legacy, new(parked.Row.Id), "observation", "legacy timer", parked.Row.Id,
            Due: fixture.Now, TargetCreation: parked.Row.Creation, SenderCreation: parked.Row.Creation)
        { ActorAuthored = true, StorageId = key.Id }.AsIssued();
        store.Messages[key] = new(envelope, 901, 0) { Done = true, KeptBy = ParkKeepReason.Consumed };
        fixture.Timer = new(legacy, envelope.Kind, envelope.Payload, envelope.Due!.Value);
        var originalHandle = fixture.Handle(parked, key);
        fixture.Seed(parked, new("interrupt-suppression", 0), "trip", ParkKeepReason.Arrived, "trip");
        fixture.FaultNextTrip = true;
        await fixture.Resume(parked, "suppress-legacy");
        await fixture.Finish(owner);
        var interrupted = store.Instances[parked.Row.Id];
        var suppressed = interrupted is { Status: InstanceStatus.Faulted, ParkRecovery: null, ParkTimerDispositions: not null } &&
            store.Messages[key] is { Done: true, KeptBy: null, Message.KeepUntil: not null };
        check(suppressed, "the legacy timer has a retained suppression after the real recovery is interrupted");
        if (!suppressed) return;

        var afterSweep = store.Messages[key].Message.KeepUntil!.Value + 1;
        await owner.DisposeAsync();
        fixture.AdvanceTo(afterSweep);
        await store.SweepAsync();
        var (_, _, next) = await fixture.ActivateAsync();
        await using var replacement = next;
        var admitted = await fixture.Retry(parked, "select-swept-legacy", [originalHandle]);
        await fixture.Finish(replacement);
        check(admitted.Outcome == Contract.AdmissionOutcome.Accepted && fixture.Delivered.SequenceEqual(["legacy timer"]) &&
            store.Instances[parked.Row.Id] is { Status: InstanceStatus.Running, ParkRecovery: null },
            "the original handle retries the swept legacy timer once using retained creation evidence");
    }

    private sealed record Parked(Contract.BoundScope Scope, Contract.Target.Ref Target, InstanceRow Row,
        InMemoryStore.FoundationParkMark Park, string Selection);

    private sealed class Fixture : Contract.IActorType
    {
        private const long Initial = 1_701_700_000_000;
        private long _seq = 1_000;
        private InMemoryIngress _ingress = null!;
        internal InMemoryStore Store { get; }
        internal ActorAdapter Adapter { get; }
        internal long Now { get; private set; } = Initial;
        internal List<string> Delivered { get; } = [];
        internal bool FaultNextTrip { get; set; }
        internal bool AllowRecoveredFault { get; set; }
        internal int RecoveredFaults { get; private set; }
        internal Contract.TimerSpec? Timer { get; set; }
        internal IReadOnlyList<string> EventWaits { get; init; } = [];
        internal string EventWaitName { get; init; } = "wait";
        private int _eventOffers;

        internal Fixture()
        {
            Store = new InMemoryStore(1, () => Now);
            Adapter = new ActorAdapter(Store, this);
        }

        internal async Task<(InMemoryStore Store, Worker Worker, IScopeActivation Owner)> ActivateAsync()
        {
            var worker = new Worker("retry-worker", 0, Store, Adapter, new WorkerOptions { Monotonic = () => Now - Initial });
            _ingress = new(Store, new Requests(worker.Node), worker.Node, (_, kind) => kind == Contract.Kinds.Recover);
            var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
            return (Store, worker, owner);
        }

        internal async Task<Parked?> StartParkedAsync(IScopeActivation owner, string tenant, string name)
        {
            var scope = ContractScopes.Bind(tenant);
            var target = await StartRunningAsync(owner, scope, name);
            return await ParkAsync(owner, scope, target, "park-" + name);
        }

        internal async Task<Contract.Target.Ref> StartRunningAsync(IScopeActivation owner, Contract.BoundScope scope, string name, bool keyed = false)
        {
            var spec = new Contract.StartSpec(Type, "ready", null);
            Contract.Target start = keyed ? new Contract.Target.Key(name, spec) : new Contract.Target.Start(spec);
            var admission = await _ingress.SendAsync(scope, start, name, Contract.Kinds.Start);
            var target = new Contract.Target.Ref(admission.Actor!.Value);
            await Finish(owner);
            return target;
        }

        internal async Task<Parked?> ParkAsync(IScopeActivation owner, Contract.BoundScope scope, Contract.Target.Ref target, string id)
        {
            await _ingress.SendAsync(scope, target, id, "fault");
            await Finish(owner);
            var read = Store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor;
            return read is { Park: { } park }
                ? new(scope, target, read.Row, park, Store.SealFoundationSelection(scope.Tenant, read)!)
                : null;
        }

        // Seeds bounded fixture rows after a real Park. Recovery itself only uses the real ingress, Worker and fenced store.
        internal InboxKey Seed(Parked parked, InboxKey key, string payload, ParkKeepReason reason = ParkKeepReason.Consumed,
            string kind = "observation", bool jobResult = false)
        {
            var message = new Message(key.Id, new(parked.Row.Id), kind, payload, TargetCreation: parked.Row.Creation)
            { FoundationJobResult = jobResult }.WithInboxScope(key.Scope, key.Match);
            Store.Messages[key] = new(message, _seq++, 0) { Done = true, KeptBy = reason };
            return key;
        }

        internal string Handle(Parked parked, InboxKey key) =>
            Store.SealFoundationKeptHandle(parked.Scope.Tenant, parked.Target.Actor.Id, key)!;

        internal ValueTask<Contract.Admission> Submit(Contract.BoundScope scope, Contract.Target.Ref target,
            string operation, string kind, string? payload = null) => _ingress.SendAsync(scope, target, operation, kind, payload);

        internal ValueTask<Contract.Admission> Publish(Contract.BoundScope scope, string key, string operation, string payload) =>
            _ingress.SendAsync(scope, new Contract.Target.Event(key), operation, Contract.Kinds.Event, payload);

        internal ValueTask<Contract.Admission> Retry(Parked parked, string operation, IReadOnlyList<string> handles) =>
            _ingress.SendAsync(parked.Scope, parked.Target, operation, Contract.Kinds.Recover,
                Json.Write(new Contract.OperatorCommand(parked.Selection, Contract.RecoverMode.Retry, handles)));

        internal ValueTask<Contract.Admission> Resume(Parked parked, string operation) =>
            _ingress.SendAsync(parked.Scope, parked.Target, operation, Contract.Kinds.Recover,
                Json.Write(new Contract.OperatorCommand(parked.Selection)));

        internal void AdvanceTo(long now) => Now = now;

        internal async Task Step(Worker worker, Parked parked)
        {
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(parked.Row.Id);
            await worker.FlushAsync();
        }

        internal async Task Finish(IScopeActivation owner)
        {
            for (var step = 0; step < 4; step++)
            {
                Now += 11;
                for (var poll = 0; poll < 10; poll++)
                {
                    await owner.WokenAsync(null);
                    await owner.RunAsync();
                }
            }
        }

        public string Type => "park-retry-probe";
        public bool Withholds(Contract.ActorHeader header) => false;
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Inbox.Any(message => message.Kind == Contract.Kinds.Start))
                foreach (var correlation in EventWaits) turn.Subscribe(correlation, EventWaitName);
            foreach (var input in turn.Inbox.Where(message => message.Kind == Contract.Kinds.Event))
            {
                if (++_eventOffers == 1) throw new InvalidOperationException("Park before applying the event");
                Delivered.Add(input.Payload!);
            }
            if (turn.Inbox.Any(message => message.Kind == "fault"))
            {
                if (!AllowRecoveredFault) throw new InvalidOperationException("original Park");
                RecoveredFaults++;
            }
            if (FaultNextTrip && turn.Inbox.Any(message => message.Kind == "trip"))
            {
                FaultNextTrip = false;
                throw new InvalidOperationException("interrupted retry");
            }
            Delivered.AddRange(turn.Inbox.Where(message => message.Kind == "observation").Select(message => message.Payload!));
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) =>
            new(Timer is { } timer ? [timer] : [], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
