using Elsa.Diagnostics.OpenTelemetry.Core.Models;

namespace Elsa.Diagnostics.OpenTelemetry.Core.Contracts;

public interface IOpenTelemetryIngestor
{
    /// <summary>
    /// Ingests a batch without authenticated source authority. This compatibility overload is safe for local
    /// callers because it always supplies <see cref="OpenTelemetryIngestionContext.Untrusted"/>.
    /// </summary>
    ValueTask IngestAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default) =>
        IngestAsync(batch, OpenTelemetryIngestionContext.Untrusted, cancellationToken);

    /// <summary>Ingests a batch with context established by the receiving server.</summary>
    ValueTask IngestAsync(
        OpenTelemetryBatch batch,
        OpenTelemetryIngestionContext ingestionContext,
        CancellationToken cancellationToken = default);
}
