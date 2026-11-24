using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// K1b slice 5b: the host's watchdog (ownership-and-fencing, "Liveness follows the Worker's passes"). A client that is
/// stuck and never resumes, while its connection stays open, holds its rows' locks however the leases run; the watchdog
/// ends that. When an owner-path store call (commit, take, poll, load or heartbeat) has outlasted twice the lease, it
/// first closes that call's connection from its own thread: it cancels the call's token, and the store aborts the call
/// on it, which closes the connection so that the server rolls the transaction back. Only when the call has still not
/// ended a third of a lease after that does it end the process, through <c>endProcess</c>: in the Embedded profile, where
/// the host process is the application, that last step is the application's policy.
/// <para>
/// Every store aborts a call on its token in the prototype; whether that closes the socket on each provider (SQL
/// Server's attention, MySQL's <c>KILL QUERY</c>, PostgreSQL's cancel request, MongoDB's operation cancel) is K4's to
/// qualify. The host wraps its store with <see cref="Watch"/> and runs <see cref="RunAsync"/> beside its loops.
/// </para>
/// </summary>
public sealed class Watchdog
{
    private readonly TimeSpan _lease;
    private readonly Action<string> _endProcess;
    private readonly Func<long> _monotonic;
    private readonly HashSet<Call> _calls = [];

    public Watchdog(TimeSpan lease, Action<string> endProcess, Func<long>? monotonic = null) =>
        (_lease, _endProcess, _monotonic) = (lease, endProcess, monotonic ?? (static () => Environment.TickCount64));

    /// <summary>Calls whose connection the watchdog closed, and the times it ended the process because one stayed stuck.</summary>
    public long Closed => Interlocked.Read(ref _closed);
    public long Ended => Interlocked.Read(ref _ended);
    private long _closed, _ended;

    /// <summary>The store as the host's Workers use it: its owner-path calls are watched; every other call goes through as it is.</summary>
    public IKernelStore Watch(IKernelStore store) => new WatchedStore(this, store);

    private sealed class Call(string what, long started, CancellationTokenSource close)
    {
        public string What => what;
        public long Started => started;
        public CancellationTokenSource Close => close;
        public long? ClosedAt;
        public bool Ended;
    }

    internal async ValueTask<T> RunAsync<T>(string what, CancellationToken cancellationToken, Func<CancellationToken, ValueTask<T>> call)
    {
        using var close = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var entry = new Call(what, _monotonic(), close);
        lock (_calls) _calls.Add(entry);
        try { return await call(close.Token); }
        finally
        {
            lock (_calls) _calls.Remove(entry);
        }
    }

    internal async ValueTask RunAsync(string what, CancellationToken cancellationToken, Func<CancellationToken, ValueTask> call) =>
        await RunAsync(what, cancellationToken, async token => { await call(token); return true; });

    /// <summary>
    /// One look at the calls in flight: close the connection of each that has outlasted twice the lease, and end the
    /// process for one still in flight a third of a lease after its close. Returns how many it closed in this look.
    /// </summary>
    public int Check()
    {
        var now = _monotonic();
        var (twice, grace) = ((long)(_lease.TotalMilliseconds * 2), (long)(_lease.TotalMilliseconds / 3));
        List<Call> closing = [];
        Call? stuck = null;
        lock (_calls)
            foreach (var call in _calls)
                if (call.ClosedAt is null && now - call.Started >= twice)
                {
                    call.ClosedAt = now;
                    closing.Add(call);
                }
                else if (call.ClosedAt is { } at && !call.Ended && now - at >= grace)
                {
                    call.Ended = true;
                    stuck ??= call;
                }
        foreach (var call in closing)
        {
            Interlocked.Increment(ref _closed);
            // Off this loop: an abort that throws or blocks neither ends the watchdog nor holds back the end-process look.
            _ = Task.Run(() => { try { call.Close.Cancel(); } catch (Exception) { /* the call ended meanwhile, or the grace covers it */ } });
        }
        if (stuck is not null)
        {
            Interlocked.Increment(ref _ended);
            _endProcess($"the {stuck.What} call has outlasted twice the lease and its connection could not be closed: the process ends");
        }
        return closing.Count;
    }

    /// <summary>The watchdog's loop: a look every third of the lease, until the token ends it.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Check();
            try { await Task.Delay(_lease / 3, cancellationToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}

/// <summary>
/// A store whose owner-path calls the <see cref="Watchdog"/> watches: commits, takes and retakes, polls, loads and both
/// heartbeats. Every member of <see cref="IKernelStore"/> is its own, so no default of the interface stands in for the
/// inner store's (a reflection check in the runtime conformance's <c>WatchdogChecks</c>, run by <c>semantic</c>, holds it to that).
/// </summary>
public sealed class WatchedStore(Watchdog watchdog, IKernelStore inner) : IKernelStore, IFoundationSchedulingStore, IDueBacklogStore
{
    public Func<long>? FoundationClock => inner.FoundationClock;
    string? IFoundationSchedulingStore.FoundationScheduleTenant(CreationAddress address) =>
        (inner as IFoundationSchedulingStore)?.FoundationScheduleTenant(address);
    ParkDrainSample? IFoundationSchedulingStore.SampleParkDrains(string node, IReadOnlyList<PartitionFence> owned, Func<InboxKey, bool> handedOffHere) =>
        (inner as IFoundationSchedulingStore)?.SampleParkDrains(node, owned, handedOffHere);
    public bool SupportsCommitWindow => inner.SupportsCommitWindow;
    bool IKernelStore.SupportsCascadePages => inner.SupportsCascadePages;
    public bool SupportsJobTrialClear => inner.SupportsJobTrialClear;
    public bool CreationIdentityAvailable => inner.CreationIdentityAvailable;
    public bool PhysicalRetirementAvailable => inner.PhysicalRetirementAvailable;
    public bool HasActorAliases => inner.HasActorAliases;
    public bool CommitClockAvailable => inner.CommitClockAvailable;
    public bool OwnedSetsAvailable => inner.OwnedSetsAvailable;

    bool IDueBacklogStore.MeasuresDueBacklog => inner is IDueBacklogStore { MeasuresDueBacklog: true };
    int IDueBacklogStore.PublishedDueBacklog(int partition) => ((IDueBacklogStore)inner).PublishedDueBacklog(partition);
    ValueTask<int> IDueBacklogStore.CountDueBacklogAsync(int partition, int cap, CancellationToken cancellationToken) =>
        watchdog.RunAsync("backlog", cancellationToken, token => ((IDueBacklogStore)inner).CountDueBacklogAsync(partition, cap, token));

    // The owner path, watched.
    public ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default) =>
        watchdog.RunAsync("commit", cancellationToken, token => inner.CommitAsync(batch, token));
    public ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default) =>
        watchdog.RunAsync("take", cancellationToken, token => inner.TakeAsync(partition, node, lease, token));
    public ValueTask<OwnedLease> TakeAsync(IReadOnlyCollection<int> partitions, string node, TimeSpan lease, CancellationToken cancellationToken = default) =>
        watchdog.RunAsync("take", cancellationToken, token => inner.TakeAsync(partitions, node, lease, token));
    public ValueTask<Lease?> RetakeAsync(int partition, string node, long epoch, TimeSpan lease, CancellationToken cancellationToken = default) =>
        watchdog.RunAsync("take", cancellationToken, token => inner.RetakeAsync(partition, node, epoch, lease, token));
    public ValueTask<Lease?> RetakeAsync(int partition, string node, long epoch, TimeSpan lease, bool countDeaths, CancellationToken cancellationToken = default) =>
        watchdog.RunAsync("take", cancellationToken, token => inner.RetakeAsync(partition, node, epoch, lease, countDeaths, token));
    public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) =>
        watchdog.RunAsync("poll", cancellationToken, token => inner.PollAsync(partition, epoch, horizon, limit, token));
    public ValueTask<Polled> PollAsync(IReadOnlyCollection<PartitionFence> owned, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) =>
        watchdog.RunAsync("poll", cancellationToken, token => inner.PollAsync(owned, horizon, limit, token));
    public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) =>
        watchdog.RunAsync("load", cancellationToken, token => inner.LoadAsync(ids, token));
    public ValueTask<IReadOnlyList<ActorLookup>> LoadActorsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) =>
        watchdog.RunAsync("load", cancellationToken, token => inner.LoadActorsAsync(ids, token));
    public ValueTask<NodeBeat> NodeHeartbeatAsync(string node, TimeSpan lease, TimeSpan window, CancellationToken cancellationToken = default) =>
        watchdog.RunAsync("heartbeat", cancellationToken, token => inner.NodeHeartbeatAsync(node, lease, window, token));
    public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default) =>
        watchdog.RunAsync("heartbeat", cancellationToken, token => inner.HeartbeatAsync(node, window, token));

    // Everything else, as it is. Whether releases and a pass's event writes are owner-path calls is open (#4880, 7048452155).
    public ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default) => inner.ReleaseAsync(partition, epoch, cancellationToken);
    public ValueTask ReleaseAsync(IReadOnlyCollection<PartitionFence> owned, CancellationToken cancellationToken = default) => inner.ReleaseAsync(owned, cancellationToken);
    public ValueTask<int?> PartitionCountAsync(CancellationToken cancellationToken = default) => inner.PartitionCountAsync(cancellationToken);
    public ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default) => inner.AddAsync(message, cancellationToken);
    public ValueTask<EventAcceptance> AddEventAsync(EventDelivery delivery, CancellationToken cancellationToken = default) => inner.AddEventAsync(delivery, cancellationToken);
    public ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default) => inner.PublishAsync(@event, cancellationToken);
    public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default) => inner.FindSubscriptionsAsync(key, cancellationToken);
    public ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default) => inner.SubscribeAsync(subscription, cancellationToken);
    public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) => inner.FindEventsAsync(keys, cancellationToken);
    public ValueTask<IReadOnlyList<EventRoute>> PublishedMatchesAsync(EventRow published, CancellationToken cancellationToken = default) => inner.PublishedMatchesAsync(published, cancellationToken);
    public ValueTask<IReadOnlyList<EventRoute>> KeptMatchesAsync(IReadOnlyList<SubscriptionRow> waits, CancellationToken cancellationToken = default) => inner.KeptMatchesAsync(waits, cancellationToken);
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default) => inner.ReadJournalAsync(partition, after, take, cancellationToken);
    public ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default) => inner.ProjectedAsync(partition, cancellationToken);
    public ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default) => inner.ProjectAsync(partition, from, to, rows, cancellationToken);
    public ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default) => inner.ReadViewsAsync(instances, cancellationToken);
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalOfAsync(int partition, string instance, long after, CancellationToken cancellationToken = default) => inner.ReadJournalOfAsync(partition, instance, after, cancellationToken);
    public ValueTask<int> SweepAsync(CancellationToken cancellationToken = default) => inner.SweepAsync(cancellationToken);
}
