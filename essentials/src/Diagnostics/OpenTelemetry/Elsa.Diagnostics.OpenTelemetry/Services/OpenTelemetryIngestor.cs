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
        await IngestAsync(batch, OpenTelemetryIngestionContext.Untrusted, cancellationToken);
    }

    public async ValueTask IngestAsync(
        OpenTelemetryBatch batch,
        OpenTelemetryIngestionContext ingestionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ingestionContext);
        var redactedBatch = redactor.Redact(batch);

        foreach (var contributor in contributors)
            await contributor.ContributeAsync(redactedBatch, ingestionContext, cancellationToken);

        await store.WriteAsync(redactedBatch, cancellationToken);
        await liveFeed.PublishAsync(redactedBatch, cancellationToken);
    }
}
