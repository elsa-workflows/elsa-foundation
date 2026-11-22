using System.Globalization;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkLargeDrainChecks
{
    private const int Burst = 1_000;
    private const int MaxPageRows = 170;
    private const long MaxPageBytes = 4L * 1_024 * 1_024;
    private const int MetadataBytes = 24 * 1_024;

    internal static async Task Run(Action<bool, string> check)
    {
        await Run(check, 50_000, lateDirect: false);
        await Run(check, Burst, lateDirect: true);
    }

    private static async Task Run(Action<bool, string> check, int arrivalsPerSender, bool lateDirect)
    {
        const long start = 1_703_001_000_000;
        var keptExpected = 2 * arrivalsPerSender;
        var expected = keptExpected + (lateDirect ? 1 : 0);
        var caseId = lateDirect ? "M3-4" : "M3-1";
        var latePayload = arrivalsPerSender.ToString(CultureInfo.InvariantCulture);
        var now = start;
        var heartbeatDue = start;
        var store = new InMemoryStore(1, () => now);
        var probe = new Probe(lateDirect ? latePayload : null);
        var scope = new Contract.BoundScope("large-park-drain");
        var ingress = new InMemoryIngress(store, new Requests("large-park-drain"), "large-park-drain",
            (_, kind) => kind == Contract.Kinds.Recover);
        var pages = 0;
        var totalRows = 0;
        var maxRows = 0;
        var maxBytes = 0L;
        var lateLandedWithSuffix = false;
        var requestedLateSend = false;
        var storeWire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed) return;
            foreach (var turn in batch.Turns)
            {
                if (requestedLateSend && !lateLandedWithSuffix)
                {
                    var late = turn.Sent.SingleOrDefault(message => message is { Kind: "input" } && message.Payload == latePayload);
                    if (late is not null)
                        lateLandedWithSuffix = store.Instances[late.Target.Id].ParkRecovery is not null &&
                            store.Messages[late.InboxKey] is { Done: false, KeptBy: null, ReleasedBy: null } &&
                            store.Messages.Values.Any(row => row.KeptBy == ParkKeepReason.Arrived &&
                                row.Message.Target == late.Target && row.Message.Sender == late.Sender);
                }
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

        for (var batch = 0; batch < arrivalsPerSender / Burst; batch++)
        {
            foreach (var sender in senders)
                await ingress.SendAsync(scope, new Contract.Target.Ref(sender),
                    $"burst-{sender.Id}-{batch}", "emit");
            for (var pass = 0; pass < 3; pass++) await Step();
        }
        var kept = store.Messages.Values.Count(row => row.KeptBy == ParkKeepReason.Arrived);
        var parked = (await new ActorList(store, 8).GetAsync(scope, recipient))!;
        var staged = parked is { Park: not null, Selection: not null } && kept == keptExpected && probe.Delivered == 0;
        check(staged, $"{caseId} the indefinite Park keeps {kept} deliveries from two actors before recovery");
        if (!staged) return;

        var operation = await ingress.SendAsync(scope, target, "resume", Contract.Kinds.Recover,
            Json.Write(new Contract.OperatorCommand(parked.Selection)));
        var address = store.FoundationTarget(scope.Tenant, recipient.Id)!.Id;
        if (lateDirect)
        {
            for (var pass = 0; pass < 16 && pages == 0; pass++) await Step();
            var firstPageLanded = pages == 1 && store.Instances[address].ParkRecovery is not null;
            check(firstPageLanded, "M3-4 the first page lands with the recovery drain open before the sender emits again");
            if (!firstPageLanded) return;
            probe.RecoveryOpen = () => store.Instances[address].ParkRecovery is not null;
            requestedLateSend = true;
            var late = await ingress.SendAsync(scope, new Contract.Target.Ref(senders[0]), "late-send", "emit-one");
            check(late.Outcome == Contract.AdmissionOutcome.Accepted, "M3-4 the original sender accepts another send turn");
        }
        var passes = 0;
        while (passes++ < 4 * expected / MaxPageRows + 32 && maxRows <= MaxPageRows && maxBytes <= MaxPageBytes && worker.Owns &&
            (probe.Delivered < expected || store.Instances[address].ParkRecovery is not null))
            await Step();

        check(operation.Outcome == Contract.AdmissionOutcome.Accepted && maxRows is > 0 and <= MaxPageRows &&
            maxBytes <= MaxPageBytes && totalRows == keptExpected + 1,
            $"{caseId} every page fits both bounds ({pages} pages; largest {maxRows} rows and {maxBytes} charged bytes)");
        check(store.Instances[address] is { Status: InstanceStatus.Running, ParkRecovery: null } &&
            probe.InOrder && probe.Delivered == expected &&
            probe.NextBySender[senders[0]] == arrivalsPerSender + (lateDirect ? 1 : 0) &&
            probe.NextBySender[senders[1]] == arrivalsPerSender &&
            store.Messages.Values.All(row => row.KeptBy is null && row.Message.Kind != Kinds.Drain),
            $"{caseId} recovery completes with {probe.Delivered} deliveries, once and in each sender's order " +
            $"(passes {passes - 1}; ordered {probe.InOrder}; owns {worker.Owns})");
        if (lateDirect)
            check(lateLandedWithSuffix && !probe.LateOfferedDuringRecovery,
                $"M3-4 the late direct send lands while the sender still has kept rows and is offered only after Complete " +
                $"(landed with suffix {lateLandedWithSuffix}; offered during recovery {probe.LateOfferedDuringRecovery})");

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

    private sealed class Probe(string? latePayload) : Contract.IActorType
    {
        public string Type => "large-park-drain";
        internal Contract.ActorRef Recipient { get; set; }
        internal Dictionary<Contract.ActorRef, int> NextBySender { get; } = [];
        internal bool InOrder { get; private set; } = true;
        internal int Delivered { get; private set; }
        internal Func<bool>? RecoveryOpen { get; set; }
        internal bool LateOfferedDuringRecovery { get; private set; }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var input in turn.Inbox)
                switch (input)
                {
                    case { Kind: "fault" }: throw new InvalidOperationException("Open the indefinite Park.");
                    case { Kind: "emit" or "emit-one" }:
                        var first = int.Parse(turn.State ?? "0", CultureInfo.InvariantCulture);
                        var end = first + (input.Kind == "emit" ? Burst : 1);
                        for (var ordinal = first; ordinal < end; ordinal++)
                            turn.Send(Recipient, "input", ordinal.ToString(CultureInfo.InvariantCulture));
                        turn.State = end.ToString(CultureInfo.InvariantCulture);
                        break;
                    case { Kind: "input" }:
                        if (latePayload is not null && input.Payload == latePayload)
                            LateOfferedDuringRecovery |= RecoveryOpen?.Invoke() == true;
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
