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
    public Func<long>? FoundationClock => inner.FoundationClock;
    public bool SupportsCommitWindow => inner.SupportsCommitWindow;
    public bool SupportsJobTrialClear => inner.SupportsJobTrialClear;
    public bool CreationIdentityAvailable => inner.CreationIdentityAvailable;
    public bool PhysicalRetirementAvailable => inner.PhysicalRetirementAvailable;
    public bool CommitClockAvailable => inner.CommitClockAvailable;
    public bool OwnedSetsAvailable => !WithoutOwnedSets && inner.OwnedSetsAvailable;   // the set take and release go through this wrapper's own calls; the set poll is one call
    public bool WithoutOwnedSets { get; init; }     // N2b: a fixed host, one Worker per scope, as on a store without owned sets
    public ValueTask<int?> PartitionCountAsync(CancellationToken cancellationToken = default) => inner.PartitionCountAsync(cancellationToken);
    private readonly object _lock = new();
    private TaskCompletionSource? _stall;
    private long _until;                    // when the stall ends, on Environment.TickCount64
    private TimeSpan _nextCommit;
    private int _commitPartition, _inFlight, _refused, _commits, _heldRefused;
    private long _heldAt;
    private long _calls, _polls, _turnCommits, _turnPartitions;
    private readonly TaskCompletionSource<string> _held = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Stalls { get; private set; }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> _releases = new();
    private int _setReleases, _setTakes, _singleTakes;
    public int SetTakes => Volatile.Read(ref _setTakes);
    public int SingleTakes => Volatile.Read(ref _singleTakes);

    /// <summary>When the node last began to give the partition up (Environment.TickCount64), if it ever did.</summary>
    public long? ReleaseBegan(int partition) => _releases.TryGetValue(partition, out var at) ? at : null;

    internal (int Partition, Task Completion)? HoldScalarPoll { get; set; }

    public int SetReleases => Volatile.Read(ref _setReleases);

    /// <summary>The node's commits answered "not landed".</summary>
    public int Refused => Volatile.Read(ref _refused);

    /// <summary>The node's commits that reached the store (after any stall).</summary>
    public int Commits => Volatile.Read(ref _commits);

    /// <summary>Commits held by <see cref="StallNextCommit"/> that the store then answered "not landed": a stale writer the fence refused.</summary>
    public int HeldRefused => Volatile.Read(ref _heldRefused);

    /// <summary>For the cost run: every call of the node, its partition polls (a set poll is one per partition here), its commits with turns, and the partitions those turns spanned in all.</summary>
    public (long Calls, long Polls, long TurnCommits, long TurnPartitions) Counts =>
        (Interlocked.Read(ref _calls), Interlocked.Read(ref _polls), Interlocked.Read(ref _turnCommits), Interlocked.Read(ref _turnPartitions));

    /// <summary>The partitions with a turn in the commit held by <see cref="StallNextCommit"/>.</summary>
    public IReadOnlyList<int> HeldPartitions { get; private set; } = [];

    /// <summary>No call of the node is under way, stalled or not.</summary>
    public bool Idle => Volatile.Read(ref _inFlight) == 0;

    /// <summary>What the commit held by <see cref="StallNextCommit"/> got back once it executed: "refused", "landed", or what it threw.</summary>
    public Task<string> Held => _held.Task;

    /// <summary>When the held commit's store call returned (Environment.TickCount64), recorded before <see cref="Held"/> completes.</summary>
    public long HeldAt => Volatile.Read(ref _heldAt);

    private void Hold(string outcome)
    {
        Volatile.Write(ref _heldAt, Environment.TickCount64);
        _held.TrySetResult(outcome);
    }

    /// <summary>Every call from now on waits until <paramref name="duration"/> from now (or until <see cref="Resume"/>), or longer if a stall under way lasts longer.</summary>
    public void Stall(TimeSpan duration)
    {
        lock (_lock) Begin(duration);
    }

    /// <summary>Ends the stall under way now.</summary>
    public void Resume()
    {
        lock (_lock) _until = Math.Min(_until, Environment.TickCount64);
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
        var until = duration == Timeout.InfiniteTimeSpan ? long.MaxValue : Environment.TickCount64 + (long)duration.TotalMilliseconds;
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
                lock (_lock)
                {
                    left = _until - Environment.TickCount64;
                    if (left <= 0) _stall = null;      // under the same lock that extends it: no extension is lost
                }
                if (left <= 0) break;
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(left, 20)));     // short steps, so a Resume ends it soon
            }
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
            // A batch with a turn in the partition: under an owned set (K1 D1) one batch spans the node's partitions.
            if (commit is { Turns.Count: > 0 } && commit.Turns.Any(t => commit.PartitionOf(t) == _commitPartition) && _nextCommit != TimeSpan.Zero)
            {
                Begin(_nextCommit);
                _nextCommit = TimeSpan.Zero;
                held = true;
                HeldPartitions = [.. commit.Turns.Select(commit.PartitionOf).Distinct().Order()];
            }
            wait = _stall?.Task;
        }
        if (wait is not null) await wait;
        return held;
    }

    private async ValueTask<T> Call<T>(Func<ValueTask<T>> call)
    {
        Interlocked.Increment(ref _inFlight);
        Interlocked.Increment(ref _calls);
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
        Interlocked.Increment(ref _calls);
        if (batch.Turns.Count > 0)
        {
            Interlocked.Increment(ref _turnCommits);
            Interlocked.Add(ref _turnPartitions, batch.Turns.Select(batch.PartitionOf).Distinct().Count());
        }
        try
        {
            var held = await Gate(batch);
            Interlocked.Increment(ref _commits);
            Committed result;
            try { result = await inner.CommitAsync(batch, cancellationToken); }
            catch (Exception e)
            {
                if (held) Hold($"threw {e.GetType().Name}: {e.Message}");
                throw;
            }
            if (!result.Landed) Interlocked.Increment(ref _refused);
            if (held && !result.Landed) Interlocked.Increment(ref _heldRefused);
            if (held) Hold(result.Landed ? "landed" : "refused");
            return result;
        }
        finally { Interlocked.Decrement(ref _inFlight); }
    }

    public ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _singleTakes);
        return Call(() => inner.TakeAsync(partition, node, lease, cancellationToken));
    }
    public ValueTask<OwnedLease> TakeAsync(IReadOnlyCollection<int> partitions, string node, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _setTakes);
        return Call(() => inner.TakeAsync(partitions, node, lease, cancellationToken));
    }
    public ValueTask<Lease?> RetakeAsync(int partition, string node, long epoch, TimeSpan lease, CancellationToken cancellationToken = default) => Call(async () =>
    {
        var taken = await inner.RetakeAsync(partition, node, epoch, lease, cancellationToken);
        if (taken is not null) _retaken[partition] = taken.Epoch;
        return taken;
    });

    public ValueTask<Lease?> RetakeAsync(int partition, string node, long epoch, TimeSpan lease, bool countDeaths, CancellationToken cancellationToken = default) => Call(async () =>
    {
        var taken = await inner.RetakeAsync(partition, node, epoch, lease, countDeaths, cancellationToken);
        if (taken is not null) _retaken[partition] = taken.Epoch;
        return taken;
    });
    public ValueTask<NodeBeat> NodeHeartbeatAsync(string node, TimeSpan lease, TimeSpan window, CancellationToken cancellationToken = default) => Call(() => inner.NodeHeartbeatAsync(node, lease, window, cancellationToken));

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> _retaken = new();

    /// <summary>The epoch at which the node last took the partition back by a guarded retake (after a reset), if it ever did.</summary>
    public long? Retaken(int partition) => _retaken.TryGetValue(partition, out var epoch) ? epoch : null;
    public ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default)
    {
        _releases[partition] = Environment.TickCount64;
        return Call(() => inner.ReleaseAsync(partition, epoch, cancellationToken));
    }
    public ValueTask ReleaseAsync(IReadOnlyCollection<PartitionFence> owned, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _setReleases);
        var at = Environment.TickCount64;
        foreach (var fence in owned) _releases[fence.Partition] = at;
        return Call(() => inner.ReleaseAsync(owned, cancellationToken));
    }
    public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _polls);
        if (HoldScalarPoll is { } hold && hold.Partition == partition)
            return Call<Polled>(async () =>
            {
                await hold.Completion.WaitAsync(cancellationToken);
                return await inner.PollAsync(partition, epoch, horizon, limit, cancellationToken);
            });
        return Call(() => inner.PollAsync(partition, epoch, horizon, limit, cancellationToken));
    }
    /// <summary>A set poll is one call, as on a SQL store; it still counts one poll per partition, for the cost run's column.</summary>
    public ValueTask<Polled> PollAsync(IReadOnlyCollection<PartitionFence> owned, TimeSpan horizon, int limit, CancellationToken cancellationToken = default)
    {
        Interlocked.Add(ref _polls, owned.Count);
        return Call(() => inner.PollAsync(owned, horizon, limit, cancellationToken));
    }
    public ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default) => Call(() => inner.AddAsync(message, cancellationToken));
    public ValueTask<EventAcceptance> AddEventAsync(EventDelivery delivery, CancellationToken cancellationToken = default) => Call(() => inner.AddEventAsync(delivery, cancellationToken));
    public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => Call(() => inner.LoadAsync(ids, cancellationToken));
    public bool HasActorAliases => inner.HasActorAliases;
    public ValueTask<IReadOnlyList<ActorLookup>> LoadActorsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => Call(() => inner.LoadActorsAsync(ids, cancellationToken));
    public ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default) => Call(() => inner.PublishAsync(@event, cancellationToken));
    public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default) => Call(() => inner.FindSubscriptionsAsync(key, cancellationToken));
    public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) => Call(() => inner.FindEventsAsync(keys, cancellationToken));
    public ValueTask<IReadOnlyList<EventRoute>> PublishedMatchesAsync(EventRow published, CancellationToken cancellationToken = default) => Call(() => inner.PublishedMatchesAsync(published, cancellationToken));
    public ValueTask<IReadOnlyList<EventRoute>> KeptMatchesAsync(IReadOnlyList<SubscriptionRow> waits, CancellationToken cancellationToken = default) => Call(() => inner.KeptMatchesAsync(waits, cancellationToken));
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default) => Call(() => inner.ReadJournalAsync(partition, after, take, cancellationToken));
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalOfAsync(int partition, string instance, long after, CancellationToken cancellationToken = default) => Call(() => inner.ReadJournalOfAsync(partition, instance, after, cancellationToken));
    public ValueTask<int> SweepAsync(CancellationToken cancellationToken = default) => Call(() => inner.SweepAsync(cancellationToken));
    public ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default) => Call(() => inner.ReadViewsAsync(instances, cancellationToken));
    public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default) => Call(() => inner.HeartbeatAsync(node, window, cancellationToken));
    public ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default) => Call(() => inner.SubscribeAsync(subscription, cancellationToken));
    public ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default) => Call(() => inner.ProjectedAsync(partition, cancellationToken));
    public ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default) => Call(() => inner.ProjectAsync(partition, from, to, rows, cancellationToken));
}
