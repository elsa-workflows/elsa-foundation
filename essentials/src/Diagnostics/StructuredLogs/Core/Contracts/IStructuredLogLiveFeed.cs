using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Core.Contracts;

/// <summary>
/// Exposes a process-local, filtered feed of committed wake hints. Each subscriber gets an independent bounded stream;
/// if it falls behind, oldest queued entries are dropped and a <see cref="DroppedEntriesSignal"/> is
/// delivered in-band rather than blocking the producer.
/// </summary>
public interface IStructuredLogLiveFeed
{
    /// <summary>
    /// Subscribes to wake hints matching <paramref name="filter"/>. The returned sequence yields a
    /// <see cref="StructuredLogStreamItem"/> per delivered entry and per drop notice until
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    IAsyncEnumerable<StructuredLogStreamItem> Subscribe(StructuredLogFilter filter, CancellationToken cancellationToken);
}
