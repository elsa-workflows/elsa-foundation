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
    }

    private static async Task TiedCohort(bool poisonFirst, Action<bool, string> check, bool narrow = false)
    {
        const long initial = 1_702_610_001_000;
        const int maxAttempts = 3;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var probe = new Probe();
        var scope = new Contract.BoundScope("tied-cohort");
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
            check(Row().Status == InstanceStatus.Faulted && order is [var tried] && tried == suspect.Message.Payload &&
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

    private static async Task Pump(IScopeActivation owner)
    {
        for (var pass = 0; pass < 24; pass++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "tied-cohort";
        internal bool HoldHere { get; set; } = true;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var order = turn.State is null ? [] : Json.Read<List<string>>(turn.State);
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == "fault") throw new InvalidOperationException("Open the Park fixture.");
                if (message.Kind != "hold" || message.Payload is not { } value) continue;
                if (HoldHere) turn.Hold(message);
                else order.Add(value);
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
