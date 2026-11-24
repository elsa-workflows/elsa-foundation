using System.Text;
using Kernel.Foundation.InMemory;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class ParkPageBoundsChecks
{
    private enum Resolution { Resume, Discard, Expiry, Terminate, Delete }
    private enum LateArrival { BeforePage, AfterPage, Tied, BeforeTake, AfterTake }

    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var resolution in Enum.GetValues<Resolution>())
            await Run(resolution, check);
        foreach (var resolution in new[] { Resolution.Resume, Resolution.Discard, Resolution.Delete })
            await Run(resolution, check, payloadBounds: true);
        await Run(Resolution.Resume, check, aboveHighWater: true);
        foreach (var arrival in Enum.GetValues<LateArrival>())
            await Run(Resolution.Resume, check, arrival: arrival);
        await Run(Resolution.Resume, check, cutAfterPage: false);
        await Run(Resolution.Resume, check, cutAfterPage: true);
    }

    private static async Task Run(Resolution resolution, Action<bool, string> check, bool aboveHighWater = false,
        LateArrival? arrival = null, bool? cutAfterPage = null, bool payloadBounds = false)
    {
        const long start = 1_701_679_300_000;
        var now = start;
        var store = new InMemoryStore(1, () => now) { ParkedRetentionWindow = TimeSpan.FromSeconds(1) };
        var scope = ContractScopes.Bind(new string('t', 64));
        var kind = new string('界', 85) + "k";
        const int pageByteLimit = 4 * 1024 * 1024;
        var largeBodies = payloadBounds
            ? new[] { new string('a', 5 * 1024 * 1024), new string('b', pageByteLimit - 1),
                new string('c', pageByteLimit), new string('d', pageByteLimit + 1) }
            : [];
        var caseId = payloadBounds ? "S-1" : "S-5";
        var probe = new Probe(kind, () => store.Instances.Values.Any(row => row.ParkRecovery is not null));
        var ingress = new InMemoryIngress(store, new Requests("page-bounds"), "page-bounds", (_, _) => true);
        var pages = new List<IReadOnlyList<ParkPageRow>>();
        var observedPressure = false;
        var lostPage = false;
        var privateTrial = false;
        var actor = default(Contract.ActorRef);
        var wire = new CountingStore(store, () => { }, (batch, landed) =>
        {
            privateTrial |= batch.Trial.Any(key => key.Scope == ParkDrainEnvelope.Scope);
            var cut = !lostPage && landed == cutAfterPage &&
                batch.Turns.Any(turn => turn.FoundationRecoveryPage?.Rows.Count == InMemoryStore.ParkPageLimit);
            if (cut && !landed) LosePage();
            if (!landed) return;
            if (aboveHighWater && store.Partitions[0].DueBacklog == 5 &&
                store.Instances.Values.Any(row => row.ParkRecovery is not null))
                observedPressure |= store.Messages.Values.Count(row => row is { Done: false, ReleasedBy: null, Message.Kind: "late" }) == 6;
            foreach (var turn in batch.Turns)
            {
                var rows = turn.FoundationParkClear?.Rows ?? turn.FoundationRecoveryPage?.Rows;
                if (rows is { Count: > 0 }) pages.Add(rows);
            }
            if (cut) LosePage();
        }, async batch =>
        {
            if (arrival == LateArrival.BeforePage && pages.Count == 0 &&
                batch.Turns.FirstOrDefault(turn => turn.FoundationRecoveryPage?.Rows.Count == InMemoryStore.ParkPageLimit)
                    ?.FoundationRecoveryPage?.Rows is { } page)
                await AdmitEarlier(page[^1].Cursor);
        });

        void LosePage()
        {
            lostPage = true;
            throw new StoreException(Failure.Unknown, "The first recovery page did not return an answer.");
        }

        var first = Worker("page-first");
        await using var initial = await Owner(first);
        var birth = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "metadata")), "birth", Contract.Kinds.Start);
        actor = birth.Actor ?? throw new InvalidOperationException("The page fixture could not start its actor.");
        var target = new Contract.Target.Ref(actor);
        await Pump(initial, 5);
        await ingress.SendAsync(scope, target, WideId(0), kind, "poison");
        await Pump(initial, 5);
        var checkpoint = Read();
        var selection = (await new ActorList(store, 10).GetAsync(scope, actor))!.Selection!;
        var firstBoundary = InMemoryStore.ParkPageLimit;
        var smallArrivals = payloadBounds ? firstBoundary - 1 : firstBoundary;
        for (var i = 1; i <= smallArrivals; i++)
            await ingress.SendAsync(scope, target, WideId(i), kind, "value");
        for (var i = 0; i < largeBodies.Length; i++)
            await ingress.SendAsync(scope, target, WideId(smallArrivals + i + 1), kind, largeBodies[i]);
        await Pump(initial, 5);
        var kept = store.Messages.Values.Where(row => row.KeptBy is not null && row.Message.Target.Id == checkpoint.Row.Id).ToArray();
        var staged = checkpoint.Park is not null && kept.Length == smallArrivals + largeBodies.Length + 1 &&
            kept.All(row => Encoding.UTF8.GetByteCount(row.Message.Id) == FoundationMetadataLimits.NameBytes &&
                Encoding.UTF8.GetByteCount(row.Message.Kind) == FoundationMetadataLimits.KindBytes);
        check(staged, $"{caseId} {resolution}: every retained row has the largest admitted outside id and kind");
        if (!staged) return;
        var restorable = kept.Where(item => item.Message.Payload == "value").Select(item => item.Message.Id).ToArray();

        if (resolution == Resolution.Discard && !payloadBounds)
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
        for (var i = 0; i < 12 && pages.Count == 0 && !lostPage; i++)
            await Pump(initial, 1);
        var firstLanded = pages is [{ Count: InMemoryStore.ParkPageLimit }] &&
            pages[0][0].Cursor.Key == kept.MinBy(row => row.Seq)!.Message.InboxKey;
        if (cutAfterPage is { } after)
        {
            var atCut = Read().Row;
            var rightBoundary = lostPage && !first.Owns && firstLanded == after &&
                atCut.ParkRecovery is { } recovery && (recovery.Cursor is not null) == after && probe.Restored == 0 &&
                store.Messages.GetValueOrDefault(recovery.Control) is { Done: false, Attempts: 0, Trial: 0 };
            check(rightBoundary, $"M3-2 and M3-9 the cursor reflects whether the lost page landed ({after})");
            if (!rightBoundary) return;
        }
        else
        {
            check(firstLanded, $"{caseId} {resolution}: page one commits the expected metadata prefix");
            if (!firstLanded) return;
        }
        if (arrival is LateArrival.AfterPage or LateArrival.Tied or LateArrival.BeforeTake)
            await AdmitEarlier(pages[0][^1].Cursor);
        if (aboveHighWater)
            for (var i = 0; i < 6; i++)
                await ingress.SendAsync(scope, target, $"unreleased-{i}", "late", "outside the recovery cohort");
        var firstEpoch = payloadBounds ? first.Fences.Single().Epoch : 0;
        var keepOwner = arrival is LateArrival.BeforePage or LateArrival.AfterPage;
        if (!keepOwner) await initial.DisposeAsync();
        now += cutAfterPage is null ? 1 : (long)Defaults.Lease.TotalMilliseconds + 1;
        var successor = keepOwner ? null : Worker("page-second");
        await using var replacement = successor is null ? null : await Owner(successor);
        if (arrival == LateArrival.AfterTake) await AdmitEarlier(pages[0][^1].Cursor);
        for (var i = 0; i < 16; i++)
        {
            now += 11;
            await Pump(replacement ?? initial, 1);
        }
        var row = store.Instances[checkpoint.Row.Id];
        var actual = pages.SelectMany(page => page).Select(item => item.Cursor.Key).ToArray();
        var expected = kept.OrderBy(item => item.Seq).Select(item => item.Message.InboxKey);
        int[] expectedPageSizes = (payloadBounds, resolution) switch
        {
            (true, Resolution.Resume) => [InMemoryStore.ParkPageLimit, 1, 1, 1, 1],
            (true, _) => [InMemoryStore.ParkPageLimit, largeBodies.Length],
            _ => [InMemoryStore.ParkPageLimit, 1]
        };
        var complete = pages.Select(page => page.Count).SequenceEqual(expectedPageSizes) && actual.SequenceEqual(expected) &&
              pages.All(page => Encoding.UTF8.GetByteCount(Json.Write(page)) <= ParkRecoveryCredit.LimitBytes) &&
              store.Messages.Values.All(message => message.KeptBy is null && message.Message.Kind != Kinds.Drain) &&
              row.ParkDiscard is null && row.ParkRecovery is null &&
              (resolution == Resolution.Delete ? row.Retired : Read().Park is null) &&
              (resolution != Resolution.Resume || probe.Restored == smallArrivals + largeBodies.Length);
        check(complete,
            $"{caseId} {resolution}: recovery finishes the suffix within the projection budget (pages {string.Join(",", pages.Select(p => p.Count))}; restored {probe.Restored}; rows match {actual.SequenceEqual(expected)}; faults {row.Fault}; pending {store.Messages.Values.Count(m => !m.Done)}; kept {store.Messages.Values.Count(m => m.KeptBy is not null)}; recovery {row.ParkRecovery?.Cursor}, {row.ParkRecovery?.Control})");

        if (payloadBounds)
        {
            var suffix = pages.Skip(1).SelectMany(page => page);
            check(complete && successor!.Fences.Single().Epoch > firstEpoch &&
                suffix.Select(item => item.PayloadBytes).SequenceEqual(largeBodies.Select(body => body.Length)),
                $"S-1 {resolution}: a real takeover precedes the suffix with the 5 MiB row first and each body size intact");
            string[] delivered = resolution == Resolution.Resume ? largeBodies : [];
            check(probe.LargeBodies.SequenceEqual(delivered),
                $"S-1 {resolution}: the actor observes exactly the expected large payloads at delivery");
        }

        if (aboveHighWater)
            check(observedPressure && probe.LateOffers == 6 && !probe.OfferedLateDuringRecovery,
                "M3-11 the partition remains above high water with unreleased arrivals while recovery advances, then offers those arrivals after Complete");
        if (arrival is not null)
            check(complete && probe.EndAfterRestored == firstBoundary && !probe.EndDuringRecovery &&
                probe.RestoredIds.SetEquals(restorable) &&
                row.Status == InstanceStatus.Finished,
                $"E-1–E-3 {arrival}: the late arrival ends the model after Complete and every restored row " +
                $"(ended after {probe.EndAfterRestored}; open at end {probe.EndDuringRecovery}; distinct {probe.RestoredIds.Count}; status {row.Status})");
        if (cutAfterPage is not null)
            check(complete && !privateTrial && probe.RestoredIds.SetEquals(restorable) && row.Status == InstanceStatus.Running,
                $"M3-2 and M3-9 the cold owner applies every input once without a trial on any drain control ({cutAfterPage})");

        async Task AdmitEarlier(ParkPageCursor cursor)
        {
            var accepted = await ingress.SendAsync(scope, new Contract.Target.Ref(actor), "late-finisher", "finish");
            var message = store.Messages.Values.Single(item => item.Message.Kind == "finish");
            if (arrival == LateArrival.Tied)
            {
                store.Messages.Remove(message.Message.InboxKey);
                message.Message = message.Message with { StorageId = "!" + cursor.Key.Id };
                store.Messages.Add(message.Message.InboxKey, message);
                message.Seq = cursor.Seq;
            }
            else message.Seq = cursor.Seq - 1;
            check(accepted.Outcome == Contract.AdmissionOutcome.Accepted && message is { Seen: 0, ReleasedBy: null, KeptBy: null } &&
                new ParkPageCursor(message.Seq, message.Message.InboxKey).CompareTo(cursor) < 0,
                $"E-1–E-3 {arrival}: the admitted unseen row orders below the cursor");
        }

        InMemoryStore.ActorCheckpoint Read() => store.ReadFoundationActor(scope.Tenant, actor.Id).Actor!;

        Worker Worker(string node) => new(node, 0, wire, new ActorAdapter(store, probe), new WorkerOptions
        {
            Monotonic = () => now - start,
            Backlog = aboveHighWater ? new(4, 2, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(10)) : null
        });
        static async Task<IScopeActivation> Owner(Worker worker) => (await new DurableHost([worker]).ActivateAsync(0))!;
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

    private sealed class Probe(string kind, Func<bool> recoveryOpen) : Contract.IActorType
    {
        public string Type => "park-page-limits";
        public int Restored { get; private set; }
        public HashSet<string> RestoredIds { get; } = [];
        public List<string> LargeBodies { get; } = [];
        public int? EndAfterRestored { get; private set; }
        public bool EndDuringRecovery { get; private set; }
        public int LateOffers { get; private set; }
        public bool OfferedLateDuringRecovery { get; private set; }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var input in turn.Inbox.Where(input => input.Kind == kind))
            {
                if (input.Payload == "poison") throw new InvalidOperationException("retain the first maximal input");
                Restored++;
                RestoredIds.Add(input.Id);
                if (input.Payload is { Length: > 1_048_576 } body) LargeBodies.Add(body);
            }
            var late = turn.Inbox.Count(input => input.Kind == "late");
            LateOffers += late;
            OfferedLateDuringRecovery |= late != 0 && recoveryOpen();
            if (turn.Inbox.Any(input => input.Kind == "finish"))
            {
                EndAfterRestored = Restored;
                EndDuringRecovery = recoveryOpen();
                turn.End(Contract.ActorStatus.Finished);
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
