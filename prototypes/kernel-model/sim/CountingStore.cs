namespace Kernel.Sim;

/// <summary>Passes everything through and counts the commits.</summary>
public sealed class CountingStore(IKernelStore inner, Action committed) : IKernelStore
{
    public ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
    {
        committed();
        return inner.CommitAsync(batch, cancellationToken);
    }

    public ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default) => inner.TakeAsync(partition, node, lease, cancellationToken);
    public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default) => inner.HeartbeatAsync(node, window, cancellationToken);
    public ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default) => inner.ReleaseAsync(partition, epoch, cancellationToken);
    public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) => inner.PollAsync(partition, epoch, horizon, limit, cancellationToken);
    public ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default) => inner.AddAsync(message, cancellationToken);
    public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => inner.LoadAsync(ids, cancellationToken);
    public ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default) => inner.PublishAsync(@event, cancellationToken);
    public ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default) => inner.SubscribeAsync(subscription, cancellationToken);
    public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default) => inner.FindSubscriptionsAsync(key, cancellationToken);
    public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) => inner.FindEventsAsync(keys, cancellationToken);
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default) => inner.ReadJournalAsync(partition, after, take, cancellationToken);
    public ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default) => inner.ProjectedAsync(partition, cancellationToken);
    public ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default) => inner.ProjectAsync(partition, from, to, rows, cancellationToken);
    public ValueTask<int> SweepAsync(CancellationToken cancellationToken = default) => inner.SweepAsync(cancellationToken);
    public ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default) => inner.ReadViewsAsync(instances, cancellationToken);
}
