using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkCachedReleaseChecks
{
    private enum CachedInput { Pending, Held, PollInFlight, LostBeforeCommit, LostAfterCommit, TakeAfterCommit }

    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var schedule in Enum.GetValues<CachedInput>())
            await Run((ok, name) => check(ok, $"{(ChangesEpoch(schedule) ? "E-10" : "E-9")} {name} ({schedule})"), schedule);
    }

    private static bool ChangesEpoch(CachedInput schedule) =>
        schedule is CachedInput.LostBeforeCommit or CachedInput.LostAfterCommit or CachedInput.TakeAfterCommit;

    private static async Task Run(Action<bool, string> check, CachedInput schedule)
    {
        const long start = 1_702_610_004_000;
        const int attempts = 3;
        const int metadataBytes = 24 * 1_024;
        var now = start;
        var store = new InMemoryStore(1, () => now);
        var held = schedule == CachedInput.Held;
        var overlapping = schedule == CachedInput.PollInFlight;
        var eventInterruption = held || overlapping;
        var probe = new Probe(held, eventInterruption);
        var polls = new DelayedCommit(store);
        var failCatchup = false;
        var losePage = false;
        var retagWrites = 0;
        var polledTags = new List<ParkRelease?>();
        var wire = new CountingStore(polls, () => { }, (batch, landed) =>
        {
            if (batch.Turns.Any(turn => turn.FoundationRecoveryPage?.Rows.Any(row => row.ReleasedBy is not null) == true))
            {
                if (landed) retagWrites++;
                if (losePage && landed == (schedule == CachedInput.LostAfterCommit))
                {
                    losePage = false;
                    throw new StoreException(Failure.Unknown, "The second recovery page returned no answer.");
                }
            }
            if (!landed && failCatchup && batch.Turns is [{ FoundationCatchUpStep: not null }])
                throw new StoreException(Failure.Definite, "The event catch-up step could not commit.");
        });
        var scope = ContractScopes.Bind("cached-release");
        var ingress = new InMemoryIngress(store, new Requests("cached-release"), "cached-release",
            (_, kind) => kind == Contract.Kinds.Recover);
        var reads = new ActorList(store, 8);
        Worker NewOwner(string name) => new(name, 0, wire, new ActorAdapter(store, probe),
            new WorkerOptions
            {
                Monotonic = () => now - start, MaxAttempts = attempts,
                OnObservedDelivery = row => { if (row.Message.Id == "cached") polledTags.Add(row.ReleasedBy); }
            });
        var worker = NewOwner("cached-release");
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var epoch = worker.Fences.Single().Epoch;
        var birth = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "actor")), "birth", Contract.Kinds.Start);
        var actor = birth.Actor!.Value;
        var id = store.FoundationTarget(scope.Tenant, actor.Id)!.Id;
        for (var pass = 0; pass < 8 && !store.Instances.ContainsKey(id); pass++) await Pump();
        if (!store.Instances.ContainsKey(id))
        {
            check(false, "the start must commit before the first fault");
            return;
        }
        var target = new Contract.Target.Ref(actor);
        await ingress.SendAsync(scope, target, "initial", "fault");
        for (var pass = 0; pass < 6 && Row().Status != InstanceStatus.Faulted; pass++)
            await Pump();
        if (Row().Status != InstanceStatus.Faulted)
        {
            check(false, "the initial Park must land before admitting the cohort");
            return;
        }
        // Admit Resume while its Park selection is current. Its lower sequence lets the same
        // owner open the next drain before it handles the pending row left by the interruption.
        var secondDue = now + 1_000;
        var park = (await reads.GetAsync(scope, actor))!.Selection;
        var queued = await ingress.SendAsync(scope, target, "second-recovery", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(park, Contract.RecoverMode.Resume)), new Contract.AddOptions(DueAt: secondDue));
        if (queued.Outcome != Contract.AdmissionOutcome.Accepted)
            throw new InvalidOperationException("The deferred recovery was refused.");
        await ingress.SendAsync(scope, target, "trip", "trip");
        await ingress.SendAsync(scope, target, "cached", "apply", new string('x', ParkRecoveryCredit.LimitBytes - 2 * metadataBytes - 16));
        await ingress.SendAsync(scope, target, "suffix", "finish", new string('x', metadataBytes));
        for (var pass = 0; pass < 5 && Input("suffix").KeptBy is null; pass++)
            await Pump();
        if (new[] { "trip", "cached", "suffix" }.Any(name => Input(name).KeptBy != ParkKeepReason.Arrived))
        {
            check(false, $"every test input must be kept as an arrival " +
                $"(trip {Input("trip").KeptBy}; cached {Input("cached").KeptBy}; suffix {Input("suffix").KeptBy})");
            return;
        }

        await Open("first-recovery");
        for (var pass = 0; pass < 5 && Row().ParkRecovery?.Cursor is null; pass++)
            await Step();
        var stale = Input("cached");
        var stageOne = stale.ReleasedBy;
        var staged = Row().ParkRecovery is { Cursor: not null } && stageOne is not null &&
            stale is { Done: false, KeptBy: null, Seen: 0 } &&
            Input("trip").ReleasedBy == stageOne &&
            Input("suffix") is { Done: true, KeptBy: ParkKeepReason.Arrived };
        check(staged, $"the first page releases the trip and cached input while the suffix stays kept " +
            $"(status {Row().Status}; cursor {Row().ParkRecovery?.Cursor}; tag {stageOne}; " +
            $"trip {Input("trip").ReleasedBy}; Seen {stale.Seen}; suffix {Input("suffix").KeptBy})");
        if (!staged) return;

        // Start the trip at the persisted retry limit before this owner reads the released row.
        if (!eventInterruption) Input("trip").Attempts = attempts;
        if (!overlapping)
        {
            await PollAndMark();
            var marked = stale.Seen == epoch && probe.Order.Count == 0;
            check(marked, "the same owner has marked the older release but has yet to deliver it");
            if (!marked) return;
        }
        if (held)
        {
            for (var pass = 0; pass < 8 && !stale.Held; pass++) await Step();
            var actuallyHeld = stale.Held && worker.RecoveryHandedOff(stale.Message.InboxKey) &&
                probe.SoloOffers == 1 && Row().ParkRecovery is not null;
            check(actuallyHeld, $"the model hands the cached delivery back before interruption (held {stale.Held}; offers {probe.SoloOffers})");
            if (!actuallyHeld) return;
        }
        if (eventInterruption)
        {
            failCatchup = true;
            for (var pass = 0; pass < 12 && Row().Status == InstanceStatus.Running; pass++)
            {
                await worker.MeetAsync();
                await worker.FlushAsync();
            }
            failCatchup = false;
        }
        for (var pass = 0; pass < 8 && Row().Status == InstanceStatus.Running; pass++)
            await Step();
        var faultKept = eventInterruption
            ? store.Messages.Values.Any(row => row.Message.ParkCatchUp is not null && row.KeptBy == ParkKeepReason.Consumed)
            : Input("trip").KeptBy == ParkKeepReason.Consumed;
        var interrupted = faultKept && Row() is { Status: InstanceStatus.Faulted, ParkRecovery: null } &&
            stale is { Done: false, KeptBy: null } && stale.ReleasedBy == stageOne && probe.Order.Count == 0;
        check(interrupted, "the interruption re-parks the actor while the previously released input stays pending");
        if (!interrupted) return;

        now = secondDue;
        if (overlapping)
        {
            // The accepted operator command and its next local page can be handed over
            // without a poll. Hold the real store answer across that page's commit.
            worker.RunTurn(id);
            await worker.FlushAsync();
            worker.RunTurn(id);
            var oldSnapshot = false;
            polls.HoldPollReply = async answer =>
            {
                oldSnapshot = answer.Messages.Any(row => row.Message.InboxKey == stale.Message.InboxKey && row.ReleasedBy == stageOne);
                await worker.FlushAsync();
            };
            await worker.PollAsync();
            polls.HoldPollReply = null;
            check(oldSnapshot, "the overlapping poll read the previous generation before the page committed");
            if (!oldSnapshot) return;
        }
        else
        {
            await Step();
            losePage = schedule is CachedInput.LostBeforeCommit or CachedInput.LostAfterCommit;
            await Step();
        }

        await using var replacement = schedule == CachedInput.TakeAfterCommit ? NewOwner("cached-successor") : null;
        var expectedEpoch = epoch;
        if (ChangesEpoch(schedule))
        {
            var pageLanded = schedule != CachedInput.LostBeforeCommit;
            var activeGeneration = Row().ParkResolutionGeneration;
            var oldOwnerReset = !worker.Owns;
            var boundary = Row().ParkRecovery is { } recovery && (recovery.Cursor is not null) == pageLanded &&
                (stale.ReleasedBy?.Generation == activeGeneration) == pageLanded &&
                stale.Seen == (pageLanded ? 0 : epoch) && !losePage && retagWrites == (pageLanded ? 1 : 0);
            check(boundary && oldOwnerReset == (replacement is null),
                $"the durable cursor, release and Seen agree at the page boundary (landed {pageLanded}, reset {oldOwnerReset})");
            if (!boundary || oldOwnerReset != (replacement is null)) return;
            if (replacement is not null)
            {
                await owner.DisposeAsync();
                worker = replacement;
            }
            polledTags.Clear();
            await worker.TakeAsync();
            if (!worker.Owns)
            {
                check(false, "the next take must acquire the partition");
                return;
            }
            expectedEpoch = worker.Fences.Single().Epoch;
            await worker.PollAsync();
            var expectedTag = pageLanded ? new ParkRelease(Row().Creation, activeGeneration) : stageOne;
            check(expectedEpoch > epoch && polledTags.SequenceEqual([expectedTag]),
                "the active owner polls the exact release generation that survived the page boundary");
            if (!pageLanded)
                for (var pass = 0; pass < 12 && retagWrites == 0; pass++) await Step();
        }
        var retagged = Row().ParkRecovery is { Gate: { } gate } && gate == stale.Message.InboxKey &&
            stageOne is { } previous && stale.ReleasedBy is { } current &&
            current.Generation > previous.Generation && current.Generation == Row().ParkResolutionGeneration && stale.Seen == 0;
        check(retagged, "the second page commits a new release tag, clears Seen and positions the gate on the cached input");
        if (!retagged) return;
        await PollAndMark();
        if (held)
            check(worker.RecoveryHandedOff(stale.Message.InboxKey), "the same owner preserves its hold after retagging");
        check(stale.Seen == expectedEpoch && probe.Order.Count == 0 && retagWrites == 1,
            "the active owner marks the committed version at its current epoch before delivery");

        for (var pass = 0; pass < 24 && (Row().ParkRecovery is not null || Row().Status != InstanceStatus.Finished); pass++)
        {
            now += 11;
            await Step();
        }
        check(Row() is { Status: InstanceStatus.Finished, ParkRecovery: null } && worker.Owns &&
            worker.Fences.Single().Epoch == expectedEpoch && probe.Order.SequenceEqual(["cached", "suffix"]) &&
            Json.Read<List<string>>(Row().State).SequenceEqual(probe.Order) && (!held || probe.SoloOffers == 1) &&
            store.Messages.Values.All(row => row.KeptBy is null),
            $"the active owner delivers the retagged input before the suffix ends the model (applied {string.Join(",", probe.Order)})");

        InstanceRow Row() => store.Instances[id];
        InMemoryStore.MessageRow Input(string name) => store.Messages.Values.Single(row => row.Message.Id == name);

        async Task Open(string operation)
        {
            var selected = (await reads.GetAsync(scope, actor))!.Selection;
            var accepted = await ingress.SendAsync(scope, target, operation, Contract.Kinds.Recover,
                Json.Write(new Contract.OperatorCommand(selected, Contract.RecoverMode.Resume)));
            if (accepted.Outcome != Contract.AdmissionOutcome.Accepted)
                throw new InvalidOperationException($"The test could not admit {operation}.");
        }

        async Task PollAndMark()
        {
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
        }

        async Task Step()
        {
            await PollAndMark();
            worker.RunTurn(id);
            await worker.FlushAsync();
        }

        async Task Pump()
        {
            now += 11;
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
    }

    private sealed class Probe(bool held, bool eventInterruption) : Contract.IActorType
    {
        public string Type => "cached-release";
        internal int SoloOffers { get; private set; }
        internal List<string> Order { get; } = [];

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var applied = turn.State is { Length: > 0 } state ? Json.Read<List<string>>(state) : [];
            foreach (var input in turn.Inbox)
            {
                if (input.Kind == "fault") throw new InvalidOperationException("Open the initial Park.");
                if (held && input.Kind == "apply" && !turn.Inbox.Any(other => other.Kind == "finish"))
                {
                    SoloOffers++;
                    turn.Hold(input);
                    continue;
                }
                if (input.Kind is "apply" or "finish")
                {
                    Order.Add(input.Id);
                    applied.Add(input.Id);
                }
                if (input.Kind == "finish") turn.End(Contract.ActorStatus.Finished);
            }
            turn.State = Json.Write(applied);
            return ValueTask.CompletedTask;
        }

        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], eventInterruption ? [new("refresh", "wait", true)] : []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
