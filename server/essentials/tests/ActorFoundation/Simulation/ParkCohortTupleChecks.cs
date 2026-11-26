using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkCohortTupleChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await TiedCohort(poisonFirst: false, check);
        await TiedCohort(poisonFirst: true, check);
        await TiedCohort(poisonFirst: false, check, narrow: true);
        await TiedCohort(poisonFirst: true, check, narrow: true);
        await TiedOrdinaryPoison(check);
        await InterruptedRelease(check, endBeforeOlderRelease: false);
        await InterruptedRelease(check, endBeforeOlderRelease: true);
    }

    private static async Task InterruptedRelease(Action<bool, string> check, bool endBeforeOlderRelease)
    {
        const long start = 1_702_610_003_000;
        const int maxAttempts = 3;
        var now = start;
        var store = new InMemoryStore(1, () => now);
        var probe = new Probe { HoldHere = false };
        var scope = ContractScopes.Bind("interrupted-suffix");
        var ingress = new InMemoryIngress(store, new Requests("interrupted-suffix"), "interrupted-suffix",
            (_, kind) => kind == Contract.Kinds.Recover);
        var reads = new ActorList(store, 5);
        var endedWithOlderPending = 0;
        var settledOlder = 0;
        var suffixKeptAtEnd = false;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed || !endBeforeOlderRelease) return;
            foreach (var turn in batch.Turns)
            {
                if (turn is { Row: { Status: InstanceStatus.Finished, ParkRecovery: not null } ended, FoundationRecoveryPage: null })
                {
                    endedWithOlderPending = store.Messages.Values.Count(row => !row.Done &&
                        row.ReleasedBy is { } release && release.Generation < ended.ParkResolutionGeneration);
                    suffixKeptAtEnd = Delivery("suffix").KeptBy == ParkKeepReason.Arrived;
                }
                if (turn is { Row: { } checkpoint, FoundationRecoveryPage: { TerminalInputs: true } page })
                    settledOlder += page.Rows.Count(row => row.ReleasedBy is { } tag && tag.Generation < checkpoint.ParkResolutionGeneration);
            }
        });
        await using var first = await Activate(Owner("before-interruption"));
        var birth = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "")), "start", Contract.Kinds.Start);
        var actor = birth.Actor!.Value;
        var target = new Contract.Target.Ref(actor);
        await Pump(first);
        await ingress.SendAsync(scope, target, "park", "fault");
        await Pump(first);
        var count = InMemoryStore.ParkPageLimit - 2;
        var expected = Enumerable.Range(0, count).Select(i => $"restored-{i:D3}").ToArray();
        await ingress.SendAsync(scope, target, "trip", "fault");
        foreach (var id in expected)
            await ingress.SendAsync(scope, target, id, "hold", id);
        await ingress.SendAsync(scope, target, "suffix", "finish-recovery");
        for (var pass = 0; pass < InMemoryStore.ParkPageLimit + 10 && Delivery("suffix").KeptBy is null; pass++)
            await Pump(first, passes: 1);

        await Open("release-first");
        for (var pass = 0; pass < 20 && Row().ParkRecovery?.Cursor is null; pass++)
            await Pump(first, passes: 1);
        var released = Row();
        var releasedBy = Delivery("trip").ReleasedBy;
        var releasedPage = released.ParkRecovery is { Cursor: not null } && releasedBy is not null &&
            expected.All(id => Delivery(id) is { Done: false, KeptBy: null } row && row.ReleasedBy == releasedBy) &&
            Delivery("suffix") is { Done: true, KeptBy: ParkKeepReason.Arrived, ReleasedBy: null };
        check(releasedPage, "E-6 first recovery releases one page and retains the end input in the suffix");
        if (!releasedPage) return;
        await first.DisposeAsync();

        Delivery("trip").Attempts = maxAttempts;
        now++;
        await using var successor = await Activate(Owner("after-interruption"));
        for (var pass = 0; pass < 20 && Row().Status != InstanceStatus.Faulted; pass++)
            await Pump(successor, passes: 1);
        var reparks = Row() is { Status: InstanceStatus.Faulted, ParkRecovery: null } &&
            Delivery("trip") is { Done: true, KeptBy: ParkKeepReason.Consumed } &&
            expected.All(id => Delivery(id) is { Done: false, KeptBy: null } row && row.ReleasedBy == releasedBy);
        check(reparks, "E-6 the exhausted first input re-parks before the older cohort reaches the actor");
        if (!reparks) return;

        // A cold one-row poll keeps the earlier-position finisher and opens recovery before the older releases are polled.
        if (endBeforeOlderRelease)
            await successor.DisposeAsync();
        await using var parkedOwner = endBeforeOlderRelease ? await Activate(Owner("parked-before-end", pollLimit: 1)) : null;
        if (endBeforeOlderRelease)
        {
            // The admitted finisher spends the first page's credit and sorts before the prior release.
            await ingress.SendAsync(scope, target, "early-end", "finish-recovery", new string('x', ParkRecoveryCredit.LimitBytes));
            Delivery("early-end").Seq = Delivery(expected[0]).Seq - 2;
        }

        await Open("release-again");
        Delivery("release-again").Seq = Delivery(expected[0]).Seq - 1;
        if (endBeforeOlderRelease)
        {
            for (var pass = 0; pass < 12 && Delivery("early-end").KeptBy is null; pass++)
                await Pump(parkedOwner!, passes: 1);
            var kept = Delivery("early-end").KeptBy == ParkKeepReason.Arrived;
            check(kept, "E-6(d) the early finisher is durably kept while the actor is parked again");
            if (!kept) return;
        }

        for (var pass = 0; pass < 3 * count && (Row().Status != InstanceStatus.Finished || Row().ParkRecovery is not null); pass++)
        {
            now += 11;
            await Pump(parkedOwner ?? successor, passes: 1);
        }
        var final = Row();
        var applied = Json.Read<List<string>>(final.State);
        if (endBeforeOlderRelease)
        {
            check(endedWithOlderPending == count && suffixKeptAtEnd,
                $"E-6(d) the actor ends during recovery while every older release is still pending ({endedWithOlderPending})");
            check(final is { Status: InstanceStatus.Finished, ParkRecovery: null, EndedDuringParkRecovery: true } &&
                applied.SequenceEqual(["early-end"]) && settledOlder == count &&
                store.Messages.Values.All(row => row is { Done: true, KeptBy: null } && row.Message.Kind != Kinds.Drain),
                $"E-6(d) terminal recovery settles the older release and kept suffix without another model delivery ({settledOlder} older cleared)");
            return;
        }
        check(final is { Status: InstanceStatus.Finished, ParkRecovery: null } &&
            final.ParkResolutionGeneration > released.ParkResolutionGeneration &&
            applied.SequenceEqual(expected.Append("suffix")) &&
            store.Messages.Values.All(row => row.KeptBy is null && row.Message.Kind != Kinds.Drain),
            $"E-6 the later generation applies the older release before the end suffix and completes (applied {applied.Count})");

        Worker Owner(string node, int pollLimit = 500) => new(node, 0, wire, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - start, MaxAttempts = maxAttempts, PassTurnBudget = 1, PollLimit = pollLimit });
        static async Task<IScopeActivation> Activate(Worker worker) => (await new DurableHost([worker]).ActivateAsync(0))!;
        InstanceRow Row() => store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row;
        InMemoryStore.MessageRow Delivery(string id) => store.Messages.Values.Single(row => row.Message.Id == id);
        async Task Open(string id)
        {
            var selection = (await reads.GetAsync(scope, actor))!.Selection;
            var result = await ingress.SendAsync(scope, target, id, Contract.Kinds.Recover,
                Json.Write(new Contract.OperatorCommand(selection, Contract.RecoverMode.Resume)));
            if (result.Outcome != Contract.AdmissionOutcome.Accepted)
                throw new InvalidOperationException($"The recovery fixture could not admit {id}.");
        }
    }

    private static async Task TiedCohort(bool poisonFirst, Action<bool, string> check, bool narrow = false)
    {
        const long initial = 1_702_610_001_000;
        const int maxAttempts = 3;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var probe = new Probe();
        var scope = ContractScopes.Bind("tied-cohort");
        var ingress = new InMemoryIngress(store, new Requests("tied-cohort"), "tied-cohort", (_, kind) => kind == Contract.Kinds.Recover);
        var reads = new ActorList(store, 5);
        var flights = new List<InboxKey[]>();
        int? flightsBeforePark = null;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed) return;
            if (batch.Trial.Count != 0) flights.Add(batch.Trial.ToArray());
            if (batch.Turns.Any(turn => turn.OpensPark)) flightsBeforePark = flights.Count;
        });
        var old = Worker("tie-original");
        await using var first = await Activate(old);
        var actor = (await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "")), "start", Contract.Kinds.Start)).Actor!.Value;
        var target = new Contract.Target.Ref(actor);
        await Pump(first);
        await ingress.SendAsync(scope, target, "park", "fault");
        await Pump(first);
        var insertionOrder = narrow ? new[] { "second", "first" } : ["first", "second"];
        foreach (var value in insertionOrder)
            await ingress.SendAsync(scope, target, value + "-input", "hold", value);
        await Pump(first);
        var park = (await reads.GetAsync(scope, actor))!.Selection!;
        await ingress.SendAsync(scope, target, "open", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(park, Contract.RecoverMode.Resume)));
        for (var pass = 0; pass < 8; pass++)
        {
            now += 20;
            await Pump(first);
        }

        var current = Row();
        var epoch = old.Fences.Single().Epoch;
        var cohort = store.Messages.Values.Where(row => row.Message.Kind == "hold" && row.ReleasedBy is not null && !row.Done)
            .OrderBy(row => row.Message.InboxKey.Id, StringComparer.Ordinal).ToArray();
        var complete = current is { Status: InstanceStatus.Running, ParkCohort: not null, ParkRecovery: null } &&
            cohort is [{ Held: true } one, { Held: true } two] && one.Seen == epoch && two.Seen == epoch &&
            one.ReleasedBy == current.ParkCohort && two.ReleasedBy == current.ParkCohort &&
            one.Message.InboxKey.Id != two.Message.InboxKey.Id;
        check(complete, "TT-1 both real model holds remain in the durable cohort after Complete");
        if (!complete) return;
        await first.DisposeAsync();

        // Model independent allocators producing a tie and one input having already exhausted its trials.
        var poison = cohort[poisonFirst ? 0 : 1];
        var suspect = cohort[poisonFirst ? 1 : 0];
        var expectedInput = suspect.Message.Payload;
        poison.Seq = suspect.Seq = 42;
        poison.Attempts = maxAttempts;
        probe.HoldHere = false;
        flights.Clear();
        flightsBeforePark = null;
        now++;
        var successor = Worker("tie-successor", narrow ? 1 : 500);
        await using var active = await Activate(successor);
        await Pump(active);
        var order = Json.Read<List<string>>(Row().State);
        if (poisonFirst)
        {
            check(Row().Status == InstanceStatus.Faulted && order.Count == 0 && flightsBeforePark == 0 &&
                  suspect.KeptBy == ParkKeepReason.Arrived && poison.KeptBy == ParkKeepReason.Consumed,
                "TT-1 the earlier full tuple parks first and retains the later suspect for recovery" + (narrow ? " with a one-row poll" : ""));
        }
        else
        {
            check(Row().Status == InstanceStatus.Faulted && order is [var tried] && tried == expectedInput &&
                  flightsBeforePark == 1 && flights.Count == 1 && flights[0].SequenceEqual([suspect.Message.InboxKey]) &&
                  suspect is { Done: true, KeptBy: null } && poison.KeptBy == ParkKeepReason.Consumed,
                "TT-1 the earlier full tuple takes the sole trial before the equal-sequence poison parks the actor" + (narrow ? " with a one-row poll" : ""));
        }

        Worker Worker(string node, int pollLimit = 500) => new(node, 0, wire, new ActorAdapter(store, probe), new WorkerOptions
        {
            Monotonic = () => now - initial, MaxAttempts = maxAttempts, TrialGroup = 1, PollLimit = pollLimit
        });
        async Task<IScopeActivation> Activate(Worker worker) => (await new DurableHost([worker]).ActivateAsync(0))!;
        InstanceRow Row() => store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!.Row;
    }

    private static async Task TiedOrdinaryPoison(Action<bool, string> check)
    {
        const long now = 1_702_610_002_000;
        const int maxAttempts = 3;
        var store = new InMemoryStore(1, () => now);
        var probe = new Probe { HoldHere = false };
        var scope = ContractScopes.Bind("ordinary-tied-poison");
        var ingress = new InMemoryIngress(store, new Requests("ordinary-tied-poison"), "ordinary-tied-poison");
        var scheduler = new HeldScheduler();
        var trials = new List<InboxKey[]>();
        var atPark = -1;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed) return;
            if (batch.Trial.Count != 0) trials.Add(batch.Trial.ToArray());
            if (batch.Turns.Any(turn => turn.OpensPark)) atPark = trials.Count;
        });
        var old = Owner("before-ordinary-tie");
        await using var first = await Activate(old);
        var birth = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "")), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(birth.Actor!.Value);
        await Pump(first);
        await ingress.SendAsync(scope, target, "arm", "arm");
        await Pump(first);
        var actor = store.FoundationTarget(scope.Tenant, target.Actor.Id)!.Id;
        var row = store.Instances[actor];
        var job = store.Messages[InboxKey.Ticket(actor, "exhausted", row.Creation)];
        await ingress.SendAsync(scope, target, "ordinary", "hold", "must wait");
        var suspect = store.Messages.Values.Single(message => message.Message.Id == "ordinary");
        // Represent two independent clocks allocating one sequence before the successor takes.
        job.Attempts = maxAttempts;
        job.Seq = suspect.Seq = 42;
        suspect.Seen = old.Fences.Single().Epoch;
        check(row is { Status: InstanceStatus.Running, ParkRecovery: null, ParkCohort: null } &&
            job.ReleasedBy is null && suspect.ReleasedBy is null && probe.JobEntries == 0,
            "TT-1 ordinary control starts with a tied older-owner suspect and exhausted ticket outside any cohort");
        await first.DisposeAsync();
        scheduler.RunAll();

        var cold = Owner("after-ordinary-tie");
        await using var successor = await Activate(cold);
        await Pump(successor);
        var terminal = store.Instances[actor];
        check(atPark == 0 && terminal is { Status: InstanceStatus.Faulted, ParkRecovery: null, ParkCohort: null } &&
            terminal.Fault?.Contains(job.Message.Id, StringComparison.Ordinal) == true &&
            Json.Read<List<string>>(terminal.State).Count == 0 && probe.JobEntries == 0 &&
            job.KeptBy == ParkKeepReason.Consumed && suspect.KeptBy == ParkKeepReason.Arrived,
            "TT-1 outside a cohort the equal-sequence job poisons first and the suspect never enters the running actor");

        Worker Owner(string node) => new(node, 0, wire, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => 0, MaxAttempts = maxAttempts, JobScheduler = scheduler });
        static async Task<IScopeActivation> Activate(Worker worker) => (await new DurableHost([worker]).ActivateAsync(0))!;
    }

    private static async Task Pump(IScopeActivation owner, int passes = 24)
    {
        for (var pass = 0; pass < passes; pass++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "tied-cohort";
        internal bool HoldHere { get; set; } = true;
        internal int JobEntries { get; private set; }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var order = turn.State is null ? [] : Json.Read<List<string>>(turn.State);
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == "fault") throw new InvalidOperationException("Open the Park fixture.");
                if (message.Kind == "finish-recovery")
                {
                    order.Add(message.Id);
                    turn.End(Contract.ActorStatus.Finished);
                }
                if (message.Kind == "arm") turn.StartJob("exhausted");
                if (message.Kind != "hold" || message.Payload is not { } value) continue;
                if (HoldHere) turn.Hold(message);
                else order.Add(value);
            }
            turn.State = Json.Write(order);
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            JobEntries++;
            return new((object?)null);
        }
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
