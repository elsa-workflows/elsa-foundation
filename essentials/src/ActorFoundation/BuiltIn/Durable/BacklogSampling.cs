namespace Kernel.Durable;

internal sealed record BacklogOptions(int HighWater, int LowWater, TimeSpan SampleInterval, TimeSpan HeartbeatInterval);

/// <summary>Each sampled estimate belongs to the epoch that read it.</summary>
internal sealed class BacklogSampling(IDueBacklogStore store, BacklogOptions options)
{
    private sealed class Sample(long epoch, long now, int published)
    {
        public long Epoch { get; } = epoch;
        public long NextRead { get; set; } = now;
        public long LastWrite { get; set; } = now;
        public int Estimate { get; set; } = published;
        public int Published { get; set; } = published;
    }

    private readonly Dictionary<int, Sample> _samples = [];
    private long SampleMs => (long)options.SampleInterval.TotalMilliseconds;
    private long HeartbeatMs => (long)options.HeartbeatInterval.TotalMilliseconds;

    internal async ValueTask ReadAsync(IReadOnlyList<PartitionFence> fences, long now, CancellationToken cancellationToken)
    {
        foreach (var fence in fences)
        {
            if (cancellationToken.IsCancellationRequested) return;
            if (!_samples.TryGetValue(fence.Partition, out var sample) || sample.Epoch != fence.Epoch)
                _samples[fence.Partition] = sample = new(fence.Epoch, now, store.PublishedDueBacklog(fence.Partition));
            if (now < sample.NextRead) continue;

            sample.NextRead = now + SampleMs;
            try
            {
                sample.Estimate = await store.CountDueBacklogAsync(fence.Partition, options.HighWater + 1, cancellationToken);
            }
            catch (Exception)
            {
                // A failed observational read leaves the last estimate and tries again at the next interval.
            }
        }
    }

    internal Dictionary<int, int>? ForCommit(IReadOnlyList<PartitionFence> fences)
    {
        Dictionary<int, int>? updates = null;
        foreach (var fence in fences)
            if (At(fence) is { } sample && sample.Estimate != sample.Published)
                (updates ??= []).Add(fence.Partition, sample.Estimate);
        return updates;
    }

    internal void Landed(CommitBatch batch, long now)
    {
        foreach (var fence in batch.Fenced)
            if (At(fence) is { } sample)
            {
                sample.LastWrite = now;
                if (batch.DueBacklog?.TryGetValue(fence.Partition, out var depth) == true)
                    sample.Published = depth;
            }
    }

    internal List<PartitionFence> PressureFences(IReadOnlyList<PartitionFence> fences, long now) =>
        [.. fences.Where(fence => At(fence) is { } sample && Crossed(sample) && now >= sample.LastWrite + HeartbeatMs)];

    internal long NextDelay(IReadOnlyList<PartitionFence> fences, long now)
    {
        var next = long.MaxValue;
        foreach (var fence in fences)
            if (At(fence) is { } sample)
            {
                next = Math.Min(next, sample.NextRead);
                if (Crossed(sample)) next = Math.Min(next, sample.LastWrite + HeartbeatMs);
            }
        return next == long.MaxValue ? next : Math.Max(1, next - now);
    }

    // The pressure list includes lowWater, while admission refuses above highWater.
    private int Band(int depth) => depth > options.HighWater ? 2 : depth >= options.LowWater ? 1 : 0;
    private bool Crossed(Sample sample) => Band(sample.Estimate) != Band(sample.Published);
    private Sample? At(PartitionFence fence) =>
        _samples.GetValueOrDefault(fence.Partition) is { } sample && sample.Epoch == fence.Epoch ? sample : null;
}
