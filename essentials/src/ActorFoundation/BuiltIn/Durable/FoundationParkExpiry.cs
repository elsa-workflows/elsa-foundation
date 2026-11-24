namespace Kernel.Durable;

internal sealed record ParkExpiry(long At, long Serial, InboxKey? Armed);

public sealed partial class InMemoryStore
{
    private readonly Dictionary<CreationAddress, ParkExpiry> _foundationParkExpiries = [];

    private readonly TimeSpan? _parkedRetentionWindow;
    internal TimeSpan? ParkedRetentionWindow
    {
        get => _parkedRetentionWindow;
        init
        {
            if (value is { } window) ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
            _parkedRetentionWindow = value;
        }
    }

    internal ParkExpiry? FoundationExpiry(InstanceRow row)
    {
        lock (_gate)
            return _foundationParkExpiries.GetValueOrDefault(new(row.Id, row.Creation));
    }

    private Message? PrepareInitialParkExpiry(TurnCommit turn, InstanceRow? previous, long now)
    {
        if (!turn.OpensPark || ParkedRetentionWindow is not { } window || turn.Row is not { Status: InstanceStatus.Faulted } row)
            return null;

        if (previous is not (null or { Status: InstanceStatus.Running }) || row.Retired || turn.Delete)
            throw InvalidDrain();

        var at = (decimal)now + (long)window.TotalMilliseconds;
        if (at > long.MaxValue)
            throw new StoreException(Failure.Definite, "The Park expiry is outside the store clock range.");
        var expiry = ParkDrainEnvelope.For(row, new(row.Revision, now)).Issue(row, serial: 1, expiryAt: (long)at);
        if (!FoundationMetadataLimits.FitsComposed(expiry))
            throw new StoreException(Failure.Definite, "The Park expiry exceeds the foundation row budget.");
        return expiry;
    }

    private void RecordParkExpiry(TurnCommit turn, InstanceRow row, CreationAddress address, Message? firstExpiry)
    {
        if (firstExpiry is not null)
        {
            _foundationParkExpiries[address] = new(firstExpiry.Due!.Value, 1, firstExpiry.InboxKey);
        }
        else if (_foundationParkExpiries.TryGetValue(address, out var expiry))
        {
            if (row.ParkRecovery is { } recovery)
                _foundationParkExpiries[address] = expiry with { Armed = null, Serial = recovery.Serial };
            else if (row.ParkDiscarded || row.Status == InstanceStatus.Running)
                _foundationParkExpiries.Remove(address);
            else if (row.ParkDiscard is { } drain && expiry.Armed is not null)
                _foundationParkExpiries[address] = expiry with { Armed = null, Serial = drain.Serial };
            else if (turn.Sent.FirstOrDefault(message => message.Kind == Kinds.ParkExpiry) is { } replacement)
                _foundationParkExpiries[address] = expiry with { Armed = replacement.InboxKey, Serial = expiry.Serial + 1 };
        }
    }

    private bool IsOutstandingExpiry(InboxKey key) =>
        key.Scope == ParkDrainEnvelope.Scope && Messages.GetValueOrDefault(key) is { Done: false, Message: var stored } &&
        _foundationParkExpiries.GetValueOrDefault(new(stored.Target.Id, stored.TargetCreation ?? 0))?.Armed == key;
}
