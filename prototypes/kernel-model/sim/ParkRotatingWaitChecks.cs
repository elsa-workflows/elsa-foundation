using System.Security.Cryptography;
using System.Text;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkRotatingWaitChecks
{
    internal static async Task Run()
    {
        const int rotations = 1_000;
        const long initial = 1_703_010_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var model = new RotatingWaits();
        var scope = new Contract.BoundScope("rotating-waits");
        var edge = new InMemoryIngress(store, new Requests("rotation"), "rotation", (_, kind) => kind == Contract.Kinds.Recover);
        var random = new Random(4229);
        var atOpen = 0;
        var atPage = 0;
        var generation = 0;
        var maxBytes = 0;
        var handles = new List<string>(2 * rotations);
        var nextOwner = 0;
        var worker = NewWorker();
        var owner = await Activate();
        try
        {
            var birth = await edge.SendAsync(scope, new Contract.Target.Start(new(model.Type, "")), "birth", Contract.Kinds.Start);
            var target = new Contract.Target.Ref(birth.Actor!.Value);
            var id = store.FoundationTarget(scope.Tenant, target.Actor.Id)!.Id;
            await Until(() => store.Instances.GetValueOrDefault(id)?.Status == InstanceStatus.Faulted, "initial timer fault");
            var initialPark = store.FoundationTurnHeader(Row()).Park!;
            // The first timer faults. Resume suppresses it and releases its sibling, which advances
            // the model and arms the next pair. A later retained input interrupts that same drain.
            for (var k = 0; k < rotations; k++)
            {
                var pair = Json.Read<WaitPair>(Row().State);
                Require(pair.Index == k, "checkpoint wait index");
                var failing = Key(pair.Fault);
                var advance = Key(pair.Advance);
                await Until(() => store.Messages[failing].KeptBy == ParkKeepReason.Consumed &&
                    (store.Messages[advance].KeptBy == ParkKeepReason.Arrived || store.Messages[advance].ReleasedBy is not null),
                    "the current timer pair is available to the next recovery");
                if (k == 0) handles.Add(Handle(advance));
                handles.Add(Handle(failing));
                var trip = await edge.SendAsync(scope, target, $"trip-{k}", "interrupt");
                Require(trip.Outcome == Contract.AdmissionOutcome.Accepted, "interrupt admitted");
                await Until(() => store.Messages.Values.Any(r => r.Message.Id == $"trip-{k}" && r.KeptBy == ParkKeepReason.Arrived),
                    "interrupt retained");
                await Open($"rotate-{k}", failing);
                await Until(() => Json.Read<WaitPair>(Row().State).Index == k + 1, "timer advances the model");
                Require(Row().ParkRecovery is not null, "timer advanced inside recovery");
                await Until(Parked, "re-park after the successful timer");
                Require(store.FoundationTurnHeader(Row()).Park == initialPark, "re-park preserved the incident");
                var next = Json.Read<WaitPair>(Row().State);
                await Until(() => store.Messages[Key(next.Fault)].KeptBy == ParkKeepReason.Arrived &&
                    store.Messages[Key(next.Advance)].KeptBy == ParkKeepReason.Arrived, "next pair retained");
                if (k + 1 < rotations) handles.Add(Handle(Key(next.Advance)));
                await Open($"arm-{k}", null);
                await Until(Parked, "the next timer faults before Complete");
                Require(store.FoundationTurnHeader(Row()).Park == initialPark, "timer fault interrupted the same incident");
            }
            var last = Row();
            var latest = Json.Read<WaitPair>(last.State);
            var plan = new[] { latest.Fault, latest.Advance }
                .Select(t => new ParkTimerRecipe(t.Id, t.Kind, t.Payload, t.Due)).ToArray();
            var currentKey = Key(latest.Fault);
            var positive = store.PlanFoundationRetry(last, scope.Tenant, target.Actor.Id, initialPark, [Handle(currentKey)], plan);
            Require(positive?.Marked is [var selected] && selected.Key == currentKey, "the same parked actor accepts its current faulted timer");
            Require(handles.Count == 2 * rotations, "a handle for each retired wait");
            foreach (var handle in handles)
                Require(store.PlanFoundationRetry(last, scope.Tenant, target.Actor.Id, initialPark, [handle], plan) is null,
                    "an earlier wait handle must be refused for the final pair");
            Require(atOpen > 0 && atPage > 0 && generation == 2 * rotations, "takeovers at both kinds of boundary");
            Console.WriteLine($"ok D-1: {rotations} rotations, {generation} fenced Opens, {atOpen} takes after Open, " +
                $"{atPage} takes after a page, {handles.Count} historical handles refused, maximum encoded Park {maxBytes} bytes");

            string Handle(InboxKey key) => store.SealFoundationKeptHandle(scope.Tenant, target.Actor.Id, key) ??
                throw new InvalidOperationException($"D-1 at Open {generation}: no handle for retained timer {key}");
            InstanceRow Row() => store.Instances[id];
            bool Parked() => Row() is { Status: InstanceStatus.Faulted, ParkRecovery: null };
            InboxKey Key(Contract.TimerSpec timer) => InboxKey.Issued(CreationProtocol.Origin(id, Row().Creation) + "#" + timer.Id);

            async Task Open(string operation, InboxKey? suppressed)
            {
                var selection = store.SealFoundationSelection(scope.Tenant, store.FoundationTurnHeader(Row()));
                var admit = await edge.SendAsync(scope, target, operation, Contract.Kinds.Recover,
                    Json.Write(new Contract.OperatorCommand(selection)));
                Require(admit.Outcome == Contract.AdmissionOutcome.Accepted, "Resume admitted");
                await Until(() => Row().ParkRecovery?.Operation == operation, $"Open {operation}");
                var row = Row();
                var bytes = row.ParkTimerDispositions is null ? [] : Convert.FromBase64String(row.ParkTimerDispositions);
                var dispositionMatches = suppressed is { } expected
                    ? bytes.Length == 33 && bytes[32] == 1 &&
                      bytes.AsSpan(0, 32).SequenceEqual(SHA256.HashData(DurableKeyJson.CodeUnits(expected.Id)))
                    : bytes.Length == 0;
                Require(dispositionMatches && row.ParkRestartedJobs is null, "Open retained dispositions only for its live waits");
                var encoded = Encoding.UTF8.GetByteCount(Json.Write(new
                {
                    initialPark, row.ParkResolutionGeneration, row.ParkRecovery, row.ParkDiscard,
                    row.ParkTimerDispositions, row.ParkRestartedJobs, row.ParkCohort, row.EndedDuringParkRecovery, row.ParkDiscarded,
                    Expiry = store.FoundationExpiry(row)
                }));
                Require(encoded <= 56 * 1024 + (suppressed.HasValue ? 64 : 0), "encoded in-memory Park bound");
                maxBytes = Math.Max(maxBytes, encoded);
                generation++;
                if (random.Next(10) == 0) { await Retake(); atOpen++; }
                await Until(() => Row().ParkRecovery?.Cursor is not null, $"first page {operation}");
                if (random.Next(10) == 0) { await Retake(); atPage++; }
            }

            async Task Until(Func<bool> condition, string step)
            {
                for (var i = 0; i < 64 && !condition(); i++)
                {
                    await worker.PollAsync();
                    await worker.LoadAsync();
                    await worker.FlushAsync();
                    worker.RunTurn(id);
                    await worker.FlushAsync();
                    now++;
                }
                Require(condition(), step);
            }
            void Require(bool passed, string step)
            {
                if (!passed) throw new InvalidOperationException($"D-1 at Open {generation}: {step}");
            }
        }
        finally { await owner.DisposeAsync(); }

        async Task Retake()
        {
            await owner.DisposeAsync();
            var previousEpoch = store.Partitions[0].Epoch;
            worker = NewWorker();
            owner = await Activate();
            if (store.Partitions[0].Epoch <= previousEpoch) throw new InvalidOperationException("D-1 successor failed to take the fence");
        }
        Worker NewWorker() => new($"rotation-{nextOwner++}", 0, store, new ActorAdapter(store, model),
            new WorkerOptions { Monotonic = () => now - initial, PollLimit = 1 });
        async Task<IScopeActivation> Activate() => (await new DurableHost([worker]).ActivateAsync(0))!;
    }

    private sealed record WaitPair(int Index, Contract.TimerSpec Fault, Contract.TimerSpec Advance);
    private sealed class RotatingWaits : Contract.IActorType
    {
        public string Type => "rotating-waits";
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state)
        {
            var pair = Json.Read<WaitPair>(state);
            return new([pair.Fault, pair.Advance], [], []);
        }
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var input in turn.Inbox)
            {
                if (input.Kind is "timer-fault" or "interrupt") throw new InvalidOperationException("Interrupt the current pair.");
                if (input.Kind == Contract.Kinds.Start) Arm(0);
                if (input.Kind == "timer-advance")
                {
                    var pair = Json.Read<WaitPair>(turn.State!);
                    if (input.Id != pair.Advance.Id) throw new InvalidOperationException("A timer reached the wrong wait.");
                    Arm(pair.Index + 1);
                }
            }
            return default;

            void Arm(int index)
            {
                Contract.TimerSpec Timer(string kind) => new(turn.Send(turn.Header.Ref, kind, null, turn.Now), kind, null, turn.Now);
                turn.State = Json.Write(new WaitPair(index, Timer("timer-fault"), Timer("timer-advance")));
            }
        }
    }
}
