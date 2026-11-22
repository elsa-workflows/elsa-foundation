using System.Diagnostics.Metrics;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkHandoffCreditChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await Backlog(large: false, check);
        await Backlog(large: true, check);
        await Backlog(large: false, check, keepHeld: true);
        await Backlog(large: true, check, keepHeld: true);
    }

    private static async Task Backlog(bool large, Action<bool, string> check, bool keepHeld = false)
    {
        const long initial = 1_702_610_017_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("drain-credit");
        var edge = new InMemoryIngress(store, new Requests("drain-credit"), "drain-credit", (_, kind) => kind == Contract.Kinds.Recover);
        var probe = new Probe { HoldInputs = keepHeld };
        var measurements = new DrainMeasurements();
        using var meter = measurements.Listen();
        var allSamplesMatch = true;
        var sampledPending = false;
        var sampledHeld = false;
        var neighbourSharesTurns = true;
        var neighbourSent = 0;
        var neighbourTurns = 0;
        string? neighbour = null;
        Contract.Target.Ref? neighbourTarget = null;
        var worker = MakeWorker("first");
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var birth = await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "")), "start", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(birth.Actor!.Value);
        var actor = store.FoundationTarget(scope.Tenant, target.Actor.Id)!.Id;
        await Step();
        await Send("fault", "fault");
        await Step();
        var limit = InMemoryStore.ParkPageLimit;
        var arrivalCount = large ? (keepHeld ? 12 : 11) : 4 * limit;
        const int mib = 1024 * 1024;
        for (var index = 0; index < arrivalCount; index++)
        {
            var data = !large || index > 10 ? "plain"
                : index == 10 ? new string('z', FoundationMetadataLimits.MessageContentBytes) : new string('x', mib);
            await Send($"arrival-{index:D4}", "input", data);
        }
        for (var pass = 0; pass < 5 && Kept() != arrivalCount + 1; pass++) await Step();
        var parked = (await new ActorList(store, 10).GetAsync(scope, target.Actor))!.Selection!;
        await Send("recover", Contract.Kinds.Recover, Json.Write(new Contract.OperatorCommand(parked, Contract.RecoverMode.Resume)));
        for (var pass = 0; pass < 5 && Row().ParkRecovery is null; pass++) await Step();
        var opened = Row().ParkRecovery is { Restored: 0, Gate: null } && Kept() == arrivalCount + 1;
        check(opened, $"HC-1 a real {(large ? "byte" : "row")} drain opens with its backlog kept");
        if (!opened) return;

        var other = await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "")), "neighbour-start", Contract.Kinds.Start);
        neighbourTarget = new Contract.Target.Ref(other.Actor!.Value);
        neighbour = store.FoundationTarget(scope.Tenant, other.Actor.Value.Id)!.Id;
        await Step();
        var first = Row().ParkRecovery!;
        var expected = large ? 3 : limit - 1;
        var release = new ParkRelease(Row().Creation, Row().ParkResolutionGeneration);
        var firstBatch = Outstanding();
        check(first.Restored == expected && firstBatch.Length == expected && firstBatch.All(row => row.Seen == 0),
            $"HC-1 the first page restores {expected} pending inputs under its {(large ? "byte" : "row")} cap");
        if (first.Restored != expected) return;
        await Step();
        var blocked = Row().ParkRecovery;
        check(blocked is not null && blocked.Restored == first.Restored && blocked.Control != first.Control &&
              store.Messages.GetValueOrDefault(blocked.Control) is { Message.Due: var due } && due == now + 10 &&
              firstBatch.All(row => row.Seen == worker.Fences.Single().Epoch && !row.Done) &&
              Json.Read<List<string>>(Row().State).Count == 0,
            "HC-1 marking the first page Seen cannot open the next page before the model handoff");
        await Step();
        check(firstBatch.All(row => keepHeld ? row.Held : row.Done), "HC-1 a model turn hands off the first restored page");
        if (keepHeld)
        {
            await RetakeHeldBacklog();
            CheckSamples();
            return;
        }

        var peakRows = 0;
        var oversizedSolo = false;
        var respected = true;
        for (var pass = 0; pass < 55 && Row().ParkRecovery is not null; pass++)
        {
            if (pass % 3 == 0) now += 11;
            await Step();
            var pending = Outstanding();
            var bytes = pending.Sum(row => Charged(row));
            var oversized = pending.Count(row => Charged(row) > 4 * mib);
            if (oversized == 1 && pending.Length == 1) oversizedSolo = true;
            peakRows = Math.Max(peakRows, pending.Length);
            respected &= pending.Length <= limit && (bytes <= 4 * mib || oversized == 1 && pending.Length == 1);
        }
        var received = Json.Read<List<string>>(Row().State);
        check(respected && peakRows <= (large ? 3 : limit) && (!large || oversizedSolo) &&
              Row().ParkRecovery is null && Kept() == 0 && Outstanding().Length == 0 &&
              received.SequenceEqual(Enumerable.Range(0, arrivalCount).Select(index => $"arrival-{index:D4}")),
            "HC-1 the drain completes with exact ordered once-only inputs and never exceeds its credit");
        CheckSamples();

        void CheckSamples() => check(allSamplesMatch && sampledPending && sampledHeld == keepHeld &&
            neighbourSharesTurns && neighbourTurns > 0,
            $"HC-3 {large}/{keepHeld}: every poll's gauges match the independent backlog and the neighbour advances in the same passes");

        async Task RetakeHeldBacklog()
        {
            var beforeSuffix = large ? 11 : 3 * limit - 1;
            var handedBefore = large ? 10 : beforeSuffix;
            for (var pass = 0; pass < 45 && !ReadyToTake(); pass++)
            {
                if (Outstanding().All(row => row.Held)) AdvanceToControl();
                await Step();
            }
            var old = worker.Fences.Single();
            var oldNode = worker.Node;
            var retained = ReadyToTake();
            check(retained, large ? "HC-2 the first owner holds ten large rows and restores an oversized row alone"
                : "HC-2 the first owner holds three pages and leaves the fourth kept");
            if (!retained) return;

            await owner.DisposeAsync();
            probe.HandedHere.Clear();
            worker = MakeWorker("second");
            await using var second = (await new DurableHost([worker]).ActivateAsync(0))!;
            var middle = worker.Fences.Single().Epoch;
            await Poll();
            await worker.LoadAsync();
            check(middle > old.Epoch && Row().ParkRecovery?.Restored == beforeSuffix && probe.HandedHere.Count == 0,
                "HC-2 the first takeover stops after polling and before any handoff");
            check(((IKernelStore)store).SampleParkDrains(oldNode, [old], _ => true) is null,
                "HC-3 the store refuses a sample from the previous owner's fence");
            await second.DisposeAsync();

            worker = MakeWorker("third");
            await using var third = (await new DurableHost([worker]).ActivateAsync(0))!;
            var allPagesRespectedCredit = worker.Fences.Single().Epoch > middle;
            for (var pass = 0; pass < 3 * beforeSuffix + 60 && Row().ParkRecovery is { } before; pass++)
            {
                if (pass % 3 == 0) now += 11;
                var pending = Outstanding().Where(row => !probe.HandedHere.Contains(row.Message.Id)).ToArray();
                await Step();
                if (Row().ParkRecovery is { } after && after.Restored > before.Restored)
                    allPagesRespectedCredit &= pending.Length < limit && pending.Sum(Charged) < 4 * mib;
            }
            check(allPagesRespectedCredit, "HC-2 a second takeover never restores another page while inherited credit is exhausted");
            check(Row() is { ParkRecovery: null, ParkCohort: not null } && Kept() == 0 &&
                  Outstanding().Length == arrivalCount && Outstanding().All(row => row.Held) &&
                  probe.HandedHere.Count == arrivalCount,
                "HC-2 re-handing off the inherited inputs lets the successor complete with all held inputs intact");

            probe.HoldInputs = false;
            await Send("release", "release");
            await Step();
            check(Outstanding().Length == 0 && Json.Read<List<string>>(Row().State).SequenceEqual(
                Enumerable.Range(0, arrivalCount).Select(index => $"arrival-{index:D4}")),
                "HC-2 the successor consumes the held cohort once in its original order");

            bool ReadyToTake() => Row().ParkRecovery?.Restored == beforeSuffix && Outstanding().Length == beforeSuffix &&
                Outstanding().Count(row => row.Held) == handedBefore && probe.HandedHere.Count == handedBefore;

            void AdvanceToControl()
            {
                if (Row().ParkRecovery is { } open && store.Messages.GetValueOrDefault(open.Control)?.Message.Due is { } due)
                    now = Math.Max(now, due);
            }
        }

        Worker MakeWorker(string node) => new(node, 0, store, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - initial, PollLimit = 1_024 });

        static long Charged(InMemoryStore.MessageRow row) => row.PayloadLength + 24 * 1024L;
        InMemoryStore.MessageRow[] Outstanding() => store.Messages.Values.Where(row => row.ReleasedBy == release && !row.Done).ToArray();
        int Kept() => store.Messages.Values.Count(row => row.Message.Target.Id == actor && row.KeptBy is not null);
        InstanceRow Row() => store.Instances[actor];
        ValueTask<Contract.Admission> Send(string operation, string kind, string? payload = null) =>
            edge.SendAsync(scope, target, operation, kind, payload);
        async Task Poll()
        {
            measurements.Clear();
            var checkpoint = store.Instances.GetValueOrDefault(actor);
            var open = checkpoint?.ParkRecovery is not null;
            var cohort = open ? new ParkRelease(checkpoint!.Creation, checkpoint.ParkResolutionGeneration) : checkpoint?.ParkCohort;
            var candidates = cohort is null ? [] : store.Messages.Values.Where(message =>
                !message.Done && message.Message.Target.Id == actor && message.ReleasedBy == cohort).ToArray();
            var held = candidates.LongCount(message => probe.HandedHere.Contains(message.Message.Id));
            var pending = open ? candidates.Where(message => !probe.HandedHere.Contains(message.Message.Id)).ToArray() : [];
            await worker.PollAsync();
            allSamplesMatch &= measurements.Match(pending.Length, pending.Sum(Charged), held);
            sampledPending |= pending.Length > 0;
            sampledHeld |= held > 0;
        }

        async Task Step()
        {
            var before = neighbour is not null && store.Instances.TryGetValue(neighbour, out var peer)
                ? Json.Read<List<string>>(peer.State).Count : (int?)null;
            if (before is not null && neighbourSent == before)
            {
                var accepted = await edge.SendAsync(scope, neighbourTarget!, $"tick-{neighbourSent}", "tick");
                neighbourSharesTurns &= accepted.Outcome == Contract.AdmissionOutcome.Accepted;
                neighbourSent++;
            }
            await Poll();
            await worker.LoadAsync();
            await worker.FlushAsync();
            var ordinaryPass = !worker.Trial.Any();
            worker.RunTurn(actor);
            if (neighbour is not null) worker.RunTurn(neighbour);
            await worker.FlushAsync();
            if (before is { } previous)
            {
                var moved = Json.Read<List<string>>(store.Instances[neighbour!].State).Count - previous;
                if (ordinaryPass) neighbourSharesTurns &= moved == 1;
                neighbourTurns += moved;
            }
        }
    }

    private sealed class DrainMeasurements
    {
        private readonly Dictionary<(string Name, string Aggregate), long> values = [];

        internal MeterListener Listen()
        {
            var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, sink) =>
            {
                if (instrument.Meter.Name == "Elsa.Kernel.Durable" && instrument.Name.StartsWith("elsa.kernel.drain.", StringComparison.Ordinal))
                    sink.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                var aggregate = tags.Length == 0 ? "" : (string)tags[0].Value!;
                values.Add((instrument.Name, aggregate), value);
            });
            listener.Start();
            return listener;
        }

        internal void Clear() => values.Clear();

        internal bool Match(long pending, long bytes, long held) => values.Count == 5 &&
            Is("pending", "sum", pending) && Is("pending", "max", pending) &&
            Is("pending.bytes", "sum", bytes) && Is("pending.bytes", "max", bytes) && Is("held", "", held);

        private bool Is(string name, string aggregate, long expected) =>
            values.TryGetValue(($"elsa.kernel.drain.{name}", aggregate), out var measured) && measured == expected;
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "handoff-credit";
        internal bool HoldInputs { get; set; }
        internal HashSet<string> HandedHere { get; } = new(StringComparer.Ordinal);

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var order = turn.State is null ? [] : Json.Read<List<string>>(turn.State);
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == "fault") throw new InvalidOperationException("Park the drain-credit fixture.");
                if (message.Kind == "tick") { order.Add(message.Id); continue; }
                if (message.Kind != "input") continue;
                HandedHere.Add(message.Id);
                if (HoldInputs) turn.Hold(message);
                else order.Add(message.Id);
            }
            turn.State = Json.Write(order);
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
