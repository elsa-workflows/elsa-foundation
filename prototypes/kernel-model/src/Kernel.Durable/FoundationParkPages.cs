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

internal readonly record struct ParkRelease(long Creation, long Generation);

internal sealed record ParkPageRow(ParkPageCursor Cursor, string Kind, string? Sender, ParkKeepReason? Reason, int PayloadBytes)
{
    internal ParkRelease? ReleasedBy { get; init; }
    internal bool ObsoleteJob { get; init; }
}

internal sealed record ParkPageClear(InMemoryStore.FoundationParkMark Park, IReadOnlyList<ParkPageRow> Rows);

internal enum ParkRecoveryMode { Resume, Retry, RetryAll }

internal sealed record ParkRecoveryDrain(string Operation, ParkRecoveryMode Mode, ParkPageCursor? Cursor,
    long Serial, InboxKey Control)
{
    internal long Restored { get; init; }
    internal long Cleared { get; init; }
    internal InboxKey? Gate { get; init; }

    internal bool Restores(ParkPageRow row, InstanceStatus status) => row.Kind == Kinds.Publish ||
        status != InstanceStatus.Running && row.Kind == Kinds.ChildFinished ||
        (status == InstanceStatus.Running && row.Kind != Kinds.Meet && !row.ObsoleteJob &&
            (row.Reason is null or ParkKeepReason.Arrived || Mode == ParkRecoveryMode.RetryAll ||
                Mode == ParkRecoveryMode.Retry && row.Reason == ParkKeepReason.Selected));
}

internal sealed record ParkRecoveryPage(InMemoryStore.FoundationParkMark Park, IReadOnlyList<ParkPageRow> Rows, bool TerminalInputs = false);

public sealed partial class InMemoryStore
{
    internal const int ParkPageLimit = 170;

    // The reference store scans its dictionary but retains at most one page of keys. Providers seek on
    // the target and tuple. The projection cannot carry the message or its payload into a drain turn.
    internal IReadOnlyList<ParkPageRow>? ReadFoundationKeptPage(CreationAddress address, FoundationParkMark park,
        ParkPageCursor? after, int limit = ParkPageLimit) =>
        ReadFoundationParkPage(address, park, after, limit, recoveryGeneration: null);

    internal IReadOnlyList<ParkPageRow>? ReadFoundationRecoveryPage(CreationAddress address, FoundationParkMark park,
        long generation, ParkPageCursor? after, int limit = ParkPageLimit) =>
        ReadFoundationParkPage(address, park, after, limit, generation);

    internal IReadOnlyList<ParkPageRow>? ReadEndedFoundationRecoveryPage(CreationAddress address, FoundationParkMark park,
        long generation, int limit = ParkPageLimit) =>
        ReadFoundationParkPage(address, park, after: null, limit, generation, terminalInputs: true);

    private IReadOnlyList<ParkPageRow>? ReadFoundationParkPage(CreationAddress address, FoundationParkMark park,
        ParkPageCursor? after, int limit, long? recoveryGeneration, bool terminalInputs = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, ParkPageLimit);
        lock (_gate)
        {
            if (Instances.GetValueOrDefault(address.Id) is not { Retired: false } current ||
                current.Creation != address.Creation || _foundationParks.GetValueOrDefault(address) != park ||
                recoveryGeneration is { } generation && (current.ParkRecovery is null ||
                    generation <= 0 || current.ParkResolutionGeneration != generation) ||
                terminalInputs && current.Status == InstanceStatus.Running)
                return null;

            var page = new PriorityQueue<MessageRow, ParkPageCursor>(limit,
                Comparer<ParkPageCursor>.Create(static (left, right) => right.CompareTo(left)));
            foreach (var (key, row) in Messages)
            {
                if (row.Message.Target.Id != address.Id || row.Message.TargetCreation != address.Creation) continue;
                var kept = row.Done && row.KeptBy is not null;
                var stale = !row.Done && current.Status == InstanceStatus.Running && recoveryGeneration is { } active &&
                    row.ReleasedBy is { } release && release.Creation == address.Creation && release.Generation < active;
                var settling = terminalInputs && !row.Done && row.ReleasedBy is { } tag && tag.Creation == address.Creation &&
                    tag.Generation <= recoveryGeneration && !ParkRecoveryCredit.IsTicket(row.Message.Kind) &&
                    row.Message.Kind != Kinds.ChildFinished && !row.Message.IsControl;
                if (terminalInputs ? !settling : !kept && !stale) continue;

                var cursor = new ParkPageCursor(row.Seq, key);
                if (after is { } previous && cursor.CompareTo(previous) <= 0) continue;
                if (page.Count == limit) page.EnqueueDequeue(row, cursor);
                else page.Enqueue(row, cursor);
            }

            var currentJobs = recoveryGeneration is null || current.Jobs is null ? null
                : JobRegistry.Read(current.Jobs).SelectMany(job => new[]
                {
                    InboxKey.Ticket(current.Id, job.Key, current.Creation, job.Generation),
                    InboxKey.JobResult(current.Id, job.Key, current.Creation, job.Generation)
                }).ToHashSet();
            var result = new ParkPageRow[page.Count];
            for (var index = result.Length - 1; index >= 0; index--)
            {
                page.TryDequeue(out var row, out var cursor);
                result[index] = new(cursor, row!.Message.Kind, row.Message.Sender, row.KeptBy, row.PayloadLength)
                {
                    ReleasedBy = row.ReleasedBy,
                    ObsoleteJob = recoveryGeneration is not null && (row.Message.Kind == Kinds.Job || row.Message.FoundationJobResult) &&
                        currentJobs?.Contains(cursor.Key) != true
                };
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
                stored.Seq != row.Cursor.Seq || stored.KeptBy != row.Reason || stored.ReleasedBy != row.ReleasedBy)
                return row;
        return null;
    }

    private void ApplyFoundationRecoveryPage(TurnCommit turn, long now)
    {
        if (turn.FoundationRecoveryPage is not { } page) return;
        var checkpoint = turn.Row!;
        var drain = checkpoint.ParkRecovery!;
        var release = new ParkRelease(checkpoint.Creation, checkpoint.ParkResolutionGeneration);
        var until = now + (long)FoundationIngressWindow(null).TotalMilliseconds;
        foreach (var row in page.Rows)
        {
            var stored = Messages[row.Cursor.Key];
            stored.Message = stored.Message with { KeepUntil = until };
            stored.KeptBy = null;
            if (page.TerminalInputs)
            {
                // The new Done marker removes this input from the next terminal page.
                (stored.Done, stored.Held, stored.Trial, stored.Attempts) = (true, false, 0, 0);
                continue;
            }
            if (!drain.Restores(row, checkpoint.Status))
            {
                (stored.Done, stored.Held, stored.Trial) = (true, false, 0);
                continue;
            }
            (stored.Done, stored.Held, stored.ParkDeadLetter) = (false, false, false);
            (stored.Seen, stored.Trial, stored.Attempts) = (0, 0, 0);
            stored.ReleasedBy = release;
        }
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
