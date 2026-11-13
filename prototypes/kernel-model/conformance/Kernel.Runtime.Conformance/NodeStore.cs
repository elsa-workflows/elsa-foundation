namespace Kernel.Runtime.Conformance;

/// <summary>
/// One node's way to the store, which the suite can stall: a pause of the process or a network that holds its packets.
/// A call made during a stall waits until the stall ends and only then executes, so a commit prepared before it carries
/// an epoch that may no longer be the partition's. It counts the node's commits that the store answered "not landed"
/// (the fence, or a count, refused them), records what the commit it was told to hold got back, and knows when none of
/// the node's calls is under way.
/// </summary>
public sealed class NodeStore(IKernelStore inner) : IKernelStore
{
    private readonly object _lock = new();
    private TaskCompletionSource? _stall;
    private long _until;                    // when the stall ends, on Environment.TickCount64
    private TimeSpan _nextCommit;
    private int _commitPartition, _inFlight, _refused;
    private readonly TaskCompletionSource<string> _held = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Stalls { get; private set; }

    /// <summary>The node's commits answered "not landed".</summary>
    public int Refused => Volatile.Read(ref _refused);

    /// <summary>No call of the node is under way, stalled or not.</summary>
    public bool Idle => Volatile.Read(ref _inFlight) == 0;

    /// <summary>What the commit held by <see cref="StallNextCommit"/> got back once it executed: "refused", "landed", or what it threw.</summary>
    public Task<string> Held => _held.Task;

    /// <summary>Every call from now on waits until <paramref name="duration"/> from now, or longer if a stall under way lasts longer.</summary>
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
        var until = Environment.TickCount64 + (long)duration.TotalMilliseconds;
        if (_stall is not null)
        {
            _until = Math.Max(_until, until);   // a stall during a stall lasts as long as the longer of the two
            return;
        }
        var stall = _stall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _until = until;
        Stalls++;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                long left;
                lock (_lock) left = _until - Environment.TickCount64;
                if (left <= 0) break;
                await Task.Delay(TimeSpan.FromMilliseconds(left));
            }
            lock (_lock) _stall = null;
            stall.SetResult();
        });
    }

    /// <summary>Waits out a stall; true when this call is the commit the case asked to hold.</summary>
    private async ValueTask<bool> Gate(CommitBatch? commit = null)
    {
        Task? wait;
        var held = false;
        lock (_lock)
        {
            if (commit is { Turns.Count: > 0 } && commit.Partition == _commitPartition && _nextCommit > TimeSpan.Zero)
            {
                Begin(_nextCommit);
                _nextCommit = TimeSpan.Zero;
                held = true;
            }
            wait = _stall?.Task;
        }
        if (wait is not null) await wait;
        return held;
    }

    private async ValueTask<T> Call<T>(Func<ValueTask<T>> call)
    {
        Interlocked.Increment(ref _inFlight);
        try
        {
            await Gate();
            return await call();
        }
        finally { Interlocked.Decrement(ref _inFlight); }
    }

    private async ValueTask Call(Func<ValueTask> call) => await Call<bool>(async () => { await call(); return true; });

    public async ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _inFlight);
        try
        {
            var held = await Gate(batch);
            Committed result;
            try { result = await inner.CommitAsync(batch, cancellationToken); }
            catch (Exception e)
            {
                if (held) _held.TrySetResult($"threw {e.GetType().Name}: {e.Message}");
                throw;
            }
            if (!result.Landed) Interlocked.Increment(ref _refused);
            if (held) _held.TrySetResult(result.Landed ? "landed" : "refused");
            return result;
        }
        finally { Interlocked.Decrement(ref _inFlight); }
    }

    public ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default) => Call(() => inner.TakeAsync(partition, node, lease, cancellationToken));
    public ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default) => Call(() => inner.ReleaseAsync(partition, epoch, cancellationToken));
    public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) => Call(() => inner.PollAsync(partition, epoch, horizon, limit, cancellationToken));
    public ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default) => Call(() => inner.AddAsync(message, cancellationToken));
    public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => Call(() => inner.LoadAsync(ids, cancellationToken));
    public ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default) => Call(() => inner.PublishAsync(@event, cancellationToken));
    public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default) => Call(() => inner.FindSubscriptionsAsync(key, cancellationToken));
    public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) => Call(() => inner.FindEventsAsync(keys, cancellationToken));
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default) => Call(() => inner.ReadJournalAsync(partition, after, take, cancellationToken));
    public ValueTask<int> SweepAsync(CancellationToken cancellationToken = default) => Call(() => inner.SweepAsync(cancellationToken));
    public ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default) => Call(() => inner.ReadViewsAsync(instances, cancellationToken));
    public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default) => Call(() => inner.HeartbeatAsync(node, window, cancellationToken));
    public ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default) => Call(() => inner.SubscribeAsync(subscription, cancellationToken));
    public ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default) => Call(() => inner.ProjectedAsync(partition, cancellationToken));
    public ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default) => Call(() => inner.ProjectAsync(partition, from, to, rows, cancellationToken));
}
