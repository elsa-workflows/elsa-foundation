using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Storage;

/// <summary>
/// The single capture entry-point. Assigns display-only <see cref="StructuredLogEntry.Sequence"/> metadata,
/// submits the entry through the store's nonblocking append path, and publishes a process-local wake hint
/// only after durable commit. Durable SSE payload and order come from store read-after pages; concurrent
/// processes may assign equal display sequences.
/// </summary>
public sealed class StructuredLogSink : IStructuredLogSink
{
    private readonly IStructuredLogStore _store;
    private readonly IStructuredLogLivePublisher _publisher;
    private readonly Lazy<SequenceCounter> _counter;
    private readonly object _publicationGate = new();
    private Task _publicationTail = Task.CompletedTask;

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
        lock (_publicationGate)
        {
            Task<StructuredLogEntry> commit;
            try
            {
                commit = _store.AppendAsync(stamped).AsTask();
            }
            catch (Exception exception)
            {
                commit = Task.FromException<StructuredLogEntry>(exception);
            }

            _publicationTail = PublishInAcceptedOrderAsync(_publicationTail, commit);
        }
    }

    private async Task PublishInAcceptedOrderAsync(
        Task previousPublication,
        Task<StructuredLogEntry> commit)
    {
        try
        {
            await previousPublication;
            var committed = await commit;
            _publisher.Publish(committed);
        }
        catch
        {
            // Capture must never throw into or recursively log through the host logging path. Durable
            // adapters own retry/drop diagnostics and complete failed or shed appends without publication.
        }
    }

    private sealed class SequenceCounter
    {
        public long Value;
    }
}
