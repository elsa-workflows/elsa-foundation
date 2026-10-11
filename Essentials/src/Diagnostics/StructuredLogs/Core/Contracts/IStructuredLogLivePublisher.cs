using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Core.Contracts;

/// <summary>
/// Publishes committed entries to the in-process feed. SSE treats these notifications only as wake hints;
/// payload and ordering always come from <see cref="IStructuredLogStore.ReadAfterAsync"/>.
/// </summary>
public interface IStructuredLogLivePublisher
{
    /// <summary>Fans a committed wake hint out to every matching local subscriber. Never blocks.</summary>
    void Publish(StructuredLogEntry entry);
}
