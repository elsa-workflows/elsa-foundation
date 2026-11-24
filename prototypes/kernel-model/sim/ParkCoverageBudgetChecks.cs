using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkCoverageBudgetChecks
{
    private const int Registrations = 2_577;
    private static readonly string Wait = new('w', FoundationMetadataLimits.NameBytes);

    internal static async Task Run(Action<bool, string> check)
    {
        await Run(check, terminate: false);
        await Run(check, terminate: true);
    }

    private static async Task Run(Action<bool, string> check, bool terminate)
    {
        const long now = 1_702_701_000_000;
        var store = new InMemoryStore(1, () => now);
        var scope = ContractScopes.Bind("coverage-budget");
        var probe = new Probe();
        var edge = new InMemoryIngress(store, new Requests("coverage-budget"), "coverage-budget",
            (_, kind) => kind is Contract.Kinds.Recover or Contract.Kinds.Terminate);
        await using var worker = new Worker("coverage-budget", 0, store, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => 0 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;
        var birth = await edge.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "recipe")), "birth", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(birth.Actor!.Value);
        for (var i = 0; i < 8; i++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
        var actorId = Read().Row.Id;
        var creation = new CreationAddress(actorId, Read().Row.Creation);
        var seeded = store.Subscriptions.Count == Registrations && store.ReadCatchUpCoverageCount(creation) == 0;
        check(seeded, "OA-1 the original turn commits all wide keeping registrations and finishes its first catch-up");
        if (!seeded) return;

        await Send("park", "fault");
        await Step();
        var parked = Read();
        var selected = (await new ActorList(store, 10).GetAsync(scope, target.Actor))!.Selection!;
        var payload = Json.Write(new Contract.OperatorCommand(selected));
        probe.PlanCount++;
        await Send("above-bound", Contract.Kinds.Recover, payload);
        await Step();
        var bounded = parked.Park is not null && Read().Row == parked.Row && store.Subscriptions.Count == 0 &&
            store.ReadCatchUpCoverageCount(creation) == 0 && PendingCatchUps().Length == 0;
        check(bounded, "OA-1 adding one wide keeping wait above the Open budget writes no subscription, coverage, or ticket");
        if (!bounded) return;

        probe.PlanCount--;
        await Send("at-bound", Contract.Kinds.Recover, payload);
        await Step();
        var registrations = store.Subscriptions.Select(row => row.FoundationIncarnation).ToHashSet();
        var catchUps = PendingCatchUps();
        var opened = Read().Row.ParkRecovery is { Cursor: null } &&
            store.Subscriptions.Count == Registrations && registrations.Count == Registrations &&
            store.ReadCatchUpCoverageCount(creation) == Registrations && catchUps.Length == 1;
        check(opened, "OA-1 one fenced Open writes 2,577 exact coverage entries and one catch-up ticket");
        if (!opened) return;

        for (var i = 0; i < 5 && Read().Park is not null; i++) await Step();
        for (var i = 0; i < 10; i++)
        {
            await Send($"restore-existing-{i}", "reregister");
            await Step();
        }
        check(Read().Park is null && probe.Repeats == 10 &&
              store.Subscriptions.Count == Registrations &&
              store.Subscriptions.Select(row => row.FoundationIncarnation).ToHashSet().SetEquals(registrations) &&
              store.ReadCatchUpCoverageCount(creation) == Registrations &&
              PendingCatchUps().SequenceEqual(catchUps),
            "OA-1 ten real re-registrations keep the original incarnation and do not grow coverage or tickets");

        if (terminate)
        {
            await Send("unsubscribe", "unsubscribe");
            await Step();
            check(store.Subscriptions.Count == Registrations - 1 && store.ReadCatchUpCoverageCount(creation) == Registrations - 1,
                "OA-1 one unsubscribe removes exactly one pending coverage entry");
            await Send("terminate", Contract.Kinds.Terminate);
        }
        else await Send("another-park", "fault");
        await Step();
        check(Read().Row.Status == (terminate ? InstanceStatus.Cancelled : InstanceStatus.Faulted) &&
              store.Subscriptions.Count == 0 && store.ReadCatchUpCoverageCount(creation) == 0,
            terminate ? "OA-1 termination clears the remaining bounded coverage" : "OA-1 Park clears every outstanding coverage entry");

        InMemoryStore.ActorCheckpoint Read() => store.ReadFoundationActor(scope.Tenant, target.Actor.Id).Actor!;
        InboxKey[] PendingCatchUps() => store.Messages.Values.Where(row => !row.Done && row.Message.ParkCatchUp is not null)
            .Select(row => row.Message.InboxKey).ToArray();
        ValueTask<Contract.Admission> Send(string operation, string kind, string? body = null) =>
            edge.SendAsync(scope, target, operation, kind, body);

        async Task Step()
        {
            // Leave the catch-up ticket pending while driving actual owner turns.
            await worker.PollAsync();
            await worker.LoadAsync();
            await worker.FlushAsync();
            worker.RunTurn(actorId);
            await worker.FlushAsync();
        }
    }

    private static string Key(int index) => $"k-{index:D4}-".PadRight(FoundationMetadataLimits.NameBytes, 'k');

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "coverage-budget";
        internal int PlanCount = Registrations;
        internal int Repeats;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var input in turn.Inbox)
                switch (input.Kind)
                {
                    case Contract.Kinds.Start:
                        for (var i = 0; i < Registrations; i++) turn.Subscribe(Key(i), Wait, keep: true);
                        break;
                    case "fault":
                        throw new InvalidOperationException("Park while keeping subscriptions.");
                    case "reregister":
                        Repeats++;
                        turn.Subscribe(Key(0), Wait, keep: true);
                        break;
                    case "unsubscribe":
                        turn.Unsubscribe(Key(0), Wait);
                        break;
                    case Contract.Kinds.Terminate:
                        turn.End(Contract.ActorStatus.Cancelled);
                        break;
                }
            turn.State = "{}";
            return ValueTask.CompletedTask;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) =>
            new([], [], Enumerable.Range(0, PlanCount).Select(index => new Contract.SubscriptionSpec(Key(index), Wait, Keep: true)).ToArray());
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
