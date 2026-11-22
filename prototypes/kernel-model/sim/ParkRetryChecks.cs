using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkRetryChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await CheckScopeAndAtomicity(check);
        await CheckBoundsAndInterruption(check);
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
            var scope = new Contract.BoundScope(tenant);
            var admission = await _ingress.SendAsync(scope, new Contract.Target.Start(new(Type, "ready", null)), name, Contract.Kinds.Start);
            var target = new Contract.Target.Ref(admission.Actor!.Value);
            await Finish(owner);
            return await ParkAsync(owner, scope, target, "park-" + name);
        }

        internal async Task<Parked?> ParkAsync(IScopeActivation owner, Contract.BoundScope scope, Contract.Target.Ref target, string id)
        {
            await _ingress.SendAsync(scope, target, id, "fault");
            await Finish(owner);
            var read = Store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor;
            return read is { Park: { } park }
                ? new(scope, target, read.Row, park, Store.SealFoundationSelection(scope.Tenant, read))
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

        internal ValueTask<Contract.Admission> Retry(Parked parked, string operation, IReadOnlyList<string> handles) =>
            _ingress.SendAsync(parked.Scope, parked.Target, operation, Contract.Kinds.Recover,
                Json.Write(new Contract.OperatorCommand(parked.Selection, Contract.RecoverMode.Retry, handles)));

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
            if (turn.Inbox.Any(message => message.Kind == "fault"))
                throw new InvalidOperationException("original Park");
            if (FaultNextTrip && turn.Inbox.Any(message => message.Kind == "trip"))
            {
                FaultNextTrip = false;
                throw new InvalidOperationException("interrupted retry");
            }
            Delivered.AddRange(turn.Inbox.Where(message => message.Kind == "observation").Select(message => message.Payload!));
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
