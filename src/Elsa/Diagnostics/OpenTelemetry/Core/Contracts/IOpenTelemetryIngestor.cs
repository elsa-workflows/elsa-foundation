using Elsa.Diagnostics.OpenTelemetry.Core.Models;

namespace Elsa.Diagnostics.OpenTelemetry.Core.Contracts;

public interface IOpenTelemetryIngestor
{
    /// <summary>
    /// Ingests a batch without authenticated source authority. Existing implementations retain this contract;
    /// the context-aware overload below safely delegates here unless they explicitly opt into trusted context.
    /// </summary>
    ValueTask IngestAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests a batch with context established by the receiving server. Legacy implementations safely ignore
    /// authority context; they cannot accidentally grant tenant authority without explicitly overriding this API.
    /// </summary>
    ValueTask IngestAsync(
        OpenTelemetryBatch batch,
        OpenTelemetryIngestionContext ingestionContext,
        CancellationToken cancellationToken = default) =>
        IngestAsync(batch, cancellationToken);
}
