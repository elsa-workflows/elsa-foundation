using System.Globalization;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class FoundationEnvelopeChecks
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

        var atCap = await Admit("at-cap", 10_000.ToString(CultureInfo.InvariantCulture));
        var overCap = await Admit("over-cap", 10_001.ToString(CultureInfo.InvariantCulture));
        var labelsAtCap = await Admit("labels-at-cap", "labels-at-cap");
        var invalidLabels = new[] { "label-count", "label-name", "label-value" };
        var refused = new List<(string Case, Contract.ActorRef Ref)>();
        foreach (var scenario in invalidLabels)
            refused.Add((scenario, await Admit(scenario, scenario)));
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

        var acceptedLabels = await reads.GetAsync(scope, labelsAtCap);
        check(acceptedLabels is { Header: { Status: Contract.ActorStatus.Running, Labels.Count: 4 } } &&
            acceptedLabels.Header.Labels.All(pair => System.Text.Encoding.UTF8.GetByteCount(pair.Key) == 1_024 &&
                System.Text.Encoding.UTF8.GetByteCount(pair.Value) == 1_024),
            "four labels with 1,024 UTF-8 bytes per name and value commit through the public actor turn");
        foreach (var (scenario, reference) in refused)
        {
            var view = await reads.GetAsync(scope, reference);
            var row = store.ReadFoundationActor(scope.Tenant, reference.Id).Actor!.Row;
            check(view is { Park: not null, Header: { Status: Contract.ActorStatus.Faulted, Labels.Count: 0 } } &&
                row.State == "null" &&
                !store.Messages.Values.Any(item => item.Message.Target.Id == row.Id && item.Message.Kind == "do-not-commit"),
                $"an excess {scenario} parks the actor and rolls back its header, checkpoint and staged send");
        }

        async Task<Contract.ActorRef> Admit(string id, string input)
        {
            var start = new Contract.StartSpec(model.Type, "probe", input);
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
            if (input.Payload.StartsWith("label", StringComparison.Ordinal))
            {
                var cap = new string('é', 512);
                var chars = new string('é', 511);
                var labels = Enumerable.Range(0, input.Payload == "label-count" ? 5 : 4)
                    .ToDictionary(index => chars + "x" + index.ToString(CultureInfo.InvariantCulture), _ => cap);
                if (input.Payload == "label-name")
                {
                    labels.Remove(chars + "x0");
                    labels.Add(cap + "x", cap);
                }
                if (input.Payload == "label-value") labels[chars + "x0"] = cap + "x";
                turn.State = Json.Write(input.Payload);
                turn.Send(turn.Header.Ref, "do-not-commit", dueAt: turn.Now + 1);
                turn.SetHeader("projected", labels);
                return default;
            }

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
