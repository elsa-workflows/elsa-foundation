namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    private void ValidateRecoveryPage(TurnCommit turn, InstanceRow? previous, IReadOnlyDictionary<int, long> epochs, long now)
    {
        var page = turn.FoundationRecoveryPage!;
        if (previous is not { ParkRecovery: { } before, ParkDiscard: null, Retired: false, Suspended: false } ||
            turn.Row is not { ParkRecovery: { } after } next ||
            next != (previous with { Revision = previous.Revision + 1, ParkRecovery = after }) ||
            StoreBatchRules.AddressPartition(previous.Id) is not { } partition || !epochs.ContainsKey(partition) ||
            _foundationParkExpiries.GetValueOrDefault(new(previous.Id, previous.Creation))?.Armed is not null ||
            turn.Consumed is not [var key] || key != before.Control || turn.Done.Count != 0 ||
            turn.Held is { Count: > 0 } || turn.FoundationReleasedHolds is { Count: > 0 } ||
            turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 || turn.Unsubscribe ||
            turn.FoundationHeader is not null || turn.FoundationKept is not null ||
            turn.FoundationHeldPromotions is { Count: > 0 } ||
            Messages.GetValueOrDefault(key) is not { Done: false } input || input.Message.Due > now ||
            turn.Sent is not [var control] || control.Due is not null || control.KeepUntil is not null)
            throw InvalidDrain();

        var envelope = ParkDrainEnvelope.For(previous, page.Park);
        var address = new CreationAddress(previous.Id, previous.Creation);
        if (!envelope.Matches(input.Message, previous, before.Control) ||
            after.Serial != checked(before.Serial + 1) || !envelope.MatchesIssued(control, next, after.Serial, after.Control))
            throw InvalidDrain();

        var current = ReadFoundationRecoveryPage(address, page.Park, previous.ParkResolutionGeneration, before.Cursor, page.Rows.Count);
        if (current is null) throw InvalidDrain();
        if (!current.SequenceEqual(page.Rows))
            throw new StoreException(Failure.Unknown, "The Park recovery inputs changed before the page committed.");

        var restored = current.Where(row => before.Restores(row, previous.Status)).ToArray();
        var gate = restored.LastOrDefault()?.Cursor.Key;
        if (after != (before with
            {
                Cursor = current[^1].Cursor, Control = control.InboxKey, Serial = after.Serial,
                Restored = checked(before.Restored + restored.Length),
                Cleared = checked(before.Cleared + current.Count - restored.Length), Gate = gate
            }))
            throw InvalidDrain();
    }
}
