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

    public void Record(DroppedEntriesSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        Record(signal.DroppedCount);
    }

    public void Record(OpenTelemetryDroppedItemSummary signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        Record(signal.Count);
    }

    private void Record(long count) =>
        _observer.RecordLoss(DiagnosticsPersistenceLossReason.SubscriberDelivery, count);
}
