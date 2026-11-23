using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class RetentionDeleteChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await PolicyAtDelivery(check);
        await DelayedAcrossReuse(check);
        await DeleteIdentityDuringClear(check, afterAttachment: false);
        await DeleteIdentityDuringClear(check, afterAttachment: true);
        await DelayedParkControlAcrossReuse(check, expiry: true);
        await DelayedParkControlAcrossReuse(check, expiry: false);
        await UnresolvedPark(check);
        await RecoveryAfterSelection(check);
        await ExpiryStartsRetention(check);
    }

    private static async Task DelayedParkControlAcrossReuse(Action<bool, string> check, bool expiry)
    {
        var fixture = new Fixture(TimeSpan.FromSeconds(1)) { PollHorizon = TimeSpan.Zero };
        var store = fixture.Store;
        var label = expiry ? "P-4(4a)" : "P-4(4b)";
        var first = fixture.Owner();
        await using var initial = await Activate(first);
        var a = await fixture.Start(first, "control-origin", callerKey: "reused-control");
        var id = fixture.Row(a).Id;
        await fixture.Send(a, "park-origin", "park");
        await Step(first, id);
        var original = fixture.Checkpoint(a);
        if (original.Park is not { } mark)
        {
            check(false, $"{label} the original caller-keyed creation parks");
            return;
        }
        var recalled = expiry ? store.FoundationExpiry(original.Row)?.Armed : await OpenAndWait(first, a, "recover-origin");
        if (recalled is not { } oldKey || !store.Messages.TryGetValue(oldKey, out var old))
        {
            check(false, $"{label} the old creation durably issues its control");
            return;
        }
        if (!expiry)
        {
            await fixture.Send(a, "terminate-origin", Contract.Kinds.Terminate);
            for (var pass = 0; pass < 12 && fixture.Row(a).Status != InstanceStatus.Cancelled; pass++) await Step(first, id);
            var ended = fixture.Row(a) is { Status: InstanceStatus.Cancelled, ParkRecovery: null } && !store.Messages.ContainsKey(oldKey);
            check(ended, $"{label} Terminate interrupts recovery, recalls the control and ends its creation");
            if (!ended) return;
        }
        await fixture.Send(a, "delete-origin", Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(await fixture.Selection(a))));
        for (var pass = 0; pass < 12 && !store.Instances[id].Retired; pass++) await Step(first, id);
        var retired = store.Instances[id] is { Retired: true } marker && marker.Creation == original.Row.Creation &&
            !store.Messages.ContainsKey(oldKey);
        check(retired, $"{label} retirement leaves a reusable watermark and removes the old control");
        if (!retired) return;

        fixture.WithholdPages = false;
        fixture.Now += 101;
        await store.SweepAsync();
        var b = await fixture.Start(first, "control-successor", callerKey: "reused-control");
        await initial.DisposeAsync();
        fixture.Now = mark.At;
        var next = fixture.Owner();
        await using var successor = await Activate(next);
        await fixture.Send(b, "park-successor", "park");
        await Step(next, id);
        var parked = fixture.Checkpoint(b);
        var armed = expiry ? store.FoundationExpiry(parked.Row)?.Armed : await OpenAndWait(next, b, "recover-successor");
        if (armed is not { } nextKey || !store.Messages.TryGetValue(nextKey, out var current))
        {
            check(false, $"{label} the successor creation durably issues its control");
            return;
        }
        var checkpoint = fixture.Checkpoint(b);
        var oldEnvelope = old.Message.Read<ParkDrainEnvelope>();
        var newEnvelope = current.Message.Read<ParkDrainEnvelope>();
        var pinned = checkpoint.Row.Id == id && checkpoint.Row.Creation == original.Row.Creation + 1 && parked.Park == mark &&
            oldEnvelope.Generation == (expiry ? 0 : 1) && oldEnvelope == (newEnvelope with { Creation = original.Row.Creation }) &&
            old.Message.Due is not null && old.Message.Due == current.Message.Due &&
            old.Seq < current.Seq && oldKey != nextKey;
        check(pinned, $"{label} the reused physical target has equal Park marks, generations and due times across creations");
        if (!pinned) return;

        fixture.WithholdPages = false;
        fixture.Now = current.Message.Due!.Value;
        fixture.Replay(old);
        var journal = store.Journal.Values.ToArray();
        var rejected = CreationProtocol.TargetError(checkpoint.Row, old.Message);
        await Step(next, id);
        var changed = fixture.Checkpoint(b) != checkpoint;
        check(rejected == "WrongCreation" && !store.Messages.ContainsKey(oldKey) && !changed &&
            store.Messages.ContainsKey(nextKey) && store.Journal.Values.SequenceEqual(journal),
            $"{label} the due predecessor control is dropped without advancing the successor control (state changed {changed})");

        await Step(next, id);
        var advanced = fixture.Row(b);
        check(!store.Messages.ContainsKey(nextKey) && (expiry
                ? advanced.ParkDiscard is { Expired: true } drain && drain.Operation == current.Message.Id
                : advanced.ParkRecovery is { Cursor: not null } recover && recover.Control != nextKey),
            $"{label} the genuine successor control performs the first transition");

        async Task<InboxKey?> OpenAndWait(Worker worker, Contract.ActorRef actor, string operation)
        {
            fixture.WithholdPages = true;
            await fixture.Send(actor, operation, Contract.Kinds.Recover, Json.Write(new Contract.OperatorCommand(await fixture.Selection(actor))));
            await Step(worker, id);
            await Step(worker, id);
            return fixture.Row(actor).ParkRecovery?.Control;
        }
    }

    private static async Task PolicyAtDelivery(Action<bool, string> check)
    {
        var fixture = new Fixture { Window = TimeSpan.FromDays(30) };
        var first = fixture.Owner();
        await using var owner = await Activate(first);
        var actor = await fixture.Start(first, "policy-start");
        await fixture.Send(actor, "finish", "finish");
        await Step(first, fixture.Row(actor).Id);
        var ended = fixture.Row(actor);
        var selection = await fixture.Selection(actor);
        var day31 = fixture.Now + (long)TimeSpan.FromDays(31).TotalMilliseconds;
        var (offered, request) = fixture.Retention(actor, selection, day31);
        var repeat = fixture.Retention(actor, selection, day31).Admission;
        var reserved = fixture.Store.Messages.Values.Single(message => message.Message.Operator?.RetentionDelete == true).Message.Id;
        var outside = await fixture.Send(actor, reserved, Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(selection)));
        check(offered.Outcome == InMemoryStore.OperatorAdmission.Accepted &&
            repeat.Outcome == InMemoryStore.OperatorAdmission.Duplicate && outside.Outcome == Contract.AdmissionOutcome.Refused,
            "RD3 the host's exact retention request is idempotent and its ID is reserved against outside operators");

        await owner.DisposeAsync();
        fixture.Window = TimeSpan.FromDays(90);
        fixture.Now = day31;
        var resumed = fixture.Owner();
        await using var second = await Activate(resumed);
        await Step(resumed, ended.Id);
        check(request.Answer is CreationResult { Result: "NotEligible" } && fixture.Row(actor) == ended &&
            fixture.Store.Messages.Values.Single(message => message.Message.Id == reserved).Done,
            "RD3 a queued retention Delete uses the extended policy at execution and leaves the row unchanged");

        await second.DisposeAsync();
        fixture.Now += (long)TimeSpan.FromDays(60).TotalMilliseconds;
        var later = fixture.Owner();
        await using var third = await Activate(later);
        var resubmitted = fixture.Retention(actor, selection).Admission;
        await Step(later, ended.Id);
        check(resubmitted.Outcome == InMemoryStore.OperatorAdmission.Accepted &&
            fixture.Store.Instances[ended.Id].Retired &&
            fixture.Store.Journal.Values.Where(row => row.Instance == ended.Id).ToArray() is [{ Revision: -1 }],
            "RD3 the same terminal transition can be selected again after the policy window and retire once");
    }

    private static async Task DelayedAcrossReuse(Action<bool, string> check)
    {
        var fixture = new Fixture { Window = TimeSpan.Zero };
        var worker = fixture.Owner();
        await using var owner = await Activate(worker);
        var a = await fixture.Start(worker, "original", callerKey: "reused");
        await fixture.Send(a, "end-original", "finish");
        await Step(worker, fixture.Row(a).Id);
        var endedA = fixture.Row(a);
        var markA = fixture.Checkpoint(a).Terminal;
        var selection = await fixture.Selection(a);
        var (queued, request) = fixture.Retention(a, selection, fixture.Now + 200);
        if (!await RejectMarkerLoss(endedA, "retirement")) return;
        await fixture.Send(a, "manual-delete", Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(selection)));
        await Step(worker, endedA.Id);
        if (!fixture.Store.Instances[endedA.Id].Retired)
        {
            check(false, "RD1 the operator retires the selected life before the delayed Retention Delete becomes due");
            return;
        }

        fixture.Now += 101;
        await fixture.Store.SweepAsync();
        var marker = fixture.Store.Instances.GetValueOrDefault(endedA.Id);
        var retained = marker is { Retired: true } && marker.Creation == endedA.Creation;
        check(retained, "RD7b sweep retains the creation high-water mark past the reuse window");
        if (!retained || !await RejectMarkerLoss(marker!, "compaction")) return;

        var b = await fixture.Start(worker, "successor", callerKey: "reused");
        await fixture.Send(b, "end-successor", "finish");
        await Step(worker, fixture.Row(b).Id);
        var endedB = fixture.Row(b);
        var markB = fixture.Checkpoint(b).Terminal;
        var journal = fixture.Store.Journal.Values.ToArray();
        fixture.Now += 99;
        await Step(worker, endedB.Id);
        check(queued.Outcome == InMemoryStore.OperatorAdmission.Accepted && endedA.Id == endedB.Id &&
            endedB.Creation == endedA.Creation + 1 && markA is not null && markB is not null &&
            markA.Revision == markB.Revision && endedA.Status == endedB.Status &&
            request.Answer is CreationResult { Result: "WrongCreation" } && fixture.Row(b) == endedB &&
            fixture.Store.Journal.Values.SequenceEqual(journal),
            "RD1/RD2 a delayed Delete names the retired creation even when the successor has the same terminal revision and status");
        check(endedB.Creation > marker!.Creation && request.Answer is CreationResult { Result: "WrongCreation" } &&
            fixture.Row(b) == endedB, "RD7b the retained watermark advances the successor and the old Delete leaves it intact");

        async Task<bool> RejectMarkerLoss(InstanceRow before, string operation)
        {
            var fence = worker.Fences.Single();
            var drop = new TurnCommit(before.Id, null, before.Revision, [], [], [], [], [], "[]", Delete: true);
            var rejected = false;
            try
            {
                await fixture.Store.CommitAsync(new(0, fence.Epoch, fence.Position, Defaults.Lease, false, [drop], [], []));
            }
            catch (StoreException error) when (error.Failure == Failure.Definite)
            {
                rejected = true;
            }
            var intact = rejected && fixture.Store.Instances.GetValueOrDefault(before.Id) == before &&
                fixture.Store.Partitions[fence.Partition].Position == fence.Position;
            check(intact, $"RD7b the fenced store refuses {operation} that would erase the creation high-water mark");
            return intact;
        }
    }

    private static async Task DeleteIdentityDuringClear(Action<bool, string> check, bool afterAttachment)
    {
        var fixture = new Fixture { PollHorizon = TimeSpan.Zero };
        var store = fixture.Store;
        var worker = fixture.Owner();
        await using var owner = await Activate(worker);
        var original = await fixture.Start(worker, "first", callerKey: "same-key");
        var first = fixture.Row(original);
        await fixture.Send(original, "end-first", "finish");
        await Step(worker, first.Id);
        var originalSelection = await fixture.Selection(original);
        var due = fixture.Now + 200;
        var (admitted, delayed) = fixture.OperatorDeleteAt(original, originalSelection, due);
        await fixture.Send(original, "retire-first", Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(originalSelection)));
        await Step(worker, first.Id);
        fixture.Now += 101;
        await store.SweepAsync();

        var current = await fixture.Start(worker, "second", callerKey: "same-key");
        var running = fixture.Row(current);
        var recreated = running.Id == first.Id && running.Creation == first.Creation + 1;
        check(recreated && admitted.Outcome == InMemoryStore.OperatorAdmission.Accepted,
            "DT-1 the delayed request was admitted for the previous creation of the same key");
        if (!recreated) return;

        await fixture.Send(current, "park-second", "park");
        await Step(worker, running.Id);
        for (var index = 0; index <= 4 * InMemoryStore.ParkPageLimit; index++)
            await fixture.Send(current, $"retained-{index:D4}", "ordinary");
        await Step(worker, running.Id);
        await Step(worker, running.Id);
        var selection = await fixture.Selection(current);
        var payload = Json.Write(new Contract.OperatorCommand(selection));
        await fixture.Send(current, "terminate-second", Contract.Kinds.Terminate, payload);
        if (afterAttachment) await fixture.Send(current, "delete-second", Contract.Kinds.Delete, payload);
        for (var pass = 0; pass < 4 && !ReachedBoundary(); pass++) await Step(worker, running.Id);
        var staged = ReachedBoundary();
        check(staged, $"DT-1 the current termination is open {(afterAttachment ? "with" : "before")} a qualified Delete attachment");
        if (!staged) return;

        var before = fixture.Row(current);
        var journal = store.Journal.Values.ToArray();
        var foreign = await fixture.SendFromAnotherTenant(current, "other-tenant-delete", payload);
        check(foreign is { Outcome: Contract.AdmissionOutcome.Refused } && fixture.Row(current) == before,
            "DT-1 the foreign tenant receives a refusal without changing the current resolution");
        fixture.Now = due;
        await Step(worker, running.Id);
        check(delayed.Answer is CreationResult { Result: "WrongCreation" } && fixture.Row(current) == before &&
              store.Journal.Values.SequenceEqual(journal),
            "DT-1 the prior creation's pending Delete is refused without changing the current resolution");

        bool ReachedBoundary() => fixture.Row(current).ParkDiscard is { Kind: ParkClearKind.Terminate } drain &&
            (drain.Deletion is not null) == afterAttachment;
    }

    private static async Task UnresolvedPark(Action<bool, string> check)
    {
        var fixture = new Fixture { Window = TimeSpan.Zero };
        var worker = fixture.Owner();
        await using var owner = await Activate(worker);
        var actor = await fixture.Start(worker, "park-start");
        var id = fixture.Row(actor).Id;
        await fixture.Send(actor, "park", "park");
        await Step(worker, id);
        var initial = fixture.Checkpoint(actor);
        var selection = await fixture.Selection(actor);
        await fixture.Send(actor, "retained", "update");
        var refusals = 0;
        fixture.Store.Refuses = candidate => candidate.Id == id && ++refusals <= 2;
        await Step(worker, id);
        fixture.Store.Refuses = null;
        var reparking = fixture.Checkpoint(actor);
        var (queued, request) = fixture.Retention(actor, selection);
        await Step(worker, id);
        check(queued.Outcome == InMemoryStore.OperatorAdmission.Accepted &&
            reparking is { Park: not null, Row.Status: InstanceStatus.Faulted } &&
            reparking.Row.Revision > initial.Row.Revision && reparking.Terminal == initial.Terminal &&
            request.Answer is CreationResult { Result: "NotEligible" } &&
            fixture.Row(actor) == reparking.Row && fixture.Store.Messages.Values.Any(message =>
                message.Message.Id == "retained" && message.KeptBy == ParkKeepReason.Consumed),
            "RD8c a re-park keeps the exact mark but the unresolved Park remains ineligible even with a zero Faulted window");

        await fixture.Send(actor, "operator-delete", Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(selection)));
        for (var pass = 0; pass < 8 && !fixture.Store.Instances[id].Retired; pass++) await Step(worker, id);
        check(fixture.Store.Instances[id].Retired, "RD8c the same selection lets an operator initiate Delete without an age rule");
    }

    private static async Task RecoveryAfterSelection(Action<bool, string> check)
    {
        var fixture = new Fixture { Window = TimeSpan.FromSeconds(20) };
        var worker = fixture.Owner();
        await using var owner = await Activate(worker);
        var actor = await fixture.Start(worker, "transition-start");
        var id = fixture.Row(actor).Id;
        await fixture.Send(actor, "park", "park");
        await Step(worker, id);
        var parked = fixture.Checkpoint(actor);
        var selection = await fixture.Selection(actor);

        // Keep the queued request outside the first owner's poll horizon. Its reply stays with the successor.
        var due = fixture.Now + 11_000;
        var (queued, request) = fixture.OperatorDeleteAt(actor, selection, due);
        var resumed = await fixture.Send(actor, "recover", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(selection, Contract.RecoverMode.Resume)));
        for (var pass = 0; pass < 10 && fixture.Checkpoint(actor).Park is not null; pass++)
        {
            fixture.Now += 11;
            await Step(worker, id);
        }
        await fixture.Send(actor, "end", "finish");
        await Step(worker, id);
        var ended = fixture.Checkpoint(actor);
        var nextSelection = await fixture.Selection(actor);
        var established = queued.Outcome == InMemoryStore.OperatorAdmission.Accepted &&
            resumed.Outcome == Contract.AdmissionOutcome.Accepted && parked.Park is not null &&
            ended is { Park: null, Row.Status: InstanceStatus.Finished, Terminal: { } newer } &&
            newer.Revision > parked.Terminal?.Revision && newer.At == fixture.Now &&
            ended.Row.Id == parked.Row.Id && ended.Row.Creation == parked.Row.Creation &&
            fixture.Now < due && request.Answer is null && nextSelection != selection;
        check(established, "RD4 the queued Park Delete predates the same creation's later Finished mark");
        if (!established) return;

        await owner.DisposeAsync();
        fixture.Now = due;
        var successor = fixture.Owner();
        await using var replacement = await Activate(successor);
        await Step(successor, id);
        check(request.Answer is CreationResult { Result: "StaleSelection" } &&
              fixture.Row(actor) == ended.Row && fixture.Checkpoint(actor).Terminal == ended.Terminal &&
              fixture.Store.Messages.Values.Single(row => row.Message.Id == "rd4-deferred").Done,
            $"RD4 the successor refuses the old transition's Delete and preserves the later terminal mark (reply {request.Answer})");

        var early = fixture.Retention(actor, nextSelection);
        await Step(successor, id);
        check(early.Admission.Outcome == InMemoryStore.OperatorAdmission.Accepted &&
              early.Request.Answer is CreationResult { Result: "NotEligible" } && fixture.Row(actor) == ended.Row,
            "RD4 the newer Finished mark still has its own unexpired retention window");

        fixture.Now = ended.Terminal!.At + (long)fixture.Window!.Value.TotalMilliseconds + 1;
        var eligible = fixture.Retention(actor, nextSelection);
        await Step(successor, id);
        check(eligible.Admission.Outcome == InMemoryStore.OperatorAdmission.Accepted && fixture.Store.Instances[id].Retired &&
              fixture.Store.Journal.Values.Where(row => row.Instance == id).ToArray() is [{ Revision: -1 }],
            "RD4 retention retires the exact newer transition after its window closes");
    }

    private static async Task ExpiryStartsRetention(Action<bool, string> check)
    {
        var fixture = new Fixture(TimeSpan.FromMilliseconds(1)) { Window = TimeSpan.FromMilliseconds(20) };
        var worker = fixture.Owner();
        await using var owner = await Activate(worker);
        var actor = await fixture.Start(worker, "expiry-start");
        var id = fixture.Row(actor).Id;
        await fixture.Send(actor, "park-expiry", "park");
        await Step(worker, id);
        var parked = fixture.Checkpoint(actor);
        var oldSelection = await fixture.Selection(actor);
        var kept = fixture.Store.Messages.Values.Single(message => message.KeptBy == ParkKeepReason.Consumed);
        var retryHandle = fixture.KeptHandle(actor, kept.Message.InboxKey);

        fixture.Now += 2;
        for (var pass = 0; pass < 8 && fixture.Checkpoint(actor).Park is not null; pass++) await Step(worker, id);
        var expired = fixture.Checkpoint(actor);
        check(expired is { Park: null, Row.Status: InstanceStatus.Faulted, Terminal: { } mark } &&
            mark.At == fixture.Now && mark != parked.Terminal &&
            fixture.Store.Messages.Values.All(message => message.KeptBy is null),
            "RD4b expiry clears Park and writes the new Faulted mark");
        if (expired.Park is not null) return;

        foreach (var mode in Enum.GetValues<Contract.RecoverMode>())
        {
            var handles = mode == Contract.RecoverMode.Retry ? new[] { retryHandle } : null;
            var rejected = await fixture.Send(actor, "recover-" + mode, Contract.Kinds.Recover,
                Json.Write(new Contract.OperatorCommand(oldSelection, mode, handles)));
            check(rejected is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection },
                $"RD4b expiry invalidates the old Park selection for {mode}");
        }

        fixture.Now += 19;
        var selection = await fixture.Selection(actor);
        var (early, request) = fixture.Retention(actor, selection);
        await Step(worker, id);
        check(early.Outcome == InMemoryStore.OperatorAdmission.Accepted &&
            request.Answer is CreationResult { Result: "NotEligible" } && fixture.Row(actor) == expired.Row,
            "RD4b the policy measures the Faulted age from the expiry's mark even though the old Park mark has elapsed");

        fixture.Now += 101;
        var eligible = fixture.Retention(actor, selection).Admission;
        await Step(worker, id);
        check(eligible.Outcome == InMemoryStore.OperatorAdmission.Accepted && fixture.Store.Instances[id].Retired,
            "RD4b the expired Park can be retired after the new Faulted window");
    }

    private static async Task<IScopeActivation> Activate(Worker worker) => (await new DurableHost([worker]).ActivateAsync(0))!;

    private static async Task Step(Worker worker, string actor)
    {
        await worker.PollAsync();
        await worker.LoadAsync();
        await worker.FlushAsync();
        worker.RunTurn(actor);
        await worker.FlushAsync();
    }

    private sealed class Fixture : Contract.IActorType
    {
        private const long Initial = 1_702_710_000_000;
        private const string Node = "retention-fixture";
        private readonly Contract.BoundScope scope = new("retention-check");
        private readonly Requests requests = new(Node);
        private readonly InMemoryIngress ingress;
        private int nextOwner;

        internal Fixture(TimeSpan? parkedWindow = null)
        {
            Store = new InMemoryStore(1, () => Now)
            {
                IngressDeduplicationWindow = TimeSpan.FromMilliseconds(100), ParkedRetentionWindow = parkedWindow
            };
            ingress = new(Store, requests, Node, (_, kind) => kind is Contract.Kinds.Delete or Contract.Kinds.Recover or Contract.Kinds.Terminate);
        }

        internal long Now { get; set; } = Initial;
        internal TimeSpan? Window { get; set; }
        internal TimeSpan PollHorizon { get; init; } = new WorkerOptions().Horizon;
        internal bool WithholdPages { get; set; }
        internal InMemoryStore Store { get; }
        public string Type => "retention-probe";

        internal Worker Owner() => new($"retention-owner-{++nextOwner}", 0, Store,
            new ActorAdapter(Store, this, retentionWindow: _ => Window),
            new WorkerOptions { Monotonic = () => Now - Initial, Transport = new Replies(requests), Horizon = PollHorizon });

        internal void Replay(InMemoryStore.MessageRow old) =>
            Store.Messages.Add(old.Message.InboxKey, new(old.Message, old.Seq, 0) { Scoped = true, Tenant = scope.Tenant });

        internal async Task<Contract.ActorRef> Start(Worker worker, string operation, string? callerKey = null)
        {
            var spec = new Contract.StartSpec(Type, "definition");
            Contract.Target target = callerKey is null ? new Contract.Target.Start(spec) : new Contract.Target.Key(callerKey, spec);
            var admitted = await ingress.SendAsync(scope, target, operation, Contract.Kinds.Start);
            var actor = admitted.Actor!.Value;
            await Step(worker, Store.FoundationTarget(scope.Tenant, actor.Id)!.Id);
            return actor;
        }

        internal async Task<string> Selection(Contract.ActorRef actor) =>
            (await new ActorList(Store, 10).GetAsync(scope, actor))!.Selection!;
        internal InMemoryStore.ActorCheckpoint Checkpoint(Contract.ActorRef actor) => Store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!;
        internal InstanceRow Row(Contract.ActorRef actor) => Checkpoint(actor).Row;
        internal string KeptHandle(Contract.ActorRef actor, InboxKey key) => Store.SealFoundationKeptHandle(scope.Tenant, actor.Id, key)!;

        internal ValueTask<Contract.Admission> Send(Contract.ActorRef actor, string id, string kind, string? payload = null) =>
            ingress.SendAsync(scope, new Contract.Target.Ref(actor), id, kind, payload);

        internal ValueTask<Contract.Admission> SendFromAnotherTenant(Contract.ActorRef actor, string id, string payload) =>
            ingress.SendAsync(new Contract.BoundScope("other-tenant"), new Contract.Target.Ref(actor), id, Contract.Kinds.Delete, payload);

        internal ((InMemoryStore.OperatorAdmission Outcome, string? Reference) Admission, Request Request)
            Retention(Contract.ActorRef actor, string selection, long? due = null)
        {
            var request = requests.OpenBound("retention-selection", (due ?? Now) + 20_000, scope.Tenant, null);
            return (Store.AdmitRetentionDelete(scope.Tenant, actor.Id, selection, due, request.ReplyAddress(Node)), request);
        }

        internal ((InMemoryStore.OperatorAdmission Outcome, string? Reference) Admission, Request Request)
            OperatorDeleteAt(Contract.ActorRef actor, string selection, long due)
        {
            var request = requests.OpenBound("rd4-deferred", due + 20_000, scope.Tenant, null);
            return (Store.AdmitFoundationOperator(scope.Tenant, actor.Id, null, "rd4-deferred", Kinds.Delete,
                null, due, null, selection, request.ReplyAddress(Node)), request);
        }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Inbox.Any(message => message.Kind == "park")) throw new InvalidOperationException("Park the actor.");
            if (turn.Inbox.Any(message => message.Kind == "finish")) turn.End(Contract.ActorStatus.Finished);
            turn.State = "saved";
            return default;
        }

        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public bool Withholds(Contract.ActorHeader header) => WithholdPages && header.Status == Contract.ActorStatus.Running;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Replies(Requests requests) : IReplyTransport
    {
        public void Send(string node, Reply reply) => requests.Receive(reply);
    }
}
