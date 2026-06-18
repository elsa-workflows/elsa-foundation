using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Core.Contracts;

/// <summary>
/// Publishes already-sequenced entries to the in-process live feed for real-time fan-out to subscribers.
/// Separated from <see cref="IStructuredLogStore"/> so the live tail stays in-memory regardless of where
/// durable history is stored (in-memory ring buffer or a persistent backend).
/// </summary>
public interface IStructuredLogLivePublisher
{
    /// <summary>Fans <paramref name="entry"/> out to every matching live subscriber. Never blocks the caller.</summary>
    void Publish(StructuredLogEntry entry);
}
