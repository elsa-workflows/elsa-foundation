using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkCachedReleaseChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        const long start = 1_702_610_004_000;
        const int attempts = 3;
        const int metadataBytes = 24 * 1_024;
        var now = start;
        var store = new InMemoryStore(1, () => now);
        var probe = new Probe();
        var scope = new Contract.BoundScope("cached-release");
        var ingress = new InMemoryIngress(store, new Requests("cached-release"), "cached-release",
            (_, kind) => kind == Contract.Kinds.Recover);
        var reads = new ActorList(store, 8);
        var worker = new Worker("cached-release", 0, store, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - start, MaxAttempts = attempts });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var epoch = worker.Fences.Single().Epoch;
        var birth = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "actor")), "birth", Contract.Kinds.Start);
        var actor = birth.Actor!.Value;
        var id = store.FoundationTarget(scope.Tenant, actor.Id)!.Id;
        for (var pass = 0; pass < 8 && !store.Instances.ContainsKey(id); pass++) await Pump();
        if (!store.Instances.ContainsKey(id))
        {
            check(false, "E-9 the start must commit before the first fault");
            return;
        }
        var target = new Contract.Target.Ref(actor);
        await ingress.SendAsync(scope, target, "initial", "fault");
        for (var pass = 0; pass < 6 && Row().Status != InstanceStatus.Faulted; pass++)
            await Pump();
        if (Row().Status != InstanceStatus.Faulted)
        {
            check(false, "E-9 the initial Park must land before admitting the cohort");
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
            check(false, $"E-9 every test input must be kept as an arrival " +
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
        check(staged, $"E-9 the first page releases the trip and cached input while the suffix stays kept " +
            $"(status {Row().Status}; cursor {Row().ParkRecovery?.Cursor}; tag {stageOne}; " +
            $"trip {Input("trip").ReleasedBy}; Seen {stale.Seen}; suffix {Input("suffix").KeptBy})");
        if (!staged) return;

        // Start the trip at the persisted retry limit before this owner reads the released row.
        Input("trip").Attempts = attempts;
        await PollAndMark();
        var marked = stale.Seen == epoch && probe.Order.Count == 0;
        check(marked, "E-9 the same owner has marked the older release but has yet to deliver it");
        if (!marked) return;
        for (var pass = 0; pass < 8 && Row().Status == InstanceStatus.Running; pass++)
            await Step();
        var interrupted = Row() is { Status: InstanceStatus.Faulted, ParkRecovery: null } &&
            Input("trip").KeptBy == ParkKeepReason.Consumed &&
            stale is { Done: false, KeptBy: null } && stale.ReleasedBy == stageOne && probe.Order.Count == 0;
        check(interrupted, "E-9 the trip re-parks without consuming the already marked input");
        if (!interrupted) return;

        now = secondDue;
        await Step();
        await Step();
        var retagged = Row().ParkRecovery is { Gate: { } gate } && gate == stale.Message.InboxKey &&
            stageOne is { } previous && stale.ReleasedBy is { } current &&
            current.Generation > previous.Generation && current.Generation == Row().ParkResolutionGeneration && stale.Seen == 0;
        check(retagged, "E-9 the second page commits a new release tag, clears Seen and positions the gate on the cached input");
        if (!retagged) return;
        await PollAndMark();
        check(stale.Seen == epoch && probe.Order.Count == 0,
            "E-9 the same owner polls and marks the committed version before delivering it");

        for (var pass = 0; pass < 24 && (Row().ParkRecovery is not null || Row().Status != InstanceStatus.Finished); pass++)
        {
            now += 11;
            await Step();
        }
        check(Row() is { Status: InstanceStatus.Finished, ParkRecovery: null } && worker.Owns &&
            worker.Fences.Single().Epoch == epoch && probe.Order.SequenceEqual(["cached", "suffix"]) &&
            Json.Read<List<string>>(Row().State).SequenceEqual(probe.Order) &&
            store.Messages.Values.All(row => row.KeptBy is null),
            $"E-9 one owner delivers the retagged input before the suffix ends the model (applied {string.Join(",", probe.Order)})");

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

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "cached-release";
        internal List<string> Order { get; } = [];

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var applied = turn.State is { Length: > 0 } state ? Json.Read<List<string>>(state) : [];
            foreach (var input in turn.Inbox)
            {
                if (input.Kind == "fault") throw new InvalidOperationException("Open the initial Park.");
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

        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
