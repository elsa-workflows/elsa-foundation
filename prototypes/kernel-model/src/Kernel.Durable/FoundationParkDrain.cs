namespace Kernel.Durable;

internal sealed record ParkDiscardDrain(ParkPageCursor? Cursor, long Cleared, long Serial, InboxKey Control);

internal sealed record ParkDrainEnvelope(long Creation, long ParkRevision, long ParkAt, long Generation)
{
    internal const long Scope = 1L << 62;

    internal static ParkDrainEnvelope For(InstanceRow row, InMemoryStore.FoundationParkMark park) =>
        new(row.Creation, park.Revision, park.At, row.ParkResolutionGeneration);

    internal Message Issue(InstanceRow row, long serial)
    {
        var key = Key(row, serial);
        return new Message(key.Id, new(row.Id), Kinds.Drain, Json.Write(this), row.Id, TargetCreation: row.Creation,
                SenderCreation: row.Creation)
            .WithInboxScope(key.Scope)
            .AsKernelTicket();
    }

    private InboxKey Key(InstanceRow row, long serial)
    {
        var origin = CreationProtocol.Origin(row.Id, row.Creation);
        return new(FormattableString.Invariant($"{origin}#park:{ParkRevision}:{ParkAt}:{Generation}:{serial}"), Scope);
    }

    internal bool Matches(Message message, InstanceRow row, InboxKey control)
    {
        if (message.Kind != Kinds.Drain || message.InboxScope != Scope || message.EventMatch is not null ||
            message.InboxKey != control || message.Sender != row.Id || message.SenderCreation != Creation ||
            message.Target.Id != row.Id || message.TargetCreation != Creation || message.Start is not null)
            return false;

        try { return message.Payload is not null && message.Read<ParkDrainEnvelope>() == this; }
        catch (System.Text.Json.JsonException) { return false; }
    }

    internal bool MatchesIssued(Message message, InstanceRow row, ParkDiscardDrain drain) =>
        message.HasIssuedTicket && drain.Control == Key(row, drain.Serial) && Matches(message, row, drain.Control);

    internal static bool IsInDomain(Message message) =>
        message.Kind == Kinds.Drain || message.InboxScope == Scope;
}

public sealed partial class InMemoryStore
{
    private void ValidateParkDrain(TurnCommit turn, InstanceRow? previous, IReadOnlyDictionary<int, long> epochs)
    {
        var next = turn.Row;
        var before = previous?.ParkDiscard;
        var after = next?.ParkDiscard;
        if (before is not null && (turn.Delete || turn.FoundationKept is { Count: > 0 })) throw InvalidDrain();
        if (!turn.Sent.Any(ParkDrainEnvelope.IsInDomain) && (next is null || turn.Delete || before == after &&
            (previous?.ParkDiscarded ?? false) == next.ParkDiscarded &&
            (previous?.ParkResolutionGeneration ?? 0) == next.ParkResolutionGeneration))
            return;

        if (next is not { Status: InstanceStatus.Faulted, Retired: false } ||
            previous is not { Status: InstanceStatus.Faulted, Retired: false } || previous.Creation != next.Creation ||
            next.Revision != previous.Revision + 1 ||
            StoreBatchRules.AddressPartition(next.Id) is not { } partition || !epochs.ContainsKey(partition) ||
            _foundationParks.GetValueOrDefault(new(next.Id, next.Creation)) is not { } park)
            throw InvalidDrain();

        var inputs = turn.Consumed.Concat(turn.Done).ToArray();
        if (inputs.Length != 1 || Messages.GetValueOrDefault(inputs[0]) is not { Done: false, Message: { } input } ||
            input.Target.Id != next.Id || input.TargetCreation != next.Creation)
            throw InvalidDrain();

        var controls = turn.Sent.Where(ParkDrainEnvelope.IsInDomain).ToArray();
        var envelope = ParkDrainEnvelope.For(next, park);
        if (before is null)
        {
            if (after is not { Cursor: null, Cleared: 0, Serial: 1 } || next.ParkDiscarded ||
                next.ParkResolutionGeneration != previous.ParkResolutionGeneration + 1 || turn.FoundationParkClear is not null ||
                input is not { Kind: "kernel.discard", Operator.Selection: not null })
                throw InvalidDrain();
        }
        else if (next.ParkResolutionGeneration != previous.ParkResolutionGeneration || !envelope.Matches(input, previous, before.Control))
            throw InvalidDrain();
        else if (after is null)
        {
            if (!next.ParkDiscarded || controls.Length != 0 || turn.FoundationParkClear is not null ||
                ReadFoundationKeptPage(new(next.Id, next.Creation), park, after: null, limit: 1) is not { Count: 0 })
                throw InvalidDrain();
            return;
        }
        else if (turn.FoundationParkClear is not { } page || page.Park != park || next.ParkDiscarded ||
            after.Cursor != page.Rows[^1].Cursor || after.Cleared != before.Cleared + page.Rows.Count ||
            after.Serial != before.Serial + 1 ||
            before.Cursor is { } previousCursor && page.Rows[0].Cursor.CompareTo(previousCursor) <= 0)
            throw InvalidDrain();

        if (controls.Length != 1 || controls[0].Due is not null || controls[0].KeepUntil is not null ||
            !envelope.MatchesIssued(controls[0], next, after))
            throw InvalidDrain();
    }

    private static StoreException InvalidDrain() =>
        new(Failure.Definite, "A Park drain must consume its exact input and advance the same fenced checkpoint.");
}
