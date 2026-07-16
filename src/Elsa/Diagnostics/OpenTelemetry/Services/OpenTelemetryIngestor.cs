using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;

namespace Elsa.Diagnostics.OpenTelemetry.Services;

public class OpenTelemetryIngestor(
    IOpenTelemetryRedactor redactor,
    IOpenTelemetryStore store,
    IOpenTelemetryLiveFeed liveFeed,
    IEnumerable<IOpenTelemetryIngestionContributor> contributors) : IOpenTelemetryIngestor
{
    public OpenTelemetryIngestor(
        IOpenTelemetryRedactor redactor,
        IOpenTelemetryStore store,
        IOpenTelemetryLiveFeed liveFeed)
        : this(redactor, store, liveFeed, [])
    {
    }

    public async ValueTask IngestAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default)
    {
        var redactedBatch = redactor.Redact(batch);

        foreach (var contributor in contributors)
            await contributor.ContributeAsync(redactedBatch, cancellationToken);

        await store.WriteAsync(redactedBatch, cancellationToken);
        await liveFeed.PublishAsync(redactedBatch, cancellationToken);
    }
}
