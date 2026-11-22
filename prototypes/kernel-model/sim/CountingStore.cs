namespace Kernel.Sim;

/// <summary>Passes everything through and counts the commits.</summary>
public sealed class CountingStore(IKernelStore inner, Action committed, Action<CommitBatch, bool>? observeCommit = null,
    Func<CommitBatch, ValueTask>? beforeCommit = null) : IKernelStore
{
    public Func<long>? FoundationClock => inner.FoundationClock;
    public bool SupportsCommitWindow => inner.SupportsCommitWindow;
    public bool SupportsJobTrialClear => inner.SupportsJobTrialClear;
    public bool CreationIdentityAvailable => inner.CreationIdentityAvailable;
    public bool PhysicalRetirementAvailable => inner.PhysicalRetirementAvailable;
    public bool CommitClockAvailable => inner.CommitClockAvailable;
    public bool OwnedSetsAvailable => inner.OwnedSetsAvailable;
    public ValueTask<int?> PartitionCountAsync(CancellationToken cancellationToken = default) => inner.PartitionCountAsync(cancellationToken);
    public ValueTask<OwnedLease> TakeAsync(IReadOnlyCollection<int> partitions, string node, TimeSpan lease, CancellationToken cancellationToken = default) => inner.TakeAsync(partitions, node, lease, cancellationToken);
    public ValueTask ReleaseAsync(IReadOnlyCollection<PartitionFence> owned, CancellationToken cancellationToken = default) => inner.ReleaseAsync(owned, cancellationToken);
    public ValueTask<Polled> PollAsync(IReadOnlyCollection<PartitionFence> owned, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) => inner.PollAsync(owned, horizon, limit, cancellationToken);
    public ValueTask<Lease?> RetakeAsync(int partition, string node, long epoch, TimeSpan lease, CancellationToken cancellationToken = default) => inner.RetakeAsync(partition, node, epoch, lease, cancellationToken);
    public ValueTask<Lease?> RetakeAsync(int partition, string node, long epoch, TimeSpan lease, bool countDeaths, CancellationToken cancellationToken = default) => inner.RetakeAsync(partition, node, epoch, lease, countDeaths, cancellationToken);
    public ValueTask<NodeBeat> NodeHeartbeatAsync(string node, TimeSpan lease, TimeSpan window, CancellationToken cancellationToken = default) => inner.NodeHeartbeatAsync(node, lease, window, cancellationToken);
    public ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
    {
        committed();
        return observeCommit is null && beforeCommit is null ? inner.CommitAsync(batch, cancellationToken) : ObserveAsync(batch, cancellationToken);
    }

    // False marks a request about to execute. True marks the store's landed result, before the caller sees it.
    private async ValueTask<Committed> ObserveAsync(CommitBatch batch, CancellationToken cancellationToken)
    {
        observeCommit?.Invoke(batch, false);
        if (beforeCommit is not null) await beforeCommit(batch);
        var result = await inner.CommitAsync(batch, cancellationToken);
        if (result.Landed) observeCommit?.Invoke(batch, true);
        return result;
    }

    public ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default) => inner.TakeAsync(partition, node, lease, cancellationToken);
    public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default) => inner.HeartbeatAsync(node, window, cancellationToken);
    public ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default) => inner.ReleaseAsync(partition, epoch, cancellationToken);
    public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) => inner.PollAsync(partition, epoch, horizon, limit, cancellationToken);
    public ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default) => inner.AddAsync(message, cancellationToken);
    public ValueTask<EventAcceptance> AddEventAsync(EventDelivery delivery, CancellationToken cancellationToken = default) => inner.AddEventAsync(delivery, cancellationToken);
    public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => inner.LoadAsync(ids, cancellationToken);
    public bool HasActorAliases => inner.HasActorAliases;
    public ValueTask<IReadOnlyList<ActorLookup>> LoadActorsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => inner.LoadActorsAsync(ids, cancellationToken);
    public ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default) => inner.PublishAsync(@event, cancellationToken);
    public ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default) => inner.SubscribeAsync(subscription, cancellationToken);
    public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default) => inner.FindSubscriptionsAsync(key, cancellationToken);
    public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) => inner.FindEventsAsync(keys, cancellationToken);
    public ValueTask<IReadOnlyList<EventRoute>> PublishedMatchesAsync(EventRow published, CancellationToken cancellationToken = default) => inner.PublishedMatchesAsync(published, cancellationToken);
    public ValueTask<IReadOnlyList<EventRoute>> KeptMatchesAsync(IReadOnlyList<SubscriptionRow> waits, CancellationToken cancellationToken = default) => inner.KeptMatchesAsync(waits, cancellationToken);
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default) => inner.ReadJournalAsync(partition, after, take, cancellationToken);
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalOfAsync(int partition, string instance, long after, CancellationToken cancellationToken = default) => inner.ReadJournalOfAsync(partition, instance, after, cancellationToken);
    public ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default) => inner.ProjectedAsync(partition, cancellationToken);
    public ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default) => inner.ProjectAsync(partition, from, to, rows, cancellationToken);
    public ValueTask<int> SweepAsync(CancellationToken cancellationToken = default) => inner.SweepAsync(cancellationToken);
    public ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default) => inner.ReadViewsAsync(instances, cancellationToken);
}
