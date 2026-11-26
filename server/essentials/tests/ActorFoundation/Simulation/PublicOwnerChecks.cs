using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

/// <summary>An actor whose former owner remains in authored code during a real takeover.</summary>
internal static class PublicOwnerChecks
{
    public static async Task Run(Action<bool, string> check, Flaw flaws)
    {
        const long start = 1_704_000_000_000;
        const int lease = 100;
        var now = start;
        var store = new InMemoryStore(1, () => now, flaws);
        var scope = ContractScopes.Bind("turn-owner");
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests("takeover"), "edge");
        Contract.IActorReads reads = new ActorList(store, 10);
        var previous = new Probe("previous", pause: true);
        var successor = new Probe("successor", pause: false);
        Worker Host(string node, Probe actor) => new(node, 0, store, new ActorAdapter(store, actor),
            new WorkerOptions { Lease = TimeSpan.FromMilliseconds(lease), Monotonic = () => now - start, Flaws = flaws });

        var born = await ingress.SendAsync(scope, new Contract.Target.Start(new(previous.Type, "probe")), "start", Contract.Kinds.Start);
        var life = born.Actor!.Value;
        var target = new Contract.Target.Ref(life);
        var first = Host("first", previous);
        await using var firstScope = (await new DurableHost([first]).ActivateAsync(0))!;
        await Pass(firstScope);
        await ingress.SendAsync(scope, target, "old-input", "first");
        await Pass(firstScope);
        check(previous.Invocations.SequenceEqual(["first"]) && previous.Active, "first owner is paused in its authored turn");

        await ingress.SendAsync(scope, target, "next-input", "second");
        await Pass(firstScope);
        check(previous.Invocations.SequenceEqual(["first"]) && previous.Active &&
            (await reads.InboxAsync(scope, life)).Items.Count == 2,
            "a second input never overlaps an authored turn on the same owner");

        now += lease + 1;
        var next = Host("second", successor);
        await using var nextScope = (await new DurableHost([next]).ActivateAsync(0))!;
        await Pass(nextScope);
        check(previous.Active && successor.Invocations.SequenceEqual(["first", "second"]) &&
            (await Entries()).SequenceEqual(["successor:first", "successor:second"]),
            "a successor replays the paused input and commits its effects before the stale code returns");

        previous.Resume();
        await first.FlushAsync();
        await Pass(nextScope);
        check(!previous.Active && (await Entries()).SequenceEqual(["successor:first", "successor:second"]) &&
            store.ReadFoundationActor(scope.Tenant, life.Id).Actor?.Row.State == Json.Write("successor:second") &&
            (await reads.InboxAsync(scope, life)).Items.Count == 0,
            "the stale actor turn cannot replace the successor's journal or checkpoint");

        async Task<string[]> Entries() => (await reads.TailAsync(scope, life)).Items
            .Select(entry => Json.Read<string>(entry.Entry)).Where(entry => entry.Contains(':')).ToArray();
    }

    private static async Task Pass(Kernel.Runtime.IScopeActivation owner)
    {
        for (var i = 0; i < 16; i++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
    }

    private sealed class Probe(string owner, bool pause) : Contract.IActorType
    {
        private readonly TaskCompletionSource _release = new();
        internal List<string> Invocations { get; } = [];
        internal bool Active { get; private set; }
        internal void Resume() => _release.SetResult();

        public string Type => "owner-probe";
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string? state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string? state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string? state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);

        public async ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var input in turn.Inbox)
            {
                if (input.Kind == Contract.Kinds.Start) continue;
                Invocations.Add(input.Kind);
                if (pause && input.Kind == "first")
                {
                    Active = true;
                    try { await _release.Task; }
                    finally { Active = false; }
                }
                var effect = owner + ":" + input.Kind;
                turn.State = Json.Write(effect);
                turn.Record(effect);
            }
        }
    }
}
