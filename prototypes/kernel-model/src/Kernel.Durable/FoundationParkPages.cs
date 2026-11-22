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
    internal long? JobGeneration { get; init; }
    internal ParkPageRow? PairedJobInput { get; init; }
}

internal sealed record ParkPageClear(InMemoryStore.FoundationParkMark Park, IReadOnlyList<ParkPageRow> Rows);

internal sealed record ParkRecoveryDeletion(string Operation, string? Selection, bool ThenDelete = false);

internal enum ParkRecoveryMode { Resume, Retry, RetryAll }

internal sealed record ParkRecoveryDrain(string Operation, ParkRecoveryMode Mode, ParkPageCursor? Cursor,
    long Serial, InboxKey Control)
{
    internal long Restored { get; init; }
    internal long Cleared { get; init; }
    internal InboxKey? Gate { get; init; }
    internal int AlreadyRearmed { get; init; }
    internal ParkRecoveryDeletion? Deletion { get; init; }

    internal bool Restores(ParkPageRow row, InstanceStatus status) => row.Kind == Kinds.Publish ||
        status != InstanceStatus.Running && row.Kind == Kinds.ChildFinished ||
        status == InstanceStatus.Running && row.Kind != Kinds.Meet && !row.ObsoleteJob && RestoresKept(row.Reason);

    internal bool RestoresKept(ParkKeepReason? reason) =>
        reason is null or ParkKeepReason.Arrived || Mode == ParkRecoveryMode.RetryAll ||
        Mode == ParkRecoveryMode.Retry && reason == ParkKeepReason.Selected;
}

internal sealed record ParkRecoveryPage(InMemoryStore.FoundationParkMark Park, IReadOnlyList<ParkPageRow> Rows, bool TerminalInputs = false)
{
    internal IEnumerable<ParkPageRow> Writes()
    {
        foreach (var row in Rows)
        {
            if (row.PairedJobInput is { } paired) yield return paired;
            yield return row;
        }
    }
}

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

            var jobs = recoveryGeneration is null || current.Jobs is null ? null : JobRegistry.Read(current.Jobs);
            var currentJobs = jobs?.SelectMany(job => new[]
                {
                    InboxKey.Ticket(current.Id, job.Key, current.Creation, job.Generation),
                    InboxKey.JobResult(current.Id, job.Key, current.Creation, job.Generation)
                }).ToHashSet();
            HashSet<InboxKey>? restartedTickets = null;
            if (terminalInputs && jobs is not null && current.ParkRestartedJobs is not null)
            {
                var restarts = ReadJobRestarts(current.ParkRestartedJobs);
                // New tickets written by Open have no release tag and have not entered the model.
                restartedTickets = jobs.Where(job => restarts.GetValueOrDefault(JobRestartDigest(job.Key), -1) == job.Generation)
                    .Select(job => InboxKey.Ticket(current.Id, job.Key, current.Creation, job.Generation)).ToHashSet();
            }
            var page = new PriorityQueue<MessageRow, ParkPageCursor>(limit,
                Comparer<ParkPageCursor>.Create(static (left, right) => right.CompareTo(left)));
            foreach (var (key, row) in Messages)
            {
                if (row.Message.Target.Id != address.Id || row.Message.TargetCreation != address.Creation) continue;
                var kept = row.Done && row.KeptBy is not null;
                var stale = !row.Done && current.Status == InstanceStatus.Running && recoveryGeneration is { } active &&
                    row.ReleasedBy is { } release && release.Creation == address.Creation && release.Generation < active;
                var settling = terminalInputs && !row.Done && (restartedTickets?.Contains(key) == true ||
                    row.Message is { Kind: Kinds.JobFinished, FoundationJobResult: true } && currentJobs?.Contains(key) == true ||
                    row.ReleasedBy is { } tag && tag.Creation == address.Creation && tag.Generation <= recoveryGeneration) &&
                    (row.Message.Kind == Kinds.Job || !ParkRecoveryCredit.IsTicket(row.Message.Kind)) &&
                    row.Message.Kind != Kinds.ChildFinished && !row.Message.IsControl;
                if (terminalInputs ? !settling : !kept && !stale) continue;

                var cursor = new ParkPageCursor(row.Seq, key);
                if (after is { } previous && cursor.CompareTo(previous) <= 0) continue;
                if (page.Count == limit) page.EnqueueDequeue(row, cursor);
                else page.Enqueue(row, cursor);
            }

            var recovery = current.ParkRecovery;
            var deadlineJobs = !terminalInputs && recovery?.Mode is (ParkRecoveryMode.RetryAll or ParkRecoveryMode.Retry) &&
                current.Status == InstanceStatus.Running ? jobs?.ToDictionary(job => job.Key, job => job.Generation, StringComparer.Ordinal) : null;
            var result = new List<ParkPageRow>(page.Count);
            foreach (var (row, cursor) in page.UnorderedItems.OrderBy(item => item.Priority))
            {
                var paired = deadlineJobs is not null ? ReadDeadlinePrerequisite(current, row, cursor, deadlineJobs, recovery!)
                    : recoveryGeneration is not null && current.Status != InstanceStatus.Running &&
                        row.Message.Kind == Kinds.Job && currentJobs?.Contains(cursor.Key) == true
                        ? ReadTerminalJobDeadline(current, row, cursor, terminalInputs ? recoveryGeneration : null) : null;
                if (paired is not null && result.Count + 2 > ParkPageLimit) break;
                var isJob = row.Message.Kind == Kinds.Job || row.Message is { Kind: Kinds.JobFinished, FoundationJobResult: true };
                result.Add(new(cursor, row.Message.Kind, row.Message.Sender, row.KeptBy, row.PayloadLength)
                {
                    ReleasedBy = row.ReleasedBy,
                    ObsoleteJob = recoveryGeneration is not null && isJob && currentJobs?.Contains(cursor.Key) != true,
                    JobGeneration = recoveryGeneration is not null && isJob ? row.Message.JobGeneration : null,
                    PairedJobInput = paired
                });
                if (paired is not null) break;
            }
            return result;
        }
    }

    private ParkPageRow? ReadTerminalJobDeadline(InstanceRow actor, MessageRow ticket, ParkPageCursor ticketCursor,
        long? terminalGeneration)
    {
        if (ticket.JobTiming.Timer is not { } timer) return null;
        var key = InboxKey.Issued(timer);
        if (Messages.GetValueOrDefault(key) is not { Done: false, Message: { Kind: Kinds.JobDeadline } message } deadline ||
            message.Target.Id != actor.Id || message.TargetCreation != actor.Creation || message.Sender != actor.Id ||
            message.SenderCreation != actor.Creation || message.Due != ticket.JobTiming.At ||
            !StringComparer.Ordinal.Equals(deadline.JobTiming.JobKey, ticket.JobTiming.JobKey))
            return null;
        var cursor = new ParkPageCursor(deadline.Seq, key);
        // An earlier released deadline is already in this terminal scan's ordered prefix.
        if (terminalGeneration is not null && cursor.CompareTo(ticketCursor) < 0 &&
            deadline.ReleasedBy is { } release && release.Creation == actor.Creation && release.Generation <= terminalGeneration)
            return null;
        return new(cursor, Kinds.JobDeadline, message.Sender, deadline.KeptBy, deadline.PayloadLength)
            { ReleasedBy = deadline.ReleasedBy };
    }

    private ParkPageRow? ReadDeadlinePrerequisite(InstanceRow actor, MessageRow deadline, ParkPageCursor cursor,
        IReadOnlyDictionary<string, long> jobs, ParkRecoveryDrain recovery)
    {
        var message = deadline.Message;
        if (message.Kind != Kinds.JobDeadline || !cursor.Key.HasIssuedOrigin || message.Sender != actor.Id ||
            message.SenderCreation != actor.Creation || deadline.JobTiming.JobKey is not { } key ||
            !jobs.TryGetValue(key, out var generation) || !recovery.RestoresKept(deadline.KeptBy))
            return null;
        var ticketKey = InboxKey.Ticket(actor.Id, key, actor.Creation, generation);
        if (Messages.GetValueOrDefault(ticketKey) is not { Done: true, KeptBy: ParkKeepReason.Consumed or ParkKeepReason.Selected } ticket ||
            !recovery.RestoresKept(ticket.KeptBy) ||
            !IsJobEnvelope(ticket.Message, actor, Kinds.Job, generation) ||
            ticket.JobTiming.Timer != message.Id || ticket.JobTiming.At != message.Due)
            return null;
        var later = new ParkPageCursor(ticket.Seq, ticketKey);
        if (later.CompareTo(cursor) <= 0) return null;
        return new(later, Kinds.Job, ticket.Message.Sender, ticket.KeptBy, ticket.PayloadLength) { ReleasedBy = ticket.ReleasedBy };
    }

    private static HashSet<InboxKey>? CurrentParkJobKeys(InstanceRow actor)
    {
        if (actor.Jobs is null) return null;
        var keys = new HashSet<InboxKey>();
        foreach (var job in JobRegistry.Read(actor.Jobs))
        {
            keys.Add(InboxKey.Ticket(actor.Id, job.Key, actor.Creation, job.Generation));
            keys.Add(InboxKey.JobResult(actor.Id, job.Key, actor.Creation, job.Generation));
        }
        return keys;
    }

    private static bool IsObsoleteParkJob(Message message, InboxKey key, HashSet<InboxKey>? currentKeys) =>
        (message.Kind == Kinds.Job || message.FoundationJobResult) && currentKeys?.Contains(key) != true;

    private ParkPageRow? ChangedFoundationParkInput(TurnCommit turn, InstanceRow? previous, IReadOnlyDictionary<int, long> epochs)
    {
        if (turn.FoundationParkClear is not { } page) return null;
        if (turn.Row is not { Retired: false } checkpoint || previous is not { Retired: false } ||
            previous is not ({ Status: InstanceStatus.Faulted } or { Status: InstanceStatus.Running, ParkDiscard.Kind: ParkClearKind.Terminate }) ||
            previous.Status != checkpoint.Status || previous.Creation != checkpoint.Creation ||
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
        foreach (var row in page.Writes())
        {
            var stored = Messages[row.Cursor.Key];
            var restores = !page.TerminalInputs && drain.Restores(row, checkpoint.Status);
            var authored = stored.Message.FoundationPublish?.Authored is not null;
            if (authored && restores)
                stored.Message = (stored.Message with { KeepUntil = null }).AsKernelTicket();
            else stored.Message = stored.Message with { KeepUntil = until };
            stored.KeptBy = null;
            if (authored && !restores) stored.ParkDeadLetter = true;
            if (page.TerminalInputs)
            {
                // The new Done marker removes this input from the next terminal page.
                (stored.Done, stored.Held, stored.Trial, stored.Attempts) = (true, false, 0, 0);
                stored.DropDonePayload();
                continue;
            }
            if (!restores)
            {
                (stored.Done, stored.Held, stored.Trial) = (true, false, 0);
                stored.DropDonePayload();
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
            stored.DropDonePayload();
        }
    }
}
