using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class TenantPassChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var (weight, hotActors, cold, budget) in new[] { (1, 3, false, 1), (2, 3, true, 1), (2, 1, false, 4) })
        {
            const long now = 1_701_700_000_000;
            var store = new InMemoryStore(1, () => now);
            var ingress = new InMemoryIngress(store, new Requests("tenant-passes"), "tenant-passes");
            var hot = new Contract.BoundScope("hot");
            var quiet = new Contract.BoundScope("quiet");
            var probe = new Probe();
            var type = new ActorAdapter(store, probe);
            var weights = weight == 1 ? null : new Dictionary<string, int> { [hot.Tenant] = weight };
            var options = new WorkerOptions { PassTurnBudget = budget, Monotonic = () => 0, TenantTurnWeights = weights };
            var sources = new List<Contract.ActorRef>();

            await using var writer = await Activate("writer");
            for (var i = 0; i < hotActors; i++)
                sources.Add(await Start(hot, $"hot-{i}"));
            var waiting = await Start(quiet, "quiet");
            await Pump(writer, 16);

            var loadedTenants = store.Instances.Values
                .GroupBy(row => ((IFoundationSchedulingStore)store).FoundationScheduleTenant(new(row.Id, row.Creation)))
                .ToDictionary(group => group.Key ?? "", group => group.Count());
            check(loadedTenants.GetValueOrDefault(hot.Tenant) == hotActors &&
                  loadedTenants.GetValueOrDefault(quiet.Tenant) == 1,
                $"weight {weight} budget {budget}: the stored checkpoint supplies its bound tenant");

            if (cold) await writer.DisposeAsync();
            await using var successor = cold ? await Activate("successor") : null;
            var owner = successor ?? writer;
            foreach (var actor in sources)
                await ingress.SendAsync(hot, new Contract.Target.Ref(actor), "enter", "work", "3");
            await ingress.SendAsync(quiet, new Contract.Target.Ref(waiting), "enter", "work", "1");

            var passBound = true;
            for (var pass = 0; pass < 24; pass++)
            {
                var prior = probe.Turns.Count;
                await Pump(owner, 1);
                passBound &= probe.Turns.Count - prior <= budget;
            }

            IReadOnlyList<Contract.ActorRef> expected = (weight, hotActors) switch
            {
                (1, _) => [sources[0], waiting, sources[1], sources[2]],
                (2, 1) => [sources[0], sources[0], waiting, sources[0]],
                _ => [sources[0], sources[1], waiting, sources[2], sources[0]]
            };
            check(passBound && probe.Turns.Take(expected.Count).SequenceEqual(expected),
                $"weight {weight} budget {budget} cold {cold}: tenant round gives quiet work a turn and rotates the hot actors");
            check(probe.Turns.Count(actor => actor == waiting) == 1 &&
                  sources.All(source => probe.Turns.Count(actor => actor == source) == 3),
                $"weight {weight} budget {budget} cold {cold}: all waiting and replenished inputs run exactly once");

            async Task<IScopeActivation> Activate(string node)
            {
                IKernelStore backend = cold ? new CountingStore(store, () => { }) : store;
                return (await new DurableHost([new Worker(node, 0, backend, type, options)]).ActivateAsync(0))!;
            }

            async Task<Contract.ActorRef> Start(Contract.BoundScope scope, string key)
            {
                var receipt = await ingress.SendAsync(scope, new Contract.Target.Key(key, new(probe.Type, "test")),
                    "birth", Contract.Kinds.Start);
                return receipt.Actor ?? throw new InvalidOperationException($"The scheduler fixture could not start {key}.");
            }
        }
    }

    private static async Task Pump(IScopeActivation owner, int passes)
    {
        for (var i = 0; i < passes; i++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "tenant-passes";
        internal List<Contract.ActorRef> Turns { get; } = [];

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var input in turn.Inbox.Where(input => input.Kind == "work"))
            {
                Turns.Add(turn.Header.Ref);
                var remaining = Json.Read<int>(input.Payload!);
                if (remaining > 1) turn.Send(turn.Header.Ref, "work", Json.Write(remaining - 1));
            }
            turn.State = "{}";
            return default;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
