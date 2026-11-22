namespace Kernel.Durable;

internal readonly record struct ParkPageCursor(long Seq, InboxKey Key) : IComparable<ParkPageCursor>
{
    public int CompareTo(ParkPageCursor other)
    {
        var order = Seq.CompareTo(other.Seq);
        if (order != 0) return order;
        order = StringComparer.Ordinal.Compare(Key.Id, other.Key.Id);
        if (order != 0) return order;
        order = Key.Scope.CompareTo(other.Key.Scope);
        return order != 0 ? order : StringComparer.Ordinal.Compare(Key.Match, other.Key.Match);
    }
}

internal sealed record ParkPageRow(ParkPageCursor Cursor, string Kind, string? Sender, ParkKeepReason Reason, int PayloadBytes);

internal sealed record ParkPageClear(InMemoryStore.FoundationParkMark Park, IReadOnlyList<ParkPageRow> Rows);

public sealed partial class InMemoryStore
{
    internal const int ParkPageLimit = 170;

    // The reference store scans its dictionary but retains at most one page of keys. Providers seek on
    // the target and tuple. The projection cannot carry the message or its payload into a drain turn.
    internal IReadOnlyList<ParkPageRow>? ReadFoundationKeptPage(CreationAddress address, FoundationParkMark park,
        ParkPageCursor? after, int limit = ParkPageLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, ParkPageLimit);
        lock (_gate)
        {
            if (Instances.GetValueOrDefault(address.Id) is not { Retired: false } current ||
                current.Creation != address.Creation || _foundationParks.GetValueOrDefault(address) != park)
                return null;

            var page = new PriorityQueue<MessageRow, ParkPageCursor>(limit,
                Comparer<ParkPageCursor>.Create(static (left, right) => right.CompareTo(left)));
            foreach (var (key, row) in Messages)
            {
                if (!row.Done || row.KeptBy is null ||
                    row.Message.Target.Id != address.Id || row.Message.TargetCreation != address.Creation)
                    continue;

                var cursor = new ParkPageCursor(row.Seq, key);
                if (after is { } previous && cursor.CompareTo(previous) <= 0) continue;
                if (page.Count == limit) page.EnqueueDequeue(row, cursor);
                else page.Enqueue(row, cursor);
            }

            var result = new ParkPageRow[page.Count];
            for (var index = result.Length - 1; index >= 0; index--)
            {
                page.TryDequeue(out var row, out var cursor);
                result[index] = new(cursor, row!.Message.Kind, row.Message.Sender, row.KeptBy!.Value, row.KeptPayloadBytes);
            }
            return result;
        }
    }

    private ParkPageRow? ChangedFoundationParkInput(TurnCommit turn, InstanceRow? previous, IReadOnlyDictionary<int, long> epochs)
    {
        if (turn.FoundationParkClear is not { } page) return null;
        if (turn.Row is not { Retired: false, Status: InstanceStatus.Faulted } checkpoint ||
            previous is not { Retired: false, Status: InstanceStatus.Faulted } || previous.Creation != checkpoint.Creation ||
            StoreBatchRules.AddressPartition(checkpoint.Id) is not { } partition || !epochs.ContainsKey(partition) ||
            _foundationParks.GetValueOrDefault(new(checkpoint.Id, checkpoint.Creation)) != page.Park)
            throw new StoreException(Failure.Definite, "A Park clear requires its current checkpoint and its own fence.");

        foreach (var row in page.Rows)
            if (Messages.GetValueOrDefault(row.Cursor.Key) is not { Done: true } stored ||
                stored.Message.Target.Id != checkpoint.Id || stored.Message.TargetCreation != checkpoint.Creation ||
                stored.Seq != row.Cursor.Seq || stored.KeptBy != row.Reason)
                return row;
        return null;
    }

    private void ClearFoundationParkPage(ParkPageClear? page, long now)
    {
        if (page is null) return;
        var until = now + (long)FoundationIngressWindow(null).TotalMilliseconds;
        foreach (var row in page.Rows)
        {
            var stored = Messages[row.Cursor.Key];
            stored.KeptBy = null;
            stored.ParkDeadLetter = stored.Message.Kind == Kinds.Publish;
            stored.Message = stored.Message with { KeepUntil = until };
        }
    }
}
