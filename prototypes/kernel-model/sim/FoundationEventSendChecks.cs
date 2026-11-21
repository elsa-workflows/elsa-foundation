using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class FoundationEventSendChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long initial = 1_700_800_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        var ingress = new EventIngress(store, new Requests("event-one-way"), "event-owner");
        var start = new Contract.StartSpec("event-send", "one-way");
        var quiet = new Contract.Target.Event("quiet");
        var keep = new Contract.AddOptions(KeepFor: TimeSpan.FromMilliseconds(20));

        var refused = await ingress.SendAsync(scope, quiet, "~issued", "forged", keep);
        check(refused is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.InvalidEventId } &&
            store.FoundationEvents.Count == 0, "outside event admission reserves the turn-issued id domain");
        var ephemeral = await ingress.SendAsync(scope, quiet, "quiet", "unkept", null);
        await ingress.SetStartAsync(scope, quiet.CorrelationKey, "future", start);
        check(ephemeral.Outcome == Contract.AdmissionOutcome.Accepted && store.Messages.Count == 0,
            "a one-way send without waiters is admitted and stays unavailable to future waits unless kept");

        var retained = new Contract.Target.Event("retained");
        var kept = await ingress.SendAsync(scope, retained, "kept", "caught-up", keep);
        var duplicate = await ingress.SendAsync(scope, retained, "kept", "changed", keep);
        await ingress.SetStartAsync(scope, retained.CorrelationKey, "late-start", start);
        check(kept.Outcome == Contract.AdmissionOutcome.Accepted && duplicate.Outcome == Contract.AdmissionOutcome.Duplicate &&
            store.Messages.Count == 2, "a kept event without waiters catches up one later start, with its original payload");

        var live = InstanceRef.In(0, "event-live-root");
        var liveRef = store.AdmitFoundationStart(scope.Tenant, new Message("arm", live, Kinds.Start,
            Start: new StartSpec("event-send", "live", "arm", Creation: new(1, 0, "arm")), TargetCreation: 1))!;
        var worker = new Worker("event-owner", 0, store, new ActorAdapter(store, new Probe()), new WorkerOptions { Monotonic = () => now - initial });
        await using var activation = (await new DurableHost([worker]).ActivateAsync(0))!;
        await Pump(activation);
        check(store.ReadFoundationActors(scope.Tenant).Actors.Any(item => item.Header.TypeData == "one-way" &&
                Json.Read<List<string>>(item.Row.State).SequenceEqual(["caught-up"])),
            "the late start processes the retained payload through the real actor adapter");

        var topic = new Contract.Target.Event("live");
        var first = await ingress.SendAsync(scope, topic, "reusable", "first", keep);
        await Pump(activation);
        now += 20;
        var onBoundary = await ingress.SendAsync(scope, topic, "reusable", "duplicate", keep);
        now++;
        var second = await ingress.SendAsync(scope, topic, "reusable", "second", keep);
        await Pump(activation);
        check(first.Outcome == Contract.AdmissionOutcome.Accepted && second.Outcome == Contract.AdmissionOutcome.Accepted &&
            onBoundary.Outcome == Contract.AdmissionOutcome.Duplicate &&
            Json.Read<List<string>>(store.ReadFoundationActor(scope.Tenant, liveRef).Actor!.Row.State).SequenceEqual(["first", "second"]),
            "an event id can be admitted after expiry before sweep and its old match cannot suppress the new delivery");
    }

    private static async Task Pump(IScopeActivation activation)
    {
        for (var step = 0; step < 20; step++)
        {
            await activation.WokenAsync(null);
            await activation.RunAsync();
            await Task.Yield();
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "event-send";
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var state = turn.State is null ? [] : Json.Read<List<string>>(turn.State);
            foreach (var message in turn.Inbox)
            {
                if (message is { Kind: Contract.Kinds.Start, Payload: "arm" }) turn.Subscribe("live", "active");
                if (message.Kind == Contract.Kinds.Event && message.Payload is { } input) state.Add(input);
            }
            turn.State = Json.Write(state);
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
