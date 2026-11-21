using System.Globalization;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class FoundationSendCapChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const long now = 1_701_300_000_000;
        var store = new InMemoryStore(1, () => now);
        var scope = new Contract.BoundScope("acme");
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests("turn-cap"), "turn-cap");
        Contract.IActorReads reads = new ActorList(store, 2);
        var model = new Probe();
        var worker = new Worker("turn-cap", 0, store, new ActorAdapter(store, model), new WorkerOptions { Monotonic = () => 0 });
        await using var owner = (await new DurableHost([worker]).ActivateAsync(0))!;

        var atCap = await Admit("at-cap", 10_000);
        var overCap = await Admit("over-cap", 10_001);
        for (var step = 0; step < 12; step++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
            await Task.Yield();
        }

        var accepted = store.ReadFoundationActor(scope.Tenant, atCap.Id).Actor;
        var failed = store.ReadFoundationActor(scope.Tenant, overCap.Id).Actor;
        check(accepted is { Row: { Status: InstanceStatus.Running, State: "10000" } } &&
            store.Messages.Values.Count(row => row.Message.Target.Id == accepted.Row.Id && row.Message.Kind == "own-timer") == 10_000,
            "the public actor can commit a turn with exactly 10,000 staged sends");
        check(failed is { Row.State: "null" } &&
            (await reads.GetAsync(scope, overCap)) is { Header.Status: Contract.ActorStatus.Faulted, Park: not null } &&
            !store.Messages.Values.Any(row => row.Message.Target.Id == failed.Row.Id && row.Message.Kind == "own-timer"),
            "one send above the bound parks the actor and rolls all staged effects back");

        async Task<Contract.ActorRef> Admit(string id, int sends)
        {
            var start = new Contract.StartSpec(model.Type, "probe", sends.ToString(CultureInfo.InvariantCulture));
            return (await ingress.SendAsync(scope, new Contract.Target.Start(start), id, Contract.Kinds.Start)).Actor!.Value;
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "turn-cap";
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var input = turn.Inbox.FirstOrDefault(message => message.Kind == Contract.Kinds.Start);
            if (input?.Payload is null) return default;
            var sends = int.Parse(input.Payload, CultureInfo.InvariantCulture);
            for (var i = 0; i < sends; i++)
                turn.Send(turn.Header.Ref, "own-timer", dueAt: turn.Now + (long)TimeSpan.FromHours(1).TotalMilliseconds);
            turn.State = input.Payload;
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
