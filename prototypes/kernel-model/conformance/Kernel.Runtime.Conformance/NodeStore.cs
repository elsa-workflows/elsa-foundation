namespace Kernel.Runtime.Conformance;

/// <summary>
/// One node's way to the store, which the suite can stall: a pause of the process or a network that holds its packets.
/// A call made during a stall waits until the stall ends and only then executes, so a commit prepared before it carries
/// an epoch that may no longer be the partition's.
/// </summary>
public sealed class NodeStore(IKernelStore inner) : IKernelStore
{
    private readonly object _lock = new();
    private TaskCompletionSource? _stall;
    private TimeSpan _nextCommit;
    private int _commitPartition;

    public int Stalls { get; private set; }

    /// <summary>Every call from now on waits for <paramref name="duration"/> from now.</summary>
    public void Stall(TimeSpan duration)
    {
        lock (_lock) Begin(duration);
    }

    /// <summary>
    /// The node's next commit of turns in <paramref name="partition"/>, whenever it comes, stalls every call of the node
    /// for <paramref name="duration"/> before it executes: a commit in flight when the network split.
    /// </summary>
    public void StallNextCommit(TimeSpan duration, int partition)
    {
        lock (_lock) (_nextCommit, _commitPartition) = (duration, partition);
    }

    private void Begin(TimeSpan duration)     // under the lock
    {
        if (_stall is not null) return;
        var stall = _stall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Stalls++;
        _ = Task.Delay(duration).ContinueWith(_ =>
        {
            lock (_lock) _stall = null;
            stall.SetResult();
        }, TaskScheduler.Default);
    }

    private ValueTask Gate(CommitBatch? commit = null)
    {
        Task? wait;
        lock (_lock)
        {
            if (commit is { Turns.Count: > 0 } && commit.Partition == _commitPartition && _nextCommit > TimeSpan.Zero)
            {
                Begin(_nextCommit);
                _nextCommit = TimeSpan.Zero;
            }
            wait = _stall?.Task;
        }
        return wait is null ? default : new(wait);
    }

    public async ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default) { await Gate(); return await inner.TakeAsync(partition, node, lease, cancellationToken); }
    public async ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default) { await Gate(); await inner.ReleaseAsync(partition, epoch, cancellationToken); }
    public async ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default) { await Gate(batch); return await inner.CommitAsync(batch, cancellationToken); }
    public async ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) { await Gate(); return await inner.PollAsync(partition, epoch, horizon, limit, cancellationToken); }
    public async ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default) { await Gate(); return await inner.AddAsync(message, cancellationToken); }
    public async ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) { await Gate(); return await inner.LoadAsync(ids, cancellationToken); }
    public async ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default) { await Gate(); await inner.PublishAsync(@event, cancellationToken); }
    public async ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default) { await Gate(); return await inner.FindSubscriptionsAsync(key, cancellationToken); }
    public async ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) { await Gate(); return await inner.FindEventsAsync(keys, cancellationToken); }
    public async ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default) { await Gate(); return await inner.ReadJournalAsync(partition, after, take, cancellationToken); }
    public async ValueTask<int> SweepAsync(CancellationToken cancellationToken = default) { await Gate(); return await inner.SweepAsync(cancellationToken); }
    public async ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default) { await Gate(); return await inner.ReadViewsAsync(instances, cancellationToken); }
    public async ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default) { await Gate(); return await inner.HeartbeatAsync(node, window, cancellationToken); }
    public async ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default) { await Gate(); await inner.SubscribeAsync(subscription, cancellationToken); }
    public async ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default) { await Gate(); return await inner.ProjectedAsync(partition, cancellationToken); }
    public async ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default) { await Gate(); return await inner.ProjectAsync(partition, from, to, rows, cancellationToken); }
}
