using Elsa.Diagnostics.OpenTelemetry.Core.Models;

namespace Elsa.Diagnostics.OpenTelemetry.Core.Contracts;

public interface IOpenTelemetrySourceRegistry
{
    long DroppedCount { get; }
    void MarkSeen(TelemetryResource resource);
    IReadOnlyCollection<TelemetryResource> List();
}
