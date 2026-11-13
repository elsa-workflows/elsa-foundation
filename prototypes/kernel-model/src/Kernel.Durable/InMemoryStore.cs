using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// The store in memory, with the semantics the SQL is meant to have: every operation is atomic, the conditional
/// updates are honoured, keys are create-only. It also keeps <see cref="Ghost"/>: things a correct kernel never does,
/// seen from the one place that sees every write.
/// </summary>
public sealed class InMemoryStore(int partitions, Func<long> clock, Flaw flaws = Flaw.None) : IKernelStore
{
    public bool CommitClockAvailable { get; set; } = true;

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
    public Dictionary<string, MessageRow> Messages { get; } = new(StringComparer.Ordinal);
    public HashSet<SubscriptionRow> Subscriptions { get; } = [];
    public Dictionary<(string Key, string Id), EventRow> EventRows { get; } = [];
    public Dictionary<(int Partition, long Position), JournalRow> Journal { get; } = [];
    public List<string> Ghost { get; } = [];
    public Dictionary<string, long> Nodes { get; } = new(StringComparer.Ordinal);
    public long[] Projection { get; } = new long[partitions];
    public long[] Deleted { get; } = new long[partitions];     // journal rows removed by deletes, per partition: for the tests
    public Dictionary<string, ViewRow> Views { get; } = new(StringComparer.Ordinal);

    /// <summary>A row the database refuses, every time: a value too long.</summary>
    public Func<InstanceRow, bool>? Refuses { get; set; }

    private bool Has(Flaw flaw) => (flaws & flaw) != 0;

    public ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var (p, now) = (Partitions[partition], clock());
            var died = p.Owner is not null && p.Owner != node;      // taken from another: only possible once its lease has expired
            if (died && p.LeaseUntil >= now) return new((Lease?)null);
            if (died || (Has(Flaw.EveryTakeCounts) && p.Owner is not null))
                foreach (var row in Messages.Values)
                    if (row.Trial == p.Epoch && !row.Done && row.Message.Target.Partition == partition) row.Attempts++;
            (p.Owner, p.Epoch, p.LeaseUntil) = (node, p.Epoch + 1, now + (long)lease.TotalMilliseconds);
            return new(new Lease(p.Epoch, p.Position, now));
        }
    }

    public ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
    {
        if (!CommitClockAvailable) CommitClockFacts.RejectUnsupported(batch);
        lock (_gate)
        {
            var (p, now) = (Partitions[batch.Partition], clock());
            bool epoch = p.Epoch == batch.Epoch, position = p.Position == batch.Position;
            var fenced = (epoch || Has(Flaw.FenceWithoutEpoch)) && (position || Has(Flaw.FenceWithoutPosition));
            if (!fenced && !Has(Flaw.CommitDespiteFence)) return new(new Committed(false, now, []));
            if (!epoch) Ghost.Add($"I1 a commit prepared under epoch {batch.Epoch} landed in partition {batch.Partition} at epoch {p.Epoch}");
            else if (!position) Ghost.Add($"I1 a commit prepared at position {batch.Position} landed in partition {batch.Partition} at position {p.Position}");

            if (fenced)
            {
                // Everything is checked before anything is written: a statement that fails aborts the transaction.
                var at = batch.Position;
                foreach (var turn in batch.Turns)
                {
                    if (turn.Delete && Instances.GetValueOrDefault(turn.Instance)?.Revision != turn.Expected) Diverged($"instance {turn.Instance} to delete is not at revision {turn.Expected}");
                    if (turn.Row is { } row)
                    {
                        if (Refuses?.Invoke(row) == true) throw new StoreException(Failure.Definite, $"the row of {row.Id} was refused");
                        var revision = Instances.GetValueOrDefault(row.Id)?.Revision;
                        if (revision != turn.Expected) Diverged($"instance {row.Id} is at revision {revision?.ToString() ?? "none"}, the commit expected {turn.Expected?.ToString() ?? "none"}");
                    }
                    foreach (var id in turn.Consumed.Concat(turn.Done).Concat(turn.Held ?? []))
                        if (!Has(Flaw.DeleteWithoutCount) && Messages.GetValueOrDefault(id) is not { Done: false }) Diverged($"consumed message {id} is not there");
                    foreach (var message in turn.Sent)
                        if (Messages.ContainsKey(message.Id)) Diverged($"sent message {message.Id} exists already");
                    if (turn.Journal is not null && Journal.ContainsKey((batch.Partition, ++at))) Diverged($"journal position {at} is written already");
                }
                (p.Position, p.LeaseUntil) = (batch.Position + batch.Positions, now + (long)batch.Lease.TotalMilliseconds);
            }

            var (next, seqs) = (batch.Position, new List<long>());
            foreach (var turn in batch.Turns)
            {
                if (turn.Row is not null) Instances[turn.Row.Id] = CommitClockFacts.Stamp(turn.Row, now);
                if (turn.Delete)
                {
                    Instances.Remove(turn.Instance);
                    foreach (var key in Journal.Where(j => j.Value.Instance == turn.Instance && j.Value.Revision > 0 && !Has(Flaw.DeleteLeavesJournal)).Select(j => j.Key).ToList())
                    {
                        Journal.Remove(key);
                        Deleted[batch.Partition]++;
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
                    Messages[message.Id] = new MessageRow(message, ++_seq, batch.HandOver && message.Target.Partition == batch.Partition ? batch.Epoch : 0);
                    seqs.Add(_seq);
                }
                foreach (var subscription in turn.Subscribed) Subscriptions.Add(subscription);
                foreach (var subscription in turn.Unsubscribed) Subscriptions.Remove(subscription);
                if (turn.Unsubscribe) Subscriptions.RemoveWhere(s => s.Instance == turn.Instance);
                if (turn.Journal is not null) Journal[(batch.Partition, ++next)] = new JournalRow(batch.Partition, next, turn.Instance, turn.Delete ? -1 : turn.Row?.Revision ?? 0, turn.Journal);
            }
            foreach (var id in batch.Seen)
                if (Messages.GetValueOrDefault(id) is { } seen) seen.Seen = batch.Epoch;
            foreach (var id in batch.Trial)
                if (Messages.GetValueOrDefault(id) is { } tried) (tried.Seen, tried.Trial) = (batch.Epoch, batch.Epoch);
            foreach (var id in batch.Turns.SelectMany(s => s.Held ?? []))
                if (!Has(Flaw.HoldKeepsTrial) && Messages.GetValueOrDefault(id) is { } held) held.Trial = 0;
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
            return new(new Cluster(Nodes.Values.Count(seen => seen >= now - (long)window.TotalMilliseconds), free, now));
        }
    }

    public ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            if (Partitions[partition].Epoch == epoch) Partitions[partition].Owner = null;
        return default;
    }

    public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var now = clock();
            var until = now + (long)horizon.TotalMilliseconds;
            var rows = Messages.Values
                .Where(m => m.Message.Target.Partition == partition && m.Seen < epoch && !m.Done && (m.Message.Due ?? 0) <= until)
                .OrderBy(m => m.Seq).Take(limit)
                .Select(m => new Stored(m.Message, m.Seq, m.Seen, m.Attempts, m.Trial)).ToList();
            return new(new Polled(rows, now));
        }
    }

    public ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (Messages.ContainsKey(message.Id)) return new(false);
            Messages[message.Id] = new MessageRow(message, ++_seq, 0);
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
        lock (_gate) Subscriptions.Add(subscription);
        return default;
    }

    public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate) return new(Subscriptions.Where(s => s.Key == key).OrderBy(s => s.Instance, StringComparer.Ordinal).ToList());
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
            foreach (var row in rows.Where(r => r.Revision > 0))
                Views[row.Instance] = Views.GetValueOrDefault(row.Instance) is { } view ? new ViewRow(row.Instance, Math.Max(view.Revision, row.Revision), view.Commits + 1) : new ViewRow(row.Instance, row.Revision, 1);
            foreach (var row in rows.Where(r => r.Revision < 0)) Views.Remove(row.Instance);
            return new(true);
        }
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
            var messages = Messages.Values.Where(m => m.Done && m.Message.KeepUntil < now).Select(m => m.Message.Id).ToList();
            var events = EventRows.Values.Where(e => e.KeepUntil < now).Select(e => (e.Key, e.Id)).ToList();
            messages.ForEach(id => Messages.Remove(id));
            events.ForEach(key => EventRows.Remove(key));
            return new(messages.Count + events.Count);
        }
    }
}
