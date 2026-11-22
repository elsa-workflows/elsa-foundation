using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class RetentionDeleteChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await PolicyAtDelivery(check);
        await DelayedAcrossReuse(check);
        await UnresolvedPark(check);
        await ExpiryStartsRetention(check);
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
        await fixture.Send(a, "manual-delete", Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(selection)));
        await Step(worker, endedA.Id);
        if (!fixture.Store.Instances[endedA.Id].Retired)
        {
            check(false, "RD1 the operator retires the selected life before the delayed Retention Delete becomes due");
            return;
        }

        fixture.Now += 101;
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
            ingress = new(Store, requests, Node, (_, kind) => kind is Contract.Kinds.Delete or Contract.Kinds.Recover);
        }

        internal long Now { get; set; } = Initial;
        internal TimeSpan? Window { get; set; }
        internal InMemoryStore Store { get; }
        public string Type => "retention-probe";

        internal Worker Owner() => new($"retention-owner-{++nextOwner}", 0, Store,
            new ActorAdapter(Store, this, retentionWindow: _ => Window),
            new WorkerOptions { Monotonic = () => Now - Initial, Transport = new Replies(requests) });

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

        internal ((InMemoryStore.OperatorAdmission Outcome, string? Reference) Admission, Request Request)
            Retention(Contract.ActorRef actor, string selection, long? due = null)
        {
            var request = requests.OpenBound("retention-selection", (due ?? Now) + 20_000, scope.Tenant, null);
            return (Store.AdmitRetentionDelete(scope.Tenant, actor.Id, selection, due, request.ReplyAddress(Node)), request);
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
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Replies(Requests requests) : IReplyTransport
    {
        public void Send(string node, Reply reply) => requests.Receive(reply);
    }
}
