using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Storage;

/// <summary>
/// The single capture entry-point. Assigns display-only <see cref="StructuredLogEntry.Sequence"/> metadata,
/// submits the entry through the store's nonblocking append path, and publishes a process-local wake hint
/// only after durable commit. Durable SSE payload and order come from store read-after pages; concurrent
/// processes may assign equal display sequences.
/// </summary>
/// <remarks>
/// <see cref="Emit"/> never reads from the store. Capture starts before a durable store is ready (a shell logs
/// while activating, before its migrations run), so any store read here would fail, and the provider's own logging
/// of that failure would re-enter this sink. A store that owns durable positions assigns the committed sequence in
/// its append path, as <c>EfStructuredLogStore</c> does; an entry the store rejects is its to account for, and a
/// rejection never affects later emits.
/// </remarks>
public sealed class StructuredLogSink : IStructuredLogSink
{
    private readonly IStructuredLogStore _store;
    private readonly IStructuredLogLivePublisher _publisher;
    private readonly object _publicationGate = new();
    private Task _publicationTail = Task.CompletedTask;
    private long _sequence;

    public StructuredLogSink(IStructuredLogStore store, IStructuredLogLivePublisher publisher)
    {
        _store = store;
        _publisher = publisher;
    }

    /// <inheritdoc />
    public void Emit(StructuredLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var stamped = entry with { Sequence = Interlocked.Increment(ref _sequence) };
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
}
