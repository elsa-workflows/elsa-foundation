using System.Collections.Concurrent;

namespace Kernel.Store.Conformance;

/// <summary>
/// Injected connection failures: a call fails before it reaches the store, or it executes and its answer is lost. The
/// error is not a <see cref="StoreException"/>, so the kernel takes a failed commit for an unknown outcome.
/// </summary>
public sealed class FaultyStore(IKernelStore inner, double rate, int seed) : IKernelStore
{
    public bool CommitClockAvailable => inner.CommitClockAvailable;
    private readonly Random _random = new(seed);
    private long _before, _after;
    public bool Calm { get; set; }
    public long Before => _before;
    public long After => _after;

    private async ValueTask<T> Call<T>(Func<ValueTask<T>> call)
    {
        double roll;
        lock (_random) roll = _random.NextDouble();
        if (!Calm && roll < rate / 2)
        {
            Interlocked.Increment(ref _before);
            throw new IOException("injected: the connection broke before the request was sent");
        }
        var result = await call();
        if (!Calm && roll < rate)
        {
            Interlocked.Increment(ref _after);
            throw new IOException("injected: the connection broke after the request executed");
        }
        return result;
    }

    private async ValueTask Call(Func<ValueTask> call) => await Call<bool>(async () => { await call(); return true; });

    public ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default) => Call(() => inner.TakeAsync(partition, node, lease, cancellationToken));
    public ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default) => Call(() => inner.ReleaseAsync(partition, epoch, cancellationToken));
    public ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default) => Call(() => inner.CommitAsync(batch, cancellationToken));
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

/// <summary>
/// Records every commit whose answer said it landed, and every failure the store classified. Invariant 1 is checked
/// on these from outside the store: two landed commits never cover the same position, and along the positions of a
/// partition the epoch never goes back.
/// </summary>
public sealed class RecordingStore(IKernelStore inner) : IKernelStore
{
    public bool CommitClockAvailable => inner.CommitClockAvailable;
    public sealed record Landed(int Partition, long Epoch, long Position, int Positions, string Turns);

    public ConcurrentQueue<Landed> Commits { get; } = new();
    public ConcurrentDictionary<string, int> Failures { get; } = new();
    private long _refused;
    public long Refused => _refused;

    public async ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await inner.CommitAsync(batch, cancellationToken);
            if (result.Landed) Commits.Enqueue(new Landed(batch.Partition, batch.Epoch, batch.Position, batch.Positions, string.Join(" ", batch.Turns.Select(s => s.Instance))));
            else Interlocked.Increment(ref _refused);
            return result;
        }
        catch (Exception e)
        {
            var kind = e is StoreException s ? $"{s.Failure}: {Shorten(s.Message)}" : $"unknown ({e.GetType().Name}): {Shorten(e.Message)}";
            Failures.AddOrUpdate(kind, 1, (_, n) => n + 1);
            throw;
        }
    }

    private static string Shorten(string message)
    {
        var line = message.Split('\n')[0];
        foreach (var digitRun in System.Text.RegularExpressions.Regex.Matches(line, @"[0-9a-f]{8}-[0-9a-f-]{27}|\b\d{3,}\b").Select(m => m.Value).Distinct()) line = line.Replace(digitRun, "#");
        return line.Length > 140 ? line[..140] : line;
    }

    /// <summary>The violations of invariant 1 that the recorded commits show.</summary>
    public List<string> CheckOneWriter()
    {
        var wrong = new List<string>();
        foreach (var partition in Commits.GroupBy(c => c.Partition))
        {
            var moved = partition.Where(c => c.Positions > 0).OrderBy(c => c.Position).ThenBy(c => c.Epoch).ToList();
            for (var i = 1; i < moved.Count; i++)
            {
                var (before, after) = (moved[i - 1], moved[i]);
                if (after.Position < before.Position + before.Positions)
                    wrong.Add($"I1 partition {partition.Key}: two commits landed over position {after.Position + 1} (epoch {before.Epoch} at {before.Position}+{before.Positions} [{before.Turns}], epoch {after.Epoch} at {after.Position}+{after.Positions} [{after.Turns}])");
                if (after.Epoch < before.Epoch)
                    wrong.Add($"I1 partition {partition.Key}: a commit prepared under epoch {after.Epoch} landed at position {after.Position} after one under epoch {before.Epoch} at {before.Position}");
            }
        }
        return wrong;
    }

    public ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default) => inner.TakeAsync(partition, node, lease, cancellationToken);
    public ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default) => inner.ReleaseAsync(partition, epoch, cancellationToken);
    public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) => inner.PollAsync(partition, epoch, horizon, limit, cancellationToken);
    public ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default) => inner.AddAsync(message, cancellationToken);
    public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => inner.LoadAsync(ids, cancellationToken);
    public ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default) => inner.PublishAsync(@event, cancellationToken);
    public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default) => inner.FindSubscriptionsAsync(key, cancellationToken);
    public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) => inner.FindEventsAsync(keys, cancellationToken);
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default) => inner.ReadJournalAsync(partition, after, take, cancellationToken);
    public ValueTask<int> SweepAsync(CancellationToken cancellationToken = default) => inner.SweepAsync(cancellationToken);
    public ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default) => inner.ReadViewsAsync(instances, cancellationToken);
    public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default) => inner.HeartbeatAsync(node, window, cancellationToken);
    public ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default) => inner.SubscribeAsync(subscription, cancellationToken);
    public ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default) => inner.ProjectedAsync(partition, cancellationToken);
    public ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default) => inner.ProjectAsync(partition, from, to, rows, cancellationToken);
}
