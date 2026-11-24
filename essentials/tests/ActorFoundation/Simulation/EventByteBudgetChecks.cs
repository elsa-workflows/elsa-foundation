using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class EventByteBudgetChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        await MatchPages(check, authored: false);
        await MatchPages(check, authored: true);
        await MeetPages(check);
    }

    private static async Task MatchPages(Action<bool, string> check, bool authored)
    {
        const long start = 1_702_702_000_000;
        const int count = InMemoryStore.ParkPageLimit;
        var now = start;
        var tag = authored ? "turn" : "outside";
        var store = new InMemoryStore(1, () => now);
        var tenant = ContractScopes.Bind("wide-events");
        var neighbour = ContractScopes.Bind("peer");
        var requests = new Requests("event-byte-budget");
        var ingress = new InMemoryIngress(store, requests, "event-byte-budget");
        var events = new EventIngress(store, requests, "event-byte-budget");
        var receivedIds = new HashSet<InboxKey>();
        var pages = new List<int>();
        var attempts = new List<int?>();
        var lost = false;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed || batch.Turns is not [{ FoundationPublicationSequence: not null, FoundationCatchUpFailed: null } step])
                return;
            var accepted = store.Messages.Values.Count(row => row.Message.Kind == Kinds.Event && receivedIds.Add(row.Message.InboxKey));
            pages.Add(accepted);
            attempts.Add(step.FoundationCatchUpExpected);
            if (!lost && accepted > 0)
            {
                lost = true;
                throw new StoreException(Failure.Unknown, "the first delivery page landed without an answer");
            }
        });
        var probe = new Probe();
        var type = new ActorAdapter(store, probe);
        Worker Owner(string name) => new(name, 0, wire, type,
            new WorkerOptions { PassTurnBudget = 3, Monotonic = () => now - start });
        var first = Owner("event-byte-first");
        await using var owner = (await new DurableHost([first]).ActivateAsync(0))!;
        await Start(tenant, "subscriber");
        var source = await Start(tenant, "source");
        var peer = await Start(neighbour, "peer");
        for (var pass = 0; pass < 7; pass++) await Step(owner);
        if (store.Subscriptions.Count != count)
        {
            check(false, $"BA-1 {tag}: the subscriber registers every distinct non-keeping wait");
            return;
        }

        var data = new string('z', FoundationMetadataLimits.EventContentBytes);
        var admission = authored
            ? await ingress.SendAsync(tenant, new Contract.Target.Ref(source), "send", "publish", data)
            : await events.SendAsync(tenant, new("wide"), "full", data, new(KeepFor: TimeSpan.FromMinutes(2)));
        check(admission.Outcome == Contract.AdmissionOutcome.Accepted,
            $"BA-1 {tag}: the actual source admits a payload exactly at its content limit");
        for (var pass = 0; pass < 12 && !lost; pass++) await Step(owner);
        var firstLanded = lost && !first.Owns && pages.SequenceEqual(authored ? [0, 1] : [1]) &&
            receivedIds.Count == 1 && attempts.All(attempt => attempt == 0);
        check(firstLanded, $"BA-1 {tag}: the first full delivery lands alone before its answer is lost (steps {string.Join(",", pages)})");
        if (!firstLanded) return;

        await owner.DisposeAsync();
        now += (long)Defaults.Lease.TotalMilliseconds + 1;
        var second = Owner("event-byte-second");
        await using var successor = (await new DurableHost([second]).ActivateAsync(0))!;
        await ingress.SendAsync(neighbour, new Contract.Target.Ref(peer), "peer-progress", "note", "own payload");
        var peerAdvanced = false;
        for (var pass = 0; pass < 3 * count + 32 && (probe.Waits.Count < count || !probe.Peer); pass++)
        {
            await Step(successor);
            if (probe.Peer && probe.Waits.Count < count) peerAdvanced = true;
        }
        check(pages.Count == count + (authored ? 1 : 0) &&
              pages.Skip(authored ? 1 : 0).All(writes => writes == 1) &&
              receivedIds.Count == count && attempts.All(attempt => attempt == 0) &&
              !store.Messages.Values.Any(row => row is { Done: false, Message.FoundationPublish: not null }),
            $"BA-1 {tag}: every remaining page writes one delivery and advances without charging failed attempts");
        check(probe.Waits.SequenceEqual(Enumerable.Range(0, count).Select(Wait)) && probe.ExactPayload && peerAdvanced,
            $"BA-1 {tag}: the cold owner delivers each full payload and wait once while another tenant progresses");

        async Task<Contract.ActorRef> Start(Contract.BoundScope scope, string name)
        {
            var accepted = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, name)), name, Contract.Kinds.Start);
            return accepted.Actor ?? throw new InvalidOperationException($"Cannot start {name}.");
        }

    }

    private static async Task MeetPages(Action<bool, string> check)
    {
        const long start = 1_702_702_001_000;
        var now = start;
        var store = new InMemoryStore(1, () => now);
        var tenant = ContractScopes.Bind("wide-meet");
        var requests = new Requests("meet-byte-budget");
        var ingress = new InMemoryIngress(store, requests, "meet-byte-budget", (_, kind) => kind == Contract.Kinds.Recover);
        var events = new EventIngress(store, requests, "meet-byte-budget");
        var received = new HashSet<InboxKey>();
        var pages = new List<int>();
        var exact = true;
        var tracking = false;
        var lost = false;
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!tracking || !landed ||
                batch.Turns is not [{ FoundationCatchUpStep: not null, FoundationPublicationSequence: null, FoundationCatchUpFailed: null } step])
                return;
            var written = store.Messages.Values.Count(row => row.Message.Kind == Kinds.Event && received.Add(row.Message.InboxKey));
            pages.Add(written);
            exact &= step.FoundationCatchUpExpected == 0;
            if (!lost && written > 0)
            {
                lost = true;
                throw new StoreException(Failure.Unknown, "the first catch-up delivery landed without an answer");
            }
        });
        var probe = new Probe();
        var type = new ActorAdapter(store, probe);
        Worker Owner(string node) => new(node, 0, wire, type, new WorkerOptions { Monotonic = () => now - start });
        var first = Owner("meet-byte-first");
        await using var owner = (await new DurableHost([first]).ActivateAsync(0))!;
        var birth = await ingress.SendAsync(tenant, new Contract.Target.Start(new(probe.Type, "catch-up")), "birth", Contract.Kinds.Start);
        var target = new Contract.Target.Ref(birth.Actor!.Value);
        for (var pass = 0; pass < 6; pass++) await Step(owner);
        await ingress.SendAsync(tenant, target, "fault", "park");
        for (var pass = 0; pass < 4; pass++) await Step(owner);
        var parked = (await new ActorList(store, 10).GetAsync(tenant, target.Actor))!;
        var data = new string('z', FoundationMetadataLimits.EventContentBytes);
        var ids = new[] { "a", "b", "c" };
        foreach (var id in ids)
            await events.SendAsync(tenant, new("history"), id, data, new(KeepFor: TimeSpan.FromMinutes(2)));
        for (var pass = 0; pass < 10; pass++) await Step(owner);
        var staged = parked.Park is not null && parked.Selection is not null && store.Subscriptions.Count == 0 &&
            store.FoundationEvents.Count == 3 && probe.EventIds.Count == 0;
        check(staged, "BA-1 meet: three source-limit events are kept while the subscriber is parked");
        if (!staged) return;

        tracking = true;
        await ingress.SendAsync(tenant, target, "resume", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(parked.Selection)));
        for (var pass = 0; pass < 12 && !lost; pass++) await Step(owner);
        var firstLanded = lost && !first.Owns && pages.SequenceEqual([1]);
        check(firstLanded, "BA-1 meet: the first catch-up writes one full event before its answer is lost");
        if (!firstLanded) return;

        await owner.DisposeAsync();
        now += (long)Defaults.Lease.TotalMilliseconds + 1;
        var second = Owner("meet-byte-second");
        await using var successor = (await new DurableHost([second]).ActivateAsync(0))!;
        for (var pass = 0; pass < 15; pass++)
        {
            now += 11;
            await Step(successor);
        }
        check(exact && pages.SequenceEqual([1, 1, 1]) && probe.EventIds.SequenceEqual(ids) && probe.ExactPayload &&
              store.Messages.Values.All(row => row.Done || row.Message.ParkCatchUp is null),
            $"BA-1 meet: the remaining full events each land once without a failed attempt (steps {string.Join(",", pages)})");
    }

    private static async Task Step(IScopeActivation activation)
    {
        await activation.WokenAsync(null);
        await activation.RunAsync();
    }

    private static string Wait(int index) => $"w-{index:D3}";

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "event-byte-budget";
        internal List<string> Waits { get; } = [];
        internal List<string> EventIds { get; } = [];
        internal bool ExactPayload { get; private set; } = true;
        internal bool Peer { get; private set; }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
                switch (message)
                {
                    case { Kind: Contract.Kinds.Start } when turn.Header.TypeData == "subscriber":
                        for (var i = 0; i < InMemoryStore.ParkPageLimit; i++) turn.Subscribe("wide", Wait(i));
                        break;
                    case { Kind: Contract.Kinds.Start } when turn.Header.TypeData == "catch-up":
                        turn.Subscribe("history", "restored", keep: true);
                        break;
                    case { Kind: "park" }:
                        throw new InvalidOperationException("Park before the kept events arrive.");
                    case { Kind: "publish" }:
                        turn.Publish("wide", message.Payload, TimeSpan.FromMinutes(2));
                        break;
                    case { Event: { } delivery }:
                        Waits.Add(delivery.Wait);
                        EventIds.Add(delivery.Id);
                        ExactPayload &= message.Payload is { Length: FoundationMetadataLimits.EventContentBytes } text &&
                            text.AsSpan().IndexOfAnyExcept('z') < 0;
                        break;
                    case { Kind: "note", Payload: "own payload" } when turn.Header.Tenant == "peer":
                        Peer = true;
                        break;
                }
            turn.State = "{}";
            return ValueTask.CompletedTask;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) =>
            new([], [], header.TypeData == "catch-up" ? [new("history", "restored", true)] : []);
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
