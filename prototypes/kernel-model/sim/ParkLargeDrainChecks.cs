using System.Globalization;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkLargeDrainChecks
{
    private const int ArrivalsPerSender = 50_000;
    private const int Burst = 1_000;
    private const int MaxPageRows = 170;
    private const long MaxPageBytes = 4L * 1_024 * 1_024;
    private const int MetadataBytes = 24 * 1_024;

    internal static async Task Run(Action<bool, string> check)
    {
        const long start = 1_703_001_000_000;
        const int expected = 2 * ArrivalsPerSender;
        var now = start;
        var heartbeatDue = start;
        var store = new InMemoryStore(1, () => now);
        var probe = new Probe();
        var scope = new Contract.BoundScope("large-park-drain");
        var ingress = new InMemoryIngress(store, new Requests("large-park-drain"), "large-park-drain",
            (_, kind) => kind == Contract.Kinds.Recover);
        var pages = 0;
        var totalRows = 0;
        var maxRows = 0;
        var maxBytes = 0L;
        var storeWire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed) return;
            foreach (var turn in batch.Turns)
            {
                if (turn.FoundationRecoveryPage is not { } page) continue;
                pages++;
                totalRows += page.Rows.Count;
                maxRows = Math.Max(maxRows, page.Rows.Count);
                maxBytes = Math.Max(maxBytes, page.Rows.Sum(row => MetadataBytes + (long)row.PayloadBytes));
            }
        });
        var worker = new Worker("large-park-drain", 0, storeWire, new ActorAdapter(store, probe),
            new WorkerOptions { Monotonic = () => now - start, PollLimit = 2 * Burst + 32 });
        var host = new DurableHost([worker]);
        await using var owner = (await host.ActivateAsync(0))!;
        var recipient = await Birth("recipient");
        var senders = new[] { await Birth("sender-a"), await Birth("sender-b") };
        probe.Recipient = recipient;
        foreach (var sender in senders) probe.NextBySender.Add(sender, 0);
        await Step();
        var target = new Contract.Target.Ref(recipient);
        await ingress.SendAsync(scope, target, "park", "fault");
        for (var pass = 0; pass < 6; pass++) await Step();

        for (var batch = 0; batch < ArrivalsPerSender / Burst; batch++)
        {
            foreach (var sender in senders)
                await ingress.SendAsync(scope, new Contract.Target.Ref(sender),
                    $"burst-{sender.Id}-{batch}", "emit");
            for (var pass = 0; pass < 3; pass++) await Step();
        }
        var kept = store.Messages.Values.Count(row => row.KeptBy == ParkKeepReason.Arrived);
        var parked = (await new ActorList(store, 8).GetAsync(scope, recipient))!;
        check(parked is { Park: not null, Selection: not null } && kept == expected && probe.Delivered == 0,
            $"M3-1 the indefinite Park keeps {kept} deliveries from two actors before recovery");
        if (parked.Selection is null || kept != expected || probe.Delivered != 0) return;

        var operation = await ingress.SendAsync(scope, target, "resume", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(parked.Selection)));
        var address = store.FoundationTarget(scope.Tenant, recipient.Id)!.Id;
        var passes = 0;
        while (passes++ < 4 * expected / MaxPageRows + 32 && maxRows <= MaxPageRows && maxBytes <= MaxPageBytes && worker.Owns &&
            (probe.Delivered < expected || store.Instances[address].ParkRecovery is not null))
            await Step();

        check(operation.Outcome == Contract.AdmissionOutcome.Accepted && maxRows is > 0 and <= MaxPageRows &&
            maxBytes <= MaxPageBytes && totalRows == expected + 1,
            $"M3-1 every page fits both bounds ({pages} pages; largest {maxRows} rows and {maxBytes} charged bytes)");
        check(store.Instances[address] is { Status: InstanceStatus.Running, ParkRecovery: null } &&
            probe.InOrder && probe.Delivered == expected &&
            probe.NextBySender.Values.All(next => next == ArrivalsPerSender) &&
            store.Messages.Values.All(row => row.KeptBy is null && row.Message.Kind != Kinds.Drain),
            $"M3-1 recovery completes with {probe.Delivered} deliveries, once and in each sender's order " +
            $"(passes {passes - 1}; ordered {probe.InOrder}; owns {worker.Owns})");

        async Task<Contract.ActorRef> Birth(string name)
        {
            var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, name)), name, Contract.Kinds.Start);
            return started.Actor ?? throw new InvalidOperationException($"The fixture could not admit {name}.");
        }

        async Task Step()
        {
            now += 11;
            if (now >= heartbeatDue)
            {
                if (await host.HeartbeatAsync(TimeSpan.FromMinutes(1)) is null)
                    throw new InvalidOperationException("The test host could not renew its node lease.");
                heartbeatDue = now + 1_000;
            }
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "large-park-drain";
        internal Contract.ActorRef Recipient { get; set; }
        internal Dictionary<Contract.ActorRef, int> NextBySender { get; } = [];
        internal bool InOrder { get; private set; } = true;
        internal int Delivered { get; private set; }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var input in turn.Inbox)
                switch (input)
                {
                    case { Kind: "fault" }: throw new InvalidOperationException("Open the indefinite Park.");
                    case { Kind: "emit" }:
                        var first = int.Parse(turn.State ?? "0", CultureInfo.InvariantCulture);
                        for (var ordinal = first; ordinal < first + Burst; ordinal++)
                            turn.Send(Recipient, "input", ordinal.ToString(CultureInfo.InvariantCulture));
                        turn.State = (first + Burst).ToString(CultureInfo.InvariantCulture);
                        break;
                    case { Kind: "input" }:
                        Delivered++;
                        if (input.Sender is not { } sender || !NextBySender.TryGetValue(sender, out var next) ||
                            input.Payload != next.ToString(CultureInfo.InvariantCulture))
                        {
                            InOrder = false;
                            break;
                        }
                        NextBySender[sender] = next + 1;
                        break;
                }
            return ValueTask.CompletedTask;
        }

        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
