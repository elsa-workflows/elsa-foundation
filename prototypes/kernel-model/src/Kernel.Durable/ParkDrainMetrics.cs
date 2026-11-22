using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Kernel.Durable;

internal readonly record struct ParkDrainSample(long Rows, long Bytes, long Held, long MaxRows, long MaxBytes);

internal static class ParkDrainMetrics
{
    private static readonly Meter Meter = new("Elsa.Kernel.Durable");
    private static readonly Gauge<long> Pending = Meter.CreateGauge<long>("elsa.kernel.drain.pending", "{message}");
    private static readonly Gauge<long> PendingBytes = Meter.CreateGauge<long>("elsa.kernel.drain.pending.bytes", "By");
    private static readonly Gauge<long> Held = Meter.CreateGauge<long>("elsa.kernel.drain.held", "{message}");

    internal static bool Enabled => Pending.Enabled || PendingBytes.Enabled || Held.Enabled;

    internal static void Record(ParkDrainSample sample)
    {
        var sum = new TagList { { "aggregate", "sum" } };
        var max = new TagList { { "aggregate", "max" } };
        Held.Record(sample.Held);
        Pending.Record(sample.Rows, sum);
        Pending.Record(sample.MaxRows, max);
        PendingBytes.Record(sample.Bytes, sum);
        PendingBytes.Record(sample.MaxBytes, max);
    }
}

public sealed partial class InMemoryStore
{
    ParkDrainSample? IKernelStore.SampleParkDrains(string node, IReadOnlyList<PartitionFence> owned, Func<InboxKey, bool> handedOffHere)
    {
        lock (_gate)
        {
            if (NodeExpired(node, FoundationClock())) return null;
            var partitions = owned.Where(fence => Partitions[fence.Partition].Owner == node &&
                Partitions[fence.Partition].Epoch == fence.Epoch).Select(fence => fence.Partition).ToHashSet();
            if (partitions.Count == 0) return null;

            long rows = 0, bytes = 0, held = 0;
            var pending = new Dictionary<string, (long Rows, long Bytes)>(StringComparer.Ordinal);
            foreach (var (key, message) in Messages)
            {
                if (message.ReleasedBy is not { } release || !partitions.Contains(message.Message.Target.Partition) ||
                    !Instances.TryGetValue(message.Message.Target.Id, out var actor) || !PendingRecoveryInput(actor, release, message)) continue;
                var open = actor.ParkRecovery is not null && release == new ParkRelease(actor.Creation, actor.ParkResolutionGeneration);
                if (!open && actor.ParkCohort != release) continue;
                if (handedOffHere(key))
                {
                    held++;
                    continue;
                }
                if (!open) continue;
                var size = ParkRecoveryCredit.StoredBytes(message.PayloadLength);
                var counts = pending.GetValueOrDefault(actor.Id);
                pending[actor.Id] = (counts.Rows + 1, counts.Bytes + size);
                rows++;
                bytes += size;
            }
            return new(rows, bytes, held, pending.Values.Select(counts => counts.Rows).DefaultIfEmpty().Max(),
                pending.Values.Select(counts => counts.Bytes).DefaultIfEmpty().Max());
        }
    }
}
