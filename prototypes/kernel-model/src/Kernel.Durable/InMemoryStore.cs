using System.Collections.Immutable;
using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// The store in memory, with the semantics the SQL is meant to have: every operation is atomic, the conditional
/// updates are honoured, keys are create-only. It also keeps <see cref="Ghost"/>: things a correct kernel never does,
/// seen from the one place that sees every write.
/// </summary>
public sealed partial class InMemoryStore(int partitions, Func<long> clock, Flaw flaws = Flaw.None) : IKernelStore
{
    public bool CommitClockAvailable { get; set; } = true;
    public bool PhysicalRetirementAvailable => true;
    public bool CreationIdentityAvailable { get; set; } = true;

    public sealed class PartitionRow
    {
        public string? Owner;
        public long Epoch, LeaseUntil, Position;
    }

    public sealed class MessageRow(Message message, long seq, long seen)
    {
        public Message Message = message;
        public long Seq = seq, Seen = seen, Trial;
        public int Attempts;
        public bool Done;
    }

    private readonly Lock _gate = new();
    private long _seq;

    public PartitionRow[] Partitions { get; } = Enumerable.Range(0, partitions).Select(_ => new PartitionRow()).ToArray();
    public Dictionary<string, InstanceRow> Instances { get; } = new(StringComparer.Ordinal);
    public Dictionary<InboxKey, MessageRow> Messages { get; } = [];
    public HashSet<SubscriptionRow> Subscriptions { get; } = new(EqualityComparer<SubscriptionRow>.Create(
        (a, b) => a is null || b is null ? a == b : (a.Key, a.Instance, a.Wait) == (b.Key, b.Instance, b.Wait),
        row => HashCode.Combine(row.Key, row.Instance, row.Wait)));
    public Dictionary<(string Key, string Id), EventRow> EventRows { get; } = [];
    public Dictionary<(int Partition, long Position), JournalRow> Journal { get; } = [];
    public List<string> Ghost { get; } = [];
    public Dictionary<string, long> Nodes { get; } = new(StringComparer.Ordinal);
    public long[] Projection { get; } = new long[partitions];
    public long[] Deleted { get; } = new long[partitions];     // journal rows removed by deletes, per partition: for the tests
    public Dictionary<string, ViewRow> Views { get; } = new(StringComparer.Ordinal);

    /// <summary>A row the database refuses, every time: a value too long.</summary>
    public Func<InstanceRow, bool>? Refuses { get; set; }

    public sealed record DeletedCheckpoint(InstanceRow Row, ImmutableArray<JournalRow> Journal)
    {
        public JournalRow? Receipt { get; init; }
    }
    private Dictionary<CreationAddress, TaskCompletionSource<DeletedCheckpoint>>? _watchedDeletes;

    /// <summary>For tests: preserve the actual checkpoint and journal at the next fenced deletion of this occurrence.</summary>
    public Task<DeletedCheckpoint> WatchDeletion(CreationAddress address)
    {
        lock (_gate)
        {
            var watches = _watchedDeletes ??= [];
            if (!watches.TryGetValue(address, out var watch)) watches[address] = watch = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return watch.Task;
        }
    }

    private bool Has(Flaw flaw) => (flaws & flaw) != 0;

    public ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(Take(partition, node, lease, clock()));
    }

    /// <summary>A take of a set: one transaction, the rows in ascending id, each decided as the one-partition take decides.</summary>
    public ValueTask<OwnedLease> TakeAsync(IReadOnlyCollection<int> partitions, string node, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var now = clock();
            var taken = new List<PartitionFence>();
            foreach (var partition in partitions.Distinct().Order())
                if (Take(partition, node, lease, now) is { } one) taken.Add(one.For(partition));
            return new(new OwnedLease(taken, now));
        }
    }

    private Lease? Take(int partition, string node, TimeSpan lease, long now)
    {
        var p = Partitions[partition];
        var died = p.Owner is not null && p.Owner != node;      // taken from another: only possible once its lease has expired
        if (died && p.LeaseUntil >= now) return null;
        if (died || (Has(Flaw.EveryTakeCounts) && p.Owner is not null))
            foreach (var row in Messages.Values)
                if (row.Trial == p.Epoch && !row.Done && row.Message.Target.Partition == partition) row.Attempts++;
        (p.Owner, p.Epoch, p.LeaseUntil) = (node, p.Epoch + 1, now + (long)lease.TotalMilliseconds);
        return new Lease(p.Epoch, p.Position, now);
    }

    public bool OwnedSetsAvailable => true;

    /// <summary>For tests: called with each fence row's partition as a commit locks it, in the order it locks them.</summary>
    public Action<int>? Locking { get; set; }

    public bool SupportsCommitWindow => true;

    public ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
    {
        StoreBatchRules.Validate(batch, ownedSets: true);
        if (!CreationIdentityAvailable) CreationProtocol.RejectUnsupported(batch, physicalRetirement: true);
        if (!CommitClockAvailable) CommitClockFacts.RejectUnsupported(batch);
        lock (_gate)
        {
            // The k-row fence: every fence row the batch touches, locked in ascending partition id, all or nothing.
            var (fences, now) = (batch.Fenced, clock());
            var (epoch, position, fenced, stale) = (true, true, true, new List<string>());
            foreach (var fence in fences)
            {
                Locking?.Invoke(fence.Partition);
                var row = Partitions[fence.Partition];
                bool e = row.Epoch == fence.Epoch, q = row.Position == fence.Position;
                (epoch, position) = (epoch && e, position && q);
                fenced &= (e || Has(Flaw.FenceWithoutEpoch)) && (q || Has(Flaw.FenceWithoutPosition));
                if (!e) stale.Add($"I1 a commit prepared under epoch {fence.Epoch} landed in partition {fence.Partition} at epoch {row.Epoch}");
                else if (!q) stale.Add($"I1 a commit prepared at position {fence.Position} landed in partition {fence.Partition} at position {row.Position}");
            }
            if (!fenced && !Has(Flaw.CommitDespiteFence)) return new(new Committed(false, now, []));
            Ghost.AddRange(stale);
            var epochs = fences.ToDictionary(f => f.Partition, f => f.Epoch);
            long MarkEpoch(MessageRow row) => epochs.GetValueOrDefault(row.Message.Target.Partition, batch.Epoch);

            if (fenced)
            {
                // Everything is checked before anything is written: a statement that fails aborts the transaction.
                var at = fences.ToDictionary(f => f.Partition, f => f.Position);
                foreach (var turn in batch.Turns)
                {
                    if (turn.Delete && Instances.GetValueOrDefault(turn.Instance)?.Revision != turn.Expected) Diverged($"instance {turn.Instance} to delete is not at revision {turn.Expected}");
                    if (turn.Row is { } row)
                    {
                        if (Refuses?.Invoke(row) == true) throw new StoreException(Failure.Definite, $"the row of {row.Id} was refused");
                        var revision = Instances.GetValueOrDefault(row.Id)?.Revision;
                        if (revision != turn.Expected) Diverged($"instance {row.Id} is at revision {revision?.ToString() ?? "none"}, the commit expected {turn.Expected?.ToString() ?? "none"}");
                        if (!row.Retired) CommitClockFacts.Validate(row, Instances.GetValueOrDefault(row.Id));
                    }
                    CreationProtocol.Validate(turn, Instances.GetValueOrDefault(turn.Instance));
                    foreach (var id in turn.Consumed.Concat(turn.Done).Concat(turn.Held ?? []))
                        if (!Has(Flaw.DeleteWithoutCount) && Messages.GetValueOrDefault(id) is not { Done: false }) Diverged($"consumed message {id} is not there");
                    foreach (var message in turn.Sent)
                        if (Messages.ContainsKey(message.InboxKey)) Diverged($"sent message {message.Id} exists already");
                    if (turn.Journal is not null && batch.PartitionOf(turn) is var journal && Journal.ContainsKey((journal, ++at[journal])))
                        Diverged($"journal position {at[journal]} of partition {journal} is written already");
                }
                // A mark records its message's own partition's epoch, so a mark outside the fenced set would be unbound, and a
                // delete outside it would be a write no fence covers.
                if (batch.Fences is not null)
                    foreach (var id in batch.Seen.Concat(batch.Trial).Concat(batch.Turns.SelectMany(t => t.Consumed.Concat(t.Done))))
                        if (Messages.GetValueOrDefault(id) is { } marked && !epochs.ContainsKey(marked.Message.Target.Partition))
                            throw new StoreException(Failure.Definite, $"message {id} is outside the batch's fenced partitions");
                if (batch.Window is { } window)
                {
                    var atCommit = clock();
                    if (atCommit < now || atCommit < window.FromInclusive || atCommit >= window.UntilExclusive)
                        throw new StoreException(Failure.WindowExpired, "The database clock left the decision's authority window.");
                }
                foreach (var fence in fences)
                    (Partitions[fence.Partition].Position, Partitions[fence.Partition].LeaseUntil) = (fence.Position + batch.PositionsIn(fence.Partition), now + (long)batch.Lease.TotalMilliseconds);
            }

            List<LifecycleObservation.Change>? changes = null;
            var (next, seqs) = (fences.ToDictionary(f => f.Partition, f => f.Position), new List<long>());
            List<(CreationAddress Address, DeletedCheckpoint Checkpoint, (int, long)? Position)>? deletes = null;
            foreach (var turn in batch.Turns)
            {
                var journal = batch.PartitionOf(turn);
                var opening = epoch && position ? Lifecycle?.BeforeTurn(turn, this) : null;
                if (epoch && position && turn.Delete && _watchedDeletes is { Count: > 0 } watches &&
                    Instances.GetValueOrDefault(turn.Instance) is { Retired: false } prior && watches.ContainsKey(new(prior.Id, prior.Creation)))
                {
                    var entries = prior.History == History.Full ? Journal.Values.Where(j => j.Instance == prior.Id).ToImmutableArray() : [];
                    (deletes ??= []).Add((new(prior.Id, prior.Creation), new(prior, entries), turn.Journal is null ? null : (journal, next[journal] + 1)));
                }
                if (turn.Row is not null) Instances[turn.Row.Id] = CommitClockFacts.Stamp(turn.Row, now);
                if (turn.Delete)
                {
                    if (turn.Row is not { Retired: true }) Instances.Remove(turn.Instance);
                    foreach (var key in Journal.Where(j => j.Value.Instance == turn.Instance && j.Value.Revision > 0 && !Has(Flaw.DeleteLeavesJournal)).Select(j => j.Key).ToList())
                    {
                        Journal.Remove(key);
                        Deleted[key.Partition]++;
                    }
                }
                foreach (var id in turn.Consumed) Messages.Remove(id);
                foreach (var id in turn.Done)
                {
                    // An outside command stays as a marker until its window has passed, so a late retry meets a duplicate key.
                    if (Has(Flaw.DeleteOutsideCommands)) Messages.Remove(id);
                    else if (Messages.GetValueOrDefault(id) is { } done) done.Done = true;
                }
                foreach (var message in turn.Sent)
                {
                    // Messages the owner hands over in memory to its own partition are written as seen already.
                    Messages[message.InboxKey] = new MessageRow(message, ++_seq, batch.HandOver && epochs.TryGetValue(message.Target.Partition, out var mine) ? mine : 0);
                    seqs.Add(_seq);
                }
                foreach (var subscription in turn.Subscribed) Subscriptions.Add(subscription);
                foreach (var subscription in turn.Unsubscribed) Subscriptions.Remove(subscription);
                if (turn.Unsubscribe) Subscriptions.RemoveWhere(s => s.Instance == turn.Instance);
                if (turn.Journal is not null) Journal[(journal, ++next[journal])] = new JournalRow(journal, next[journal], turn.Instance, turn.Delete ? -1 : turn.Row?.Revision ?? 0, turn.Journal);
                if (opening is not null)
                    (changes ??= []).Add(opening with { After = Instances.GetValueOrDefault(turn.Instance),
                        Receipt = turn.Delete && turn.Journal is not null ? Journal[(journal, next[journal])] : null });
            }
            foreach (var id in batch.Seen)
                if (Messages.GetValueOrDefault(id) is { } seen) seen.Seen = MarkEpoch(seen);
            foreach (var id in batch.Trial)
                if (Messages.GetValueOrDefault(id) is { } tried) (tried.Seen, tried.Trial) = (MarkEpoch(tried), MarkEpoch(tried));
            foreach (var id in batch.Turns.SelectMany(s => s.Held ?? []))
                if (!Has(Flaw.HoldKeepsTrial) && Messages.GetValueOrDefault(id) is { } held) held.Trial = 0;
            if (changes is not null) Lifecycle!.Publish(changes);
            if (deletes is not null)
                foreach (var (address, checkpoint, receiptPosition) in deletes)
                    if (_watchedDeletes!.Remove(address, out var watch))
                        watch.SetResult(checkpoint with { Receipt = receiptPosition is { } slot ? Journal.GetValueOrDefault(slot) : null });
            return new(new Committed(fenced, now, seqs));

            // A count that does not match: memory and rows have parted. At the current epoch and position that is a kernel bug.
            void Diverged(string what)
            {
                if (epoch && position) Ghost.Add("I9 " + what);
                throw new StoreException(Failure.Unknown, what);
            }
        }
    }

    public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var now = Nodes[node] = clock();
            var free = Enumerable.Range(0, Partitions.Length).Where(p => Partitions[p].Owner is null || Partitions[p].LeaseUntil < now).ToList();
            var heard = Nodes.Where(n => n.Value >= now - (long)window.TotalMilliseconds).Select(n => n.Key).Order(StringComparer.Ordinal).ToList();
            return new(new Cluster(heard.Count, free, now, heard.IndexOf(node)));
        }
    }

    public ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default) => ReleaseAsync([new PartitionFence(partition, epoch, 0)], cancellationToken);

    /// <summary>A release of a set, in one transaction: each row still at its epoch is freed; kept rows are not written.</summary>
    public ValueTask ReleaseAsync(IReadOnlyCollection<PartitionFence> owned, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            foreach (var fence in owned.OrderBy(f => f.Partition))
                if (Partitions[fence.Partition].Epoch == fence.Epoch) Partitions[fence.Partition].Owner = null;
        return default;
    }

    /// <summary>The retake after a reset: only a row this node still holds at that epoch; one more epoch, no death.</summary>
    public ValueTask<Lease?> RetakeAsync(int partition, string node, long epoch, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var (row, now) = (Partitions[partition], clock());
            if (row.Owner != node || row.Epoch != epoch) return new((Lease?)null);
            (row.Epoch, row.LeaseUntil) = (row.Epoch + 1, now + (long)lease.TotalMilliseconds);
            return new(new Lease(row.Epoch, row.Position, now));
        }
    }

    public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) =>
        PollAsync([new PartitionFence(partition, epoch, 0)], horizon, limit, cancellationToken);

    /// <summary>One query over an owned set: each row is compared with the epoch of its own partition.</summary>
    public ValueTask<Polled> PollAsync(IReadOnlyCollection<PartitionFence> owned, TimeSpan horizon, int limit, CancellationToken cancellationToken = default)
    {
        StoreBatchRules.RequireDistinct(owned);
        lock (_gate)
        {
            var now = clock();
            var until = now + (long)horizon.TotalMilliseconds;
            var epochs = owned.ToDictionary(f => f.Partition, f => f.Epoch);
            var rows = Messages.Values
                .Where(m => epochs.TryGetValue(m.Message.Target.Partition, out var epoch) && m.Seen < epoch && !m.Done && (m.Message.Due ?? 0) <= until)
                .OrderBy(m => m.Seq).Take(limit)
                .Select(m => new Stored(m.Message, m.Seq, m.Seen, m.Attempts, m.Trial)).ToList();
            return new(new Polled(rows, now));
        }
    }

    public ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default)
    {
        if (!CreationIdentityAvailable) CreationProtocol.RejectUnsupported(message);
        Kinds.RequireApplicationSend(message.Kind);
        return InsertAsync(message.AsOrdinary(), cancellationToken);
    }

    private long _eventSerial;
    public ValueTask<EventAcceptance> AddEventAsync(EventDelivery delivery, CancellationToken cancellationToken = default)
    {
        if (!CreationIdentityAvailable) CreationProtocol.RejectUnsupported(delivery.Message);
        lock (_gate)
        {
            if (Messages.TryGetValue(delivery.Message.InboxKey, out var existing))
                return ValueTask.FromResult(new EventAcceptance(false, existing.Message.Target));
            var message = delivery.Message.Start is null ? delivery.Message : delivery.At(checked(++_eventSerial));
            Messages.Add(message.InboxKey, new MessageRow(message, ++_seq, 0));
            return ValueTask.FromResult(new EventAcceptance(true, message.Target));
        }
    }

    private ValueTask<bool> InsertAsync(Message message, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Messages.ContainsKey(message.InboxKey)) return new(false);
            Messages[message.InboxKey] = new MessageRow(message, ++_seq, 0);
            return new(true);
        }
    }

    public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(ids.Select(id => Instances.GetValueOrDefault(id)).OfType<InstanceRow>().ToList());
    }

    public ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default)
    {
        lock (_gate) EventRows.TryAdd((@event.Key, @event.Id), @event);
        return default;
    }

    public ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default)
    {
        if (!CreationIdentityAvailable) CreationProtocol.RejectUnsupported(subscription);
        lock (_gate) Subscriptions.Add(subscription);
        return default;
    }

    public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(Subscriptions.Where(s => s.Key == key).OrderBy(s => s.Instance, StringComparer.Ordinal).ToList());
    }

    public ValueTask<IReadOnlyList<EventRoute>> PublishedMatchesAsync(EventRow published, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(EventRows.TryGetValue((published.Key, published.Id), out var stored)
            ? Subscriptions.Where(s => s.Key == stored.Key).OrderBy(s => s.Instance, StringComparer.Ordinal).Select(s => new EventRoute(stored, s)).ToList() : []);
    }

    public ValueTask<IReadOnlyList<EventRoute>> KeptMatchesAsync(IReadOnlyList<SubscriptionRow> waits, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var now = clock();
            var selected = waits.Select(w => (w.Key, w.Instance, w.Wait, w.Creation)).ToHashSet();
            return new((from e in EventRows.Values orderby e.Id
                join s in Subscriptions on e.Key equals s.Key
                where e.KeepUntil >= now && selected.Contains((s.Key, s.Instance, s.Wait, s.Creation))
                select new EventRoute(e, s)).ToList());
        }
    }

    public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var now = clock();
            return new(EventRows.Values.Where(e => keys.Contains(e.Key) && e.KeepUntil >= now).OrderBy(e => e.Id, StringComparer.Ordinal).ToList());
        }
    }

    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(Journal.Values.Where(j => j.Partition == partition && j.Position > after).OrderBy(j => j.Position).Take(take).ToList());
    }

    public ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(new Projection(Projection[partition], Partitions[partition].Position));
    }

    public ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (Projection[partition] != from && !Has(Flaw.ProjectorBlindWrite)) return new(false);
            Projection[partition] = to;
            foreach (var change in ViewChange.From(rows))
            {
                if (change.Commits == 0) Views.Remove(change.Instance);
                else
                {
                    int prior = change.Reset ? 0 : Views.GetValueOrDefault(change.Instance)?.Commits ?? 0;
                    Views[change.Instance] = new(change.Instance, change.Revision, prior + change.Commits);
                }
            }
            return new(true);
        }
    }

    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalOfAsync(int partition, string instance, long after, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(Journal.Values.Where(j => j.Partition == partition && j.Instance == instance && j.Position > after).OrderBy(j => j.Position).ToList());
    }

    public ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(instances.Select(i => Views.GetValueOrDefault(i)).OfType<ViewRow>().ToList());
    }

    public ValueTask<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var now = clock();
            var messages = Messages.Values.Where(m => m.Done && m.Message.KeepUntil < now).Select(m => m.Message.InboxKey).ToList();
            var events = EventRows.Values.Where(e => e.KeepUntil < now).Select(e => (e.Key, e.Id)).ToList();
            messages.ForEach(id => Messages.Remove(id));
            events.ForEach(key => EventRows.Remove(key));
            return new(messages.Count + events.Count);
        }
    }
}
