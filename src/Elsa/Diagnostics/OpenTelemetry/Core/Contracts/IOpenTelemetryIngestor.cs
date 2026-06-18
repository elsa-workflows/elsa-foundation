using Elsa.Diagnostics.OpenTelemetry.Core.Models;

namespace Elsa.Diagnostics.OpenTelemetry.Core.Contracts;

public interface IOpenTelemetryIngestor
{
    ValueTask IngestAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default);
}
