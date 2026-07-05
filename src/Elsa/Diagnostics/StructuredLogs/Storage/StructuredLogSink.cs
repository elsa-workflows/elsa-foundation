using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Storage;

/// <summary>
/// The single capture entry-point. Owns monotonic <see cref="StructuredLogEntry.Sequence"/> assignment so
/// the durable store and the in-process live feed always observe the same stamped entry. Stamps once, then
/// appends to the history store and publishes to the live feed. The sequence counter is seeded from the
/// store's high-water mark lazily on the first emit (so construction never touches storage) and is
/// guaranteed to complete before the first sequence is assigned, so persistent backends keep increasing
/// across restarts.
/// </summary>
public sealed class StructuredLogSink : IStructuredLogSink
{
    private readonly IStructuredLogStore _store;
    private readonly IStructuredLogLivePublisher _publisher;
    private readonly Lazy<SequenceCounter> _counter;

    public StructuredLogSink(IStructuredLogStore store, IStructuredLogLivePublisher publisher)
    {
        _store = store;
        _publisher = publisher;
        // ExecutionAndPublication guarantees seeding runs exactly once and every Emit — including
        // concurrent first emits — observes the seeded value before stamping a sequence. Emit is
        // synchronous (it sits on the logging hot path), so the one-time seed blocks on the async
        // store query; for the in-memory store this completes synchronously.
        _counter = new(
            () => new SequenceCounter { Value = _store.GetHighWaterMarkAsync().GetAwaiter().GetResult() },
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public void Emit(StructuredLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var counter = _counter.Value;
        var stamped = entry with { Sequence = Interlocked.Increment(ref counter.Value) };
        _store.Append(stamped);
        _publisher.Publish(stamped);
    }

    private sealed class SequenceCounter
    {
        public long Value;
    }
}
