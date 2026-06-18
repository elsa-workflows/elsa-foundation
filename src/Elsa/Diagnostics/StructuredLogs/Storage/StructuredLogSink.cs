using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Storage;

/// <summary>
/// The single capture entry-point. Owns monotonic <see cref="StructuredLogEntry.Sequence"/> assignment so
/// the durable store and the in-process live feed always observe the same stamped entry. Stamps once, then
/// appends to the history store and publishes to the live feed. The sequence counter is seeded from the
/// store's high-water mark at construction so persistent backends keep increasing across restarts.
/// </summary>
public sealed class StructuredLogSink : IStructuredLogSink
{
    private readonly IStructuredLogStore _store;
    private readonly IStructuredLogLivePublisher _publisher;
    private long _sequence;

    public StructuredLogSink(IStructuredLogStore store, IStructuredLogLivePublisher publisher)
    {
        _store = store;
        _publisher = publisher;
        _sequence = store.GetHighWaterMark();
    }

    /// <inheritdoc />
    public void Emit(StructuredLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var stamped = entry with { Sequence = Interlocked.Increment(ref _sequence) };
        _store.Append(stamped);
        _publisher.Publish(stamped);
    }
}
