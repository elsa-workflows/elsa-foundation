namespace Kernel.Durable;

internal readonly record struct ParkRecoveryCredit(bool GateReady, int Rows, long Bytes)
{
    internal const int LimitBytes = 4 * 1024 * 1024;
    internal bool CanRead => GateReady && Rows < InMemoryStore.ParkPageLimit && Bytes < LimitBytes;
    internal static bool IsTicket(string kind) => kind is Kinds.Job or Kinds.Meet or Kinds.Publish;

    // The in-memory projection charges the full metadata allowance until providers report stored sizes.
    internal static long StoredBytes(int payloadBytes) => payloadBytes + 24L * 1024;
}

public sealed partial class InMemoryStore
{
    internal ParkPageCursor? FirstUnheldRecoveryInput(InstanceRow actor, ParkRelease release, Predicate<InboxKey> isHeld)
    {
        lock (_gate)
        {
            ParkPageCursor? first = null;
            foreach (var (key, row) in Messages)
            {
                if (!PendingRecoveryInput(actor, release, row) || isHeld(key)) continue;
                var candidate = new ParkPageCursor(row.Seq, key);
                if (first is null || candidate.CompareTo(first.Value) < 0) first = candidate;
            }
            return first;
        }
    }

    private static bool PendingRecoveryInput(InstanceRow actor, ParkRelease release, MessageRow row) =>
        !row.Done && row.ReleasedBy == release && row.Message.Target.Id == actor.Id &&
        row.Message.TargetCreation == actor.Creation && !ParkRecoveryCredit.IsTicket(row.Message.Kind);

    internal ParkRecoveryCredit FoundationRecoveryCredit(InstanceRow actor, Func<InboxKey, bool> handedOffHere)
    {
        lock (_gate)
        {
            var gate = actor.ParkRecovery!.Gate;
            var ready = gate is not { } key || Messages.GetValueOrDefault(key) is not { Done: false } pending ||
                ParkRecoveryCredit.IsTicket(pending.Message.Kind) || handedOffHere(key);
            var release = new ParkRelease(actor.Creation, actor.ParkResolutionGeneration);
            var rows = 0;
            var bytes = 0L;
            foreach (var (id, row) in Messages)
            {
                if (!PendingRecoveryInput(actor, release, row) || handedOffHere(id))
                    continue;
                rows++;
                bytes += ParkRecoveryCredit.StoredBytes(row.PayloadLength);
                if (rows >= ParkPageLimit || bytes >= ParkRecoveryCredit.LimitBytes) break;
            }
            return new(ready, rows, bytes);
        }
    }

    private (long At, bool Due)? ValidateRecoveryOpen(TurnCommit turn, InstanceRow? previous,
        IReadOnlyDictionary<int, long> epochs, long now)
    {
        if (previous is not { Status: InstanceStatus.Faulted, Retired: false, ParkDiscard: null, ParkDiscarded: false } ||
            turn.Row is not { ParkRecovery: { Cursor: null, Restored: 0, Cleared: 0, Gate: null } recovery } next ||
            next != (previous with
            {
                Revision = checked(previous.Revision + 1), Status = InstanceStatus.Running, Fault = null,
                ParkResolutionGeneration = checked(previous.ParkResolutionGeneration + 1), ParkRecovery = recovery, ParkCohort = null
            }) || StoreBatchRules.AddressPartition(previous.Id) is not { } partition || !epochs.ContainsKey(partition) ||
            turn.OpensPark || turn.Delete || turn.FoundationParkClear is not null ||
            turn.FoundationKept is not null || turn.FoundationHeader is not null ||
            turn.Held is { Count: > 0 } || turn.FoundationHeldPromotions is { Count: > 0 } ||
            turn.FoundationReleasedHolds is { Count: > 0 } ||
            turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 || turn.Unsubscribe ||
            turn.Done is not [var request] || turn.Sent is not [var control] ||
            control.Due is not null || control.KeepUntil is not null ||
            Messages.GetValueOrDefault(request) is not { Done: false, Message: { Kind: "kernel.recover" } input } ||
            input.Target.Id != next.Id || input.TargetCreation != next.Creation || input.Due > now ||
            input.Operator is not { Selection: { } selection, RecoverMode: { } mode } ||
            mode != recovery.Mode || input.Id != recovery.Operation)
            throw InvalidDrain();

        var address = new CreationAddress(next.Id, next.Creation);
        if (!_foundationParks.TryGetValue(address, out var park) ||
            !_referenceBirths.TryGetValue(address, out var binding) ||
            !MatchesFoundationSelection(binding.Tenant, binding.Reference, selection))
            throw InvalidDrain();
        var expiry = _foundationParkExpiries.GetValueOrDefault(address);
        var recalled = expiry?.Armed is { } armed ? turn.Consumed is [var key] && key == armed : turn.Consumed.Count == 0;
        if (!recalled || recovery.Serial != checked((expiry?.Serial ?? 0) + 1) ||
            !ParkDrainEnvelope.For(next, park).MatchesIssued(control, next, recovery.Serial, recovery.Control))
            throw InvalidDrain();
        if (expiry is null) return null;
        if (now >= expiry.At)
            throw new StoreException(Failure.WindowExpired, "The Park expiry became due before recovery opened.");
        return (expiry.At, false);
    }

    private void ValidateRecoveryInterruption(TurnCommit turn, InstanceRow previous, IReadOnlyDictionary<int, long> epochs)
    {
        var drain = previous.ParkRecovery!;
        var next = turn.Row!;
        var address = new CreationAddress(previous.Id, previous.Creation);
        if (previous.Status != InstanceStatus.Running || next != (previous with
            {
                Revision = checked(previous.Revision + 1), Status = InstanceStatus.Faulted, Fault = next.Fault,
                ParkRecovery = null, ParkCohort = null
            }) || StoreBatchRules.AddressPartition(previous.Id) is not { } partition || !epochs.ContainsKey(partition) ||
            !_foundationParks.TryGetValue(address, out var park) ||
            turn.OpensPark || turn.Delete || !turn.Unsubscribe || turn.FoundationParkClear is not null ||
            turn.FoundationRecoveryPage is not null || turn.FoundationHeader is not null ||
            turn.FoundationKept!.Values.Any(reason => reason != ParkKeepReason.Consumed) ||
            turn.Consumed is not [var control] || control != drain.Control)
            throw InvalidDrain();

        var expiry = _foundationParkExpiries.GetValueOrDefault(address);
        if (expiry is null)
        {
            if (turn.Sent.Count != 0) throw InvalidDrain();
            return;
        }
        if (expiry.Armed is not null || turn.Sent is not [var replacement] || !replacement.HasIssuedTicket ||
            replacement.Due != expiry.At || replacement.KeepUntil is not null ||
            !ParkDrainEnvelope.For(next, park).MatchesExpiry(replacement, next,
                expiry with { Serial = checked(drain.Serial + 1), Armed = replacement.InboxKey }))
            throw InvalidDrain();
    }

    private void ValidateRecoveryStep(TurnCommit turn, InstanceRow? previous, IReadOnlyDictionary<int, long> epochs, long now)
    {
        var page = turn.FoundationRecoveryPage;
        if (previous is not { ParkRecovery: { } before, ParkDiscard: null, Retired: false } ||
            turn.Row is not { } next || next.Revision != checked(previous.Revision + 1) ||
            StoreBatchRules.AddressPartition(previous.Id) is not { } partition || !epochs.ContainsKey(partition) ||
            turn.OpensPark || turn.Delete || turn.FoundationParkClear is not null ||
            turn.Consumed is not [var key] || key != before.Control || turn.Done.Count != 0 ||
            turn.Held is { Count: > 0 } || turn.FoundationReleasedHolds is { Count: > 0 } ||
            turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 || turn.Unsubscribe ||
            turn.FoundationHeader is not null || turn.FoundationKept is not null ||
            turn.FoundationHeldPromotions is { Count: > 0 } ||
            Messages.GetValueOrDefault(key) is not { Done: false } input || input.Message.Due > now)
            throw InvalidDrain();

        var address = new CreationAddress(previous.Id, previous.Creation);
        var release = new ParkRelease(address.Creation, previous.ParkResolutionGeneration);
        if (!_foundationParks.TryGetValue(address, out var park) || _foundationParkExpiries.GetValueOrDefault(address)?.Armed is not null)
            throw InvalidDrain();
        var envelope = ParkDrainEnvelope.For(previous, park);
        if (!envelope.Matches(input.Message, previous, before.Control)) throw InvalidDrain();
        var suspended = previous.Status == InstanceStatus.Running && previous.Suspended;

        if (next.ParkRecovery is not { } after)
        {
            ParkRelease? remaining = FirstUnheldRecoveryInput(previous, release, _ => false) is null ? null : release;
            if (page is not null || turn.Sent.Count != 0 || suspended ||
                next != (previous with { Revision = next.Revision, ParkRecovery = null, ParkCohort = remaining }) ||
                ReadFoundationRecoveryPage(address, park, release.Generation, after: before.Cursor, limit: 1) is not { Count: 0 })
                throw InvalidDrain();
            return;
        }

        if (next != (previous with { Revision = next.Revision, ParkRecovery = after }) ||
            turn.Sent is not [var control] || control.KeepUntil is not null ||
            after.Serial != checked(before.Serial + 1) || !envelope.MatchesIssued(control, next, after.Serial, after.Control))
            throw InvalidDrain();
        var rescheduled = before with { Serial = after.Serial, Control = control.InboxKey };
        if (page is null)
        {
            if (control.Due is null || after != rescheduled) throw InvalidDrain();
            return;
        }
        if (suspended || page.Park != park || control.Due is not null) throw InvalidDrain();

        var current = ReadFoundationRecoveryPage(address, park, release.Generation, before.Cursor, page.Rows.Count);
        if (current is null) throw InvalidDrain();
        if (!current.SequenceEqual(page.Rows))
            throw new StoreException(Failure.Unknown, "The Park recovery inputs changed before the page committed.");

        var restored = current.Where(row => before.Restores(row, previous.Status)).ToArray();
        var gate = restored.LastOrDefault()?.Cursor.Key;
        if (after != (rescheduled with
            {
                Cursor = current[^1].Cursor,
                Restored = checked(before.Restored + restored.Length),
                Cleared = checked(before.Cleared + current.Count - restored.Length), Gate = gate
            }))
            throw InvalidDrain();
    }
}
