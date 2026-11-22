using System.Text;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkPageBoundsChecks
{
    private enum Resolution { Resume, Discard, Expiry, Terminate, Delete }

    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var resolution in Enum.GetValues<Resolution>())
            await Run(resolution, check);
    }

    private static async Task Run(Resolution resolution, Action<bool, string> check)
    {
        const long start = 1_701_679_300_000;
        var now = start;
        var store = new InMemoryStore(1, () => now) { ParkedRetentionWindow = TimeSpan.FromSeconds(1) };
        var scope = new Contract.BoundScope(new string('t', 64));
        var kind = new string('界', 85) + "k";
        var probe = new Probe(kind);
        var ingress = new InMemoryIngress(store, new Requests("page-bounds"), "page-bounds", (_, _) => true);
        var pages = new List<IReadOnlyList<ParkPageRow>>();
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            if (!landed) return;
            foreach (var turn in batch.Turns)
            {
                var rows = turn.FoundationParkClear?.Rows ?? turn.FoundationRecoveryPage?.Rows;
                if (rows is { Count: > 0 }) pages.Add(rows);
            }
        });

        await using var initial = await Owner("page-first");
        var birth = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "metadata")), "birth", Contract.Kinds.Start);
        var actor = birth.Actor ?? throw new InvalidOperationException("The page fixture could not start its actor.");
        var target = new Contract.Target.Ref(actor);
        await Pump(initial, 5);
        await ingress.SendAsync(scope, target, WideId(0), kind, "poison");
        await Pump(initial, 5);
        var checkpoint = Read();
        var selection = (await new ActorList(store, 10).GetAsync(scope, actor))!.Selection!;
        var firstBoundary = InMemoryStore.ParkPageLimit;
        for (var i = 1; i <= firstBoundary; i++)
            await ingress.SendAsync(scope, target, WideId(i), kind, "value");
        await Pump(initial, 5);
        var kept = store.Messages.Values.Where(row => row.KeptBy is not null && row.Message.Target.Id == checkpoint.Row.Id).ToArray();
        var staged = checkpoint.Park is not null && kept.Length == firstBoundary + 1 &&
            kept.All(row => Encoding.UTF8.GetByteCount(row.Message.Id) == FoundationMetadataLimits.NameBytes &&
                Encoding.UTF8.GetByteCount(row.Message.Kind) == FoundationMetadataLimits.KindBytes);
        check(staged, $"S-5 {resolution}: every retained row has the largest admitted outside id and kind");
        if (!staged) return;

        if (resolution == Resolution.Discard)
        {
            var inserted = false;
            try
            {
                var admission = await ingress.SendAsync(scope, target, "too-wide",
                    new string('x', ParkRecoveryCredit.LimitBytes + 1), "must not be kept");
                inserted = admission.Outcome == Contract.AdmissionOutcome.Accepted;
            }
            catch (Contract.MetadataLimitExceeded) { }
            check(!inserted && store.Messages.Values.All(row => row.Message.Id != "too-wide"),
                "S-5 oversized kind: admitting a kind larger than the page budget would make a singleton page impossible");
        }

        if (resolution == Resolution.Expiry)
            now += (long)store.ParkedRetentionWindow!.Value.TotalMilliseconds;
        else
        {
            var command = resolution switch
            {
                Resolution.Resume => Contract.Kinds.Recover,
                Resolution.Discard => Contract.Kinds.Discard,
                Resolution.Terminate => Contract.Kinds.Terminate,
                Resolution.Delete => Contract.Kinds.Delete,
                _ => throw new InvalidOperationException()
            };
            var admission = await ingress.SendAsync(scope, target, "resolve", command,
                Json.Write(new Contract.OperatorCommand(selection, Contract.RecoverMode.Resume)));
            if (admission.Outcome != Contract.AdmissionOutcome.Accepted)
                throw new InvalidOperationException($"The fixture could not admit {resolution}: {admission.Outcome}.");
        }
        for (var i = 0; i < 12 && pages.Count == 0; i++)
            await Pump(initial, 1);
        check(pages is [{ Count: InMemoryStore.ParkPageLimit }] && pages[0][0].Cursor.Key == kept.MinBy(row => row.Seq)!.Message.InboxKey,
            $"S-5 {resolution}: page one includes the largest first input and stops at the row boundary");
        await initial.DisposeAsync();

        now++;
        await using var replacement = await Owner("page-second");
        for (var i = 0; i < 16; i++)
        {
            now += 11;
            await Pump(replacement, 1);
        }
        var row = store.Instances[checkpoint.Row.Id];
        var actual = pages.SelectMany(page => page).Select(item => item.Cursor.Key).ToArray();
        var expected = kept.OrderBy(item => item.Seq).Select(item => item.Message.InboxKey);
        check(pages is [{ Count: InMemoryStore.ParkPageLimit }, { Count: 1 }] && actual.SequenceEqual(expected) &&
              pages.All(page => Encoding.UTF8.GetByteCount(Json.Write(page)) <= ParkRecoveryCredit.LimitBytes) &&
              store.Messages.Values.All(message => message.KeptBy is null && message.Message.Kind != Kinds.Drain) &&
              row.ParkDiscard is null && row.ParkRecovery is null &&
              (resolution == Resolution.Delete ? row.Retired : Read().Park is null) &&
              (resolution != Resolution.Resume || probe.Restored == firstBoundary),
            $"S-5 {resolution}: the cold owner finishes the one-row suffix within the projection budget (pages {string.Join(",", pages.Select(p => p.Count))}; restored {probe.Restored}; rows match {actual.SequenceEqual(expected)}; faults {row.Fault}; pending {store.Messages.Values.Count(m => !m.Done)}; kept {store.Messages.Values.Count(m => m.KeptBy is not null)}; recovery {row.ParkRecovery?.Cursor}, {row.ParkRecovery?.Control})");

        InMemoryStore.ActorCheckpoint Read() => store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!;

        async Task<IScopeActivation> Owner(string node) =>
            (await new DurableHost([new Worker(node, 0, wire, new ActorAdapter(store, probe),
                new WorkerOptions { Monotonic = () => now - start })]).ActivateAsync(0))!;
    }

    private static string WideId(int index)
    {
        var prefix = $"item-{index:D3}";
        var remaining = FoundationMetadataLimits.NameBytes - prefix.Length;
        return prefix + new string('界', remaining / 3) + new string('x', remaining % 3);
    }

    private static async Task Pump(IScopeActivation owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await owner.WokenAsync(null);
            await owner.RunAsync();
        }
    }

    private sealed class Probe(string kind) : Contract.IActorType
    {
        public string Type => "park-page-limits";
        public int Restored { get; private set; }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var input in turn.Inbox.Where(input => input.Kind == kind))
            {
                if (input.Payload == "poison") throw new InvalidOperationException("retain the first maximal input");
                Restored++;
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
