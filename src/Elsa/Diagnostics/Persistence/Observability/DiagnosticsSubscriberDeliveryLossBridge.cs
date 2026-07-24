using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.Persistence.Observability;

/// <summary>
/// Maps the two domain-owned live-feed loss signals into the shared low-cardinality subscriber-delivery
/// classification. It observes signals that already exist; it does not move fan-out into persistence.
/// </summary>
public sealed class DiagnosticsSubscriberDeliveryLossBridge
{
    private readonly IDiagnosticsPersistenceObserver _observer;

    public DiagnosticsSubscriberDeliveryLossBridge(IDiagnosticsPersistenceObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _observer = observer;
    }

    public void RecordOpenTelemetry(OpenTelemetryDroppedItemSummary signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentOutOfRangeException.ThrowIfLessThan(signal.Count, 1);
        RecordLoss(signal.Count);
    }

    /// <summary>
    /// Creates one cumulative-signal recorder for one Structured Logs subscription. Per-subscription state
    /// prevents repeated cumulative signals from being counted twice and is released with the subscriber.
    /// </summary>
    public Action<DroppedEntriesSignal> CreateStructuredLogRecorder()
    {
        var counter = new CumulativeDropCounter();
        return signal =>
        {
            ArgumentNullException.ThrowIfNull(signal);
            ArgumentOutOfRangeException.ThrowIfLessThan(signal.DroppedCount, 1);
            RecordLoss(counter.Advance(signal.DroppedCount));
        };
    }

    private void RecordLoss(long count)
    {
        if (count == 0)
            return;

        try
        {
            _observer.RecordLoss(DiagnosticsPersistenceLossReason.SubscriberDelivery, count);
        }
        catch
        {
            // Observability must never break live-feed delivery.
        }
    }

    private sealed class CumulativeDropCounter
    {
        private long _count;

        public long Advance(long count)
        {
            while (true)
            {
                var previous = Volatile.Read(ref _count);
                if (count <= previous)
                    return 0;
                if (Interlocked.CompareExchange(ref _count, count, previous) == previous)
                    return count - previous;
            }
        }
    }
}
